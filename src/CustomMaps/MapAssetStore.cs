// <copyright file="MapAssetStore.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.CustomMaps;

using System.IO;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Filesystem store for custom map asset files. Files are laid out as
/// <c>{root}/World{N+1}/{assetName}</c> matching the client convention
/// (so the client's <c>Data\World\Custom\WorldN+1\</c> can be bind-mounted
/// straight into the store's root for dev iteration).
/// </summary>
public sealed class MapAssetStore
{
    /// <summary>Environment variable that overrides the default root path. Used by the all-in-one container.</summary>
    public const string RootEnvironmentVariable = "CUSTOM_MAPS_PATH";

    /// <summary>Default root path on the server (inside the Docker container, this is a mounted volume).</summary>
    public const string DefaultRootPath = "/app/custom-maps";

    /// <summary>Initializes a new instance pointing at the configured root path.</summary>
    /// <param name="rootPath">Root directory holding per-map subdirectories. Created on first write if missing.</param>
    public MapAssetStore(string rootPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootPath);
        this.RootPath = rootPath;
    }

    /// <summary>Factory that reads <see cref="RootEnvironmentVariable"/>, falling back to <see cref="DefaultRootPath"/>.</summary>
    public static MapAssetStore FromEnvironment()
    {
        var envValue = Environment.GetEnvironmentVariable(RootEnvironmentVariable);
        return new MapAssetStore(string.IsNullOrWhiteSpace(envValue) ? DefaultRootPath : envValue);
    }

    /// <summary>Root directory of the store.</summary>
    public string RootPath { get; }

    /// <summary>Path of the subdirectory holding <paramref name="mapNumber"/>'s assets.</summary>
    public string GetMapDirectory(short mapNumber)
    {
        return Path.Combine(this.RootPath, $"World{mapNumber + 1}");
    }

    /// <summary>True if the map's directory exists and contains at least one asset file.</summary>
    public bool MapExists(short mapNumber)
    {
        var dir = this.GetMapDirectory(mapNumber);
        return Directory.Exists(dir) && Directory.EnumerateFiles(dir).Any();
    }

    /// <summary>Lists map numbers that have a directory under the root.</summary>
    public IEnumerable<short> ListInstalledMapNumbers()
    {
        if (!Directory.Exists(this.RootPath))
        {
            yield break;
        }

        foreach (var dir in Directory.EnumerateDirectories(this.RootPath))
        {
            var name = Path.GetFileName(dir);
            if (name.StartsWith("World", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(name.AsSpan(5), out var folderIndex)
                && folderIndex > 0
                && folderIndex <= short.MaxValue)
            {
                yield return (short)(folderIndex - 1);
            }
        }
    }

    /// <summary>Writes one asset file under the map's directory. Creates the directory if missing.</summary>
    /// <remarks>Writes to a <c>.tmp</c> sibling and renames in place, so a crashed write never leaves a partial file behind.</remarks>
    public async Task WriteAssetAsync(short mapNumber, string assetFileName, byte[] data, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(assetFileName);
        ArgumentNullException.ThrowIfNull(data);
        EnsureSafeFileName(assetFileName);

        var dir = this.GetMapDirectory(mapNumber);
        Directory.CreateDirectory(dir);

        var finalPath = Path.Combine(dir, assetFileName.Replace('/', Path.DirectorySeparatorChar));

        // Ensure the parent of finalPath exists (handles the "source_World33/EncTerrain.obj" case).
        var parent = Path.GetDirectoryName(finalPath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var tmpPath = finalPath + ".tmp";

        await File.WriteAllBytesAsync(tmpPath, data, cancellationToken).ConfigureAwait(false);

        if (File.Exists(finalPath))
        {
            File.Replace(tmpPath, finalPath, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tmpPath, finalPath);
        }
    }

    /// <summary>Reads one asset file. Throws <see cref="FileNotFoundException"/> if missing.</summary>
    public Task<byte[]> ReadAssetAsync(short mapNumber, string assetFileName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(assetFileName);
        EnsureSafeFileName(assetFileName);

        var path = Path.Combine(this.GetMapDirectory(mapNumber), assetFileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Asset '{assetFileName}' not found for map {mapNumber}.", path);
        }

        return File.ReadAllBytesAsync(path, cancellationToken);
    }

    /// <summary>Enumerates the asset filenames under the map's directory (just names, no paths).</summary>
    public IEnumerable<string> ListAssetFileNames(short mapNumber)
    {
        var dir = this.GetMapDirectory(mapNumber);
        if (!Directory.Exists(dir))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(dir))
        {
            yield return Path.GetFileName(file);
        }
    }

    /// <summary>Removes a map's directory and all of its files.</summary>
    public void DeleteMap(short mapNumber)
    {
        var dir = this.GetMapDirectory(mapNumber);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void EnsureSafeFileName(string fileName)
    {
        // Allow a single forward-slash for the per-source subdir naming (e.g. "source_World33/EncTerrain201.obj"),
        // but reject backslashes, path traversal, and absolute paths.
        if (fileName.Contains("..", StringComparison.Ordinal)
            || fileName.Contains('\\', StringComparison.Ordinal)
            || Path.IsPathRooted(fileName))
        {
            throw new ArgumentException($"Unsafe asset file name: '{fileName}'", nameof(fileName));
        }

        // Cap nesting depth at 1 (root or one subdir).
        if (fileName.Count(c => c == '/') > 1)
        {
            throw new ArgumentException($"Asset file name nests too deeply: '{fileName}'", nameof(fileName));
        }
    }
}
