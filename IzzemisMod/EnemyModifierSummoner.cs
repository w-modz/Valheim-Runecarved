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
        private const string WaitingKey = "runecarved_summon_waiting_death";
        private const string TimingVersionKey = "runecarved_summon_death_timing";
        private const string SlotsVersionKey = "runecarved_summon_slots_v2";
        private const string PendingKey = "runecarved_summon_pending";
        private const string PendingSlotKey = "runecarved_summon_pending_slot_";
        private const string PositionKey = "runecarved_summon_position_";
        private const string ServerKey = "runecarved_summon_server";
        private const string SlotServerKey = "runecarved_summon_slot_server_";
        private const string QueryRpc = "Runecarved_SummonSlotQuery";
        private const string ResultRpc = "Runecarved_SummonSlotResult";
        private const int MaxSummons = 20;
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
        private static bool warnedNoServer;
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
                if (defaultCount < 0 || defaultCount > MaxSummons)
                {
                    throw new InvalidOperationException($"Default summon count for '{prefab}' must be between 0 and 20.");
                }
                Counts.Add(prefab, config.Bind(
                    "Enemy Modifiers - Summoner", prefab, defaultCount,
                    new ConfigDescription("Maximum number of zero-star summoned copies.",
                        new AcceptableValueRange<int>(0, MaxSummons))));
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
            warnedNoServer = false;
            ZRoutedRpc.instance.Register<ZDOID, int, ZDOID>(QueryRpc, Query);
            ZRoutedRpc.instance.Register<ZDOID, int, ZDOID, bool>(ResultRpc, Result);
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
            if (!zdo.GetBool(TimingVersionKey))
            {
                zdo.Set(WaitingKey, zdo.GetInt(CountKey) > 0 || zdo.GetInt(PendingKey) > 0);
                zdo.Set(TimingVersionKey, true);
            }
            if (!zdo.GetBool(SlotsVersionKey))
            {
                int oldCount = zdo.GetInt(CountKey);
                int pending = zdo.GetInt(PendingKey);
                for (int i = 0; i < MaxSummons; i++)
                {
                    if (i < oldCount)
                    {
                        zdo.Set(SlotServerKey + i, zdo.GetLong(ServerKey));
                    }
                    else if (zdo.GetZDOID(IdKey + i) != ZDOID.None)
                    {
                        zdo.Set(IdKey + i, ZDOID.None);
                    }
                    if (i < pending)
                    {
                        zdo.Set(PendingSlotKey + i, i);
                    }
                }
                zdo.Set(SlotsVersionKey, true);
            }
            if (zdo.GetInt(PendingKey) > 0)
            {
                if (EnemyModifierCombatRules.IntervalElapsed(now, zdo.GetLong(CycleKey), 2))
                {
                    SpawnPending(character, zdo);
                }
                return;
            }
            int activeCount = 0;
            EnemyModifierSummoner tracker = character.GetComponent<EnemyModifierSummoner>();
            if (tracker == null)
            {
                tracker = character.gameObject.AddComponent<EnemyModifierSummoner>();
            }
            tracker.queryDelay -= dt;
            bool query = tracker.queryDelay <= 0f;
            if (query)
            {
                tracker.queryDelay = 3f;
            }
            for (int i = 0; i < MaxSummons; i++)
            {
                ZDOID id = zdo.GetZDOID(IdKey + i);
                if (id != ZDOID.None)
                {
                    activeCount++;
                    if (query)
                    {
                        ZRoutedRpc.instance.InvokeRoutedRPC(QueryRpc, character.GetZDOID(), i, id);
                    }
                }
            }
            if (zdo.GetBool(WaitingKey))
            {
                if (activeCount > 0)
                {
                    return;
                }
                zdo.Set(WaitingKey, false);
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
            BeginSummon(character, zdo, now, activeCount);
        }

        private static void BeginSummon(Character character, ZDO zdo, long now, int activeCount)
        {
            string name = Utils.GetPrefabName(character.gameObject);
            if (!Counts.TryGetValue(name, out ConfigEntry<int> setting))
            {
                Jotunn.Logger.LogWarning($"Summoner has no count configured for prefab '{name}'.");
                zdo.Set(TimeKey, now);
                return;
            }
            int missing = setting.Value - activeCount;
            if (missing <= 0)
            {
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
            zdo.Set(TimeKey, now);
            float radius = Mathf.Max(character.GetRadius() + 2f, setting.Value * 0.6f);
            float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            int scheduled = 0;
            for (int slot = 0; slot < MaxSummons && scheduled < missing; slot++)
            {
                if (zdo.GetZDOID(IdKey + slot) != ZDOID.None)
                {
                    continue;
                }
                float currentAngle = angle + scheduled * Mathf.PI * 2f / missing;
                Vector3 position = character.transform.position +
                    new Vector3(Mathf.Cos(currentAngle), 0f, Mathf.Sin(currentAngle)) * radius;
                zdo.Set(PositionKey + scheduled, position);
                zdo.Set(PendingSlotKey + scheduled, slot);
                if (!EnemySummonIndicator.Create(position, now))
                {
                    Jotunn.Logger.LogError("Summoner canceled the cycle because a spawn indicator could not be created.");
                    return;
                }
                scheduled++;
            }
            zdo.Set(PendingKey, scheduled);
            zdo.Set(WaitingKey, true);
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
            ZNetPeer server = ZNet.instance.GetServerPeer();
            long serverId = ZNet.instance.IsServer() ? ZNet.GetUID() : (server != null ? server.m_uid : 0L);
            if (serverId == 0L)
            {
                if (!warnedNoServer)
                {
                    Jotunn.Logger.LogWarning("Summoner is waiting for a connected server before spawning its summons.");
                    warnedNoServer = true;
                }
                return;
            }
            warnedNoServer = false;
            long now = ZNet.instance.GetTime().Ticks;
            if (now - zdo.GetLong(CycleKey) > 3 * TimeSpan.TicksPerSecond)
            {
                for (int i = 0; i < pending; i++)
                {
                    if (!zdo.GetVec3(PositionKey + i, out Vector3 position) ||
                        !EnemySummonIndicator.IsAvailable || !EnemySummonIndicator.Create(position, now))
                    {
                        Jotunn.Logger.LogError("Summoner could not renew a pending spawn indicator.");
                        zdo.Set(PendingKey, 0);
                        return;
                    }
                }
                zdo.Set(CycleKey, now);
                return;
            }
            for (int i = 0; i < pending; i++)
            {
                int slot = zdo.GetInt(PendingSlotKey + i);
                if (slot < 0 || slot >= MaxSummons || zdo.GetZDOID(IdKey + slot) != ZDOID.None)
                {
                    Jotunn.Logger.LogError($"Summoner cannot spawn into occupied or invalid slot {slot}.");
                    continue;
                }
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
                zdo.Set(SlotServerKey + slot, serverId);
                zdo.Set(IdKey + slot, summon.GetZDOID());
            }
            zdo.Set(PendingKey, 0);
        }

        private static void Query(long sender, ZDOID parentId, int slot, ZDOID summonId)
        {
            if (!ZNet.instance.IsServer() || slot < 0 || slot >= MaxSummons || summonId == ZDOID.None)
            {
                return;
            }
            ZDO parent = ZDOMan.instance.GetZDO(parentId);
            if (parent == null || parent.GetOwner() != sender || parent.GetZDOID(IdKey + slot) != summonId)
            {
                return;
            }
            if (IsConfirmedGone(summonId, parent.GetLong(SlotServerKey + slot)))
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, ResultRpc, parentId, slot, summonId, true);
            }
            else if (ZDOMan.instance.GetZDO(summonId) == null)
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, ResultRpc, parentId, slot, summonId, false);
            }
        }

        internal static void RecordUnseenDestruction(ZDOMan manager, ZDOID id)
        {
            if (ZNet.instance != null && ZNet.instance.IsServer() && manager.GetZDO(id) == null)
            {
                UnseenDestroyed.Add(id);
            }
        }

        private static void Result(long sender, ZDOID parentId, int slot, ZDOID summonId, bool gone)
        {
            ZNetPeer server = ZNet.instance.GetServerPeer();
            long serverId = ZNet.instance.IsServer() ? ZNet.GetUID() : (server != null ? server.m_uid : 0L);
            if (serverId == 0L || sender != serverId || slot < 0 || slot >= MaxSummons || ZNetScene.instance == null)
            {
                return;
            }
            GameObject parent = ZNetScene.instance.FindInstance(parentId);
            ZNetView view = parent != null ? parent.GetComponent<ZNetView>() : null;
            if (view != null && view.IsValid() && view.IsOwner() && view.GetZDO().GetZDOID(IdKey + slot) == summonId)
            {
                ZDO zdo = view.GetZDO();
                if (gone)
                {
                    if (zdo.GetBool(WaitingKey) && ZNet.instance != null)
                    {
                        zdo.Set(TimeKey, ZNet.instance.GetTime().Ticks);
                        zdo.Set(WaitingKey, false);
                    }
                    zdo.Set(IdKey + slot, ZDOID.None);
                }
                else
                {
                    EnemyModifierSummoner tracker = parent.GetComponent<EnemyModifierSummoner>();
                    if (tracker != null && !tracker.warnedUnconfirmed)
                    {
                        tracker.warnedUnconfirmed = true;
                        Jotunn.Logger.LogWarning("Summoner is waiting for the server to confirm a missing summon was destroyed.");
                    }
                }
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
