using System;
using System.Collections.Generic;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.Rendering;

namespace Runecarved
{
    internal sealed class BossModifierBinding : MonoBehaviour, IDestructible, IHitProjectile
    {
        private const string PrefabName = "Runecarved_Binding";
        private const string StartRpc = "Runecarved_StartBinding";
        private const string DamageRpc = "Runecarved_BindingDamage";
        private const string CycleKey = "runecarved_binding_cycle";
        private const string TargetKey = "runecarved_binding_target";
        private const string StartKey = "runecarved_binding_start";
        private const string HealthKey = "runecarved_binding_health";
        private const string StatusName = "Runecarved_BindingImmobilized";
        private const string WarningName = "Runecarved_BindingWarning";
        private const string VisualName = "Runecarved_BindingVisual";
        private const int WarningSeconds = 5;
        private const int DurationSeconds = 5;
        private const float PlayerRange = 100f;
        private static readonly Dictionary<string, float> BossHealth = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            { "Eikthyr", 100f },
            { "gd_king", 200f },
            { "Bonemass", 300f },
            { "Dragon", 400f },
            { "GoblinKing", 500f },
            { "SeekerQueen", 600f },
            { "Fader", 700f },
            { "FrozenKing", 800f },
            { "FrozenKing_p2", 800f },
            { "FrozenKing_p3", 800f }
        };
        private static readonly HashSet<string> UnknownBossWarnings = new HashSet<string>();
        private static readonly List<BossModifierBinding> Instances = new List<BossModifierBinding>();
        private static readonly AccessTools.FieldRef<StatusEffect, int> StatusHash =
            AccessTools.FieldRefAccess<StatusEffect, int>("m_nameHash");
        private static readonly AccessTools.FieldRef<StatusEffect, float> StatusTime =
            AccessTools.FieldRefAccess<StatusEffect, float>("m_time");
        private static GameObject prefab;
        private static StatusEffect statusPrototype;
        private static bool warnedStatus;
        private static bool warnedVineVisual;
        private ZNetView view;
        private BoxCollider hitbox;
        private Player target;
        private bool statusApplied;
        private bool ignoredTargetCollision;
        private Material visualMaterial;

        internal static void Register()
        {
            Runecarved.ModLocalization.AddTranslation("$runecarved_binding_warning", "You will be bound in 5 seconds!");
            PrefabManager.OnVanillaPrefabsAvailable += RegisterPrefab;
        }

