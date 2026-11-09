using System;
using System.Collections.Generic;
using Jotunn.Managers;
using UnityEngine;

namespace Runecarved
{
    internal sealed class EnemyModifierCombatAbilities : MonoBehaviour
    {
        private const string HealVisualName = "Runecarved_HealerVisual";
        private const string HealTimeKey = "runecarved_healer_time";
        private const string CastTimeKey = "runecarved_caster_time";
        private const string VisualTimeKey = "runecarved_healer_visual_time";
        private const string HealTargetCountKey = "runecarved_healer_target_count";
        private const string HealTargetKey = "runecarved_healer_target_";
        private const string HealAmountKey = "runecarved_healer_amount";
        private const string HealTicksKey = "runecarved_healer_ticks";
        private const int HealTickCount = 8;
        private const long HealTickInterval = TimeSpan.TicksPerSecond / 2;
        private static GameObject healVisual;
        private static GameObject fireball;
        private ZNetView view;

        internal static void Register()
        {
            PrefabManager.OnVanillaPrefabsAvailable += RegisterPrefabs;
        }

        private static void RegisterPrefabs()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= RegisterPrefabs;
            fireball = PrefabManager.Instance.GetPrefab("GoblinShaman_projectile_fireball");
            if (fireball == null || fireball.GetComponent<Projectile>() == null)
            {
                Jotunn.Logger.LogError("Caster fireball prefab is unavailable.");
                fireball = null;
            }

            GameObject source = PrefabManager.Instance.GetPrefab("shaman_heal_aoe");
            if (source == null)
            {
                Jotunn.Logger.LogError("Healer visual prefab is unavailable.");
                return;
            }
            healVisual = PrefabManager.Instance.CreateEmptyPrefab(HealVisualName);
            if (healVisual == null)
            {
                Jotunn.Logger.LogError("Could not register Healer visual prefab.");
                return;
            }
            DestroyImmediate(healVisual.GetComponent<Collider>());
            DestroyImmediate(healVisual.GetComponent<MeshRenderer>());
            DestroyImmediate(healVisual.GetComponent<MeshFilter>());
            healVisual.GetComponent<ZNetView>().m_persistent = false;
            foreach (string childName in new[] { "flames", "flames_world", "Point light" })
            {
                Transform child = source.transform.Find(childName);
                if (child == null)
                {
                    Jotunn.Logger.LogWarning($"Healer visual child '{childName}' is unavailable.");
                    continue;
                }
                Instantiate(child.gameObject, healVisual.transform, false);
            }
            healVisual.AddComponent<EnemyModifierCombatAbilities>();
            PrefabManager.Instance.AddPrefab(healVisual);
        }

        private void Awake()
        {
            view = GetComponent<ZNetView>();
        }

        private void FixedUpdate()
        {
            if (view == null || !view.IsValid() || !view.IsOwner() || ZNet.instance == null ||
                ZNetScene.instance == null)
            {
                return;
            }
            ZDO zdo = view.GetZDO();
            long created = zdo.GetLong(VisualTimeKey);
            if (created <= 0)
            {
                return;
            }

            long elapsed = ZNet.instance.GetTime().Ticks - created;
            int dueTicks = (int)Math.Min(HealTickCount, Math.Max(0L, elapsed / HealTickInterval));
            int previousTicks = zdo.GetInt(HealTicksKey);
            if (dueTicks > previousTicks)
            {
                float amount = zdo.GetFloat(HealAmountKey) * (dueTicks - previousTicks) / HealTickCount;
                int targetCount = zdo.GetInt(HealTargetCountKey);
                for (int i = 0; i < targetCount; i++)
                {
                    GameObject targetObject = ZNetScene.instance.FindInstance(zdo.GetZDOID(HealTargetKey + i));
                    Character target = targetObject != null ? targetObject.GetComponent<Character>() : null;
                    if (target != null && !target.IsDead())
                    {
                        target.Heal(amount);
                    }
                    else if (target == null)
                    {
                        Jotunn.Logger.LogWarning($"Healer target {zdo.GetZDOID(HealTargetKey + i)} is unavailable; missed {dueTicks - previousTicks} healing tick(s).");
                    }
                }
                zdo.Set(HealTicksKey, dueTicks);
            }
            if (dueTicks == HealTickCount)
            {
                ZNetScene.instance.Destroy(gameObject);
            }
        }

