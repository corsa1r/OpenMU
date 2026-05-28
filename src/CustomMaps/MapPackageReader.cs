// <copyright file="MapPackageReader.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.CustomMaps;

using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

/// <summary>
/// Reads a <c>.bmap</c> map package from a stream or file. The reader holds the
/// archive open for the lifetime of the instance — assets are streamed on demand,
/// not buffered, so packages with multi-MB <c>.obj</c> entries don't balloon memory.
/// </summary>
/// <remarks>
/// Typical usage:
/// <code>
/// using var reader = MapPackageReader.OpenFile(path);
/// var issues = reader.Validate();
/// if (issues.Count > 0) throw new InvalidDataException(string.Join("; ", issues));
///
/// foreach (var asset in reader.Manifest.Assets)
/// {
///     using var stream = reader.OpenAsset(asset.Path);
///     // ... write stream to /app/custom-maps/WorldN+1/...
/// }
/// </code>
/// </remarks>
public sealed class MapPackageReader : IDisposable
{
    private readonly ZipArchive _archive;
    private readonly bool _leaveOpen;
    private bool _disposed;

    private MapPackageReader(ZipArchive archive, MapPackageManifest manifest, bool leaveOpen)
    {
        this._archive = archive;
        this.Manifest = manifest;
        this._leaveOpen = leaveOpen;
    }

    /// <summary>The parsed manifest. Read-only after construction.</summary>
    public MapPackageManifest Manifest { get; }

    /// <summary>Opens a package from a stream. The stream must be seekable.</summary>
    /// <param name="stream">The package stream.</param>
    /// <param name="leaveOpen">When <c>true</c>, the underlying stream is not disposed with the reader.</param>
    public static MapPackageReader Open(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek)
        {
            throw new ArgumentException("Stream must support seeking.", nameof(stream));
        }

