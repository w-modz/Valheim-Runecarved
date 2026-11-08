using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace Runecarved
{
    [HarmonyPatch(typeof(Attack), "FireProjectileBurst")]
    internal static class EnemyModifierProjectilePatch
    {
        private static readonly float[] SpreadAngles = { -20f, -10f, 10f, 20f };
        private static readonly AccessTools.FieldRef<Attack, ItemDrop.ItemData> AmmoItem =
            AccessTools.FieldRefAccess<Attack, ItemDrop.ItemData>("m_ammoItem");

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo setup = AccessTools.Method(typeof(IProjectile), nameof(IProjectile.Setup));
            MethodInfo replacement = AccessTools.Method(typeof(EnemyModifierProjectilePatch), nameof(SetupProjectile));
            var result = new List<CodeInstruction>();
            int matches = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(setup))
                {
                    var loadAttack = new CodeInstruction(OpCodes.Ldarg_0);
                    // Keep branches and exception handlers attached to the injected instruction
                    loadAttack.labels.AddRange(instruction.labels);
                    instruction.labels.Clear();
                    loadAttack.blocks.AddRange(instruction.blocks);
                    instruction.blocks.Clear();
                    result.Add(loadAttack);
                    result.Add(new CodeInstruction(OpCodes.Call, replacement));
                    matches++;
                }
                else
                {
                    result.Add(instruction);
                }
            }
            if (matches != 1)
            {
                // Fail and throw exception if a game update changes the method shape instead of skipping the patch
                throw new InvalidOperationException($"Expected one projectile setup in FireProjectileBurst, found {matches}.");
            }
            return result;
        }

        private static void SetupProjectile(
            IProjectile projectile,
            Character owner,
            Vector3 velocity,
            float hitNoise,
            HitData hit,
            ItemDrop.ItemData item,
            ItemDrop.ItemData ammo,
            Attack attack)
        {
            int extraProjectiles = 0;
            float speedMultiplier = 1f;
            foreach (EnemyModifierDefinition modifier in EnemyModifiers.GetModifiers(owner))
            {
                extraProjectiles += modifier.ExtraProjectiles;
                speedMultiplier *= modifier.ProjectileSpeedMultiplier;
            }
            velocity *= speedMultiplier;
            projectile.Setup(owner, velocity, hitNoise, hit, item, ammo);

            if (extraProjectiles == 0)
            {
                return;
            }
            ItemDrop.ItemData equippedAmmo = AmmoItem(attack);
            // Ammo-specific prefabs take precedence, with the attack prefab as the vanilla fallback
            GameObject prefab = equippedAmmo != null && equippedAmmo.m_shared.m_attack.m_attackProjectile != null
                ? equippedAmmo.m_shared.m_attack.m_attackProjectile
                : attack.m_attackProjectile;
            if (prefab == null || !(projectile is Component original))
            {
                Jotunn.Logger.LogWarning("Cannot create Volley projectiles: the original projectile or its prefab is unavailable.");
                return;
            }
            if (extraProjectiles > SpreadAngles.Length)
            {
                throw new InvalidOperationException("Volley projectile count exceeds the configured spread angles.");
            }
            for (int i = 0; i < extraProjectiles; i++)
            {
                Vector3 extraVelocity = Quaternion.AngleAxis(SpreadAngles[i], Vector3.up) * velocity;
                Quaternion rotation = Quaternion.AngleAxis(SpreadAngles[i], Vector3.up) * original.transform.rotation;
                GameObject clone = UnityEngine.Object.Instantiate(prefab, original.transform.position, rotation);
                IProjectile extra = clone.GetComponent<IProjectile>();
                if (extra == null)
                {
                    ZNetView view = clone.GetComponent<ZNetView>();
                    if (view != null && view.IsValid())
                    {
                        view.Destroy();
                    }
                    else
                    {
                        UnityEngine.Object.Destroy(clone);
                    }
                    Jotunn.Logger.LogWarning($"Cannot create Volley projectile: prefab '{prefab.name}' has no projectile component.");
                    continue;
                }
                extra.Setup(owner, extraVelocity, hitNoise, hit?.Clone(), item, ammo);
                if (attack.m_spawnOnHitChance > 0f && attack.m_spawnOnHit != null && extra is Projectile spawned)
                {
                    spawned.m_spawnOnHit = attack.m_spawnOnHit;
                    spawned.m_spawnOnHitChance = attack.m_spawnOnHitChance;
                }
            }
        }
    }
}
