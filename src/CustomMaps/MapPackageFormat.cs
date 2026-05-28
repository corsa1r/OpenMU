// <copyright file="MapPackageFormat.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.CustomMaps;

/// <summary>
/// Shared constants describing the on-disk layout of a <c>.bmap</c> map package.
/// A package is a ZIP archive with a single <see cref="ManifestEntryName"/> at the root
/// and binary asset entries under the <see cref="AssetsPrefix"/> directory.
/// </summary>
public static class MapPackageFormat
{
    /// <summary>The current schema version emitted by the writer. Bumped only on incompatible manifest changes.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>The minimum schema version the reader will accept.</summary>
    public const int MinimumSupportedSchemaVersion = 1;

    /// <summary>Name of the manifest entry at the ZIP root.</summary>
    public const string ManifestEntryName = "manifest.json";

    /// <summary>Directory prefix (inside the ZIP) for binary asset entries.</summary>
    public const string AssetsPrefix = "assets/";

    /// <summary>Default file extension for an exported package.</summary>
    public const string DefaultExtension = ".bmap";

    /// <summary>Hard cap on the manifest entry size when reading. Manifests are tiny JSON blobs;
    /// anything larger is almost certainly an attempt to OOM the importer.</summary>
    public const long MaxManifestBytes = 64 * 1024;

    /// <summary>Hard cap on any single asset entry. The largest legitimate asset
    /// (a fully populated <c>EncTerrain.obj</c>) tops out well under this.</summary>
    public const long MaxAssetBytes = 10L * 1024L * 1024L;

    /// <summary>Hard cap on the number of asset entries in one package.</summary>
    public const int MaxAssetCount = 64;

    /// <summary>Canonical filenames the reader/writer recognise. Anything else is ignored
    /// on read and rejected on write — keeps stray files from sneaking in.</summary>
    public static class AssetNames
    {
        /// <summary>Encrypted terrain attribute file. <c>{0}</c> = map number + 1 (matches client folder naming).</summary>
        public const string EncTerrainAttPattern = "EncTerrain{0}.att";

        /// <summary>Encrypted terrain object file.</summary>
        public const string EncTerrainObjPattern = "EncTerrain{0}.obj";

        /// <summary>Encrypted texture mapping file.</summary>
        public const string EncTerrainMapPattern = "EncTerrain{0}.map";

        /// <summary>Per-tile grass density mask.</summary>
        public const string EncTerrainGrassPattern = "EncTerrain{0}.grass";

        /// <summary>Legacy 8-bit heightmap.</summary>
        public const string TerrainHeight = "TerrainHeight.OZB";

        /// <summary>Vertex lighting JPEG.</summary>
        public const string TerrainLight = "TerrainLight.OZJ";

        /// <summary>Editor darkness mask (256×256 bytes, 0 = no darkening, 255 = fully black).
        /// Consumed by the client's lighting bake when re-baking; stored as an opaque asset
        /// server-side and shipped through as part of the .bmap round-trip.</summary>
        public const string TerrainDarkness = "Darkness.dat";

        /// <summary>Source bank manifest (mirrors the editor's local <c>sources.json</c>).</summary>
        public const string SourcesManifest = "sources.json";

        /// <summary>Per-source object stream directory prefix. <c>{0}</c> = source world folder index.</summary>
        public const string SourceObjDirPattern = "source_World{0}/";
    }
}
