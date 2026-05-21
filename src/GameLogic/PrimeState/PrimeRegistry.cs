// <copyright file="PrimeRegistry.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PrimeState;

using System.Runtime.CompilerServices;
using MUnique.OpenMU.DataModel.Configuration;

/// <summary>
/// Global weak-keyed registry that attaches a <see cref="PrimeStateContainer"/> to any
/// <see cref="IAttackable"/> instance without modifying the class hierarchy.
/// The weak reference ensures containers are GC'd when their owner is collected.
/// </summary>
public static class PrimeRegistry
{
    // ConditionalWeakTable is thread-safe and does not prevent GC of the key.
    private static readonly ConditionalWeakTable<IAttackable, PrimeStateContainer> Table = new();

    /// <summary>Returns the (lazily created) state container for <paramref name="target"/>.</summary>
    internal static PrimeStateContainer GetOrCreate(IAttackable target) =>
        Table.GetOrCreateValue(target);

    /// <summary>Returns <c>true</c> when <paramref name="element"/> has an un-expired prime on <paramref name="target"/>.</summary>
    public static bool HasActivePrime(IAttackable target, SkillComboElement element) =>
        Table.TryGetValue(target, out var container) && container.HasActivePrime(element);
}
