// <copyright file="SkillComboType.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.DataModel.Configuration;

/// <summary>
/// Indicates the role a skill plays within the Primer/Detonator combo chain.
/// </summary>
public enum SkillComboType
{
    /// <summary>Skill has no combo role.</summary>
    None,

    /// <summary>Applies an elemental prime mark to the hit target.</summary>
    Primer,

    /// <summary>Detonates any matching prime mark on the target, triggering a splash explosion.</summary>
    Detonator,
}

/// <summary>
/// Elemental affinity used to pair Primers with their matching Detonators.
/// </summary>
public enum SkillComboElement
{
    /// <summary>No elemental affinity.</summary>
    None,

    Fire,
    Ice,
    Lightning,
    Physical,
}
