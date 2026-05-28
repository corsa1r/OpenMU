// <copyright file="MapPackageExportService.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Web.AdminPanel.Services;

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MUnique.OpenMU.CustomMaps;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.Persistence;

/// <summary>
/// Exports a previously imported custom map back into a <c>.bmap</c> package — the inverse of
/// <see cref="MapPackageImportService"/>. Used for backups, copying between environments,
/// and sharing maps with other server operators.
/// </summary>
public sealed class MapPackageExportService
{
    private readonly IPersistenceContextProvider _contextProvider;
    private readonly MapAssetStore _assetStore;

    /// <summary>Initializes a new instance of the <see cref="MapPackageExportService"/> class.</summary>
    public MapPackageExportService(IPersistenceContextProvider contextProvider, MapAssetStore assetStore)
    {
        this._contextProvider = contextProvider;
        this._assetStore = assetStore;
    }

    /// <summary>
    /// Builds a package for the map identified by <paramref name="mapNumber"/> and
    /// <paramref name="discriminator"/>. Returns the package bytes ready for download.
    /// </summary>
    public async Task<byte[]> ExportAsync(short mapNumber, int discriminator = 0, CancellationToken cancellationToken = default)
    {
        using var context = this._contextProvider.CreateNewContext();
        var gameConfiguration = (await context.GetAsync<GameConfiguration>(cancellationToken).ConfigureAwait(false)).FirstOrDefault()
            ?? throw new InvalidOperationException("No GameConfiguration in DB — run Setup first.");
        var map = gameConfiguration.Maps.FirstOrDefault(m => m.Number == mapNumber && m.Discriminator == discriminator)
            ?? throw new InvalidOperationException($"No GameMapDefinition for Number={mapNumber}, Discriminator={discriminator}.");

        if (!map.IsCustomMap)
        {
            throw new InvalidOperationException($"Map {mapNumber} ({map.Name}) is not flagged as a custom map — only custom maps can be exported.");
        }

        if (!this._assetStore.MapExists(mapNumber))
        {
            throw new InvalidOperationException($"Map {mapNumber} has no asset files on disk.");
        }

        using var buffer = new MemoryStream();
        using (var writer = MapPackageWriter.Create(buffer, leaveOpen: true))
        {
            writer.Map = new MapPackageMapDefinition
            {
                Number = map.Number,
                Discriminator = map.Discriminator,
                Name = map.Name,
                IsCustomMap = true,
                ExpMultiplier = map.ExpMultiplier,
                SafezoneMapNumber = map.SafezoneMap?.Number,
            };

            // Spawn gate — pick the first IsSpawnGate=true gate on this map if any.
            var spawnGate = map.ExitGates.FirstOrDefault(g => g.IsSpawnGate);
            if (spawnGate is not null)
            {
                writer.SpawnGate = new MapPackageSpawnGate
                {
                    X1 = spawnGate.X1,
                    X2 = spawnGate.X2,
                    Y1 = spawnGate.Y1,
                    Y2 = spawnGate.Y2,
                    Direction = spawnGate.Direction.ToString(),
                };
            }

            // WarpInfo — pick the first warp targeting any gate on this map.
            var warp = gameConfiguration.WarpList.FirstOrDefault(
                w => w.Gate is not null && map.ExitGates.Contains(w.Gate));
            if (warp is not null)
            {
                writer.WarpInfo = new MapPackageWarpInfo
                {
                    Name = warp.Name,
                    Index = warp.Index,
                    LevelRequirement = warp.LevelRequirement,
                    Costs = warp.Costs,
                };
            }

            writer.CreatedBy = "admin-panel";
            writer.EditorVersion = "openmu-export";

            // Stream every asset file from disk into the package.
            foreach (var assetFileName in this._assetStore.ListAssetFileNames(mapNumber))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bytes = await this._assetStore.ReadAssetAsync(mapNumber, assetFileName, cancellationToken)
                    .ConfigureAwait(false);
                writer.AddAsset(MapPackageFormat.AssetsPrefix + assetFileName, bytes);
            }

            writer.Commit();
        }

        return buffer.ToArray();
    }

}
