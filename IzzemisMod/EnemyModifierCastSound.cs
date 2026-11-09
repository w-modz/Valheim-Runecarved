using UnityEngine;

namespace Runecarved
{
    internal static class EnemyModifierCastSound
    {
        private const string SoundRpc = "Runecarved_EnemyCastSound";
        private const string SourcePrefab = "fx_shaman_protect";
        private static bool warnedMissingSound;

        internal static void Register()
        {
            ZRoutedRpc.instance.Register<Vector3, bool>(SoundRpc, PlayLocal);
        }

        internal static void Play(Vector3 position, bool immediate = false)
        {
            if (ZRoutedRpc.instance == null)
            {
                Jotunn.Logger.LogWarning("Cannot play enemy cast sound: routed RPC is unavailable.");
                return;
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, SoundRpc, position, immediate);
        }

        private static void PlayLocal(long sender, Vector3 position, bool immediate)
        {
            GameObject source = ZNetScene.instance != null
                ? ZNetScene.instance.GetPrefab(SourcePrefab)
                : null;
            if (source == null)
            {
                WarnMissingSound();
                return;
            }
            foreach (string childName in new[] { "sfx", "sfx_shield" })
            {
                Transform child = source.transform.Find(childName);
                ZSFX prefabSound = child != null ? child.GetComponent<ZSFX>() : null;
                if (prefabSound == null)
                {
                    WarnMissingSound();
                    continue;
                }
                GameObject instance = Object.Instantiate(child.gameObject, position, Quaternion.identity);
                ZSFX sound = instance.GetComponent<ZSFX>();
                CamShaker shaker = instance.GetComponent<CamShaker>();
                if (shaker != null)
                {
                    shaker.enabled = false;
                }
                if (immediate && childName == "sfx")
                {
                    sound.m_playOnAwake = false;
                    sound.Play();
                }
                float lifetime = 1f;
                foreach (AudioClip clip in sound.m_audioClips)
                {
                    if (clip != null)
                    {
                        lifetime = Mathf.Max(lifetime, sound.m_maxDelay +
                            clip.length / Mathf.Max(0.01f, sound.m_minPitch) + 0.5f);
                    }
                }
                Object.Destroy(instance, lifetime);
            }
        }

        private static void WarnMissingSound()
        {
            if (!warnedMissingSound)
            {
                warnedMissingSound = true;
                Jotunn.Logger.LogWarning("Goblin Shaman protection sound is unavailable.");
            }
        }
    }
}
