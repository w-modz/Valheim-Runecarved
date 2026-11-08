using System;
using System.Collections.Generic;
using Jotunn.Entities;

namespace Runecarved
{
    internal delegate void EnemyAttackDamageEffect(ref HitData.DamageTypes damage, float originalDamage);

    internal sealed class EnemyModifierDefinition
    {
        internal string Id { get; }
        internal string DisplayToken { get; }
        internal string DisplayName { get; }
        internal float Weight { get; }
        internal EnemyAttackDamageEffect AttackDamageEffect { get; }

        internal EnemyModifierDefinition(
            string id,
            string displayToken,
            string displayName,
            float weight,
            EnemyAttackDamageEffect attackDamageEffect = null)
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

            Id = id;
            DisplayToken = displayToken;
            DisplayName = displayName;
            Weight = weight;
            AttackDamageEffect = attackDamageEffect;
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
                    (ref HitData.DamageTypes damage, float original) => damage.m_lightning += original * 0.25f)
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
