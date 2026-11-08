using System;
using System.Collections.Generic;
using Jotunn.Entities;

namespace Runecarved
{
    internal delegate void EnemyAttackDamageEffect(ref HitData.DamageTypes damage, float originalDamage);
    internal delegate void EnemyResistanceEffect(ref HitData.DamageTypes damage);

    internal enum EnemyModifierGroup
    {
        Enemy,
        Shared,
        Boss
    }

    internal sealed class EnemyModifierDefinition
    {
        internal string Id { get; }
        internal string DisplayToken { get; }
        internal string DisplayName { get; }
        internal float Weight { get; }
        internal EnemyModifierGroup Group { get; }
        internal EnemyAttackDamageEffect AttackDamageEffect { get; }
        internal EnemyResistanceEffect ResistanceEffect { get; }
        internal float MovementMultiplier { get; }
        internal float DamageTakenMultiplier { get; }
        internal bool Shattering { get; }

        internal EnemyModifierDefinition(
            string id,
            string displayToken,
            string displayName,
            float weight,
            EnemyAttackDamageEffect attackDamageEffect = null,
            EnemyModifierGroup group = EnemyModifierGroup.Enemy,
            EnemyResistanceEffect resistanceEffect = null,
            float movementMultiplier = 1f,
            float damageTakenMultiplier = 1f,
            bool shattering = false)
        {
            if (string.IsNullOrWhiteSpace(id) || id.IndexOf(',') >= 0)
            {
                throw new ArgumentException("Enemy modifier IDs must be nonempty and cannot contain commas.", nameof(id));
            }
            if (string.IsNullOrWhiteSpace(displayToken) || string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException("Enemy modifiers require a display token and name.");
            }
            if (float.IsNaN(weight) || float.IsInfinity(weight) || weight < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(weight), "Enemy modifier weights must be finite and nonnegative.");
            }
            if (float.IsNaN(movementMultiplier) || float.IsInfinity(movementMultiplier) || movementMultiplier < 0f ||
                float.IsNaN(damageTakenMultiplier) || float.IsInfinity(damageTakenMultiplier) || damageTakenMultiplier < 0f)
            {
                throw new ArgumentOutOfRangeException("Enemy modifier multipliers must be finite and nonnegative.");
            }

            Id = id;
            DisplayToken = displayToken;
            DisplayName = displayName;
            Weight = weight;
            Group = group;
            AttackDamageEffect = attackDamageEffect;
            ResistanceEffect = resistanceEffect;
            MovementMultiplier = movementMultiplier;
            DamageTakenMultiplier = damageTakenMultiplier;
            Shattering = shattering;
        }
    }

    internal static class EnemyModifierCatalog
    {
        private static readonly IReadOnlyList<EnemyModifierDefinition> Definitions =
            Array.AsReadOnly(new[]
            {
                new EnemyModifierDefinition(
                    "fire_infusion", "$runecarved_enemy_fire_infusion", "Fire Infusion", 1f,
                    (ref HitData.DamageTypes damage, float original) => damage.m_fire += original * 0.25f),
                new EnemyModifierDefinition(
                    "poison_infusion", "$runecarved_enemy_poison_infusion", "Poison Infusion", 1f,
                    (ref HitData.DamageTypes damage, float original) => damage.m_poison += original * 0.25f),
                new EnemyModifierDefinition(
                    "frost_infusion", "$runecarved_enemy_frost_infusion", "Frost Infusion", 1f,
                    (ref HitData.DamageTypes damage, float original) => damage.m_frost += original * 0.25f),
                new EnemyModifierDefinition(
                    "lightning_infusion", "$runecarved_enemy_lightning_infusion", "Lightning Infusion", 1f,
                    (ref HitData.DamageTypes damage, float original) => damage.m_lightning += original * 0.25f),
                new EnemyModifierDefinition(
                    "fire_ward", "$runecarved_enemy_fire_ward", "Fire Ward", 1f,
                    group: EnemyModifierGroup.Shared, resistanceEffect: (ref HitData.DamageTypes damage) => damage.m_fire *= 0.5f),
                new EnemyModifierDefinition(
                    "poison_ward", "$runecarved_enemy_poison_ward", "Poison Ward", 1f,
                    group: EnemyModifierGroup.Shared, resistanceEffect: (ref HitData.DamageTypes damage) => damage.m_poison *= 0.5f),
                new EnemyModifierDefinition(
                    "frost_ward", "$runecarved_enemy_frost_ward", "Frost Ward", 1f,
                    group: EnemyModifierGroup.Shared, resistanceEffect: (ref HitData.DamageTypes damage) => damage.m_frost *= 0.5f),
                new EnemyModifierDefinition(
                    "lightning_ward", "$runecarved_enemy_lightning_ward", "Lightning Ward", 1f,
                    group: EnemyModifierGroup.Shared, resistanceEffect: (ref HitData.DamageTypes damage) => damage.m_lightning *= 0.5f),
                new EnemyModifierDefinition(
                    "blunt_ironhide", "$runecarved_enemy_blunt_ironhide", "Blunt Ironhide", 1f,
                    group: EnemyModifierGroup.Shared, resistanceEffect: (ref HitData.DamageTypes damage) => damage.m_blunt *= 0.25f),
                new EnemyModifierDefinition(
                    "pierce_ironhide", "$runecarved_enemy_pierce_ironhide", "Pierce Ironhide", 1f,
                    group: EnemyModifierGroup.Shared, resistanceEffect: (ref HitData.DamageTypes damage) => damage.m_pierce *= 0.25f),
                new EnemyModifierDefinition(
                    "slash_ironhide", "$runecarved_enemy_slash_ironhide", "Slash Ironhide", 1f,
                    group: EnemyModifierGroup.Shared, resistanceEffect: (ref HitData.DamageTypes damage) => damage.m_slash *= 0.25f),
                new EnemyModifierDefinition(
                    "fleeting", "$runecarved_enemy_fleeting", "Fleeting", 1f,
                    group: EnemyModifierGroup.Shared, movementMultiplier: 1.25f),
                new EnemyModifierDefinition(
                    "armored", "$runecarved_enemy_armored", "Armored", 1f,
                    group: EnemyModifierGroup.Shared, damageTakenMultiplier: 0.75f),
                new EnemyModifierDefinition(
                    "shattering", "$runecarved_enemy_shattering", "Shattering", 1f,
                    group: EnemyModifierGroup.Shared, shattering: true)
            });

        private static readonly Dictionary<string, EnemyModifierDefinition> ById = CreateLookup();

        internal static IReadOnlyList<EnemyModifierDefinition> All => Definitions;

        internal static bool TryGet(string id, out EnemyModifierDefinition definition)
        {
            return ById.TryGetValue(id, out definition);
        }

        internal static void RegisterLocalization(CustomLocalization localization)
        {
            foreach (EnemyModifierDefinition definition in Definitions)
            {
                localization.AddTranslation(definition.DisplayToken, definition.DisplayName);
            }
        }

        private static Dictionary<string, EnemyModifierDefinition> CreateLookup()
        {
            var lookup = new Dictionary<string, EnemyModifierDefinition>(StringComparer.Ordinal);
            foreach (EnemyModifierDefinition definition in Definitions)
            {
                lookup.Add(definition.Id, definition);
            }
            return lookup;
        }
    }
}
