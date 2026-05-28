// <copyright file="AddCrywolfSecondFloorPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.Initialization.Updates;

using System.Runtime.InteropServices;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.Persistence.Initialization.VersionSeasonSix.Maps;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Adds the Crywolf 2nd Floor map (Number 35) to an already-initialized
/// Season Six database. The client expects this map slot via
/// <c>WD_35CRYWOLF_2ND</c> but earlier seeds skipped it.
/// </summary>
[PlugIn]
[Display(Name = PlugInName, Description = PlugInDescription)]
[Guid("E7A4F3D2-1B5C-4F8A-9C61-7D2E3A4B5C6D")]
public class AddCrywolfSecondFloorPlugIn : UpdatePlugInBase
{
    internal const string PlugInName = "Add Crywolf 2nd Floor";

    internal const string PlugInDescription =
        "Adds the Crywolf 2nd Floor map (Number 35) which the client expects but the seed skipped.";

    /// <inheritdoc />
    public override UpdateVersion Version => UpdateVersion.AddCrywolfSecondFloor;

    /// <inheritdoc />
    public override string DataInitializationKey => VersionSeasonSix.DataInitialization.Id;

    /// <inheritdoc />
    public override string Name => PlugInName;

    /// <inheritdoc />
    public override string Description => PlugInDescription;

    /// <inheritdoc />
    public override bool IsMandatory => true;

    /// <inheritdoc />
    public override DateTime CreatedAt => new(2026, 05, 28, 12, 0, 0, DateTimeKind.Utc);

    /// <inheritdoc />
    protected override async ValueTask ApplyAsync(IContext context, GameConfiguration gameConfiguration)
    {
        if (gameConfiguration.Maps.Any(m => m.Number == CrywolfSecondFloor.Number))
        {
            return;
        }

        var initializer = new CrywolfSecondFloor(context, gameConfiguration);
        initializer.Initialize();
        initializer.SetSafezoneMap();

        var newMap = gameConfiguration.Maps.First(m => m.Number == CrywolfSecondFloor.Number);

        var serverConfigurations = await context.GetAsync<GameServerConfiguration>().ConfigureAwait(false);
        foreach (var serverConfig in serverConfigurations)
        {
            if (!serverConfig.Maps.Any(m => m.Number == CrywolfSecondFloor.Number))
            {
                serverConfig.Maps.Add(newMap);
            }
        }
    }
}
