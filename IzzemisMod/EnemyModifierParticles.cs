using UnityEngine;
using UnityEngine.Rendering;

namespace Runecarved
{
    internal sealed class EnemyModifierParticles : MonoBehaviour
    {
        private const string ParticleObjectName = "RunecarvedEnemyModifierParticles";
        private const string ShieldWindupObjectName = "RunecarvedEnemyShieldWindupParticles";
        private const string FrenzyObjectName = "RunecarvedBossFrenzyParticles";
        private const string ScaledObjectName = "RunecarvedBossScaledParticles";
        private const float SyncInterval = 0.5f;
        private const float EmissionRate = 15f;
        private const int MaxParticles = 45;
        private const float MinParticleSize = 0.03f;
        private const float MaxParticleSize = 0.06f;
        private static readonly Color ParticleColor = new Color(1f, 0.1f, 0.1f, 1f);
        private static readonly Color ShieldWindupColor = new Color(0.1f, 0.45f, 1f, 1f);
        private static bool warnedMissingFrenzyVisual;
        private static bool warnedMissingScaledVisual;

        private Character character;
        private CapsuleCollider capsule;
        private float nextSync;

        internal static void Ensure(Character character)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null ||
                character.IsDead() || character.GetComponent<EnemyModifierParticles>() != null)
            {
                return;
            }
            var modifiers = EnemyModifiers.GetModifiers(character);
            if (character.IsBoss()
                ? !EnemyModifiers.IsFrenzied(character, modifiers) && !BossModifierScaled.IsProtected(character, modifiers)
                : modifiers.Count == 0)
            {
                return;
            }

            EnemyModifierParticles effect = character.gameObject.AddComponent<EnemyModifierParticles>();
            effect.character = character;
            effect.capsule = character.GetComponent<CapsuleCollider>();
            if (effect.capsule == null)
            {
                Jotunn.Logger.LogWarning($"Cannot display enemy modifier particles: {character.name} has no capsule collider.");
            }
        }

        private void Update()
        {
            if (character == null || capsule == null || Time.time < nextSync)
            {
                return;
            }

            // Throttle network and modifier checks instead of repeating them on every frame
            nextSync = Time.time + SyncInterval;
            ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(character.GetZDOID()) : null;
            var modifiers = EnemyModifiers.GetModifiers(character);
            if (character.IsDead() || zdo == null || !zdo.HasOwner())
            {
                RarityItemParticles.Remove(transform, ParticleObjectName);
                RarityItemParticles.Remove(transform, ShieldWindupObjectName);
                RarityItemParticles.Remove(transform, FrenzyObjectName);
                RarityItemParticles.Remove(transform, ScaledObjectName);
                return;
            }

            if (character.IsBoss())
            {
                UpdateBossVisual(FrenzyObjectName, "MeadHealthMedium", "vfx_Potion_health_medium",
                    EnemyModifiers.IsFrenzied(character, modifiers), ref warnedMissingFrenzyVisual);
                UpdateBossVisual(ScaledObjectName, "MeadEitrMinor", "vfx_Potion_eitr_minor",
                    BossModifierScaled.IsProtected(character, modifiers), ref warnedMissingScaledVisual);
                return;
            }

            if (modifiers.Count == 0)
            {
                RarityItemParticles.Remove(transform, ParticleObjectName);
                RarityItemParticles.Remove(transform, ShieldWindupObjectName);
                return;
            }

            EnsureParticles(ParticleObjectName, ParticleColor);
            if (ZNet.instance != null && EnemyModifierShields.IsWindingUp(zdo, ZNet.instance.GetTime().Ticks))
            {
                EnsureParticles(ShieldWindupObjectName, ShieldWindupColor);
            }
            else
            {
                RarityItemParticles.Remove(transform, ShieldWindupObjectName);
            }
        }

        private void UpdateBossVisual(string objectName, string itemName, string visualName, bool active, ref bool warned)
        {
            Transform existing = transform.Find(objectName);
            if (!active)
            {
                if (existing != null)
                {
                    Destroy(existing.gameObject);
                }
                return;
            }

            if (existing == null)
            {
                if (ObjectDB.instance == null)
                {
                    return;
                }
                GameObject item = ObjectDB.instance.GetItemPrefab(itemName);
                ItemDrop drop = item != null ? item.GetComponent<ItemDrop>() : null;
                StatusEffect status = drop != null ? drop.m_itemData.m_shared.m_consumeStatusEffect : null;
                GameObject source = null;
                if (status != null)
                {
                    foreach (EffectList.EffectData effect in status.m_startEffects.m_effectPrefabs)
                    {
                        if (effect.m_prefab != null && effect.m_prefab.name == visualName)
                        {
                            source = effect.m_prefab;
                            break;
                        }
                    }
                }
                Transform trails = source != null ? source.transform.Find("trails") : null;
                Transform flakes = source != null ? source.transform.Find("flakes") : null;
                if (trails == null || flakes == null ||
                    trails.GetComponent<ParticleSystem>() == null || flakes.GetComponent<ParticleSystem>() == null)
                {
                    if (!warned)
                    {
                        Jotunn.Logger.LogWarning($"Cannot display boss modifier particles: {visualName} is unavailable.");
                        warned = true;
                    }
                    return;
                }
                warned = false;
                GameObject visual = new GameObject(objectName);
                existing = visual.transform;
                existing.SetParent(transform, false);
                foreach (Transform sourceParticles in new[] { trails, flakes })
                {
                    GameObject child = Instantiate(sourceParticles.gameObject, existing, false);
                    ParticleSystem particles = child.GetComponent<ParticleSystem>();
                    particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ParticleSystem.MainModule main = particles.main;
                    main.loop = true;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                    ParticleSystem.EmissionModule emission = particles.emission;
                    emission.SetBursts(new ParticleSystem.Burst[0]);
                    particles.Play(true);
                }
            }

            existing.localPosition = capsule.center;
            float diameter = capsule.radius * 2f;
            existing.localScale = new Vector3(diameter, Mathf.Max(capsule.height, diameter), diameter);
        }

        private void EnsureParticles(string objectName, Color color)
        {
            float radius = capsule.radius;
            float halfHeight = Mathf.Max(capsule.height * 0.5f, radius);
            ParticleSystem particles = RarityItemParticles.Ensure(
                transform,
                color,
                capsule.center,
                1f,
                objectName,
                new Vector3(radius, halfHeight, radius));
            if (particles != null)
            {
                ParticleSystem.MainModule main = particles.main;
                main.startSize = new ParticleSystem.MinMaxCurve(MinParticleSize, MaxParticleSize);
                main.maxParticles = MaxParticles;
                ParticleSystem.EmissionModule emission = particles.emission;
                emission.rateOverTime = EmissionRate;
            }
        }

        private void OnDestroy()
        {
            RarityItemParticles.Remove(transform, ParticleObjectName);
            RarityItemParticles.Remove(transform, ShieldWindupObjectName);
            RarityItemParticles.Remove(transform, FrenzyObjectName);
            RarityItemParticles.Remove(transform, ScaledObjectName);
        }
    }
}
