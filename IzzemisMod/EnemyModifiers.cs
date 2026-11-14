using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Runecarved
{
    internal static class EnemyModifiers
    {
        internal const int MaxCommandModifiers = 2;
        internal const float FrenzyMultiplier = 1.25f;
        private const string DataKey = "com.izzemi.runecarved.enemy_modifiers";
        private const string DataVersion = "1:";
        private const int CacheLimit = 256;
        private static readonly IReadOnlyList<EnemyModifierDefinition> NoModifiers =
            Array.AsReadOnly(new EnemyModifierDefinition[0]);
        private static readonly HashSet<string> InvalidDataWarnings = new HashSet<string>();
        private static readonly Dictionary<string, ParsedModifiers> ParsedData =
            new Dictionary<string, ParsedModifiers>(StringComparer.Ordinal);
        private static readonly AccessTools.FieldRef<Character, ZNetView> NetView =
            AccessTools.FieldRefAccess<Character, ZNetView>("m_nview");
        private static readonly AccessTools.FieldRef<Character, bool> Tamed =
            AccessTools.FieldRefAccess<Character, bool>("m_tamed");

        private sealed class ParsedModifiers
        {
            internal IReadOnlyList<EnemyModifierDefinition> Known { get; }
            internal bool HasUnknown { get; }

            internal ParsedModifiers(IReadOnlyList<EnemyModifierDefinition> known, bool hasUnknown)
            {
                Known = known;
                HasUnknown = hasUnknown;
            }
        }

        internal static void EnsureRolled(Character character)
        {
            if (!IsEligible(character) ||
                (!character.IsBoss() && !EnemyModifierAllowlist.IsAllowed(character.gameObject)))
            {
                return;
            }

            ZNetView view = NetView(character);
            if (view == null || !view.IsValid() || !view.IsOwner())
            {
                return;
            }

            ZDO zdo = view.GetZDO();
            if (character.IsBoss())
            {
                if (!zdo.GetString(DataKey, out _))
                {
                    if (TryRollBossModifiers(out List<EnemyModifierDefinition> rolled))
                    {
                        SaveModifiers(zdo, rolled);
                    }
                }
                return;
            }
            // Each star beyond level one grants one modifier, up to the catalog size
            int desired = Math.Min(zdo.GetInt(ZDOVars.s_level, 1) - 1, EnemyModifierCatalog.All.Count);
            IReadOnlyList<EnemyModifierDefinition> existing = NoModifiers;
            if (zdo.GetString(DataKey, out string data))
            {
                if (!TryParse(data, out ParsedModifiers saved) || saved.HasUnknown)
                {
                    return;
                }
                existing = saved.Known;
            }

            if (existing.Count >= desired)
            {
                return;
            }

            var selected = new List<EnemyModifierDefinition>(existing);
            var available = new List<EnemyModifierDefinition>();
            foreach (EnemyModifierDefinition definition in EnemyModifierCatalog.All)
            {
                if (IsCompatible(definition, character.gameObject) &&
                    definition.Weight > 0f && !selected.Contains(definition))
                {
                    available.Add(definition);
                }
            }

            int originalCount = selected.Count;
            while (selected.Count < desired && available.Count > 0)
            {
                EnemyModifierDefinition chosen = ChooseWeighted(available);
                selected.Add(chosen);
                available.Remove(chosen);
            }

            if (selected.Count < desired)
            {
                WarnInvalidData("Not enough positive-weight enemy modifiers to fill all star slots.");
            }
            if (selected.Count == originalCount)
            {
                return;
            }

            SaveModifiers(zdo, selected);
        }

        private static void SaveModifiers(ZDO zdo, List<EnemyModifierDefinition> selected)
        {
            var ids = new List<string>();
            foreach (EnemyModifierDefinition definition in EnemyModifierCatalog.All)
            {
                if (selected.Contains(definition))
                {
                    ids.Add(definition.Id);
                }
            }
            zdo.Set(DataKey, DataVersion + string.Join(",", ids));
        }

        internal static bool IsEligiblePrefab(GameObject prefab)
        {
            Character character = prefab.GetComponent<Character>();
            ZNetView view = prefab.GetComponent<ZNetView>();
            return character != null && (character.IsBoss() || EnemyModifierAllowlist.IsAllowed(prefab)) && !character.IsPlayer() &&
                prefab.GetComponent<MonsterAI>() != null &&
                character.GetFaction() != Character.Faction.PlayerSpawned &&
                !Tamed(character) && view != null &&
                (character.m_nViewOverride == null || character.m_nViewOverride == view);
        }

        internal static bool IsCompatible(EnemyModifierDefinition modifier, GameObject prefab)
        {
            Character character = prefab.GetComponent<Character>();
            return character != null &&
                (character.IsBoss() ? modifier.Group != EnemyModifierGroup.Enemy : modifier.Group != EnemyModifierGroup.Boss) &&
                (!modifier.RequiresRanged || EnemyModifierRangedList.IsRanged(prefab));
        }

        internal static bool TryRollBossModifiers(out List<EnemyModifierDefinition> modifiers)
        {
            modifiers = new List<EnemyModifierDefinition>();
            foreach (EnemyModifierGroup group in new[] { EnemyModifierGroup.Boss, EnemyModifierGroup.Shared })
            {
                var candidates = new List<EnemyModifierDefinition>();
                foreach (EnemyModifierDefinition definition in EnemyModifierCatalog.All)
                {
                    if (definition.Group == group && definition.Weight > 0f)
                    {
                        candidates.Add(definition);
                    }
                }
                if (candidates.Count == 0)
                {
                    string error = $"Cannot roll boss modifiers: no positive-weight {group} modifiers.";
                    if (InvalidDataWarnings.Add(error))
                    {
                        Jotunn.Logger.LogError(error);
                    }
                    modifiers.Clear();
                    return false;
                }
                modifiers.Add(ChooseWeighted(candidates));
            }
            return true;
        }

        internal static bool TryAssign(Character character, IReadOnlyList<EnemyModifierDefinition> modifiers, out string error)
        {
            error = null;
            if (character == null || EnemyModifierSummoner.IsSummon(character) || character.IsPlayer() ||
                !(character.GetBaseAI() is MonsterAI) ||
                character.GetFaction() == Character.Faction.PlayerSpawned)
            {
                error = "The spawned object is not an eligible enemy.";
                return false;
            }
            if (!character.IsBoss() && !EnemyModifierAllowlist.IsAllowed(character.gameObject))
            {
                error = "This enemy prefab is not enabled for enemy modifiers.";
                return false;
            }

            ZNetView view = NetView(character);
            if (view == null || !view.IsValid() || !view.IsOwner())
            {
                error = "The spawned enemy has no valid locally owned network data.";
                return false;
            }
            if (character.IsTamed())
            {
                error = "The spawned enemy is tamed and cannot receive modifiers.";
                return false;
            }
            if (modifiers == null || modifiers.Count == 0 || modifiers.Count > MaxCommandModifiers)
            {
                error = $"Select between 1 and {MaxCommandModifiers} enemy modifiers.";
                return false;
            }

            var ids = new List<string>();
            var unique = new HashSet<string>(StringComparer.Ordinal);
            var groups = new HashSet<EnemyModifierGroup>();
            foreach (EnemyModifierDefinition modifier in modifiers)
            {
                if (modifier == null || !EnemyModifierCatalog.TryGet(modifier.Id, out EnemyModifierDefinition known) ||
                    !ReferenceEquals(modifier, known) ||
                    !unique.Add(modifier.Id))
                {
                    error = "Enemy modifiers must be known and distinct.";
                    return false;
                }
                if (!IsCompatible(modifier, character.gameObject))
                {
                    error = $"Modifier '{modifier.Id}' is not compatible with this creature.";
                    return false;
                }
                if (character.IsBoss() && !groups.Add(modifier.Group))
                {
                    error = "A boss can have at most one boss modifier and one shared modifier.";
                    return false;
                }
                ids.Add(modifier.Id);
            }

            view.GetZDO().Set(DataKey, DataVersion + string.Join(",", ids));
            if (!character.IsBoss())
            {
                character.SetLevel(modifiers.Count + 1);
            }
            return true;
        }

        internal static EnemyModifierDefinition ChooseWeighted(IReadOnlyList<EnemyModifierDefinition> available)
        {
            if (available == null || available.Count == 0)
            {
                throw new ArgumentException("Weighted selection requires at least one modifier.", nameof(available));
            }

            double total = 0d;
            foreach (EnemyModifierDefinition definition in available)
            {
                total += definition.Weight;
            }
            if (total <= 0d)
            {
                throw new ArgumentException("Weighted selection requires a positive total weight.", nameof(available));
            }

            double roll = UnityEngine.Random.value * total;
            EnemyModifierDefinition lastPositive = null;
            foreach (EnemyModifierDefinition definition in available)
            {
                if (definition.Weight <= 0f)
                {
                    continue;
                }
                lastPositive = definition;
                roll -= definition.Weight;
                if (roll < 0d)
                {
                    return definition;
                }
            }
            return lastPositive;
        }

        internal static IReadOnlyList<EnemyModifierDefinition> GetModifiers(Character character)
        {
            if (!IsEligible(character))
            {
                return NoModifiers;
            }

            ZNetView view = NetView(character);
            if (view == null || !view.IsValid() ||
                !view.GetZDO().GetString(DataKey, out string data))
            {
                return NoModifiers;
            }

            return TryParse(data, out ParsedModifiers modifiers) ? modifiers.Known : NoModifiers;
        }

        internal static bool IsFrenzied(Character character, IReadOnlyList<EnemyModifierDefinition> modifiers)
        {
            if (character == null || !character.IsBoss() || character.IsDead() ||
                character.GetHealthPercentage() >= 0.4f)
            {
                return false;
            }
            foreach (EnemyModifierDefinition modifier in modifiers)
            {
                if (modifier.Id == "frenzy")
                {
                    return true;
                }
            }
            return false;
        }

        private static bool TryParse(string data, out ParsedModifiers modifiers)
        {
            if (ParsedData.TryGetValue(data, out modifiers))
            {
                return true;
            }

            modifiers = null;
            if (!data.StartsWith(DataVersion, StringComparison.Ordinal))
            {
                WarnInvalidData($"Unknown or invalid enemy modifier data '{data}'.");
                return false;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            bool hasUnknown = false;
            foreach (string savedId in data.Substring(DataVersion.Length).Split(','))
            {
                string id = savedId == EnemyModifierCatalog.LegacySunderId
                    ? EnemyModifierCatalog.SunderingId : savedId;
                if (string.IsNullOrWhiteSpace(id) || !ids.Add(id))
                {
                    WarnInvalidData($"Unknown or invalid enemy modifier data '{data}'.");
                    return false;
                }
                if (!EnemyModifierCatalog.TryGet(id, out _))
                {
                    // Keep the saved roll intact so an older mod version cannot overwrite newer modifier IDs
                    hasUnknown = true;
                    WarnInvalidData($"Unknown enemy modifier '{id}' in saved data; preserving the saved roll.");
                }
            }

            var known = new List<EnemyModifierDefinition>();
            foreach (EnemyModifierDefinition definition in EnemyModifierCatalog.All)
            {
                if (ids.Contains(definition.Id))
                {
                    known.Add(definition);
                }
            }
            modifiers = new ParsedModifiers(known.AsReadOnly(), hasUnknown);
            if (ParsedData.Count < CacheLimit)
            {
                ParsedData.Add(data, modifiers);
            }
            return true;
        }

        private static void WarnInvalidData(string message)
        {
            if (InvalidDataWarnings.Add(message))
            {
                Jotunn.Logger.LogWarning(message);
            }
        }

        private static bool IsEligible(Character character)
        {
            if (character == null || EnemyModifierSummoner.IsSummon(character) || character.IsPlayer() ||
                !(character.GetBaseAI() is MonsterAI) ||
                character.GetFaction() == Character.Faction.PlayerSpawned)
            {
                return false;
            }

            ZNetView view = NetView(character);
            return view != null && view.IsValid() &&
                (character.IsBoss() || view.GetZDO().GetInt(ZDOVars.s_level, 1) > 1) &&
                !character.IsTamed();
        }

        internal static void ApplyAttackModifiers(ref HitData hit)
        {
            if (hit == null || hit.m_hitType != HitData.HitType.EnemyHit)
            {
                return;
            }

            Character attacker = hit.GetAttacker();
            IReadOnlyList<EnemyModifierDefinition> modifiers = GetModifiers(attacker);
            float originalDamage = hit.m_damage.GetTotalDamage();
            if (originalDamage <= 0f)
            {
                return;
            }

            bool cloned = false;
            foreach (EnemyModifierDefinition modifier in modifiers)
            {
                if (modifier.AttackDamageEffect == null)
                {
                    continue;
                }
                if (!cloned)
                {
                    hit = hit.Clone();
                    cloned = true;
                }
                modifier.AttackDamageEffect(ref hit.m_damage, originalDamage);
            }
            if (IsFrenzied(attacker, modifiers))
            {
                if (!cloned)
                {
                    hit = hit.Clone();
                }
                hit.m_damage.Modify(FrenzyMultiplier);
            }
        }
    }

    [HarmonyPatch(typeof(Character), "Awake")]
    internal static class EnemyModifierAwakePatch
    {
        private static void Postfix(Character __instance)
        {
            EnemyModifierShields.Register(__instance);
            BossModifierBinding.RegisterPlayer(__instance);
            EnemyModifiers.EnsureRolled(__instance);
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.SetLevel))]
    internal static class EnemyModifierLevelPatch
    {
        private static void Postfix(Character __instance)
        {
            EnemyModifiers.EnsureRolled(__instance);
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.CustomFixedUpdate))]
    internal static class EnemyModifierOwnershipPatch
    {
        private static void Postfix(Character __instance, float dt)
        {
            EnemyModifiers.EnsureRolled(__instance);
            EnemyModifierParticles.Ensure(__instance);
            EnemyModifierRegrowth.Update(__instance, dt);
            EnemyModifierFiretread.UpdateEmitter(__instance, dt);
            EnemyModifierCombatAbilities.UpdateCaster(__instance);
            EnemyModifierShields.Update(__instance);
            EnemyModifierSummoner.UpdateSummoner(__instance, dt);
            BossModifierSummons.UpdateSummons(__instance, dt);
            BossModifierBinding.UpdateBoss(__instance);
        }
    }

    [HarmonyPatch]
    internal static class EnemyModifierDamagePatch
    {
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Character), nameof(Character.Damage));
            yield return AccessTools.Method(typeof(Destructible), nameof(Destructible.Damage));
            yield return AccessTools.Method(typeof(WearNTear), nameof(WearNTear.Damage));
            yield return AccessTools.Method(typeof(TreeBase), nameof(TreeBase.Damage));
            yield return AccessTools.Method(typeof(TreeLog), nameof(TreeLog.Damage));
            yield return AccessTools.Method(typeof(MineRock), nameof(MineRock.Damage));
            yield return AccessTools.Method(typeof(MineRock5), nameof(MineRock5.Damage));
        }

        private static void Prefix(ref HitData hit)
        {
            EnemyModifiers.ApplyAttackModifiers(ref hit);
        }
    }
}
