using UnityEngine;
using UnityEngine.Rendering;

namespace Runecarved
{
    internal sealed class EnemyModifierParticles : MonoBehaviour
    {
        private const string ParticleObjectName = "RunecarvedEnemyModifierParticles";
        private const float SyncInterval = 0.5f;
        private const float EmissionRate = 15f;
        private const int MaxParticles = 45;
        private const float MinParticleSize = 0.03f;
        private const float MaxParticleSize = 0.06f;
        private static readonly Color ParticleColor = new Color(1f, 0.1f, 0.1f, 1f);

        private Character character;
        private CapsuleCollider capsule;
        private float nextSync;

        internal static void Ensure(Character character)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null ||
                character.IsDead() ||
                character.GetComponent<EnemyModifierParticles>() != null ||
                EnemyModifiers.GetModifiers(character).Count == 0)
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
            if (character.IsDead() || zdo == null || !zdo.HasOwner() ||
                EnemyModifiers.GetModifiers(character).Count == 0)
            {
                RarityItemParticles.Remove(transform, ParticleObjectName);
                return;
            }

            float radius = capsule.radius;
            float halfHeight = Mathf.Max(capsule.height * 0.5f, radius);
            ParticleSystem particles = RarityItemParticles.Ensure(
                transform,
                ParticleColor,
                capsule.center,
                1f,
                ParticleObjectName,
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
        }
    }
}
