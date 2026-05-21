// <copyright file="IShowDpsPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Views.World;

/// <summary>
/// Interface of a view whose implementation informs nearby observers about a player's DPS.
/// </summary>
public interface IShowDpsPlugIn : IViewPlugIn
{
    /// <summary>
    /// Shows the DPS of a player to the observing client.
    /// </summary>
    /// <param name="source">The player whose DPS is being reported.</param>
    /// <param name="dps">The current damage-per-second value.</param>
    ValueTask ShowDpsAsync(IIdentifiable source, uint dps);
}
