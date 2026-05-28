// <copyright file="MapPackageManifest.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.CustomMaps;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Lenient <see cref="DateTimeOffset"/>? deserialiser: treats <c>null</c> or empty string as <c>null</c>
/// instead of throwing. Older / minimal exporters may emit either form for an unknown timestamp.
/// </summary>
internal sealed class LenientDateTimeOffsetConverter : JsonConverter<DateTimeOffset?>
{
    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            if (DateTimeOffset.TryParse(text, out var parsed))
            {
                return parsed;
            }

            return null;
        }

        return null;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value.Value.ToString("o"));
        }
    }
}

/// <summary>
/// In-memory representation of a map package's <c>manifest.json</c>.
/// </summary>
/// <remarks>
/// The package format is designed so that the same JSON shape can be authored by the C++ editor
/// and consumed by the C# server. JSON property names are camelCase to match common web conventions.
/// </remarks>
public sealed class MapPackageManifest
{
    /// <summary>Schema version. Reader rejects packages with versions outside the supported range.</summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = MapPackageFormat.CurrentSchemaVersion;

    /// <summary>Authoring metadata (who/when/with what made this package).</summary>
    [JsonPropertyName("package")]
    public MapPackageInfo Package { get; set; } = new();

    /// <summary>Map identity (Number, Name, IsCustomMap, etc.).</summary>
    [JsonPropertyName("map")]
    public MapPackageMapDefinition Map { get; set; } = new();

    /// <summary>
    /// Suggested spawn gate to create on first import. Ignored on re-import — the admin panel
    /// owns gate coordinates once the map exists in the DB.
    /// </summary>
    [JsonPropertyName("spawnGate")]
    public MapPackageSpawnGate? SpawnGate { get; set; }

    /// <summary>
    /// Suggested warp-list entry to create on first import. Ignored on re-import — admin owns
    /// level/cost tuning once the map exists.
    /// </summary>
    [JsonPropertyName("warpInfo")]
    public MapPackageWarpInfo? WarpInfo { get; set; }

    /// <summary>List of asset entries packaged alongside this manifest. Each has a SHA-256 the
    /// reader verifies before accepting the package.</summary>
    [JsonPropertyName("assets")]
    public List<MapPackageAsset> Assets { get; set; } = new();
}

/// <summary>Authoring metadata block.</summary>
public sealed class MapPackageInfo
{
    /// <summary>UTC timestamp of export. Nullable — older / minimal exporters may omit it
    /// or emit an empty string; the lenient converter normalises both to <c>null</c>.</summary>
    [JsonPropertyName("createdAt")]
    [JsonConverter(typeof(LenientDateTimeOffsetConverter))]
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Free-form identifier of the human who exported (login name, display name, etc.). Optional.</summary>
    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; set; }

    /// <summary>Version string of the tool that produced the package. Optional, informational.</summary>
    [JsonPropertyName("editorVersion")]
    public string? EditorVersion { get; set; }
}

/// <summary>Map identity carried in the package — seeds the corresponding DB row on first import.</summary>
public sealed class MapPackageMapDefinition
{
    /// <summary>The <c>GameMapDefinition.Number</c> this package represents.</summary>
    [JsonPropertyName("number")]
    public short Number { get; set; }

    /// <summary>The <c>GameMapDefinition.Discriminator</c> (0 for the standard slot).</summary>
    [JsonPropertyName("discriminator")]
    public int Discriminator { get; set; }

    /// <summary>The map's display name. Seeds <c>GameMapDefinition.Name</c> on first import.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Always <c>true</c> for editor-authored maps. Seeds <c>GameMapDefinition.IsCustomMap</c>.</summary>
    [JsonPropertyName("isCustomMap")]
    public bool IsCustomMap { get; set; } = true;

    /// <summary>Per-map experience multiplier. Defaults to <c>1.0</c>.</summary>
    [JsonPropertyName("expMultiplier")]
    public double ExpMultiplier { get; set; } = 1.0;

    /// <summary>
    /// Map Number of the safezone to use for respawn. <c>null</c> means "use this map's own
    /// spawn gate" (typical for towns/safe maps) — the importer falls back to Lorencia for
    /// hostile maps that don't carry their own safezone.
    /// </summary>
    [JsonPropertyName("safezoneMapNumber")]
    public short? SafezoneMapNumber { get; set; }
}

/// <summary>Seed values for an <c>ExitGate</c> row created on first import.</summary>
public sealed class MapPackageSpawnGate
{
    /// <summary>Minimum X coordinate of the spawn rectangle (0..255).</summary>
    [JsonPropertyName("x1")]
    public byte X1 { get; set; }

    /// <summary>Maximum X coordinate of the spawn rectangle (0..255).</summary>
    [JsonPropertyName("x2")]
    public byte X2 { get; set; }

    /// <summary>Minimum Y coordinate of the spawn rectangle (0..255).</summary>
    [JsonPropertyName("y1")]
    public byte Y1 { get; set; }

    /// <summary>Maximum Y coordinate of the spawn rectangle (0..255).</summary>
    [JsonPropertyName("y2")]
    public byte Y2 { get; set; }

    /// <summary>String form of <c>MUnique.OpenMU.GameLogic.Direction</c> — case-insensitive on import.</summary>
    [JsonPropertyName("direction")]
    public string Direction { get; set; } = "Undefined";
}

/// <summary>Seed values for a <c>WarpInfo</c> row created on first import.</summary>
public sealed class MapPackageWarpInfo
{
    /// <summary>Stable identifier name for the warp (matches <c>WarpInfo.Name</c>).</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Explicit warp-list index. <c>null</c> means "auto-assign the next free index" at import time.
    /// Set this only if you need to pin the index for a known client BMD.
    /// </summary>
    [JsonPropertyName("index")]
    public int? Index { get; set; }

    /// <summary>Minimum character level required to use this warp.</summary>
    [JsonPropertyName("levelRequirement")]
    public int LevelRequirement { get; set; }

    /// <summary>Zen cost charged on use.</summary>
    [JsonPropertyName("costs")]
    public int Costs { get; set; }
}

/// <summary>Describes one binary asset entry inside the package.</summary>
public sealed class MapPackageAsset
{
    /// <summary>Path of the entry inside the ZIP, including the <see cref="MapPackageFormat.AssetsPrefix"/>.</summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>Lowercase hexadecimal SHA-256 of the entry's raw bytes. The reader rejects packages where this doesn't match.</summary>
    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Uncompressed byte length of the entry. Cross-checked against the actual stream length on read.</summary>
    [JsonPropertyName("bytes")]
    public long Bytes { get; set; }
}