        internal static void UpdateCaster(Character character)
        {
            if (!character.IsOwner() || character.IsDead() || ZNet.instance == null)
            {
                return;
            }

            bool healer = false;
            bool caster = false;
            foreach (EnemyModifierDefinition modifier in EnemyModifiers.GetModifiers(character))
            {
                healer |= modifier.Id == "healer";
                caster |= modifier.Id == "caster";
            }
            if (!healer && !caster)
            {
                return;
            }

            BaseAI ai = character.GetBaseAI();
            Character target = ai != null ? ai.GetTargetCreature() : null;
            if (ai == null || !ai.IsAlerted() || target == null || target.IsDead())
            {
                return;
            }
            ZNetView nview = character.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid())
            {
                return;
            }
            ZDO zdo = nview.GetZDO();
            long now = ZNet.instance.GetTime().Ticks;
            if (healer && healVisual != null && Ready(zdo, HealTimeKey, now))
            {
                Heal(character, now);
                zdo.Set(HealTimeKey, now);
            }
            if (caster && fireball != null && target.IsPlayer() && Ready(zdo, CastTimeKey, now))
            {
                Cast(character, target);
                zdo.Set(CastTimeKey, now);
            }
        }

        private static bool Ready(ZDO zdo, string key, long now)
        {
            long last = zdo.GetLong(key);
            if (last == 0 || last > now)
            {
                zdo.Set(key, now);
                return false;
            }
            return now - last >= 8 * TimeSpan.TicksPerSecond;
        }

        private static void Heal(Character source, long now)
        {
            GameObject effect = Instantiate(healVisual, source.transform.position, Quaternion.identity);
            ZNetView effectView = effect.GetComponent<ZNetView>();
            if (effectView == null || !effectView.IsValid())
            {
                Jotunn.Logger.LogWarning("Could not create the Healer visual.");
                Destroy(effect);
                return;
            }
            var targets = new List<Character>();
            Character.GetCharactersInRange(source.transform.position, 5f, targets);
            ZDO zdo = effectView.GetZDO();
            int count = 0;
            foreach (Character target in targets)
            {
                if (target == null || target.IsDead() || target.IsPlayer() || target.IsTamed() ||
                    target.IsBoss() || !(target.GetBaseAI() is MonsterAI) || BaseAI.IsEnemy(source, target))
                {
                    continue;
                }
                ZDOID targetId = target.GetZDOID();
                if (targetId == ZDOID.None)
                {
                    Jotunn.Logger.LogWarning($"Cannot heal {target.name}: its network view is invalid.");
                    continue;
                }
                zdo.Set(HealTargetKey + count, targetId);
                count++;
            }
            zdo.Set(HealTargetCountKey, count);
            zdo.Set(HealAmountKey, source.GetMaxHealth() * 0.3f);
            zdo.Set(VisualTimeKey, now);
        }

        private static void Cast(Character source, Character target)
        {
            Vector3 start = source.GetCenterPoint();
            Vector3 direction = target.GetCenterPoint() - start;
            if (direction.sqrMagnitude < 0.001f)
            {
                return;
            }
            direction.Normalize();
            GameObject instance = Instantiate(fireball, start, Quaternion.LookRotation(direction));
            Projectile projectile = instance.GetComponent<Projectile>();
            HitData hit = new HitData();
            hit.m_damage.m_fire = source.GetMaxHealth() * 0.5f;
            hit.m_hitType = HitData.HitType.EnemyHit;
            hit.m_dodgeable = true;
            hit.m_blockable = true;
            projectile.Setup(source, direction * 20f, -1f, hit, null, null);
        }
    }
}
