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
        var container = PrimeRegistry.GetOrCreate(primaryTarget);
        var consumed = container.Consume(skill.ComboElement);

        if (consumed is null)
        {
            return;
        }

        // Immediately clear the prime icon from every nearby client.
        await NotifyObserversAsync(
            primaryTarget,
            p => p.ShowPrimeClearedAsync(primaryTarget, skill.ComboElement))
            .ConfigureAwait(false);

        double multiplier = skill.DamageMultiplier;

        // Apply explosion bonus to the primary target using the detonator skill so damageFactor is applied.
        await primaryTarget.AttackByAsync(attacker, skillEntry, isCombo: true, damageFactor: multiplier).ConfigureAwait(false);

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
