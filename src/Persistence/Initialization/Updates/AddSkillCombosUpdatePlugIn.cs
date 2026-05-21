// <copyright file="AddSkillCombosUpdatePlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.Initialization.Updates;

using System.Runtime.InteropServices;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.Persistence.Initialization.Skills;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Assigns Primer / Detonator combo roles to the chosen skills.
/// Edit the body of <see cref="ApplyAsync"/> to match your skill roster.
/// </summary>
[PlugIn]
[Display(Name = PlugInName, Description = PlugInDescription)]
[Guid("F2A14B83-5C67-4D91-AEF3-2B4C5D6E7F80")]
public sealed class AddSkillCombosUpdatePlugIn : UpdatePlugInBase
{
    internal const string PlugInName = "Add Skill Combos";
    internal const string PlugInDescription = "Assigns elemental Primer and Detonator combo roles to selected skills.";

    /// <inheritdoc />
    public override UpdateVersion Version => UpdateVersion.AddSkillCombos;

    /// <inheritdoc />
    public override string DataInitializationKey => VersionSeasonSix.DataInitialization.Id;

    /// <inheritdoc />
    public override string Name => PlugInName;

    /// <inheritdoc />
    public override string Description => PlugInDescription;

    /// <inheritdoc />
    public override bool IsMandatory => false;

    /// <inheritdoc />
    public override DateTime CreatedAt => new(2026, 05, 20, 0, 0, 0, DateTimeKind.Utc);

    /// <inheritdoc />
    protected override async ValueTask ApplyAsync(IContext context, GameConfiguration gameConfiguration)
    {
        // ── Helper ────────────────────────────────────────────────────────
        Skill GetSkill(SkillNumber number) =>
            gameConfiguration.Skills.First(s => s.Number == (short)number);

        // ── Fire combo ────────────────────────────────────────────────────
        // Primer  : Flare (Dark Wizard ignite-style, short range)
        var flare = GetSkill(SkillNumber.FlameStrike);
        flare.ComboType = SkillComboType.Primer;
        flare.ComboElement = SkillComboElement.Fire;
        flare.PrimeDurationMs = 6000;       // mark lasts 6 s

        // Detonator: Meteorite — explodes any Fire prime on the target
        var meteor = GetSkill(SkillNumber.Meteorite);
        meteor.ComboType = SkillComboType.Detonator;
        meteor.ComboElement = SkillComboElement.Fire;
        meteor.DetonationRadius = 4.0f;     // splash 4 map units
        meteor.DamageMultiplier = 3.5f;     // splash hits × 3.5

        // ── Ice combo ─────────────────────────────────────────────────────
        // Primer  : Ice Arrow — chills the target
        var iceArrow = GetSkill(SkillNumber.IceArrow);
        iceArrow.ComboType = SkillComboType.Primer;
        iceArrow.ComboElement = SkillComboElement.Ice;
        iceArrow.PrimeDurationMs = 8000;

        // Detonator: Ice Storm — shatters the ice mark into a burst
        var iceStorm = GetSkill(SkillNumber.IceStorm);
        iceStorm.ComboType = SkillComboType.Detonator;
        iceStorm.ComboElement = SkillComboElement.Ice;
        iceStorm.DetonationRadius = 3.0f;
        iceStorm.DamageMultiplier = 3.0f;

        // ── Lightning combo ───────────────────────────────────────────────
        // Primer  : Lightning — static charge
        var lightning = GetSkill(SkillNumber.Lightning);
        lightning.ComboType = SkillComboType.Primer;
        lightning.ComboElement = SkillComboElement.Lightning;
        lightning.PrimeDurationMs = 5000;

        // Detonator: Chain Lightning — discharges the static in an arc
        var chainLightning = GetSkill(SkillNumber.ChainLightning);
        chainLightning.ComboType = SkillComboType.Detonator;
        chainLightning.ComboElement = SkillComboElement.Lightning;
        chainLightning.DetonationRadius = 5.0f;
        chainLightning.DamageMultiplier = 4.0f;
    }
}
