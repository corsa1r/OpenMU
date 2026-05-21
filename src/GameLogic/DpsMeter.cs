// <copyright file="DpsMeter.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic;

/// <summary>
/// Tracks damage-per-second for a player using a rolling time window.
/// </summary>
public sealed class DpsMeter
{
    private readonly Queue<(long Timestamp, uint Damage)> _window = new();
    private readonly object _lock = new();
    private const long WindowMs = 5000;
    private uint _lastBroadcast;

    /// <summary>
    /// Records damage dealt at the current moment.
    /// </summary>
    public void RecordDamage(uint damage)
    {
        var now = Environment.TickCount64;
        lock (this._lock)
        {
            this._window.Enqueue((now, damage));
            this.PurgeOldEntries(now);
        }
    }

    /// <summary>
    /// Gets the current DPS and returns <c>true</c> if it has changed enough to warrant a broadcast.
    /// </summary>
    public bool TryGetUpdatedDps(out uint dps)
    {
        lock (this._lock)
        {
            this.PurgeOldEntries(Environment.TickCount64);
            var total = 0L;
            foreach (var (_, damage) in this._window)
                total += damage;

            dps = this._window.Count == 0 ? 0 : (uint)(total * 1000 / WindowMs);
        }

        if (dps == this._lastBroadcast)
            return false;

        if (dps > 0 && Math.Abs((long)dps - this._lastBroadcast) < 50)
            return false;

        this._lastBroadcast = dps;
        return true;
    }

    private void PurgeOldEntries(long now)
    {
        while (this._window.TryPeek(out var entry) && now - entry.Timestamp > WindowMs)
            this._window.Dequeue();
    }
}
