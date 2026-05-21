// <copyright file="PrimeExpirationScheduler.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PrimeState;

using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic.Views.World;

/// <summary>
/// Centralized, single-timer expiration wheel for all active prime marks.
/// Uses a min-heap (<see cref="PriorityQueue{TElement,TPriority}"/>) with lazy deletion so
/// refreshed primes never spawn additional timers — only one <see cref="System.Threading.Timer"/>
/// is ever alive regardless of how many primes are active.
/// </summary>
public sealed class PrimeExpirationScheduler : IDisposable
{
    /// <summary>The process-wide singleton instance.</summary>
    public static readonly PrimeExpirationScheduler Instance = new();

    private readonly PriorityQueue<ExpirationEntry, long> _queue = new();
    private readonly object _syncRoot = new();
    private readonly System.Threading.Timer _timer;
    private bool _disposed;

    private PrimeExpirationScheduler() =>
        _timer = new System.Threading.Timer(OnTimerFired, null, Timeout.Infinite, Timeout.Infinite);

    // ── Internal entry type ───────────────────────────────────────────────

    private readonly record struct ExpirationEntry(
        IAttackable Target,
        SkillComboElement Element,
        // Captured at schedule time; used for lazy-deletion matching in TryExpire.
        long ScheduledExpiresAtMs);

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Schedules (or re-schedules after a refresh) the expiration of a prime mark.
    /// <paramref name="expiresAtMs"/> must be the exact value returned by
    /// <see cref="PrimeStateContainer.ApplyOrRefresh"/> for lazy deletion to work correctly.
    /// Thread-safe.
    /// </summary>
    public void Schedule(IAttackable target, SkillComboElement element, long expiresAtMs)
    {
        lock (_syncRoot)
        {
            _queue.Enqueue(new ExpirationEntry(target, element, expiresAtMs), expiresAtMs);
            ArmTimer();
        }
    }

    // ── Private machinery ─────────────────────────────────────────────────

    /// <summary>Arms the timer to fire at the earliest pending expiry. Must be called under <see cref="_syncRoot"/>.</summary>
    private void ArmTimer()
    {
        if (!_queue.TryPeek(out _, out var nextMs))
        {
            return;
        }

        var delayMs = Math.Max(0L, nextMs - Environment.TickCount64);
        _timer.Change(delayMs, Timeout.Infinite);
    }

    private void OnTimerFired(object? _)
    {
        var now = Environment.TickCount64;
        List<(IAttackable Target, SkillComboElement Element)>? expired = null;

        lock (_syncRoot)
        {
            while (_queue.TryPeek(out var entry, out var dueMs) && dueMs <= now)
            {
                _queue.Dequeue();
                var container = PrimeRegistry.GetOrCreate(entry.Target);

                if (container.TryExpire(entry.Element, entry.ScheduledExpiresAtMs))
                {
                    expired ??= new List<(IAttackable, SkillComboElement)>();
                    expired.Add((entry.Target, entry.Element));
                }
            }

            ArmTimer();
        }

        // Dispatch client notifications outside the lock on the thread-pool.
        if (expired is not null)
        {
            _ = SendExpiredNotificationsAsync(expired);
        }
    }

    private static async Task SendExpiredNotificationsAsync(
        List<(IAttackable Target, SkillComboElement Element)> expired)
    {
        foreach (var (target, element) in expired)
        {
            if (target is IObservable observable)
            {
                await observable.ForEachWorldObserverAsync<IShowPrimeStatusPlugIn>(
                    p => p.ShowPrimeClearedAsync(target, element),
                    includeThis: true).ConfigureAwait(false);
            }
        }
    }

    // ── IDisposable ───────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Dispose();
    }
}
