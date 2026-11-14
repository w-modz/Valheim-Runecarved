using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Runecarved
{
    // ApplyResistance has no target parameter; retain the matching RPC context for Shattering.
    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    internal static class EnemyModifierDamageContextPatch
    {
        internal sealed class DamageContext
        {
            internal Character Target;
            internal HitData Hit;
        }

        [ThreadStatic]
        internal static DamageContext Current;

        private static void Prefix(Character __instance, HitData hit, out DamageContext __state)
        {
            __state = Current;
            Current = new DamageContext { Target = __instance, Hit = hit };
        }

        private static Exception Finalizer(DamageContext __state, Exception __exception)
        {
            Current = __state;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(HitData), nameof(HitData.ApplyResistance))]
    internal static class EnemyModifierTarredPatch
    {
        private static readonly AccessTools.FieldRef<StatusEffect, float> EffectTime =
            AccessTools.FieldRefAccess<StatusEffect, float>("m_time");

        private static void Postfix(HitData __instance)
        {
            EnemyModifierDamageContextPatch.DamageContext context = EnemyModifierDamageContextPatch.Current;
            if (context == null || !ReferenceEquals(context.Hit, __instance) ||
                !context.Target.IsPlayer() || __instance.m_hitType != HitData.HitType.EnemyHit)
            {
                return;
            }

            foreach (EnemyModifierDefinition modifier in EnemyModifiers.GetModifiers(__instance.GetAttacker()))
            {
                if (!modifier.Tarred)
                {
                    continue;
                }

                SEMan effects = context.Target.GetSEMan();
                StatusEffect tarred = effects.GetStatusEffect(SEMan.s_statusEffectTared);
                if (tarred == null)
                {
                    tarred = effects.AddStatusEffect(SEMan.s_statusEffectTared);
                }
                if (tarred != null && EnemyModifierTiming.ShouldExtendTarred(tarred.m_ttl, tarred.GetRemaningTime()))
                {
                    EffectTime(tarred) = EnemyModifierTiming.TarredElapsed(tarred.m_ttl);
                }
                break;
            }
        }
    }

    [HarmonyPatch(typeof(HitData), nameof(HitData.ApplyResistance))]
    internal static class EnemyModifierShatteringPatch
    {
        private static void Postfix(HitData __instance)
        {
            EnemyModifierDamageContextPatch.DamageContext context = EnemyModifierDamageContextPatch.Current;
            if (context == null || !ReferenceEquals(context.Hit, __instance))
            {
                return;
            }

            if (!context.Target.IsPlayer() || __instance.m_hitType != HitData.HitType.EnemyHit ||
                __instance.GetTotalDamage() <= 0.1f || __instance.m_staggerMultiplier >= 100f ||
                CinematicsManager.IsPlaying())
            {
                return;
            }

            foreach (EnemyModifierDefinition modifier in EnemyModifiers.GetModifiers(__instance.GetAttacker()))
            {
                if (modifier.Shattering)
                {
                    context.Target.Stagger(__instance.m_dir);
                    break;
                }
            }
        }
    }

    [HarmonyPatch(
        typeof(Character),
        nameof(Character.ApplyDamage),
        new[] { typeof(HitData), typeof(bool), typeof(bool), typeof(HitData.DamageModifier) })]
    internal static class EnemyModifierDamageTakenPatch
    {
        private static void Prefix(Character __instance, ref HitData hit)
        {
            if (hit == null)
            {
                return;
            }

            // Fire and poison are applied as separate damage-over-time hits by the game.
            bool cloned = false;
            IReadOnlyList<EnemyModifierDefinition> modifiers = EnemyModifiers.GetModifiers(__instance);
            foreach (EnemyModifierDefinition modifier in modifiers)
            {
                if (modifier.ResistanceEffect == null && modifier.DamageTakenMultiplier == 1f)
                {
                    continue;
                }
                if (!cloned)
                {
                    hit = hit.Clone();
                    cloned = true;
                }
                modifier.ResistanceEffect?.Invoke(ref hit.m_damage);
                hit.m_damage.Modify(modifier.DamageTakenMultiplier);
            }
            if (BossModifierScaled.IsProtected(__instance, modifiers))
            {
                if (!cloned)
                {
                    hit = hit.Clone();
                }
                hit.m_damage.Modify(0.5f);
            }
        }
    }

    [HarmonyPatch]
    internal static class EnemyModifierMovementPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(Character), "GetAttackSpeedFactorMovement");
            yield return AccessTools.DeclaredMethod(typeof(Humanoid), "GetAttackSpeedFactorMovement");
        }

        private static void Postfix(Character __instance, ref float __result)
        {
            IReadOnlyList<EnemyModifierDefinition> modifiers = EnemyModifiers.GetModifiers(__instance);
            foreach (EnemyModifierDefinition modifier in modifiers)
            {
                __result *= modifier.MovementMultiplier;
            }
            if (EnemyModifiers.IsFrenzied(__instance, modifiers))
            {
                __result *= EnemyModifiers.FrenzyMultiplier;
            }
        }
    }
}