        var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen);
        try
        {
            var manifest = ReadManifest(archive);
            return new MapPackageReader(archive, manifest, leaveOpen);
        }
        catch
        {
            archive.Dispose();
            throw;
        }
    }

    /// <summary>Opens a package from a file path.</summary>
    public static MapPackageReader OpenFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            return Open(fileStream, leaveOpen: false);
        }
        catch
        {
            fileStream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens an asset entry by its manifest path (e.g. <c>"assets/EncTerrain201.att"</c>).
    /// Throws <see cref="FileNotFoundException"/> if the entry is missing.
    /// Does NOT validate SHA-256 — call <see cref="Validate"/> first for full integrity checking,
    /// or use <see cref="ReadAsset"/> if you want hash validation on every read.
    /// </summary>
    public Stream OpenAsset(string relativePath)
    {
        this.ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(relativePath);

        var entry = this._archive.GetEntry(relativePath)
            ?? throw new FileNotFoundException($"Asset not found in package: {relativePath}", relativePath);

        return entry.Open();
    }

    /// <summary>
    /// Reads an asset's full content into memory and verifies its SHA-256 matches the manifest.
    /// Throws <see cref="InvalidDataException"/> on mismatch.
    /// </summary>
    public byte[] ReadAsset(string relativePath)
    {
        this.ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(relativePath);

        var assetEntry = this.Manifest.Assets.FirstOrDefault(a => a.Path == relativePath)
            ?? throw new FileNotFoundException($"Asset not declared in manifest: {relativePath}", relativePath);

        if (assetEntry.Bytes > MapPackageFormat.MaxAssetBytes)
        {
            throw new InvalidDataException(
                $"Asset '{relativePath}' declares size {assetEntry.Bytes} which exceeds the maximum {MapPackageFormat.MaxAssetBytes}.");
        }

        using var stream = this.OpenAsset(relativePath);
        using var buffer = new MemoryStream(capacity: checked((int)assetEntry.Bytes));
        stream.CopyTo(buffer);

        var bytes = buffer.ToArray();
        if (bytes.LongLength != assetEntry.Bytes)
        {
            throw new InvalidDataException(
                $"Asset '{relativePath}' has actual size {bytes.LongLength} but manifest declares {assetEntry.Bytes}.");
        }

        var actualHash = ComputeSha256Hex(bytes);
        if (!string.Equals(actualHash, assetEntry.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Asset '{relativePath}' hash mismatch — manifest says {assetEntry.Sha256}, actual {actualHash}.");
        }

        return bytes;
    }

    /// <summary>
    /// Runs all integrity checks (schema version, asset hashes, size caps, duplicate paths).
    /// Returns the list of problems found; an empty list means the package is acceptable.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        this.ThrowIfDisposed();
        var issues = new List<string>();

        if (this.Manifest.SchemaVersion < MapPackageFormat.MinimumSupportedSchemaVersion
            || this.Manifest.SchemaVersion > MapPackageFormat.CurrentSchemaVersion)
        {
            issues.Add($"Unsupported schema version {this.Manifest.SchemaVersion} (supported: {MapPackageFormat.MinimumSupportedSchemaVersion}..{MapPackageFormat.CurrentSchemaVersion}).");
        }

        if (string.IsNullOrWhiteSpace(this.Manifest.Map.Name))
        {
            issues.Add("Map name is empty.");
        }

        if (this.Manifest.Map.Number < 0)
        {
            issues.Add($"Map number {this.Manifest.Map.Number} is negative.");
        }

        if (this.Manifest.Assets.Count > MapPackageFormat.MaxAssetCount)
        {
            issues.Add($"Package declares {this.Manifest.Assets.Count} assets, exceeding the cap of {MapPackageFormat.MaxAssetCount}.");
        }

        var seenPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var asset in this.Manifest.Assets)
        {
            if (!seenPaths.Add(asset.Path))
            {
                issues.Add($"Duplicate asset path in manifest: {asset.Path}");
                continue;
            }

            if (!asset.Path.StartsWith(MapPackageFormat.AssetsPrefix, StringComparison.Ordinal))
            {
                issues.Add($"Asset path '{asset.Path}' must start with '{MapPackageFormat.AssetsPrefix}'.");
                continue;
            }

            if (ContainsTraversal(asset.Path))
            {
                issues.Add($"Asset path '{asset.Path}' contains path traversal.");
                continue;
            }

            if (asset.Bytes < 0 || asset.Bytes > MapPackageFormat.MaxAssetBytes)
            {
                issues.Add($"Asset '{asset.Path}' size {asset.Bytes} is outside [0, {MapPackageFormat.MaxAssetBytes}].");
                continue;
            }

            var entry = this._archive.GetEntry(asset.Path);
            if (entry is null)
            {
                issues.Add($"Asset '{asset.Path}' declared in manifest is missing from the archive.");
                continue;
            }

            if (entry.Length != asset.Bytes)
            {
                issues.Add($"Asset '{asset.Path}' archive size {entry.Length} disagrees with manifest size {asset.Bytes}.");
                continue;
            }

            try
            {
                using var stream = entry.Open();
                using var sha = SHA256.Create();
                var hashBytes = sha.ComputeHash(stream);
                var actualHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
                if (!string.Equals(actualHash, asset.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add($"Asset '{asset.Path}' hash mismatch — manifest {asset.Sha256}, actual {actualHash}.");
                }
            }
            catch (Exception ex)
            {
                issues.Add($"Failed to hash asset '{asset.Path}': {ex.Message}");
            }
        }

        return issues;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (this._disposed)
        {
            return;
        }

        this._disposed = true;
        if (!this._leaveOpen)
        {
            this._archive.Dispose();
        }
    }

    private static MapPackageManifest ReadManifest(ZipArchive archive)
    {
        var entry = archive.GetEntry(MapPackageFormat.ManifestEntryName)
            ?? throw new InvalidDataException($"Package is missing '{MapPackageFormat.ManifestEntryName}'.");

        if (entry.Length > MapPackageFormat.MaxManifestBytes)
        {
            throw new InvalidDataException(
                $"Manifest size {entry.Length} exceeds the maximum {MapPackageFormat.MaxManifestBytes}.");
        }

        using var stream = entry.Open();
        using var buffer = new MemoryStream(capacity: checked((int)entry.Length));
        stream.CopyTo(buffer);
        buffer.Position = 0;

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        var manifest = JsonSerializer.Deserialize<MapPackageManifest>(buffer, options)
            ?? throw new InvalidDataException("Manifest deserialised to null.");

        return manifest;
    }

    private static string ComputeSha256Hex(ReadOnlySpan<byte> data)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(data, hash);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool ContainsTraversal(string path)
    {
        return path.Contains("..", StringComparison.Ordinal)
            || path.Contains('\\', StringComparison.Ordinal)
            || Path.IsPathRooted(path);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);
    }
}
