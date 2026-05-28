// <copyright file="MapPackageWriter.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.CustomMaps;

using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

/// <summary>
/// Composes a <c>.bmap</c> map package. Assets are added via <see cref="AddAsset(string, byte[])"/>
/// or <see cref="AddAsset(string, Stream)"/>; their SHA-256 and length are recorded in the manifest
/// as they're written. <see cref="Commit"/> finalises the package by writing <c>manifest.json</c> last.
/// </summary>
/// <remarks>
/// Typical usage:
/// <code>
/// using var writer = MapPackageWriter.CreateFile(outputPath);
/// writer.Map.Number = 201;
/// writer.Map.Name = "Champion Arena";
/// writer.WarpInfo = new MapPackageWarpInfo { Name = "ChampionArena", LevelRequirement = 80, Costs = 10000 };
/// writer.AddAsset("assets/EncTerrain202.att", attBytes);
/// writer.Commit();
/// </code>
/// Disposal without an explicit <see cref="Commit"/> call commits automatically (so a using-block
/// produces a valid package), unless <see cref="Cancel"/> was called first.
/// </remarks>
public sealed class MapPackageWriter : IDisposable
{
    private readonly ZipArchive _archive;
    private readonly bool _leaveOpen;
    private readonly List<MapPackageAsset> _assets = new();
    private readonly HashSet<string> _seenPaths = new(StringComparer.Ordinal);
    private bool _committed;
    private bool _cancelled;
    private bool _disposed;

    private MapPackageWriter(ZipArchive archive, bool leaveOpen)
    {
        this._archive = archive;
        this._leaveOpen = leaveOpen;
    }

    /// <summary>Map identity metadata. Always written; defaults assume an editor-authored custom map.</summary>
    public MapPackageMapDefinition Map { get; set; } = new() { IsCustomMap = true };

    /// <summary>Optional spawn-gate seed for first import. Omit to skip.</summary>
    public MapPackageSpawnGate? SpawnGate { get; set; }

    /// <summary>Optional warp-list seed for first import. Omit to skip.</summary>
    public MapPackageWarpInfo? WarpInfo { get; set; }

    /// <summary>Identifier of the human creating the package. Optional.</summary>
    public string? CreatedBy { get; set; }

    /// <summary>Version string of the authoring tool. Optional.</summary>
    public string? EditorVersion { get; set; }

    /// <summary>Creates a writer over a stream. Stream must be writable and seekable.</summary>
    public static MapPackageWriter Create(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite || !stream.CanSeek)
        {
            throw new ArgumentException("Stream must be writable and seekable.", nameof(stream));
        }

        var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen);
        return new MapPackageWriter(archive, leaveOpen);
    }

    /// <summary>Creates a writer that produces a file at <paramref name="path"/>. Overwrites if it already exists.</summary>
    public static MapPackageWriter CreateFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        try
        {
            return Create(fileStream, leaveOpen: false);
        }
        catch
        {
            fileStream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Adds an asset entry. The path must start with <see cref="MapPackageFormat.AssetsPrefix"/>
    /// and must be unique within the package. Computes and stores SHA-256 in the manifest.
    /// </summary>
    public void AddAsset(string relativePath, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        this.AddAssetCore(relativePath, data.AsMemory());
    }

    /// <summary>Adds an asset entry by streaming from <paramref name="source"/>. Buffers in memory while hashing.</summary>
    public void AddAsset(string relativePath, Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);

        // We have to buffer because we hash before writing the entry, and the zip
        // entry stream is forward-only. Asset sizes are capped at MaxAssetBytes so
        // this is bounded.
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        if (buffer.Length > MapPackageFormat.MaxAssetBytes)
        {
            throw new ArgumentException(
                $"Asset '{relativePath}' size {buffer.Length} exceeds the maximum {MapPackageFormat.MaxAssetBytes}.",
                nameof(source));
        }

        this.AddAssetCore(relativePath, buffer.GetBuffer().AsMemory(0, checked((int)buffer.Length)));
    }

    /// <summary>
    /// Finalises the package: validates required fields, writes <c>manifest.json</c>, and closes the archive.
    /// Calling Commit twice is a no-op.
    /// </summary>
    public void Commit()
    {
        this.ThrowIfDisposed();
        if (this._cancelled)
        {
            throw new InvalidOperationException("Package was cancelled — cannot commit.");
        }

        if (this._committed)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(this.Map.Name))
        {
            throw new InvalidOperationException("Map.Name must be set before committing.");
        }

        if (this.Map.Number < 0)
        {
            throw new InvalidOperationException($"Map.Number {this.Map.Number} is invalid.");
        }

        var manifest = new MapPackageManifest
        {
            SchemaVersion = MapPackageFormat.CurrentSchemaVersion,
            Package = new MapPackageInfo
            {
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = this.CreatedBy,
                EditorVersion = this.EditorVersion,
            },
            Map = this.Map,
            SpawnGate = this.SpawnGate,
            WarpInfo = this.WarpInfo,
            Assets = this._assets,
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(manifest, options);

        var manifestEntry = this._archive.CreateEntry(MapPackageFormat.ManifestEntryName, CompressionLevel.Optimal);
        using (var stream = manifestEntry.Open())
        {
            stream.Write(json, 0, json.Length);
        }

        this._committed = true;
    }

    /// <summary>Discards the package — Dispose will not commit. Useful in error paths.</summary>
    public void Cancel()
    {
        this._cancelled = true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (this._disposed)
        {
            return;
        }

        try
        {
            if (!this._committed && !this._cancelled)
            {
                this.Commit();
            }
        }
        finally
        {
            this._disposed = true;
            if (!this._leaveOpen)
            {
                this._archive.Dispose();
            }
        }
    }

    private void AddAssetCore(string relativePath, ReadOnlyMemory<byte> data)
    {
        this.ThrowIfDisposed();
        if (this._committed)
        {
            throw new InvalidOperationException("Package has been committed — no more assets can be added.");
        }

        ArgumentException.ThrowIfNullOrEmpty(relativePath);

        if (!relativePath.StartsWith(MapPackageFormat.AssetsPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Asset path must start with '{MapPackageFormat.AssetsPrefix}', got '{relativePath}'.",
                nameof(relativePath));
        }

        if (relativePath.Contains("..", StringComparison.Ordinal) || relativePath.Contains('\\', StringComparison.Ordinal))
        {
            throw new ArgumentException($"Asset path '{relativePath}' is not allowed.", nameof(relativePath));
        }

        if (data.Length > MapPackageFormat.MaxAssetBytes)
        {
            throw new ArgumentException(
                $"Asset '{relativePath}' size {data.Length} exceeds the maximum {MapPackageFormat.MaxAssetBytes}.",
                nameof(data));
        }

        if (!this._seenPaths.Add(relativePath))
        {
            throw new ArgumentException($"Asset '{relativePath}' already added.", nameof(relativePath));
        }

        if (this._assets.Count >= MapPackageFormat.MaxAssetCount)
        {
            throw new InvalidOperationException($"Package already contains {MapPackageFormat.MaxAssetCount} assets — cap reached.");
        }

        Span<byte> hashBytes = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(data.Span, hashBytes);
        var hashHex = Convert.ToHexString(hashBytes).ToLowerInvariant();

        var entry = this._archive.CreateEntry(relativePath, CompressionLevel.Optimal);
        using (var stream = entry.Open())
        {
            stream.Write(data.Span);
        }

        this._assets.Add(new MapPackageAsset
        {
            Path = relativePath,
            Sha256 = hashHex,
            Bytes = data.Length,
        });
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);
    }
}
