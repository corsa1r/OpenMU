// <copyright file="MapPackageImportService.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Web.AdminPanel.Services;

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MUnique.OpenMU.CustomMaps;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.Persistence;

/// <summary>
/// Imports a <c>.bmap</c> map package: writes asset files to the filesystem and seeds DB rows
/// on first import (per the conflict policy where the admin panel owns level/cost/spawn-coords
/// once the map exists, and re-imports only refresh asset files).
/// </summary>
public sealed class MapPackageImportService
{
    private readonly IPersistenceContextProvider _contextProvider;
    private readonly MapAssetStore _assetStore;
    private readonly IDictionary<int, MUnique.OpenMU.Interfaces.IGameServer> _gameServers;
    private readonly IDataSource<GameConfiguration> _gameConfigurationSource;

    /// <summary>Initializes a new instance of the <see cref="MapPackageImportService"/> class.</summary>
    public MapPackageImportService(
        IPersistenceContextProvider contextProvider,
        MapAssetStore assetStore,
        IDictionary<int, MUnique.OpenMU.Interfaces.IGameServer> gameServers,
        IDataSource<GameConfiguration> gameConfigurationSource)
    {
        this._contextProvider = contextProvider;
        this._assetStore = assetStore;
        this._gameServers = gameServers;
        this._gameConfigurationSource = gameConfigurationSource;
    }

    /// <summary>Imports a map package from a stream.</summary>
    /// <param name="packageStream">The uploaded package. Must be seekable.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The import result describing what happened.</returns>
    public async Task<MapPackageImportResult> ImportAsync(Stream packageStream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packageStream);

        using var reader = MapPackageReader.Open(packageStream, leaveOpen: true);

        var issues = reader.Validate();
        if (issues.Count > 0)
        {
            return MapPackageImportResult.Failed(
                $"Package validation failed: {string.Join("; ", issues)}");
        }

        var manifest = reader.Manifest;

        if (!manifest.Map.IsCustomMap)
        {
            return MapPackageImportResult.Failed("Package's IsCustomMap is false — only custom-map packages are supported.");
        }

        // Same pattern as DataUpdateService.ApplyUpdatesAsync: create one context, load the
        // GameConfiguration through it, and save through the same context. Cross-context
        // sharing was making EF treat already-persisted child rows (DropItemGroupItemDefinition
        // join entries) as fresh inserts → duplicate-key violations.
        using var context = this._contextProvider.CreateNewContext();
        var gameConfiguration = (await context.GetAsync<GameConfiguration>(cancellationToken).ConfigureAwait(false)).FirstOrDefault()
            ?? throw new InvalidOperationException("No GameConfiguration in DB — run Setup first.");

        var existing = gameConfiguration.Maps.FirstOrDefault(
            m => m.Number == manifest.Map.Number && m.Discriminator == manifest.Map.Discriminator);

        bool isCreate = existing is null;

        if (existing is { IsCustomMap: false })
        {
            return MapPackageImportResult.Failed(
                $"Map Number {manifest.Map.Number} already exists and is not a custom map. Choose a different Number.");
        }

