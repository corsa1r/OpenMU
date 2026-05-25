// <copyright file="IShowMonsterLevelPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Views.World;

/// <summary>
/// View plug-in that pushes a monster's configured level to the client so it
/// can display it (e.g. as a "(level) Name" prefix on the floating health bar).
/// </summary>
/// <remarks>
/// Sent at scope-in for every NPC in <see cref="INewNpcsInScopePlugIn"/>, and
/// can also be re-broadcast at runtime when an admin-panel change updates a
/// monster's level — the client trusts the latest value it receives.
/// </remarks>
public interface IShowMonsterLevelPlugIn : IViewPlugIn
{
    /// <summary>Pushes the current <paramref name="level"/> of <paramref name="target"/> to the client.</summary>
    ValueTask ShowMonsterLevelAsync(IAttackable target, ushort level);
}
