using System.Collections.Generic;
using HarmonyLib;

namespace Runecarved
{
    internal static class BossModifierScaled
    {
        private const string LastMeleeKey = "runecarved_scaled_last_melee";
        private static bool warnedMissingTime;

        internal static bool HasModifier(Character character, IReadOnlyList<EnemyModifierDefinition> modifiers)
        {
            if (character == null || !character.IsBoss())
            {
                return false;
            }
            foreach (EnemyModifierDefinition modifier in modifiers)
            {
                if (modifier.Id == "scaled")
                {
                    return true;
                }
            }
            return false;
        }

        internal static bool IsProtected(Character character, IReadOnlyList<EnemyModifierDefinition> modifiers)
        {
            if (!HasModifier(character, modifiers))
            {
                return false;
            }
            ZNetView view = GetView(character);
            if (view == null || !view.IsValid())
            {
                return false;
            }
            if (ZNet.instance == null)
            {
                if (!warnedMissingTime)
                {
                    warnedMissingTime = true;
                    Jotunn.Logger.LogWarning("Scaled cannot check its melee timer without network time.");
                }
                return false;
            }
            warnedMissingTime = false;
            ZDO zdo = view.GetZDO();
            long last = zdo.GetLong(LastMeleeKey);
            long now = ZNet.instance.GetTime().Ticks;
            if (last > now)
            {
                if (view.IsOwner())
                {
                    zdo.Set(LastMeleeKey, now);
                }
                return false;
            }
            return last == 0 || EnemyModifierCombatRules.IntervalElapsed(now, last, 15);
        }

        internal static ZNetView GetView(Character character)
        {
            return character.m_nViewOverride != null
                ? character.m_nViewOverride
                : character.GetComponent<ZNetView>();
        }

        internal static void RecordMelee(ZNetView view)
        {
            if (ZNet.instance == null)
            {
                Jotunn.Logger.LogWarning("Scaled could not record melee damage without network time.");
                return;
            }
            view.GetZDO().Set(LastMeleeKey, ZNet.instance.GetTime().Ticks);
        }
    }

    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    internal static class BossModifierScaledMeleePatch
    {
        private static void Prefix(Character __instance, HitData hit, out float? __state)
        {
            __state = null;
            if (__instance == null || !__instance.IsBoss() || hit == null || hit.m_ranged ||
                (hit.m_hitType != HitData.HitType.PlayerHit && hit.m_hitType != HitData.HitType.EnemyHit))
            {
                return;
            }
            Character attacker = hit.GetAttacker();
            ZNetView view = BossModifierScaled.GetView(__instance);
            if (attacker == null || attacker == __instance || view == null || !view.IsValid() || !view.IsOwner() ||
                !BossModifierScaled.HasModifier(__instance, EnemyModifiers.GetModifiers(__instance)))
            {
                return;
            }
            __state = __instance.GetHealth();
        }

        private static void Postfix(Character __instance, float? __state)
        {
            if (!__state.HasValue || __instance.GetHealth() >= __state.Value)
            {
                return;
            }
            ZNetView view = BossModifierScaled.GetView(__instance);
            if (view != null && view.IsValid() && view.IsOwner())
            {
                BossModifierScaled.RecordMelee(view);
            }
        }
    }
}
