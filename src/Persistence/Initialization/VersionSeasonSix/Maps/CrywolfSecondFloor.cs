// <copyright file="CrywolfSecondFloor.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.Initialization.VersionSeasonSix.Maps;

using MUnique.OpenMU.DataModel.Configuration;

/// <summary>
/// The initialization for the Crywolf 2nd Floor map (client constant WD_35CRYWOLF_2ND).
/// </summary>
/// <remarks>
/// Bare-bones map shell — no spawns, no monsters, no exit gates yet.
/// To make the map playable, add at least one <see cref="ExitGate"/> with
/// <c>IsSpawnGate=true</c> and populate <see cref="CreateMonsterSpawns"/>.
/// A <c>Resources/Terrain35.att</c> embedded resource is required for proper
/// terrain attributes; without it, <c>UpdateTerrainFromResources</c> falls back
/// to a nearby map's terrain.
/// </remarks>
internal class CrywolfSecondFloor : BaseMapInitializer
{
    internal const byte Number = 35;
    internal const string Name = "Crywolf 2nd Floor";

    public CrywolfSecondFloor(IContext context, GameConfiguration gameConfiguration)
        : base(context, gameConfiguration)
    {
    }

    /// <inheritdoc/>
    protected override byte MapNumber => Number;

    /// <inheritdoc/>
    protected override string MapName => Name;
}
