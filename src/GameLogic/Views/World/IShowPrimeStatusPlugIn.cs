// <copyright file="IShowPrimeStatusPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Views.World;

using MUnique.OpenMU.DataModel.Configuration;

/// <summary>
/// View plug-in that notifies clients about elemental prime-mark state changes on a target.
/// Applied: the client should display a prime icon with a countdown timer over the target's health bar.
/// Cleared: the client should remove the icon (expiry or detonation).
/// </summary>
public interface IShowPrimeStatusPlugIn : IViewPlugIn
{
    /// <summary>Informs the client that <paramref name="target"/> received a prime mark.</summary>
    /// <param name="target">The entity that was primed.</param>
    /// <param name="element">The elemental affinity of the prime.</param>
    /// <param name="durationMs">Remaining duration in milliseconds (sent on both fresh apply and refresh).</param>
    ValueTask ShowPrimeAppliedAsync(IAttackable target, SkillComboElement element, int durationMs);

    /// <summary>Informs the client that the prime mark on <paramref name="target"/> is gone.</summary>
    ValueTask ShowPrimeClearedAsync(IAttackable target, SkillComboElement element);

    /// <summary>Tells the client to play the elemental detonation burst effect at <paramref name="target"/>'s position.</summary>
    ValueTask ShowDetonationAsync(IAttackable target, SkillComboElement element);
}
