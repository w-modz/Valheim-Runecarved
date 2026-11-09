using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Runecarved
{
    internal static class EnemyModifierShields
    {
        private const string ShieldName = "Runecarved_EnemyShield";
        private const string ShieldRpc = "Runecarved_EnemyShield";
        private const string ShieldHealthKey = "runecarved_shield_health";
        private const string ShieldExpiryKey = "runecarved_shield_expiry";
        private const string LastDamageKey = "runecarved_shield_last_damage";
        private const string BulwarkTimeKey = "runecarved_bulwark_time";
        private const string ShieldWindupKey = "runecarved_shield_windup";
        private const string ShieldWindupDamageKey = "runecarved_shield_windup_damage";
        private const string BulwarkWindupKey = "runecarved_bulwark_windup";
        internal const int WindupSeconds = 3;
        private static readonly AccessTools.FieldRef<SE_Shield, float> ShieldTotal =
            AccessTools.FieldRefAccess<SE_Shield, float>("m_totalAbsorbDamage");
        private static readonly AccessTools.FieldRef<SE_Shield, float> ShieldDamage =
            AccessTools.FieldRefAccess<SE_Shield, float>("m_damage");
        private static readonly AccessTools.FieldRef<StatusEffect, Character> EffectCharacter =
            AccessTools.FieldRefAccess<StatusEffect, Character>("m_character");
        private static readonly AccessTools.FieldRef<SEMan, Character> ManagerCharacter =
            AccessTools.FieldRefAccess<SEMan, Character>("m_character");
        private static readonly AccessTools.FieldRef<StatusEffect, int> NameHash =
            AccessTools.FieldRefAccess<StatusEffect, int>("m_nameHash");
        private static SE_Shield shieldPrototype;
        private static bool warnedMissingShield;

        internal static void Register(Character character)
        {
            if (!(character.GetBaseAI() is MonsterAI))
            {
                return;
            }
            ZNetView view = character.m_nViewOverride != null
                ? character.m_nViewOverride : character.GetComponent<ZNetView>();
            if (view != null && view.IsValid())
            {
                view.Register<float>(ShieldRpc, (sender, amount) => Apply(character, amount));
            }
        }

        internal static void Update(Character character)
        {
            if (!(character.GetBaseAI() is MonsterAI) || ZNet.instance == null)
            {
                return;
            }
            ZNetView view = character.m_nViewOverride != null
                ? character.m_nViewOverride : character.GetComponent<ZNetView>();
            if (view == null || !view.IsValid())
            {
                return;
            }
            SEMan effects = character.GetSEMan();
            SE_Shield active = effects.GetStatusEffect(ShieldName.GetStableHashCode()) as SE_Shield;
            if (!view.IsOwner())
            {
                if (active != null)
                {
                    effects.RemoveStatusEffect(active, true);
                }
                return;
            }
            ZDO zdo = view.GetZDO();
            long now = ZNet.instance.GetTime().Ticks;
            long expiry = zdo.GetLong(ShieldExpiryKey);
            float health = zdo.GetFloat(ShieldHealthKey);
            if (health > 0f && expiry > now && active == null && !character.IsDead())
            {
                var existingShields = new List<SE_Shield>();
                foreach (StatusEffect effect in effects.GetStatusEffects())
                {
                    if (effect is SE_Shield shield)
                    {
                        if (!EnemyModifierCombatRules.ShouldReplaceShield(
                            ShieldTotal(shield) - ShieldDamage(shield), health))
                        {
                            zdo.Set(ShieldHealthKey, 0f);
                            return;
                        }
                        existingShields.Add(shield);
                    }
                }
                if (EnsurePrototype())
                {
                    foreach (SE_Shield shield in existingShields)
                    {
                        effects.RemoveStatusEffect(shield, true);
                    }
                    CreateShield(character, health, (expiry - now) / (float)TimeSpan.TicksPerSecond);
                }
            }
            else if ((expiry <= now || health <= 0f) && active != null)
            {
                effects.RemoveStatusEffect(active, true);
            }
            if (character.IsDead())
            {
                return;
            }

            bool shielded = false;
            bool bulwark = false;
            foreach (EnemyModifierDefinition modifier in EnemyModifiers.GetModifiers(character))
            {
                shielded |= modifier.Id == "shielded";
                bulwark |= modifier.Id == "bulwark";
            }
            if (shielded)
            {
                long last = zdo.GetLong(LastDamageKey);
                long windup = zdo.GetLong(ShieldWindupKey);
                if (last == 0 || last > now)
                {
                    zdo.Set(LastDamageKey, now);
                    zdo.Set(ShieldWindupKey, 0L);
                }
                else if (windup > 0)
                {
                    if (last != zdo.GetLong(ShieldWindupDamageKey) || windup > now)
                    {
                        zdo.Set(ShieldWindupKey, 0L);
                    }
                    else if (EnemyModifierCombatRules.IntervalElapsed(now, windup, WindupSeconds))
                    {
                        zdo.Set(ShieldWindupKey, 0L);
                        Apply(character, character.GetMaxHealth() * 0.2f);
                    }
                }
                else if (EnemyModifierCombatRules.IntervalElapsed(now, last, 10 - WindupSeconds) &&
                    (health <= 0f || expiry <= now) &&
                    CanApply(character, character.GetMaxHealth() * 0.2f))
                {
                    zdo.Set(ShieldWindupDamageKey, last);
                    zdo.Set(ShieldWindupKey, now);
                    EnemyModifierCastSound.Play(character.transform.position, true);
                }
            }
            else if (zdo.GetLong(ShieldWindupKey) != 0)
            {
                zdo.Set(ShieldWindupKey, 0L);
            }
            BaseAI ai = character.GetBaseAI();
            Character target = ai.GetTargetCreature();
            if (!bulwark || !ai.IsAlerted() || target == null || target.IsDead())
            {
                if (zdo.GetLong(BulwarkWindupKey) != 0)
                {
                    zdo.Set(BulwarkWindupKey, 0L);
                }
                return;
            }
            long lastBulwark = zdo.GetLong(BulwarkTimeKey);
            long bulwarkWindup = zdo.GetLong(BulwarkWindupKey);
            if (lastBulwark == 0 || lastBulwark > now)
            {
                zdo.Set(BulwarkTimeKey, now);
                zdo.Set(BulwarkWindupKey, 0L);
            }
            else if (bulwarkWindup > now)
            {
                zdo.Set(BulwarkWindupKey, 0L);
            }
            else if (bulwarkWindup > 0 && EnemyModifierCombatRules.IntervalElapsed(now, bulwarkWindup, WindupSeconds))
            {
                zdo.Set(BulwarkWindupKey, 0L);
                zdo.Set(BulwarkTimeKey, now);
                var targets = new List<Character>();
                Character.GetCharactersInRange(character.transform.position, 5f, targets);
                foreach (Character ally in targets)
                {
                    if (ally == null || ally.IsDead() || ally.IsPlayer() || ally.IsTamed() ||
                        ally.IsBoss() || !(ally.GetBaseAI() is MonsterAI) || BaseAI.IsEnemy(character, ally))
                    {
                        continue;
                    }
                    ZNetView allyView = ally.m_nViewOverride != null
                        ? ally.m_nViewOverride : ally.GetComponent<ZNetView>();
                    if (allyView != null && allyView.IsValid())
                    {
                        allyView.InvokeRPC(ShieldRpc, character.GetMaxHealth() * 0.2f);
                    }
                }
            }
            else if (bulwarkWindup == 0 && EnemyModifierCombatRules.IntervalElapsed(now, lastBulwark, 15 - WindupSeconds))
            {
                zdo.Set(BulwarkWindupKey, now);
                EnemyModifierCastSound.Play(character.transform.position, true);
            }
        }

        internal static bool IsWindingUp(ZDO zdo, long now)
        {
            long shield = zdo.GetLong(ShieldWindupKey);
            long bulwark = zdo.GetLong(BulwarkWindupKey);
            return IsActiveWindup(shield, now) || IsActiveWindup(bulwark, now);
        }

        private static bool IsActiveWindup(long start, long now)
        {
            return start > 0 && now >= start &&
                !EnemyModifierCombatRules.IntervalElapsed(now, start, WindupSeconds);
        }

        private static bool CanApply(Character character, float amount)
        {
            foreach (StatusEffect effect in character.GetSEMan().GetStatusEffects())
            {
                if (effect is SE_Shield shield &&
                    !EnemyModifierCombatRules.ShouldReplaceShield(ShieldTotal(shield) - ShieldDamage(shield), amount))
                {
                    return false;
                }
            }
            return EnsurePrototype();
        }

        private static bool Apply(Character character, float amount)
        {
            if (!character.IsOwner() || character.IsDead() || amount <= 0f || ZNet.instance == null)
            {
                return false;
            }
            SEMan effects = character.GetSEMan();
            var shields = new List<SE_Shield>();
            foreach (StatusEffect effect in effects.GetStatusEffects())
            {
                if (effect is SE_Shield shield)
                {
                    if (!EnemyModifierCombatRules.ShouldReplaceShield(
                        ShieldTotal(shield) - ShieldDamage(shield), amount))
                    {
                        return false;
                    }
                    shields.Add(shield);
                }
            }
            if (!EnsurePrototype())
            {
                return false;
            }
            foreach (SE_Shield shield in shields)
            {
                effects.RemoveStatusEffect(shield, true);
            }
            ZNetView view = character.m_nViewOverride != null
                ? character.m_nViewOverride : character.GetComponent<ZNetView>();
            ZDO zdo = view.GetZDO();
            zdo.Set(ShieldHealthKey, amount);
            zdo.Set(ShieldExpiryKey, ZNet.instance.GetTime().Ticks + 40 * TimeSpan.TicksPerSecond);
            CreateShield(character, amount, 40f);
            return true;
        }

        private static void CreateShield(Character character, float amount, float duration)
        {
            if (!EnsurePrototype())
            {
                return;
            }
            SE_Shield added = character.GetSEMan().AddStatusEffect(shieldPrototype) as SE_Shield;
            if (added != null)
            {
                added.m_ttl = duration;
                ShieldTotal(added) = amount;
                ShieldDamage(added) = 0f;
            }
        }

        private static bool EnsurePrototype()
        {
            if (shieldPrototype != null)
            {
                return true;
            }
            SE_Shield source = ObjectDB.instance != null
                ? ObjectDB.instance.GetStatusEffect("GoblinShaman_shield".GetStableHashCode()) as SE_Shield
                : null;
            if (source == null)
            {
                if (!warnedMissingShield && ObjectDB.instance != null)
                {
                    warnedMissingShield = true;
                    Jotunn.Logger.LogError("Goblin Shaman shield status effect is unavailable.");
                }
                return false;
            }
            shieldPrototype = UnityEngine.Object.Instantiate(source);
            shieldPrototype.name = ShieldName;
            NameHash(shieldPrototype) = 0;
            shieldPrototype.m_ttlPerItemLevel = 0;
            return true;
        }

        internal static void RecordDamage(Character character, HitData hit)
        {
            if (character == null || !character.IsOwner() || hit == null ||
                hit.GetTotalDamage() <= 0f || ZNet.instance == null)
            {
                return;
            }
            bool shielded = false;
            foreach (EnemyModifierDefinition modifier in EnemyModifiers.GetModifiers(character))
            {
                shielded |= modifier.Id == "shielded";
            }
            if (!shielded)
            {
                return;
            }
            ZNetView view = character.m_nViewOverride != null
                ? character.m_nViewOverride : character.GetComponent<ZNetView>();
            if (view != null && view.IsValid())
            {
                view.GetZDO().Set(LastDamageKey, ZNet.instance.GetTime().Ticks);
            }
        }

        internal static void CheckNewShield(SEMan manager, StatusEffect added)
        {
            if (!(added is SE_Shield incoming) || incoming.name == ShieldName)
            {
                return;
            }
            Character character = ManagerCharacter(manager);
            if (character == null || !character.IsOwner())
            {
                return;
            }
            SE_Shield ours = manager.GetStatusEffect(ShieldName.GetStableHashCode()) as SE_Shield;
            if (ours == null)
            {
                return;
            }
            if (EnemyModifierCombatRules.ShouldReplaceShield(
                ShieldTotal(ours) - ShieldDamage(ours), ShieldTotal(incoming) - ShieldDamage(incoming)))
            {
                manager.RemoveStatusEffect(ours, true);
                ZNetView view = character.m_nViewOverride != null
                    ? character.m_nViewOverride : character.GetComponent<ZNetView>();
                if (view != null && view.IsValid())
                {
                    view.GetZDO().Set(ShieldHealthKey, 0f);
                }
            }
            else
            {
                manager.RemoveStatusEffect(incoming, true);
            }
        }

        internal static void SaveDamage(SE_Shield shield)
        {
            if (shield.name != ShieldName)
            {
                return;
            }
            Character character = EffectCharacter(shield);
            ZNetView view = character != null
                ? (character.m_nViewOverride != null ? character.m_nViewOverride : character.GetComponent<ZNetView>())
                : null;
            if (view != null && view.IsValid() && view.IsOwner())
            {
                view.GetZDO().Set(ShieldHealthKey, Mathf.Max(0f, ShieldTotal(shield) - ShieldDamage(shield)));
            }
        }
    }

    [HarmonyPatch(typeof(SEMan), nameof(SEMan.OnDamaged))]
    internal static class EnemyShieldDamageTimerPatch
    {
        private static void Prefix(HitData hit)
        {
            EnemyModifierShields.RecordDamage(EnemyModifierDamageContextPatch.Current?.Target, hit);
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage),
        new[] { typeof(HitData), typeof(bool), typeof(bool), typeof(HitData.DamageModifier) })]
    internal static class EnemyShieldHealthDamagePatch
    {
        private static void Prefix(Character __instance, out float __state)
        {
            __state = __instance.GetHealth();
        }

        private static void Postfix(Character __instance, HitData hit, float __state)
        {
            if (__instance.GetHealth() < __state)
            {
                EnemyModifierShields.RecordDamage(__instance, hit);
            }
        }
    }

    [HarmonyPatch(typeof(SE_Shield), nameof(SE_Shield.OnDamaged))]
    internal static class EnemyShieldDamagePatch
    {
        private static void Postfix(SE_Shield __instance)
        {
            EnemyModifierShields.SaveDamage(__instance);
        }
    }

    [HarmonyPatch(typeof(SEMan), nameof(SEMan.AddStatusEffect),
        new[] { typeof(StatusEffect), typeof(bool), typeof(int), typeof(float), typeof(short) })]
    internal static class EnemyShieldNewEffectPatch
    {
        private static void Postfix(SEMan __instance, StatusEffect __result)
        {
            EnemyModifierShields.CheckNewShield(__instance, __result);
        }
    }
}
