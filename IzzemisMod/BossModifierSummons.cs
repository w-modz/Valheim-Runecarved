using System;
using System.Collections.Generic;
using UnityEngine;

namespace Runecarved
{
    internal sealed class BossModifierSummons : MonoBehaviour
    {
        private sealed class SummonRule
        {
            internal readonly string Id;
            internal readonly string Name;
            internal readonly string Flag;
            internal readonly string TimeKey;
            internal readonly string IdKey;
            internal readonly string ServerKey;
            internal readonly string PendingKey;
            internal readonly string PositionKey;
            internal readonly string PrefabKey;
            internal readonly string QueryRpc;
            internal readonly string ResultRpc;
            internal readonly int Limit;
            internal readonly int Interval;
            internal readonly Dictionary<string, string[]> Prefabs;

            internal SummonRule(string id, string name, string flag, string queryRpc, string resultRpc,
                int limit, int interval, Dictionary<string, string[]> prefabs)
            {
                Id = id;
                Name = name;
                Flag = flag;
                string prefix = "runecarved_" + id + "_";
                TimeKey = prefix + "time";
                IdKey = prefix + "id_";
                ServerKey = prefix + "server_";
                PendingKey = prefix + "pending_";
                PositionKey = prefix + "position_";
                PrefabKey = prefix + "prefab_";
                QueryRpc = queryRpc;
                ResultRpc = resultRpc;
                Limit = limit;
                Interval = interval;
                Prefabs = prefabs;
            }
        }

