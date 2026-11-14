using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Runecarved
{
    internal sealed class EnemyModifierSummoner : MonoBehaviour
    {
        private const string SummonFlag = "runecarved_summoned_enemy";
        private const string CountKey = "runecarved_summon_count";
        private const string IdKey = "runecarved_summon_id_";
        private const string CycleKey = "runecarved_summon_cycle";
        private const string TimeKey = "runecarved_summon_time";
        private const string PendingKey = "runecarved_summon_pending";
        private const string PositionKey = "runecarved_summon_position_";
        private const string ServerKey = "runecarved_summon_server";
        private const string QueryRpc = "Runecarved_SummonQuery";
        private const string ResultRpc = "Runecarved_SummonResult";
        private static readonly Dictionary<string, int> DefaultCounts =
            new Dictionary<string, int>(StringComparer.Ordinal)
        {
            // Black Forest
            { "Greydwarf", 3 },
            { "Greydwarf_Shaman", 1 },
            { "Greydwarf_Elite", 1 },
            { "Skeleton", 3 },
            { "Skeleton_Poison", 3 },
            { "Ghost", 3 },
            { "Troll", 1 },

            // Swamp
            { "Blob", 3 },
            { "BlobElite", 1 },
            { "Draugr", 3 },
            { "Draugr_Ranged", 3 },
            { "Draugr_Elite", 2 },
            { "Wraith", 2 },
            { "Abomination", 1 },

            // Mountain
            { "Wolf", 3 },
            { "Fenring", 2 },
            { "Fenring_Cultist", 1 },
            { "Bat", 0 },
            { "Ulv", 3 },
            { "StoneGolem", 1 },

            // Plains
            { "Goblin", 3 },
            { "GoblinArcher", 3 },
            { "GoblinShaman", 1 },
            { "GoblinBrute", 1 },
            { "Lox", 1 },

            // Mistlands
            { "Seeker", 3 },
            { "SeekerBrute", 1 },
            { "Gjall", 1 },
            { "Dverger", 1 },
            { "DvergerMage", 1 },
            { "DvergerMageFire", 1 },
            { "DvergerMageIce", 1 },
            { "DvergerMageSupport", 1 },

            // Ashlands
            { "Charred_Melee", 2 },
            { "Charred_Archer", 2 },
            { "Charred_Mage", 1 },
            { "Charred_Twitcher", 3 },
            { "Asksvin", 2 },
            { "Morgen", 1 },
            { "FallenValkyrie", 1 }
        };
        private static readonly Dictionary<string, ConfigEntry<int>> Counts =
            new Dictionary<string, ConfigEntry<int>>(StringComparer.Ordinal);
        private static readonly HashSet<ZDOID> UnseenDestroyed = new HashSet<ZDOID>();
        private static readonly AccessTools.FieldRef<ZDOMan, Dictionary<ZDOID, long>> DeadObjects =
            AccessTools.FieldRefAccess<ZDOMan, Dictionary<ZDOID, long>>("m_deadZDOs");
        private float queryDelay;
        private bool warnedUnconfirmed;

        internal static void Bind(ConfigFile config)
        {
            foreach (string prefab in EnemyModifierAllowlist.PrefabNames)
            {
                if (!DefaultCounts.TryGetValue(prefab, out int defaultCount))
                {
                    if (EnemyModifierAllowlist.IsEnabledByDefault(prefab))
                    {
                        throw new InvalidOperationException($"Missing default summon count for '{prefab}'.");
                    }
                    defaultCount = 1;
                }
                if (defaultCount < 0 || defaultCount > 20)
                {
                    throw new InvalidOperationException($"Default summon count for '{prefab}' must be between 0 and 20.");
                }
                Counts.Add(prefab, config.Bind(
                    "Enemy Modifiers - Summoner", prefab, defaultCount,
                    new ConfigDescription("Number of zero-star copies summoned per cycle.",
                        new AcceptableValueRange<int>(0, 20))));
            }
        }

        internal static bool IsSummon(Character character)
        {
            ZNetView view = character != null
                ? (character.m_nViewOverride != null ? character.m_nViewOverride : character.GetComponent<ZNetView>())
                : null;
            return view != null && view.IsValid() && view.GetZDO().GetBool(SummonFlag);
        }

        internal static bool IsLootlessSummon(Character character)
        {
            return IsSummon(character) || BossModifierSummons.IsBossSummon(character);
        }

        internal static bool IsConfirmedGone(ZDOID id, long serverId)
        {
            bool exists = ZDOMan.instance.GetZDO(id) != null;
            bool serverChanged = serverId != ZNet.GetUID();
            bool destroyed = DeadObjects(ZDOMan.instance).ContainsKey(id) || UnseenDestroyed.Contains(id);
            return EnemyModifierCombatRules.SummonConfirmedGone(serverChanged, exists, destroyed);
        }

        internal static void RegisterRpcs()
        {
            UnseenDestroyed.Clear();
            ZRoutedRpc.instance.Register<ZDOID, long>(QueryRpc, Query);
            ZRoutedRpc.instance.Register<ZDOID, long>(ResultRpc, Result);
        }

        internal static void UpdateSummoner(Character character, float dt)
        {
            if (!character.IsOwner() || character.IsDead() || ZNet.instance == null ||
                ZNetScene.instance == null || ZDOMan.instance == null || ZRoutedRpc.instance == null)
            {
                return;
            }
            bool summoner = false;
            foreach (EnemyModifierDefinition modifier in EnemyModifiers.GetModifiers(character))
            {
                summoner |= modifier.Id == "summoner";
            }
            if (!summoner)
            {
                return;
            }
            ZNetView view = character.GetComponent<ZNetView>();
            if (view == null || !view.IsValid())
            {
                return;
            }
            ZDO zdo = view.GetZDO();
            long now = ZNet.instance.GetTime().Ticks;
            if (zdo.GetInt(PendingKey) > 0)
            {
                if (EnemyModifierCombatRules.IntervalElapsed(now, zdo.GetLong(CycleKey), 2))
                {
                    SpawnPending(character, zdo);
                }
                return;
            }
            BaseAI ai = character.GetBaseAI();
            Character target = ai != null ? ai.GetTargetCreature() : null;
            if (ai == null || !ai.IsAlerted() || target == null || target.IsDead())
            {
                return;
            }
            long last = zdo.GetLong(TimeKey);
            if (last == 0 || last > now)
            {
                zdo.Set(TimeKey, now);
                return;
            }
            if (!EnemyModifierCombatRules.IntervalElapsed(now, last, 15))
            {
                return;
            }
            if (zdo.GetInt(CountKey) > 0)
            {
                EnemyModifierSummoner tracker = character.GetComponent<EnemyModifierSummoner>();
                if (tracker == null)
                {
                    tracker = character.gameObject.AddComponent<EnemyModifierSummoner>();
                }
                tracker.queryDelay -= dt;
                if (tracker.queryDelay <= 0f)
                {
                    tracker.queryDelay = 3f;
                    ZRoutedRpc.instance.InvokeRoutedRPC(QueryRpc, character.GetZDOID(), zdo.GetLong(CycleKey));
                }
                return;
            }
            BeginSummon(character, zdo, now);
        }

        private static void BeginSummon(Character character, ZDO zdo, long now)
        {
            string name = Utils.GetPrefabName(character.gameObject);
            if (!Counts.TryGetValue(name, out ConfigEntry<int> setting))
            {
                Jotunn.Logger.LogWarning($"Summoner has no count configured for prefab '{name}'.");
                zdo.Set(TimeKey, now);
                return;
            }
            if (setting.Value == 0)
            {
                zdo.Set(TimeKey, now);
                return;
            }
            GameObject prefab = ZNetScene.instance.GetPrefab(name);
            if (prefab == null)
            {
                Jotunn.Logger.LogWarning($"Summoner could not find prefab '{name}'.");
                zdo.Set(TimeKey, now);
                return;
            }
            if (!EnemySummonIndicator.IsAvailable)
            {
                Jotunn.Logger.LogWarning("Summoner cannot start: spawn indicators are unavailable.");
                zdo.Set(TimeKey, now);
                return;
            }
            ZNetPeer server = ZNet.instance.GetServerPeer();
            long serverId = ZNet.instance.IsServer() ? ZNet.GetUID() : (server != null ? server.m_uid : 0L);
            if (serverId == 0L)
            {
                Jotunn.Logger.LogWarning("Summoner cannot create a cycle without a connected server.");
                zdo.Set(TimeKey, now);
                return;
            }
            zdo.Set(CycleKey, now);
            zdo.Set(ServerKey, serverId);
            zdo.Set(TimeKey, now);
            float radius = Mathf.Max(character.GetRadius() + 2f, setting.Value * 0.6f);
            float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            for (int i = 0; i < setting.Value; i++)
            {
                float currentAngle = angle + i * Mathf.PI * 2f / setting.Value;
                Vector3 position = character.transform.position +
                    new Vector3(Mathf.Cos(currentAngle), 0f, Mathf.Sin(currentAngle)) * radius;
                zdo.Set(PositionKey + i, position);
                if (!EnemySummonIndicator.Create(position, now))
                {
                    Jotunn.Logger.LogError("Summoner canceled the cycle because a spawn indicator could not be created.");
                    return;
                }
            }
            zdo.Set(PendingKey, setting.Value);
            EnemyModifierCastSound.Play(character.transform.position);
        }

        private static void SpawnPending(Character character, ZDO zdo)
        {
            string name = Utils.GetPrefabName(character.gameObject);
            GameObject prefab = ZNetScene.instance.GetPrefab(name);
            if (prefab == null)
            {
                Jotunn.Logger.LogError($"Summoner could not find prefab '{name}' for the pending cycle.");
                zdo.Set(PendingKey, 0);
                return;
            }
            int pending = zdo.GetInt(PendingKey);
            int count = 0;
            for (int i = 0; i < pending; i++)
            {
                if (!zdo.GetVec3(PositionKey + i, out Vector3 position))
                {
                    Jotunn.Logger.LogError($"Summoner is missing spawn position {i} for '{name}'.");
                    continue;
                }
                GameObject instance = Instantiate(prefab, position, character.transform.rotation);
                Character summon = instance.GetComponent<Character>();
                ZNetView summonView = instance.GetComponent<ZNetView>();
                if (summon == null || summonView == null || !summonView.IsValid() || !summonView.IsOwner())
                {
                    Jotunn.Logger.LogError($"Summoner created an invalid networked '{name}' instance.");
                    if (summonView != null && summonView.IsValid() && summonView.IsOwner())
                    {
                        ZNetScene.instance.Destroy(instance);
                    }
                    else
                    {
                        Destroy(instance);
                    }
                    continue;
                }
                summonView.GetZDO().Set(SummonFlag, true);
                summon.SetLevel(1);
                zdo.Set(IdKey + count, summon.GetZDOID());
                count++;
                zdo.Set(CountKey, count);
            }
            zdo.Set(PendingKey, 0);
        }

        private static void Query(long sender, ZDOID parentId, long cycle)
        {
            if (!ZNet.instance.IsServer())
            {
                return;
            }
            ZDO parent = ZDOMan.instance.GetZDO(parentId);
            if (parent == null || parent.GetOwner() != sender || parent.GetLong(CycleKey) != cycle)
            {
                return;
            }
            int count = parent.GetInt(CountKey);
            if (count <= 0 || count > 20)
            {
                return;
            }
            for (int i = 0; i < count; i++)
            {
                ZDOID id = parent.GetZDOID(IdKey + i);
                if (id == ZDOID.None)
                {
                    return;
                }
                if (!IsConfirmedGone(id, parent.GetLong(ServerKey)))
                {
                    if (ZDOMan.instance.GetZDO(id) == null)
                    {
                        ZRoutedRpc.instance.InvokeRoutedRPC(sender, ResultRpc, parentId, -cycle);
                    }

                    return;
                }
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, ResultRpc, parentId, cycle);
        }

        internal static void RecordUnseenDestruction(ZDOMan manager, ZDOID id)
        {
            if (ZNet.instance != null && ZNet.instance.IsServer() && manager.GetZDO(id) == null)
            {
                UnseenDestroyed.Add(id);
            }
        }

        private static void Result(long sender, ZDOID parentId, long cycle)
        {
            ZNetPeer server = ZNet.instance.GetServerPeer();
            long serverId = ZNet.instance.IsServer() ? ZNet.GetUID() : (server != null ? server.m_uid : 0L);
            if (sender != serverId || ZNetScene.instance == null)
            {
                return;
            }
            bool unconfirmed = cycle < 0;
            if (unconfirmed)
            {
                cycle = -cycle;
            }
            GameObject parent = ZNetScene.instance.FindInstance(parentId);
            ZNetView view = parent != null ? parent.GetComponent<ZNetView>() : null;
            if (view != null && view.IsValid() && view.IsOwner() && view.GetZDO().GetLong(CycleKey) == cycle)
            {
                if (unconfirmed)
                {
                    EnemyModifierSummoner tracker = parent.GetComponent<EnemyModifierSummoner>();
                    if (tracker != null && !tracker.warnedUnconfirmed)
                    {
                        tracker.warnedUnconfirmed = true;
                        Jotunn.Logger.LogWarning("Summoner is waiting for the server to confirm a missing summon was destroyed.");
                    }
                    return;
                }
                view.GetZDO().Set(CountKey, 0);
            }
        }
    }

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class EnemySummonerRpcPatch
    {
        private static void Postfix()
        {
            EnemyModifierSummoner.RegisterRpcs();
            BossModifierSummons.RegisterRpcs();
            EnemyModifierCastSound.Register();
        }

    }

    [HarmonyPatch(typeof(ZDOMan), "HandleDestroyedZDO")]
    internal static class EnemySummonUnseenDestructionPatch
    {
        private static void Prefix(ZDOMan __instance, ZDOID uid)
        {
            EnemyModifierSummoner.RecordUnseenDestruction(__instance, uid);
        }
    }

    [HarmonyPatch(typeof(Character), "OnDeath")]
    internal static class EnemySummonDeathPatch
    {
        private static void Prefix(Character __instance)
        {
            if (EnemyModifierSummoner.IsLootlessSummon(__instance))
            {
                CharacterDrop drop = __instance.GetComponent<CharacterDrop>();
                if (drop != null)
                {
                    drop.SetDropsEnabled(false);
                }
            }
        }
    }

    [HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.GenerateDropList))]
    [HarmonyPriority(Priority.Last)]
    internal static class EnemySummonDropPatch
    {
        private static bool Prefix(CharacterDrop __instance, ref List<KeyValuePair<GameObject, int>> __result)
        {
            if (!EnemyModifierSummoner.IsLootlessSummon(__instance.GetComponent<Character>()))
            {
                return true;
            }
            __result = new List<KeyValuePair<GameObject, int>>();
            return false;
        }

        private static void Postfix(CharacterDrop __instance, List<KeyValuePair<GameObject, int>> __result)
        {
            if (EnemyModifierSummoner.IsLootlessSummon(__instance.GetComponent<Character>()))
            {
                __result.Clear();
            }
        }
    }
}
