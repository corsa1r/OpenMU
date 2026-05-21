// <copyright file="PrimeStateContainer.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PrimeState;

using System.Collections.Concurrent;
using MUnique.OpenMU.DataModel.Configuration;

/// <summary>
/// Tracks all active elemental prime marks on a single <see cref="IAttackable"/>.
/// Operations are lock-free via <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// </summary>
internal sealed class PrimeStateContainer
{
    private readonly ConcurrentDictionary<SkillComboElement, ActivePrime> _primes = new();

    /// <summary>
    /// Applies a fresh prime or refreshes the expiry window if the same element is already active.
    /// Returns the absolute expiry tick used, suitable for handing to <see cref="PrimeExpirationScheduler"/>.
    /// </summary>
    public long ApplyOrRefresh(SkillComboElement element, IAttacker attacker, int durationMs)
    {
        var expiresAtMs = Environment.TickCount64 + durationMs;

        _primes.AddOrUpdate(
            element,
            addValueFactory: _ => new ActivePrime(element, attacker, expiresAtMs),
            updateValueFactory: (_, existing) =>
            {
                existing.Refresh(expiresAtMs);
                return existing;
            });

        return expiresAtMs;
    }

    /// <summary>
    /// Atomically removes and returns the prime for <paramref name="element"/> if one exists.
    /// The caller takes ownership and must handle notification; returns <c>null</c> when absent.
    /// </summary>
    public ActivePrime? Consume(SkillComboElement element) =>
        _primes.TryRemove(element, out var prime) ? prime : null;

    /// <summary>
    /// Called by <see cref="PrimeExpirationScheduler"/> on timeout.
    /// Only removes the prime when its stored expiry exactly matches the scheduled ticket,
    /// providing correct lazy-deletion semantics after any mid-life refresh.
    /// </summary>
    public bool TryExpire(SkillComboElement element, long scheduledExpiresAtMs)
    {
        if (!_primes.TryGetValue(element, out var prime))
        {
            return false;
        }

        // The prime was refreshed since we queued this expiry ticket — leave it alive.
        if (prime.ExpiresAtMs != scheduledExpiresAtMs)
        {
            return false;
        }

        return _primes.TryRemove(element, out _);
    }

    public bool HasActivePrime(SkillComboElement element) =>
        _primes.TryGetValue(element, out var p) && !p.IsExpired;
}
