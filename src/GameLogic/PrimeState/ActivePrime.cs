// <copyright file="ActivePrime.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PrimeState;

using MUnique.OpenMU.DataModel.Configuration;

/// <summary>
/// Represents a single active elemental prime mark on a target.
/// All expiry reads and writes are Interlocked to be safe across threads.
/// </summary>
internal sealed class ActivePrime
{
    private long _expiresAtMs;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActivePrime"/> class.
    /// </summary>
    /// <param name="element">The elemental affinity of the prime.</param>
    /// <param name="attacker">The attacker who applied the prime.</param>
    /// <param name="expiresAtMs">Absolute <see cref="Environment.TickCount64"/> value at expiry.</param>
    public ActivePrime(SkillComboElement element, IAttacker attacker, long expiresAtMs)
    {
        this.Element = element;
        this.Attacker = attacker;
        this._expiresAtMs = expiresAtMs;
    }

    /// <summary>Gets the elemental affinity of this prime.</summary>
    public SkillComboElement Element { get; }

    /// <summary>Gets the attacker who applied this prime.</summary>
    public IAttacker Attacker { get; }

    /// <summary>Gets the absolute <see cref="Environment.TickCount64"/> value at which this prime expires.</summary>
    public long ExpiresAtMs => Interlocked.Read(ref this._expiresAtMs);

    /// <summary>Gets a value indicating whether this prime has passed its expiry time.</summary>
    public bool IsExpired => Environment.TickCount64 >= Interlocked.Read(ref this._expiresAtMs);

    /// <summary>
    /// Resets the expiry window to <paramref name="newExpiresAtMs"/>.
    /// </summary>
    /// <param name="newExpiresAtMs">The new absolute expiry tick.</param>
    public void Refresh(long newExpiresAtMs) =>
        Interlocked.Exchange(ref this._expiresAtMs, newExpiresAtMs);
}
