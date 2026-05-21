// <copyright file="Skill.Combo.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.DataModel.Configuration;

/// <summary>
/// Primer/Detonator combo configuration properties, partial extension of <see cref="Skill"/>.
/// </summary>
/// <remarks>
/// EF Core picks these up automatically via code-first conventions.
/// After adding this file run: dotnet ef migrations add AddSkillComboFields
/// and set HasDefaultValue(5000), HasDefaultValue(3.0f) in the migration if needed.
/// </remarks>
public partial class Skill
{
    /// <summary>Gets or sets the combo role for this skill.</summary>
    public SkillComboType ComboType { get; set; }

    /// <summary>Gets or sets the elemental affinity used to match Primers to Detonators.</summary>
    public SkillComboElement ComboElement { get; set; }

    /// <summary>Gets or sets how long (milliseconds) a prime mark persists on a target. Default: 5000.</summary>
    public int PrimeDurationMs { get; set; } = 5000;

    /// <summary>Gets or sets the splash radius (map units) of a detonation explosion. Default: 3.0.</summary>
    public float DetonationRadius { get; set; } = 3.0f;

    /// <summary>Gets or sets the factor by which base hit damage is multiplied for the combo splash. Default: 3.0.</summary>
    public float DamageMultiplier { get; set; } = 3.0f;
}