        private static void RegisterPrefab()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= RegisterPrefab;
            prefab = PrefabManager.Instance.CreateEmptyPrefab(PrefabName);
            if (prefab == null)
            {
                Jotunn.Logger.LogError("Could not register the Binding prefab.");
                return;
            }
            DestroyImmediate(prefab.GetComponent<MeshRenderer>());
            DestroyImmediate(prefab.GetComponent<MeshFilter>());
            BoxCollider collider = prefab.GetComponent<BoxCollider>();
            if (collider == null)
            {
                collider = prefab.AddComponent<BoxCollider>();
            }
            collider.center = new Vector3(0f, 1f, 0f);
            collider.size = new Vector3(1.2f, 2f, 0.3f);
            collider.enabled = false;
            prefab.layer = LayerMask.NameToLayer("hitbox");
            prefab.GetComponent<ZNetView>().m_persistent = false;
            prefab.AddComponent<BossModifierBinding>();
            PrefabManager.Instance.AddPrefab(prefab);
        }

        internal static void RegisterPlayer(Character character)
        {
            if (!(character is Player player))
            {
                return;
            }
            ZNetView playerView = player.m_nViewOverride != null
                ? player.m_nViewOverride : player.GetComponent<ZNetView>();
            if (playerView != null && playerView.IsValid())
            {
                playerView.Register<float>(StartRpc, (sender, health) => StartWarning(player, health));
            }
        }

        internal static void UpdateBoss(Character boss)
        {
            if (!boss.IsBoss() || !boss.IsOwner() || boss.IsDead() || ZNet.instance == null ||
                ZNetScene.instance == null || prefab == null)
            {
                return;
            }
            bool binding = false;
            foreach (EnemyModifierDefinition modifier in EnemyModifiers.GetModifiers(boss))
            {
                binding |= modifier.Id == "binding";
            }
            BaseAI ai = boss.GetBaseAI();
            Character combatTarget = ai != null ? ai.GetTargetCreature() : null;
            if (!binding || ai == null || !ai.IsAlerted() || combatTarget == null || combatTarget.IsDead())
            {
                return;
            }
            ZNetView bossView = boss.m_nViewOverride != null ? boss.m_nViewOverride : boss.GetComponent<ZNetView>();
            if (bossView == null || !bossView.IsValid())
            {
                return;
            }
            ZDO zdo = bossView.GetZDO();
            long now = ZNet.instance.GetTime().Ticks;
            long cycle = zdo.GetLong(CycleKey);
            if (cycle == 0 || cycle > now + WarningSeconds * TimeSpan.TicksPerSecond)
            {
                zdo.Set(CycleKey, now);
                return;
            }
            if (now < cycle || now - cycle < 25 * TimeSpan.TicksPerSecond)
            {
                return;
            }
            string bossName = Utils.GetPrefabName(boss.gameObject);
            if (!BossHealth.TryGetValue(bossName, out float health))
            {
                if (UnknownBossWarnings.Add(bossName))
                {
                    Jotunn.Logger.LogWarning($"Binding has no health defined for boss '{bossName}'.");
                }
                zdo.Set(CycleKey, now);
                return;
            }
            var players = new List<Player>();
            Player.GetPlayersInRange(boss.transform.position, PlayerRange, players);
            players.RemoveAll(player =>
            {
                if (player == null || player.IsDead())
                {
                    return true;
                }
                ZNetView playerView = player.m_nViewOverride != null
                    ? player.m_nViewOverride : player.GetComponent<ZNetView>();
                return playerView == null || !playerView.IsValid() || !playerView.GetZDO().HasOwner();
            });
            if (players.Count == 0)
            {
                return;
            }
            Player chosen = players[UnityEngine.Random.Range(0, players.Count)];
            ZNetView targetView = chosen.m_nViewOverride != null ? chosen.m_nViewOverride : chosen.GetComponent<ZNetView>();
            if (targetView == null || !targetView.IsValid() || !targetView.GetZDO().HasOwner())
            {
                return;
            }
            zdo.Set(CycleKey, now + WarningSeconds * TimeSpan.TicksPerSecond);
            targetView.InvokeRPC(StartRpc, health);
        }

        private static void StartWarning(Player player, float health)
        {
            if (!player.IsOwner() || player.IsDead() || prefab == null || ZNet.instance == null ||
                ZNetScene.instance == null || health <= 0f)
            {
                return;
            }
            GameObject binding = Instantiate(prefab, player.transform.position, Quaternion.identity);
            ZNetView bindingView = binding.GetComponent<ZNetView>();
            if (bindingView == null || !bindingView.IsValid() || !bindingView.IsOwner())
            {
                Jotunn.Logger.LogError("Could not create the Binding warning.");
                Destroy(binding);
                return;
            }
            ZDO zdo = bindingView.GetZDO();
            zdo.Set(TargetKey, player.GetZDOID());
            zdo.Set(HealthKey, health);
            zdo.Set(StartKey, ZNet.instance.GetTime().Ticks);
            player.Message(MessageHud.MessageType.Center, "$runecarved_binding_warning");
        }

        private void Awake()
        {
            view = GetComponent<ZNetView>();
            hitbox = GetComponent<BoxCollider>();
            if (view != null && view.IsValid())
            {
                view.Register<HitData>(DamageRpc, RPC_Damage);
                Instances.Add(this);
            }
        }

        private void Update()
        {
            if (view == null || !view.IsValid() || ZNet.instance == null || ZNetScene.instance == null)
            {
                return;
            }
            ZDO zdo = view.GetZDO();
            long start = zdo.GetLong(StartKey);
            if (start <= 0)
            {
                return;
            }
            long elapsed = ZNet.instance.GetTime().Ticks - start;
            if (target == null)
            {
                GameObject targetObject = ZNetScene.instance.FindInstance(zdo.GetZDOID(TargetKey));
                target = targetObject != null ? targetObject.GetComponent<Player>() : null;
            }
            if (target == null || target.IsDead() || elapsed < 0 ||
                elapsed >= (WarningSeconds + DurationSeconds) * TimeSpan.TicksPerSecond)
            {
                if (view.IsOwner() && (elapsed < 0 || elapsed >= (WarningSeconds + DurationSeconds) * TimeSpan.TicksPerSecond ||
                    target != null && target.IsDead()))
                {
                    ZNetScene.instance.Destroy(gameObject);
                }
                return;
            }

            transform.position = target.transform.position + target.transform.forward * 0.9f;
            transform.rotation = target.transform.rotation;
            if (view.IsOwner())
            {
                zdo.SetPosition(transform.position);
            }
            bool active = elapsed >= WarningSeconds * TimeSpan.TicksPerSecond;
            hitbox.enabled = active;
            if (active && !ignoredTargetCollision)
            {
                Collider targetCollider = target.GetComponent<Collider>();
                if (targetCollider != null)
                {
                    Physics.IgnoreCollision(hitbox, targetCollider);
                    ignoredTargetCollision = true;
                }
            }
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                UpdateVisual(active);
            }
            if (active && target.IsOwner() && !statusApplied && EnsureStatus())
            {
                SEMan effects = target.GetSEMan();
                StatusEffect status = effects.GetStatusEffect(StatusName.GetStableHashCode());
                float age = (elapsed - WarningSeconds * TimeSpan.TicksPerSecond) / (float)TimeSpan.TicksPerSecond;
                if (status == null)
                {
                    status = effects.AddStatusEffect(statusPrototype);
                    if (status != null)
                    {
                        StatusTime(status) = age;
                    }
                }
                else
                {
                    StatusTime(status) = Mathf.Min(StatusTime(status), age);
                }
                if (status != null)
                {
                    statusApplied = true;
                    warnedStatus = false;
                }
                else if (!warnedStatus)
                {
                    Jotunn.Logger.LogError("Could not apply Binding immobilization.");
                    warnedStatus = true;
                }
            }
        }

        private void UpdateVisual(bool active)
        {
            if (!active)
            {
                ParticleSystem warning = RarityItemParticles.Ensure(transform, new Color(1f, 0.4f, 0.05f, 1f),
                    new Vector3(0f, 1f, -0.9f), 1f, WarningName);
                if (warning != null)
                {
                    ParticleSystem.MainModule main = warning.main;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
                    ParticleSystem.EmissionModule emission = warning.emission;
                    emission.rateOverTime = 45f;
                }
                return;
            }
            RarityItemParticles.Remove(transform, WarningName);
            if (transform.Find(VisualName) != null)
            {
                return;
            }
            GameObject visual = new GameObject(VisualName);
            visual.transform.SetParent(transform, false);
            GameObject projectile = PrefabManager.Instance.GetPrefab("gdking_root_projectile");
            Transform rootParticles = projectile != null ? projectile.transform.Find("Particle System") : null;
            ParticleSystemRenderer sourceRenderer = rootParticles != null
                ? rootParticles.GetComponent<ParticleSystemRenderer>() : null;
            if (sourceRenderer != null && sourceRenderer.sharedMaterial != null)
            {
                warnedVineVisual = false;
                visual.transform.localPosition = new Vector3(0f, 0f, -0.9f);
                ParticleSystem vines = visual.AddComponent<ParticleSystem>();
                vines.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ParticleSystem.MainModule main = vines.main;
                main.playOnAwake = false;
                main.loop = false;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.startLifetime = DurationSeconds + 1f;
                main.startSpeed = 0f;
                main.startSize = 0.55f;
                main.maxParticles = 36;
                ParticleSystem.EmissionModule emission = vines.emission;
                emission.enabled = false;
                ParticleSystemRenderer renderer = visual.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = sourceRenderer.renderMode;
                renderer.sharedMaterial = sourceRenderer.sharedMaterial;
                vines.Play();
                for (int i = 0; i < 36; i++)
                {
                    float progress = i / 35f;
                    float angle = progress * Mathf.PI * 6f;
                    var particle = new ParticleSystem.EmitParams
                    {
                        position = new Vector3(Mathf.Cos(angle) * 0.45f, 0.25f + progress * 1.5f,
                            Mathf.Sin(angle) * 0.45f),
                        rotation = i * 45f
                    };
                    vines.Emit(particle, 1);
                }
                return;
            }
            if (!warnedVineVisual)
            {
                Jotunn.Logger.LogWarning("Cannot display Binding vines: the Elder's projectile visual is unavailable.");
                warnedVineVisual = true;
            }
            for (int i = 0; i < 5; i++)
            {
                GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bar.transform.SetParent(visual.transform, false);
                bar.transform.localPosition = i < 3
                    ? new Vector3(0f, 0.35f + i * 0.65f, 0f)
                    : new Vector3(i == 3 ? -0.5f : 0.5f, 1f, 0f);
                bar.transform.localScale = i < 3
                    ? new Vector3(1.2f, 0.1f, 0.12f)
                    : new Vector3(0.1f, 1.8f, 0.12f);
                Collider barCollider = bar.GetComponent<Collider>();
                barCollider.enabled = false;
                Destroy(barCollider);
                Renderer renderer = bar.GetComponent<Renderer>();
                if (visualMaterial == null)
                {
                    visualMaterial = renderer.material;
                    visualMaterial.color = new Color(0.5f, 0.15f, 0.7f, 1f);
                }
                renderer.sharedMaterial = visualMaterial;
            }
        }

        private static bool EnsureStatus()
        {
            if (statusPrototype != null)
            {
                return true;
            }
            StatusEffect vanilla = ObjectDB.instance != null
                ? ObjectDB.instance.GetStatusEffect("Immobilized".GetStableHashCode()) : null;
            if (vanilla == null)
            {
                if (!warnedStatus && ObjectDB.instance != null)
                {
                    Jotunn.Logger.LogError("Binding cannot find the vanilla Immobilized status effect.");
                    warnedStatus = true;
                }
                return false;
            }
            statusPrototype = Instantiate(vanilla);
            statusPrototype.name = StatusName;
            StatusHash(statusPrototype) = 0;
            warnedStatus = false;
            return true;
        }

        public DestructibleType GetDestructibleType() => DestructibleType.Default;

        public bool OnProjectileHit(Character owner, ItemDrop.ItemData weapon, Projectile projectile,
            Collider collider, Vector3 hitPoint, bool water, Vector3 normal)
        {
            return owner is Player;
        }

        public void Damage(HitData hit)
        {
            if (view != null && view.IsValid() && hit != null && hit.m_hitType == HitData.HitType.PlayerHit)
            {
                view.InvokeRPC(DamageRpc, hit);
            }
        }

        private void RPC_Damage(long sender, HitData hit)
        {
            if (!view.IsOwner() || ZNet.instance == null || hit == null || !(hit.GetAttacker() is Player))
            {
                return;
            }
            ZDO zdo = view.GetZDO();
            long elapsed = ZNet.instance.GetTime().Ticks - zdo.GetLong(StartKey);
            if (elapsed < WarningSeconds * TimeSpan.TicksPerSecond ||
                elapsed >= (WarningSeconds + DurationSeconds) * TimeSpan.TicksPerSecond)
            {
                return;
            }
            float damage = hit.GetTotalDamage();
            if (float.IsNaN(damage) || float.IsInfinity(damage) || damage <= 0f)
            {
                return;
            }
            float health = zdo.GetFloat(HealthKey) - damage;
            zdo.Set(HealthKey, health);
            if (health <= 0f && ZNetScene.instance != null)
            {
                ZNetScene.instance.Destroy(gameObject);
            }
        }

        internal static List<BoxCollider> HideFromNpcMelee()
        {
            List<BoxCollider> hidden = null;
            foreach (BossModifierBinding binding in Instances)
            {
                if (binding != null && binding.hitbox != null && binding.hitbox.enabled)
                {
                    if (hidden == null)
                    {
                        hidden = new List<BoxCollider>();
                    }
                    binding.hitbox.enabled = false;
                    hidden.Add(binding.hitbox);
                }
            }
            return hidden;
        }

        private void OnDestroy()
        {
            Instances.Remove(this);
            if (visualMaterial != null)
            {
                Destroy(visualMaterial);
            }
            if (!statusApplied || target == null || !target.IsOwner())
            {
                return;
            }
            foreach (BossModifierBinding other in Instances)
            {
                if (other != null && other.target == target && other.statusApplied)
                {
                    return;
                }
            }
            target.GetSEMan().RemoveStatusEffect(StatusName.GetStableHashCode(), true);
        }
    }

    [HarmonyPatch(typeof(Attack), "DoMeleeAttack")]
    internal static class BindingNpcMeleePatch
    {
        private static void Prefix(Humanoid ___m_character, out List<BoxCollider> __state)
        {
            __state = ___m_character != null && !___m_character.IsPlayer()
                ? BossModifierBinding.HideFromNpcMelee() : null;
        }

        private static Exception Finalizer(List<BoxCollider> __state, Exception __exception)
        {
            if (__state != null)
            {
                foreach (BoxCollider collider in __state)
                {
                    if (collider != null)
                    {
                        collider.enabled = true;
                    }
                }
            }
            return __exception;
        }
    }
}