        // 1. Write asset files first. If anything fails, DB is untouched. Stash the .att
        // bytes as we go so we can decode them into the OpenMU TerrainData column without
        // re-reading from disk.
        var assetWrites = new List<string>();
        byte[]? encryptedAttBytes = null;
        var expectedAttFileName = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            MapPackageFormat.AssetNames.EncTerrainAttPattern,
            manifest.Map.Number + 1);

        foreach (var asset in manifest.Assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = reader.ReadAsset(asset.Path);
            var fileName = asset.Path[MapPackageFormat.AssetsPrefix.Length..]; // strip "assets/"
            await this._assetStore.WriteAssetAsync(manifest.Map.Number, fileName, bytes, cancellationToken)
                .ConfigureAwait(false);
            assetWrites.Add(fileName);

            if (string.Equals(fileName, expectedAttFileName, StringComparison.OrdinalIgnoreCase))
            {
                encryptedAttBytes = bytes;
            }
        }

        // 2. Decode the .att into OpenMU's TerrainData byte layout so server-side movement
        // validation respects the user's painted walls / safezones. Falls back to keeping
        // TerrainData untouched if the .att is missing or malformed (the default-walkable
        // fallback in GameMapTerrain takes over).
        byte[]? terrainData = null;
        if (encryptedAttBytes is { Length: AttFileDecoder.ExpectedEncryptedSize })
        {
            try
            {
                terrainData = AttFileDecoder.DecodeToOpenMuTerrainData(encryptedAttBytes);
            }
            catch
            {
                terrainData = null;
            }
        }

        // 3. Create or refresh DB rows.
        GameMapDefinition mapDefinition;
        if (isCreate)
        {
            mapDefinition = await this.CreateMapDefinitionAsync(context, gameConfiguration, manifest, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            mapDefinition = existing!;
            // Update only asset-derived fields. Admin owns the rest (Name, LevelReq, etc.).
        }

        // TerrainData is asset-derived (comes from the .att) — refresh on every import,
        // including re-imports. This is the one DB field that re-import is allowed to
        // overwrite, since the admin panel can't edit it directly.
        if (terrainData is not null)
        {
            mapDefinition.TerrainData = terrainData;
        }

        bool saved;
        try
        {
            saved = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Surface the real reason — EF wraps the underlying DB / FK / null-violation error
            // in InnerException. The outer "An error occurred while saving the entity changes" message
            // is useless on its own.
            var deepest = ex;
            while (deepest.InnerException is { } inner)
            {
                deepest = inner;
            }

            return MapPackageImportResult.Failed(
                $"SaveChanges failed: {deepest.GetType().Name}: {deepest.Message}");
        }

        if (!saved)
        {
            // The asset files are already on disk. They'll be overwritten on next successful import,
            // but the partial state is harmless until then.
            return MapPackageImportResult.Failed("Failed to persist DB changes after writing assets.");
        }

        // The admin panel uses a long-lived singleton IDataSource<GameConfiguration> that
        // caches the entire object graph in memory. SaveChangesAsync above wrote through a
        // separate context, so the cached singleton still holds the pre-import state —
        // the Map editor in the admin panel would render TerrainData as null and show all
        // tiles as walkable. Force the singleton to reload from the DB on next access.
        try
        {
            await this._gameConfigurationSource.ForceDiscardChangesAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best-effort cache invalidation; if it fails, a manual admin-panel refresh
            // will still pick up the changes from the DB.
        }

        // Hot-reload the map on every running game server so live GameMap instances pick up
        // the new TerrainData / display name / safezone immediately. Players currently on the
        // map get warped back to their safezone (the GameMap they were on is torn down).
        int playersKicked = 0;
        var mapId = (ushort)manifest.Map.Number;
        foreach (var server in this._gameServers.Values)
        {
            if (server is IGameServerContextProvider provider)
            {
                try
                {
                    playersKicked += await provider.Context.ReloadMapAsync(mapId).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort reload — a broken game server shouldn't fail the import.
                    // Worst case the operator has to restart manually.
                }
            }
        }

        return new MapPackageImportResult
        {
            Success = true,
            IsCreate = isCreate,
            MapNumber = manifest.Map.Number,
            MapName = mapDefinition.Name,
            AssetsWritten = assetWrites,
            PlayersWarpedToSafezone = playersKicked,
        };
    }

    private async Task<GameMapDefinition> CreateMapDefinitionAsync(
        IContext context,
        GameConfiguration gameConfiguration,
        MapPackageManifest manifest,
        CancellationToken cancellationToken)
    {
        // CreateNew<T> assigns a fresh Guid through IIdentifiable.Id, so we don't need
        // a deterministic GUID here — the map is a user-driven row, not a seed-time idempotent one.
        var map = context.CreateNew<GameMapDefinition>();
        map.Number = manifest.Map.Number;
        map.Discriminator = manifest.Map.Discriminator;
        map.Name = manifest.Map.Name;
        map.IsCustomMap = true;
        map.ExpMultiplier = manifest.Map.ExpMultiplier;

        gameConfiguration.Maps.Add(map);

        // Spawn gate (optional in manifest).
        ExitGate? spawnGate = null;
        if (manifest.SpawnGate is { } gateSpec)
        {
            spawnGate = context.CreateNew<ExitGate>();
            spawnGate.X1 = gateSpec.X1;
            spawnGate.X2 = gateSpec.X2;
            spawnGate.Y1 = gateSpec.Y1;
            spawnGate.Y2 = gateSpec.Y2;
            spawnGate.IsSpawnGate = true;
            spawnGate.Direction = Enum.TryParse<Direction>(gateSpec.Direction, ignoreCase: true, out var d)
                ? d
                : Direction.Undefined;
            spawnGate.Map = map;
            map.ExitGates.Add(spawnGate);
        }

        // Safezone map — point to self (so death respawns on this map) if not specified.
        var safezoneNumber = manifest.Map.SafezoneMapNumber ?? manifest.Map.Number;
        map.SafezoneMap = gameConfiguration.Maps.FirstOrDefault(m => m.Number == safezoneNumber) ?? map;

        // WarpInfo (optional in manifest).
        if (manifest.WarpInfo is { } warpSpec && spawnGate is not null)
        {
            var warpInfo = context.CreateNew<WarpInfo>();
            warpInfo.Name = warpSpec.Name;
            warpInfo.LevelRequirement = warpSpec.LevelRequirement;
            warpInfo.Costs = warpSpec.Costs;
            warpInfo.Index = warpSpec.Index ?? this.AssignNextWarpIndex(gameConfiguration);
            warpInfo.Gate = spawnGate;
            gameConfiguration.WarpList.Add(warpInfo);
        }

        // Add to every GameServerConfiguration so the map is actually hosted.
        var serverConfigurations = await context.GetAsync<GameServerConfiguration>(cancellationToken).ConfigureAwait(false);
        foreach (var serverConfig in serverConfigurations)
        {
            if (!serverConfig.Maps.Any(m => m.Number == map.Number && m.Discriminator == map.Discriminator))
            {
                serverConfig.Maps.Add(map);
            }
        }

        return map;
    }

    private int AssignNextWarpIndex(GameConfiguration gameConfiguration)
    {
        var max = 0;
        foreach (var w in gameConfiguration.WarpList)
        {
            if (w.Index > max)
            {
                max = w.Index;
            }
        }

        return max + 1;
    }
}

/// <summary>Result of <see cref="MapPackageImportService.ImportAsync"/>.</summary>
public sealed class MapPackageImportResult
{
    /// <summary>Whether the import completed.</summary>
    public bool Success { get; init; }

    /// <summary>True when the map was newly created; false when an existing map was updated.</summary>
    public bool IsCreate { get; init; }

    /// <summary>The map number that was imported.</summary>
    public short MapNumber { get; init; }

    /// <summary>The map display name.</summary>
    public string MapName { get; init; } = string.Empty;

    /// <summary>Asset filenames written to disk (relative to the map's directory).</summary>
    public IReadOnlyList<string> AssetsWritten { get; init; } = Array.Empty<string>();

    /// <summary>Error message when <see cref="Success"/> is false.</summary>
    public string? Error { get; init; }

    /// <summary>How many players were warped to their safezone because they were on this map when the live GameMap got torn down for reload.</summary>
    public int PlayersWarpedToSafezone { get; init; }

    /// <summary>Constructs a failed result.</summary>
    public static MapPackageImportResult Failed(string message) => new() { Success = false, Error = message };
}
