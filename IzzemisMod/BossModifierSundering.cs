using System;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.Rendering;

namespace Runecarved
{
    internal static class BossModifierSundering
    {
        private const string EffectName = "Runecarved_Sundered";
        private static bool warnedMissingEffect;

        internal static void Register()
        {
            Runecarved.ModLocalization.AddTranslation("$runecarved_sundered", "Sundered");
            Runecarved.ModLocalization.AddTranslation("$runecarved_sundered_tooltip", "Body armor reduced by 50%.");

            StatusEffect effect = ScriptableObject.CreateInstance<StatusEffect>();
            effect.name = EffectName;
            effect.m_name = "$runecarved_sundered";
            effect.m_tooltip = "$runecarved_sundered_tooltip";
            effect.m_ttl = 15f;
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                effect.m_icon = RuneItems.LoadIconResource("Icons.Sundered.png") ??
                    RuneItems.CreatePlaceholderIcon(new Color(0.8f, 0.2f, 0.1f, 1f));
            }
            if (!ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(effect, false)))
            {
                Jotunn.Logger.LogError("Could not register the Sundered status effect.");
            }
        }

        internal static void Apply(Character target)
        {
            SEMan effects = target.GetSEMan();
            int hash = EffectName.GetStableHashCode();
            StatusEffect existing = effects.GetStatusEffect(hash);
            if (existing != null)
            {
                existing.ResetTime();
                return;
            }
            StatusEffect effect = ObjectDB.instance != null ? ObjectDB.instance.GetStatusEffect(hash) : null;
            if (effect == null || effects.AddStatusEffect(effect) == null)
            {
                if (!warnedMissingEffect)
                {
                    Jotunn.Logger.LogError("Could not apply the Sundered status effect.");
                    warnedMissingEffect = true;
                }
                return;
            }
            warnedMissingEffect = false;
        }

        internal static bool IsSundered(Player player)
        {
            return player.GetSEMan().GetStatusEffect(EffectName.GetStableHashCode()) != null;
        }
    }

    [HarmonyPatch(typeof(HitData), nameof(HitData.ApplyResistance))]
    internal static class BossModifierSunderingHitPatch
    {
        private static void Postfix(HitData __instance)
        {
            EnemyModifierDamageContextPatch.DamageContext context = EnemyModifierDamageContextPatch.Current;
            if (context == null || !ReferenceEquals(context.Hit, __instance) ||
                !context.Target.IsPlayer() || !context.Target.IsOwner() ||
                __instance.m_hitType != HitData.HitType.EnemyHit || CinematicsManager.IsPlaying())
            {
                return;
            }

            Character attacker = __instance.GetAttacker();
            if (attacker == null || !attacker.IsBoss())
            {
                return;
            }
            foreach (EnemyModifierDefinition modifier in EnemyModifiers.GetModifiers(attacker))
            {
                if (modifier.Id == EnemyModifierCatalog.SunderingId)
                {
                    BossModifierSundering.Apply(context.Target);
                    break;
                }
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.GetBodyArmor))]
    internal static class SunderedArmorPatch
    {
        private static void Postfix(Player __instance, ref float __result)
        {
            if (BossModifierSundering.IsSundered(__instance))
            {
                __result *= 0.5f;
            }
        }
    }
}
