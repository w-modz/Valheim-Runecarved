using System;
using Jotunn.Managers;
using UnityEngine;

namespace Runecarved
{
    internal sealed class EnemySummonIndicator : MonoBehaviour
    {
        private const string PrefabName = "Runecarved_SummonIndicator";
        private const string StartKey = "runecarved_summon_indicator_start";
        private static GameObject prefab;
        private ZNetView view;

        internal static void Register()
        {
            PrefabManager.OnVanillaPrefabsAvailable += RegisterPrefab;
        }

        private static void RegisterPrefab()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= RegisterPrefab;
            GameObject source = PrefabManager.Instance.GetPrefab("BonePileSpawner");
            Transform particles = source != null ? source.transform.Find("particles") : null;
            if (particles == null)
            {
                Jotunn.Logger.LogError("Cannot register summon indicators: evil bone pile particles are unavailable.");
                return;
            }
            prefab = PrefabManager.Instance.CreateEmptyPrefab(PrefabName);
            if (prefab == null)
            {
                Jotunn.Logger.LogError("Could not register the summon indicator prefab.");
                return;
            }
            DestroyImmediate(prefab.GetComponent<Collider>());
            DestroyImmediate(prefab.GetComponent<MeshRenderer>());
            DestroyImmediate(prefab.GetComponent<MeshFilter>());
            prefab.GetComponent<ZNetView>().m_persistent = false;
            Instantiate(particles.gameObject, prefab.transform, false);
            prefab.AddComponent<EnemySummonIndicator>();
            PrefabManager.Instance.AddPrefab(prefab);
        }

        internal static bool IsAvailable => prefab != null;

        internal static bool Create(Vector3 position, long now)
        {
            GameObject indicator = Instantiate(prefab, position, Quaternion.identity);
            ZNetView indicatorView = indicator.GetComponent<ZNetView>();
            if (indicatorView == null || !indicatorView.IsValid() || !indicatorView.IsOwner())
            {
                Jotunn.Logger.LogError("Could not create a networked summon indicator.");
                Destroy(indicator);
                return false;
            }
            indicatorView.GetZDO().Set(StartKey, now);
            return true;
        }

        private void Awake()
        {
            view = GetComponent<ZNetView>();
        }

        private void FixedUpdate()
        {
            if (view == null || !view.IsValid() || !view.IsOwner() ||
                ZNet.instance == null || ZNetScene.instance == null)
            {
                return;
            }
            long start = view.GetZDO().GetLong(StartKey);
            if (start > 0 && ZNet.instance.GetTime().Ticks - start >= 2 * TimeSpan.TicksPerSecond)
            {
                ZNetScene.instance.Destroy(gameObject);
            }
        }
    }
}