        private static readonly SummonRule Guarded = new SummonRule(
            "guarded", "Guarded", "runecarved_guarded_guard", "Runecarved_GuardQuery",
            "Runecarved_GuardResult", 3, 30, new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                { "Eikthyr", new[] { "Greyling" } },
                { "gd_king", new[] { "Greydwarf" } },
                { "Bonemass", new[] { "Draugr", "Draugr_Ranged" } },
                { "Dragon", new[] { "Hatchling", "Wolf" } },
                { "GoblinKing", new[] { "Goblin", "GoblinArcher" } },
                { "SeekerQueen", new[] { "Seeker" } },
                { "Fader", new[] { "Charred_Archer", "Charred_Melee" } }
            });
        private static readonly SummonRule Sentry = new SummonRule(
            "sentry", "Sentry", "runecarved_sentry_summon", "Runecarved_SentryQuery",
            "Runecarved_SentryResult", 1, 60, new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                { "Eikthyr", new[] { "Greydwarf_Elite" } },
                { "gd_king", new[] { "Troll" } },
                { "Bonemass", new[] { "Abomination" } },
                { "Dragon", new[] { "StoneGolem" } },
                { "GoblinKing", new[] { "GoblinBrute" } },
                { "SeekerQueen", new[] { "SeekerBrute" } },
                { "Fader", new[] { "FallenValkyrie" } }
            });
        private static readonly HashSet<string> UnknownBossWarnings = new HashSet<string>();
        private static bool warnedNoServer;
        private float guardedQueryDelay;
        private float sentryQueryDelay;
        private bool warnedGuardedUnconfirmed;
        private bool warnedSentryUnconfirmed;

        internal static bool IsBossSummon(Character character)
        {
            ZNetView view = character != null
                ? (character.m_nViewOverride != null ? character.m_nViewOverride : character.GetComponent<ZNetView>())
                : null;
            return view != null && view.IsValid() &&
                (view.GetZDO().GetBool(Guarded.Flag) || view.GetZDO().GetBool(Sentry.Flag));
        }

        internal static void RegisterRpcs()
        {
            UnknownBossWarnings.Clear();
            warnedNoServer = false;
            ZRoutedRpc.instance.Register<ZDOID, int, ZDOID>(Guarded.QueryRpc, QueryGuarded);
            ZRoutedRpc.instance.Register<ZDOID, int, ZDOID, bool>(Guarded.ResultRpc, ResultGuarded);
            ZRoutedRpc.instance.Register<ZDOID, int, ZDOID>(Sentry.QueryRpc, QuerySentry);
            ZRoutedRpc.instance.Register<ZDOID, int, ZDOID, bool>(Sentry.ResultRpc, ResultSentry);
        }

        internal static void UpdateSummons(Character boss, float dt)
        {
            if (!boss.IsBoss() || !boss.IsOwner() || boss.IsDead() || ZNet.instance == null ||
                ZNetScene.instance == null || ZDOMan.instance == null || ZRoutedRpc.instance == null)
            {
                return;
            }
            foreach (EnemyModifierDefinition modifier in EnemyModifiers.GetModifiers(boss))
            {
                if (modifier.Id == Guarded.Id)
                {
                    UpdateRule(boss, dt, Guarded);
                }
                else if (modifier.Id == Sentry.Id)
                {
                    UpdateRule(boss, dt, Sentry);
                }
            }
        }

        private static void UpdateRule(Character boss, float dt, SummonRule rule)
        {
            ZNetView view = boss.m_nViewOverride != null ? boss.m_nViewOverride : boss.GetComponent<ZNetView>();
            if (view == null || !view.IsValid())
            {
                return;
            }
            ZDO zdo = view.GetZDO();
            long now = ZNet.instance.GetTime().Ticks;
            for (int i = 0; i < rule.Limit; i++)
            {
                if (EnemyModifierCombatRules.IntervalElapsed(now, zdo.GetLong(rule.PendingKey + i), 2))
                {
                    SpawnPending(boss, zdo, i, rule);
                }
            }

            BossModifierSummons tracker = boss.GetComponent<BossModifierSummons>();
            if (tracker == null)
            {
                tracker = boss.gameObject.AddComponent<BossModifierSummons>();
            }
            ref float queryDelay = ref (rule == Guarded ? ref tracker.guardedQueryDelay : ref tracker.sentryQueryDelay);
            queryDelay -= dt;
            if (queryDelay <= 0f)
            {
                queryDelay = 3f;
                for (int i = 0; i < rule.Limit; i++)
                {
                    ZDOID id = zdo.GetZDOID(rule.IdKey + i);
                    if (id != ZDOID.None)
                    {
                        ZRoutedRpc.instance.InvokeRoutedRPC(rule.QueryRpc, boss.GetZDOID(), i, id);
                    }
                }
            }

            BaseAI ai = boss.GetBaseAI();
            Character target = ai != null ? ai.GetTargetCreature() : null;
            if (ai == null || !ai.IsAlerted() || target == null || target.IsDead())
            {
                return;
            }
            long last = zdo.GetLong(rule.TimeKey);
            if (last == 0 || last > now)
            {
                zdo.Set(rule.TimeKey, now);
                return;
            }
            if (!EnemyModifierCombatRules.IntervalElapsed(now, last, rule.Interval))
            {
                return;
            }
            zdo.Set(rule.TimeKey, now);
            BeginSummon(boss, zdo, now, rule);
        }

        private static void BeginSummon(Character boss, ZDO zdo, long now, SummonRule rule)
        {
            string bossName = Utils.GetPrefabName(boss.gameObject);
            if (!rule.Prefabs.TryGetValue(bossName, out string[] choices))
            {
                if (UnknownBossWarnings.Add(rule.Id + ":" + bossName))
                {
                    Jotunn.Logger.LogWarning($"{rule.Name} has no summons defined for boss '{bossName}'.");
                }
                return;
            }
            if (!EnemySummonIndicator.IsAvailable || ZoneSystem.instance == null)
            {
                Jotunn.Logger.LogWarning($"{rule.Name} cannot summon: spawn indicators or the zone system are unavailable.");
                return;
            }
            ZNetPeer server = ZNet.instance.GetServerPeer();
            long serverId = ZNet.instance.IsServer() ? ZNet.GetUID() : (server != null ? server.m_uid : 0L);
            if (serverId == 0L)
            {
                Jotunn.Logger.LogWarning($"{rule.Name} cannot summon without a connected server.");
                return;
            }

            float radius = Mathf.Max(boss.GetRadius() + 2f, 3f);
            float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            for (int i = 0; i < rule.Limit; i++)
            {
                if (zdo.GetZDOID(rule.IdKey + i) != ZDOID.None || zdo.GetLong(rule.PendingKey + i) != 0)
                {
                    continue;
                }
                string prefabName = choices[UnityEngine.Random.Range(0, choices.Length)];
                GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
                if (prefab == null || prefab.GetComponent<Character>() == null || prefab.GetComponent<ZNetView>() == null)
                {
                    Jotunn.Logger.LogError($"{rule.Name} cannot find a networked character prefab '{prefabName}'.");
                    continue;
                }
                float currentAngle = angle + i * Mathf.PI * 2f / rule.Limit;
                Vector3 position = boss.transform.position +
                    new Vector3(Mathf.Cos(currentAngle), 0f, Mathf.Sin(currentAngle)) * radius;
                if (!ZoneSystem.instance.GetSolidHeight(position, out float height, 2))
                {
                    Jotunn.Logger.LogWarning($"{rule.Name} could not find ground for '{prefabName}' near '{bossName}'.");
                    continue;
                }
                position.y = height;
                if (!EnemySummonIndicator.Create(position, now))
                {
                    Jotunn.Logger.LogError($"{rule.Name} could not create a spawn indicator for '{prefabName}'.");
                    continue;
                }
                zdo.Set(rule.PositionKey + i, position);
                zdo.Set(rule.PrefabKey + i, prefabName);
                zdo.Set(rule.PendingKey + i, now);
            }
        }

        private static void SpawnPending(Character boss, ZDO zdo, int slot, SummonRule rule)
        {
            ZNetPeer server = ZNet.instance.GetServerPeer();
            long serverId = ZNet.instance.IsServer() ? ZNet.GetUID() : (server != null ? server.m_uid : 0L);
            if (serverId == 0L)
            {
                if (!warnedNoServer)
                {
                    warnedNoServer = true;
                    Jotunn.Logger.LogWarning($"{rule.Name} is waiting for a connected server before spawning its summons.");
                }
                return;
            }
            warnedNoServer = false;
            string prefabName = zdo.GetString(rule.PrefabKey + slot);
            GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
            if (prefab == null || !zdo.GetVec3(rule.PositionKey + slot, out Vector3 position))
            {
                Jotunn.Logger.LogError($"{rule.Name} could not restore pending summon '{prefabName}' in slot {slot}.");
                zdo.Set(rule.PendingKey + slot, 0L);
                return;
            }
            long now = ZNet.instance.GetTime().Ticks;
            if (now - zdo.GetLong(rule.PendingKey + slot) > 3 * TimeSpan.TicksPerSecond)
            {
                if (!EnemySummonIndicator.IsAvailable || !EnemySummonIndicator.Create(position, now))
                {
                    Jotunn.Logger.LogError($"{rule.Name} could not renew the spawn indicator for '{prefabName}'.");
                    zdo.Set(rule.PendingKey + slot, 0L);
                    return;
                }
                zdo.Set(rule.PendingKey + slot, now);
                return;
            }
            GameObject instance = Instantiate(prefab, position, boss.transform.rotation);
            Character summon = instance.GetComponent<Character>();
            ZNetView summonView = instance.GetComponent<ZNetView>();
            if (summon == null || summonView == null || !summonView.IsValid() || !summonView.IsOwner())
            {
                Jotunn.Logger.LogError($"{rule.Name} created an invalid networked '{prefabName}' instance.");
                if (summonView != null && summonView.IsValid() && summonView.IsOwner())
                {
                    ZNetScene.instance.Destroy(instance);
                }
                else
                {
                    Destroy(instance);
                }
                zdo.Set(rule.PendingKey + slot, 0L);
                return;
            }
            summonView.GetZDO().Set(rule.Flag, true);
            float health = boss.GetHealthPercentage();
            summon.SetLevel(health < 0.25f ? 3 : health < 0.5f ? 2 : 1);
            zdo.Set(rule.ServerKey + slot, serverId);
            zdo.Set(rule.IdKey + slot, summon.GetZDOID());
            zdo.Set(rule.PendingKey + slot, 0L);
        }

        private static void QueryGuarded(long sender, ZDOID bossId, int slot, ZDOID id) =>
            Query(sender, bossId, slot, id, Guarded);

        private static void QuerySentry(long sender, ZDOID bossId, int slot, ZDOID id) =>
            Query(sender, bossId, slot, id, Sentry);

        private static void Query(long sender, ZDOID bossId, int slot, ZDOID summonId, SummonRule rule)
        {
            if (!ZNet.instance.IsServer() || slot < 0 || slot >= rule.Limit || summonId == ZDOID.None)
            {
                return;
            }
            ZDO boss = ZDOMan.instance.GetZDO(bossId);
            if (boss == null || boss.GetOwner() != sender || boss.GetZDOID(rule.IdKey + slot) != summonId)
            {
                return;
            }
            if (EnemyModifierSummoner.IsConfirmedGone(summonId, boss.GetLong(rule.ServerKey + slot)))
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, rule.ResultRpc, bossId, slot, summonId, true);
            }
            else if (ZDOMan.instance.GetZDO(summonId) == null)
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, rule.ResultRpc, bossId, slot, summonId, false);
            }
        }

        private static void ResultGuarded(long sender, ZDOID bossId, int slot, ZDOID id, bool gone) =>
            Result(sender, bossId, slot, id, gone, Guarded);

        private static void ResultSentry(long sender, ZDOID bossId, int slot, ZDOID id, bool gone) =>
            Result(sender, bossId, slot, id, gone, Sentry);

        private static void Result(long sender, ZDOID bossId, int slot, ZDOID summonId, bool gone, SummonRule rule)
        {
            ZNetPeer server = ZNet.instance.GetServerPeer();
            long serverId = ZNet.instance.IsServer() ? ZNet.GetUID() : (server != null ? server.m_uid : 0L);
            if (serverId == 0L || sender != serverId || slot < 0 || slot >= rule.Limit || ZNetScene.instance == null)
            {
                return;
            }
            GameObject instance = ZNetScene.instance.FindInstance(bossId);
            ZNetView view = instance != null ? instance.GetComponent<ZNetView>() : null;
            if (view == null || !view.IsValid() || !view.IsOwner() ||
                view.GetZDO().GetZDOID(rule.IdKey + slot) != summonId)
            {
                return;
            }
            if (gone)
            {
                view.GetZDO().Set(rule.IdKey + slot, ZDOID.None);
            }
            else
            {
                BossModifierSummons tracker = instance.GetComponent<BossModifierSummons>();
                if (tracker != null)
                {
                    ref bool warned = ref (rule == Guarded ? ref tracker.warnedGuardedUnconfirmed : ref tracker.warnedSentryUnconfirmed);
                    if (!warned)
                    {
                        warned = true;
                        Jotunn.Logger.LogWarning($"{rule.Name} is waiting for the server to confirm a missing summon was destroyed.");
                    }
                }
            }
        }
    }
}
