using System;
using System.Collections.Generic;
using Jotunn.Managers;
using UnityEngine;

namespace Runecarved
{
    internal sealed class EnemyModifierFiretread : MonoBehaviour
    {
        private const string PrefabName = "Runecarved_Firetread";
        private const string CampfirePrefabName = "fire_pit";
        private const float CampfireFlameRadius = 0.5f;
        private const float FlareOpacityMultiplier = 0.25f;
        private const string SourceKey = "runecarved_firetread_source";
        private const string CreatedKey = "runecarved_firetread_created";
        private const string DamageKey = "runecarved_firetread_damage";
        private const float Radius = 1.5f;
        private const double Lifetime = 8.0;
        private static readonly Dictionary<ZDOID, double> LastHits = new Dictionary<ZDOID, double>();
        private static Player lastPlayer;
        private static GameObject prefab;

        private ZNetView view;

        internal static void Register()
        {
            PrefabManager.OnVanillaPrefabsAvailable += AddPrefab;
        }

        private static void AddPrefab()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= AddPrefab;
            prefab = PrefabManager.Instance.CreateEmptyPrefab(PrefabName);
            if (prefab == null)
            {
                Jotunn.Logger.LogError("Could not register the Firetread prefab.");
                return;
            }
            DestroyImmediate(prefab.GetComponent<Collider>());
            DestroyImmediate(prefab.GetComponent<MeshRenderer>());
            DestroyImmediate(prefab.GetComponent<MeshFilter>());
            GameObject campfire = PrefabManager.Instance.GetPrefab(CampfirePrefabName);
            if (campfire == null)
            {
                Jotunn.Logger.LogWarning("Cannot display Firetread: campfire prefab was not found.");
            }
            else
            {
                Transform flames = campfire.transform.Find("_enabled_high/Particles");
                if (flames == null)
                {
                    Jotunn.Logger.LogWarning("Cannot display Firetread: campfire particles were not found.");
                }
                else
                {
                    GameObject visual = Instantiate(flames.gameObject, prefab.transform, false);
                    visual.SetActive(true);
                    float scale = Radius / CampfireFlameRadius;
                    visual.transform.localScale = new Vector3(scale, 1f, scale);
                    foreach (ParticleSystem particles in visual.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        ParticleSystem.MainModule main = particles.main;
                        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                    }
                    Transform smoke = visual.transform.Find("smoke (1)");
                    ParticleSystem smokeParticles = smoke != null ? smoke.GetComponent<ParticleSystem>() : null;
                    if (smokeParticles == null)
                    {
                        Jotunn.Logger.LogWarning("Cannot reduce Firetread smoke: campfire smoke particles were not found.");
                    }
                    else
                    {
                        smokeParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                        smoke.gameObject.SetActive(false);
                    }

                    Transform flare = visual.transform.Find("flare");
                    ParticleSystem flareParticles = flare != null ? flare.GetComponent<ParticleSystem>() : null;
                    if (flareParticles == null)
                    {
                        Jotunn.Logger.LogWarning("Cannot reduce Firetread flare: campfire flare particles were not found.");
                    }
                    else
                    {
                        ParticleSystem.MainModule main = flareParticles.main;
                        Color color = main.startColor.color;
                        color.a *= FlareOpacityMultiplier;
                        main.startColor = color;
                    }
                }
            }
            prefab.AddComponent<EnemyModifierFiretread>();
            PrefabManager.Instance.AddPrefab(prefab);
        }

        internal static void UpdateEmitter(Character character, float dt)
        {
            FiretreadEmitter emitter = character.GetComponent<FiretreadEmitter>();
            bool enabled = character.IsOwner() && !character.IsDead();
            if (enabled)
            {
                enabled = false;
                foreach (EnemyModifierDefinition modifier in EnemyModifiers.GetModifiers(character))
                {
                    if (modifier.Firetread)
                    {
                        enabled = true;
                        break;
                    }
                }
            }

            if (!enabled)
            {
                if (emitter != null)
                {
                    emitter.ResetPosition();
                }
                return;
            }
            if (emitter == null)
            {
                emitter = character.gameObject.AddComponent<FiretreadEmitter>();
                emitter.ResetPosition();
            }
            emitter.Tick(character, dt);
        }

        private void Awake()
        {
            view = GetComponent<ZNetView>();
        }

        private void FixedUpdate()
        {
            if (view == null || !view.IsValid() || ZNet.instance == null)
            {
                return;
            }

            ZDO zdo = view.GetZDO();
            long created = zdo.GetLong(CreatedKey);
            ZDOID source = zdo.GetZDOID(SourceKey);
            if (created == 0 || source.IsNone())
            {
                return;
            }
            double now = ZNet.instance.GetTimeSeconds();
            if (EnemyModifierTiming.IsExpired(now, created / (double)TimeSpan.TicksPerSecond, Lifetime))
            {
                if (view.IsOwner())
                {
                    ZNetScene.instance.Destroy(gameObject);
                }
                return;
            }

            Player player = Player.m_localPlayer;
            if (player == null || !player.IsOwner() || player.IsDead())
            {
                return;
            }
            if (lastPlayer != player)
            {
                LastHits.Clear();
                lastPlayer = player;
            }
            Collider collider = player.GetComponent<Collider>();
            if (collider == null || (collider.ClosestPoint(transform.position) - transform.position).sqrMagnitude > Radius * Radius)
            {
                return;
            }
            if (LastHits.TryGetValue(source, out double lastHit) && !EnemyModifierTiming.CanHit(now, lastHit))
            {
                return;
            }
            LastHits[source] = now;
            var stale = new List<ZDOID>();
            foreach (KeyValuePair<ZDOID, double> entry in LastHits)
            {
                if (now - entry.Value >= Lifetime)
                {
                    stale.Add(entry.Key);
                }
            }
            foreach (ZDOID id in stale)
            {
                LastHits.Remove(id);
            }

            var hit = new HitData();
            hit.m_damage.m_fire = zdo.GetFloat(DamageKey);
            hit.m_point = collider.ClosestPoint(transform.position);
            hit.m_dir = Vector3.up;
            player.Damage(hit);
        }

        private sealed class FiretreadEmitter : MonoBehaviour
        {
            private const float SpawnInterval = 0.25f;
            private const float MinimumMovement = 0.025f;
            private float elapsed;
            private float distance;
            private Vector3 previousPosition;

            internal void ResetPosition()
            {
                elapsed = 0f;
                distance = 0f;
                previousPosition = transform.position;
            }

            internal void Tick(Character character, float dt)
            {
                Vector3 position = character.transform.position;
                Vector3 movement = position - previousPosition;
                previousPosition = position;
                movement.y = 0f;
                distance += movement.magnitude;
                elapsed += dt;
                if (elapsed < SpawnInterval)
                {
                    return;
                }
                elapsed %= SpawnInterval;
                float moved = distance;
                distance = 0f;
                if (moved < MinimumMovement || prefab == null || ZNet.instance == null)
                {
                    return;
                }

                GameObject patch = Instantiate(prefab, position, Quaternion.identity);
                ZNetView patchView = patch.GetComponent<ZNetView>();
                if (patchView == null || !patchView.IsValid())
                {
                    Jotunn.Logger.LogWarning("Could not create a networked Firetread patch.");
                    Destroy(patch);
                    return;
                }
                ZDO zdo = patchView.GetZDO();
                zdo.Set(SourceKey, character.GetZDOID());
                zdo.Set(DamageKey, character.GetMaxHealth() * 0.10f);
                zdo.Set(CreatedKey, ZNet.instance.GetTime().Ticks);
            }
        }
    }
}
