// <copyright file="ComboProcessor.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PrimeState;

using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.Views.World;

/// <summary>
/// Stateless helper that implements the Primer/Detonator pipeline.
/// Call <see cref="ProcessHitAsync"/> immediately after any successful
/// <see cref="IAttackable.AttackByAsync"/> that may involve a combo-typed skill.
/// </summary>
public static class ComboProcessor
{
    /// <summary>
    /// Returns <c>true</c> when <paramref name="skill"/> is a Detonator and
    /// <paramref name="target"/> currently carries a matching primer — i.e. the
    /// upcoming hit would consume the primer and fire the detonation bonus.
    /// Callers use this to decide whether to skip the skill's normal cast hit
    /// (so only the multiplied detonation damage is dealt, not cast + bonus).
    /// </summary>
    public static bool WillDetonate(IAttackable target, Skill? skill)
    {
        return skill is { ComboType: SkillComboType.Detonator }
               && target.IsAlive
               && PrimeRegistry.HasActivePrime(target, skill.ComboElement);
    }

    /// <summary>
    /// Evaluates whether the skill carried by <paramref name="skillEntry"/> triggers
    /// Primer or Detonator behaviour and acts accordingly.
    /// No-ops instantly when <see cref="Skill.ComboType"/> is <see cref="SkillComboType.None"/>.
    /// </summary>
    /// <param name="attacker">The attacking player.</param>
    /// <param name="target">The entity that was hit.</param>
    /// <param name="skillEntry">The skill used in the hit.</param>
    /// <param name="hitInfo">The confirmed hit result; must not be a miss.</param>
    public static async ValueTask ProcessHitAsync(
        Player attacker,
        IAttackable target,
        SkillEntry skillEntry,
        HitInfo hitInfo)
    {
        var skill = skillEntry.Skill;

        if (skill is null || skill.ComboType == SkillComboType.None)
        {
            return;
        }

        switch (skill.ComboType)
        {
            case SkillComboType.Primer:
                await ApplyPrimerAsync(attacker, target, skill).ConfigureAwait(false);
                break;

            case SkillComboType.Detonator:
                await TryDetonateAsync(attacker, target, skillEntry, skill, hitInfo).ConfigureAwait(false);
                break;
        }
    }

    // ── Primer path ───────────────────────────────────────────────────────

    private static async ValueTask ApplyPrimerAsync(Player attacker, IAttackable target, Skill skill)
    {
        // Don't apply primers to corpses — IAttackable references linger
        // briefly after death (despawn delay, loot drop) and the registry's
        // ConditionalWeakTable keeps state alive as long as the ref does, so
        // a player could otherwise prime a corpse and detonate it.
        if (!target.IsAlive)
        {
            return;
        }

        var container = PrimeRegistry.GetOrCreate(target);
        var expiresAtMs = container.ApplyOrRefresh(skill.ComboElement, attacker, skill.PrimeDurationMs);

        // Register (or re-register after a refresh) with the global expiry wheel.
        PrimeExpirationScheduler.Instance.Schedule(target, skill.ComboElement, expiresAtMs);

        // Notify all nearby observers: prime icon should appear / countdown reset.
        await NotifyObserversAsync(
            target,
            p => p.ShowPrimeAppliedAsync(target, skill.ComboElement, skill.PrimeDurationMs))
            .ConfigureAwait(false);
    }

    // ── Detonator path ────────────────────────────────────────────────────

    private static async ValueTask TryDetonateAsync(
        Player attacker,
        IAttackable primaryTarget,
        SkillEntry skillEntry,
        Skill skill,
        HitInfo primaryHit)
    {
        // Mirror the IsAlive guard from ApplyPrimerAsync — never detonate a
        // corpse. WillDetonate already checks this for the caller, but
        // belt-and-braces here covers the path where ProcessHitAsync is
        // invoked directly without WillDetonate.
        if (!primaryTarget.IsAlive)
        {
            return;
        }

        var container = PrimeRegistry.GetOrCreate(primaryTarget);
        if (!container.HasActivePrime(skill.ComboElement))
        {
            return;
        }

        double multiplier = skill.DamageMultiplier;

        // Do-then-commit: roll the primary damage FIRST, before consuming the
        // primer or sending visual notifications. If the attack roll misses
        // (CalculateDamageAsync → IsAttackSuccessfulTo returns false), we
        // abort the whole detonation — primer stays active for retry, no
        // explosion effect plays, no "primer cleared" packet goes out. This
        // prevents the silent-fizzle bug where the player saw effect+sound
        // and lost the primer but took no damage.
        //
        // PvP balance is preserved: the defender's defense rate still
        // dictates hit chance exactly as for a non-combo attack.
        var hitInfo = await primaryTarget.AttackByAsync(attacker, skillEntry, isCombo: true, damageFactor: multiplier).ConfigureAwait(false);
        if (hitInfo is null or { HealthDamage: 0, ShieldDamage: 0 })
        {
            return;
        }

        // Hit landed — commit: consume the primer, notify the client, then
        // splash the radius.
        container.Consume(skill.ComboElement);

        await NotifyObserversAsync(
            primaryTarget,
            p => p.ShowPrimeClearedAsync(primaryTarget, skill.ComboElement))
            .ConfigureAwait(false);

        var detonationRadius = skill.DetonationRadius;
        await NotifyObserversAsync(
            primaryTarget,
            p => p.ShowDetonationAsync(primaryTarget, skill.ComboElement, detonationRadius))
            .ConfigureAwait(false);

        var map = attacker.CurrentMap;
        if (map is null)
        {
            return;
        }

        // Splash — centred on the primed target's position.
        var radius = (int)Math.Ceiling(skill.DetonationRadius);
        var splashTargets = map.GetAttackablesInRange(primaryTarget.Position, radius);

        foreach (var splashTarget in splashTargets)
        {
            if (ReferenceEquals(splashTarget, primaryTarget) || ReferenceEquals(splashTarget, attacker))
            {
                continue;
            }

            if (!splashTarget.IsAlive || splashTarget.IsAtSafezone())
            {
                continue;
            }

            await splashTarget.AttackByAsync(attacker, skillEntry, isCombo: true, damageFactor: multiplier).ConfigureAwait(false);
        }
    }

    // ── Shared helper ─────────────────────────────────────────────────────

    private static ValueTask NotifyObserversAsync(
        IAttackable target,
        Func<IShowPrimeStatusPlugIn, ValueTask> action)
    {
        if (target is IObservable observable)
        {
            return observable.ForEachWorldObserverAsync<IShowPrimeStatusPlugIn>(action, includeThis: true);
        }

        return ValueTask.CompletedTask;
    }
}
