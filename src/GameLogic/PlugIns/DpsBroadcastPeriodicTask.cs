// <copyright file="DpsBroadcastPeriodicTask.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns;

using System.Runtime.InteropServices;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Broadcasts each active player's current DPS to nearby observers once per second.
/// </summary>
[PlugIn]
[Guid("C1D2E3F4-A5B6-7890-CDEF-123456789ABC")]
public class DpsBroadcastPeriodicTask : IPeriodicTaskPlugIn
{
    /// <inheritdoc/>
    public async ValueTask ExecuteTaskAsync(GameContext gameContext)
    {
        var players = await gameContext.GetPlayersAsync().ConfigureAwait(false);
        foreach (var player in players)
        {
            try
            {
                await player.BroadcastDpsAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                player.Logger.LogError(ex, "Unexpected error when broadcasting DPS for player '{player}'.", player);
            }
        }
    }

    /// <inheritdoc/>
    public void ForceStart()
    {
    }
}
