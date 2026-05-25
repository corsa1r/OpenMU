// <copyright file="MonsterAttributeHolder.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Attributes;

using System.Collections.Concurrent;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.GameLogic.NPC;

/// <summary>
/// The attribute system for monsters, which is considering monster definitions.
/// </summary>
public class MonsterAttributeHolder : IAttributeSystem
{
    private static readonly IDictionary<AttributeDefinition, Func<AttackableNpcBase, float>> StatMapping =
        new Dictionary<AttributeDefinition, Func<AttackableNpcBase, float>>
        {
            { Stats.CurrentHealth, m => m.Health },
            { Stats.DefensePvm, m => m.Attributes.GetValueOfAttribute(Stats.DefenseBase) + ((m as Monster)?.SummonedBy?.Attributes?[Stats.SummonedMonsterDefenseIncrease] ?? 0) },
            { Stats.DefensePvp, m => m.Attributes.GetValueOfAttribute(Stats.DefenseBase) + ((m as Monster)?.SummonedBy?.Attributes?[Stats.SummonedMonsterDefenseIncrease] ?? 0) },
            { Stats.DamageReceiveDecrement, m => 1.0f },
            { Stats.AttackDamageIncrease, m => 1.0f },
            { Stats.ShieldBypassChance, m => 1.0f },
            { Stats.DefenseDecrement, m => 1.0f - m.Attributes.GetValueOfAttribute(Stats.InnovationDefDecrement) },
        };

    private static readonly IDictionary<AttributeDefinition, Action<AttackableNpcBase, float>> SetterMapping =
        new Dictionary<AttributeDefinition, Action<AttackableNpcBase, float>>
        {
            { Stats.CurrentHealth, (m, v) => m.Health = (int)v },
        };

    private static readonly ConcurrentDictionary<MonsterDefinition, IDictionary<AttributeDefinition, float>> MonsterStatAttributesCache = new();

    /// <summary>
    /// Refreshes the cached stat-attribute dictionary for <paramref name="def"/> by re-reading
    /// from <see cref="MonsterDefinition.Attributes"/>. The cached dict is mutated **in place**
    /// so every live <see cref="MonsterAttributeHolder"/> sharing the reference picks up the
    /// new values on its next read — no respawn required. Safe to call from any thread; the
    /// per-dict mutation is done under a lock on the dict instance.
    ///
    /// Call this after admin-panel edits / config saves so server-side combat formulas
    /// (defense, damage, level used in drop tables, etc.) immediately reflect the change.
    /// </summary>
    public static void RefreshAttributeCache(MonsterDefinition def)
    {
        if (!MonsterStatAttributesCache.TryGetValue(def, out var cached))
        {
            // No cache entry yet — next spawn will populate fresh from the new definition.
            return;
        }

        // ToDictionary throws if AttributeDefinition is null; mirror the original
        // factory's check by skipping null entries instead of asserting.
        var fresh = def.Attributes
            .Where(a => a.AttributeDefinition is not null)
            .ToDictionary(a => a.AttributeDefinition!, a => a.Value);

        // Mutate in place so existing instance references stay valid.
        lock (cached)
        {
            cached.Clear();
            foreach (var kv in fresh)
            {
                cached[kv.Key] = kv.Value;
            }
        }
    }

    private readonly AttackableNpcBase _monster;

    private readonly IDictionary<AttributeDefinition, float> _statAttributes;

    private readonly object _attributesLock = new();

    /// <summary>
    /// Attribute dictionary of a monster instance.
    /// Most monster instances don't have additional attributes, so we just instantiate one if needed.
    /// </summary>
    private IDictionary<AttributeDefinition, IComposableAttribute>? _attributes;

    /// <summary>
    /// Initializes a new instance of the <see cref="MonsterAttributeHolder"/> class.
    /// </summary>
    /// <param name="monster">The monster.</param>
    public MonsterAttributeHolder(AttackableNpcBase monster)
    {
        this._monster = monster;
        this._statAttributes = GetStatAttributeOfMonster(monster.Definition);
    }

    /// <inheritdoc/>
    public float this[AttributeDefinition attributeDefinition]
    {
        get => this.GetValueOfAttribute(attributeDefinition);

        set
        {
            if (SetterMapping.TryGetValue(attributeDefinition, out var setAction))
            {
                setAction(this._monster, value);
            }
        }
    }

    /// <inheritdoc/>
    public float GetValueOfAttribute(AttributeDefinition attributeDefinition)
    {
        IDictionary<AttributeDefinition, IComposableAttribute>? attributes;
        lock (this._attributesLock)
        {
            attributes = this._attributes;
        }

        if (attributes is not null
            && attributes.TryGetValue(attributeDefinition, out var attribute))
        {
            return attribute.Value;
        }

        if (this._statAttributes.TryGetValue(attributeDefinition, out float value))
        {
            return value;
        }

        if (StatMapping.TryGetValue(attributeDefinition, out var mappingFunction))
        {
            return mappingFunction(this._monster);
        }

        return 0;
    }

    /// <inheritdoc/>
    public void AddElement(IElement element, AttributeDefinition targetAttribute)
    {
        var attributeDictionary = this.GetAttributeDictionary();
        if (!attributeDictionary.TryGetValue(targetAttribute, out var attribute))
        {
            attribute = new ComposableAttribute(targetAttribute);
            var attrValue = this.GetValueOfAttribute(targetAttribute);
            var nullValue = element.AggregateType == AggregateType.Multiplicate ? 1 : 0;
            attrValue = Math.Abs(attrValue) < 0.01f ? nullValue : attrValue;
            attribute.AddElement(new SimpleElement { Value = attrValue });
            attributeDictionary.Add(targetAttribute, attribute);
        }

        attribute.AddElement(element);
    }

    /// <inheritdoc/>
    public void RemoveElement(IElement element, AttributeDefinition targetAttribute)
    {
        IDictionary<AttributeDefinition, IComposableAttribute>? attributes;
        lock (this._attributesLock)
        {
            attributes = this._attributes;
        }

        if (attributes is null)
        {
            return;
        }

        if (attributes.TryGetValue(targetAttribute, out var attribute))
        {
            attribute.RemoveElement(element);
            if (attribute.Elements.Skip(1).Take(1).Any())
            {
                attributes.Remove(targetAttribute);
            }
        }

        if (attributes.Count == 0)
        {
            lock (this._attributesLock)
            {
                this._attributes = null;
            }
        }
    }

    /// <inheritdoc/>
    public void AddAttributeRelationship(AttributeRelationship combination, IAttributeSystem sourceAttributeHolder, AggregateType aggregateType)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public IElement GetOrCreateAttribute(AttributeDefinition attributeDefinition)
    {
        throw new NotImplementedException();
    }

    private static IDictionary<AttributeDefinition, float> GetStatAttributeOfMonster(MonsterDefinition monsterDefinition)
    {
        return MonsterStatAttributesCache.GetOrAdd(monsterDefinition, monsterDef => monsterDef.Attributes.ToDictionary(
                m => m.AttributeDefinition ?? throw Error.NotInitializedProperty(m, nameof(m.AttributeDefinition)),
                m => m.Value));
    }

    private IDictionary<AttributeDefinition, IComposableAttribute> GetAttributeDictionary()
    {
        lock (this._attributesLock)
        {
            var attributes = this._attributes;
            if (attributes is null)
            {
                attributes = new Dictionary<AttributeDefinition, IComposableAttribute>();
                this._attributes = attributes;
            }

            return attributes;
        }
    }
}