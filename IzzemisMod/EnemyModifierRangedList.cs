using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace Runecarved
{
    internal static class EnemyModifierRangedList
    {
        private const string DefaultPrefabs =
            "Draugr_Ranged, Surtling, Hatchling, GoblinArcher, GoblinShaman, Gjall, " +
            "DvergerMageFire, DvergerMageIce, Dverger, " +
            "Charred_Archer, Charred_Mage, Charred_Twitcher";
        private static readonly HashSet<string> Prefabs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static ConfigEntry<string> rangedPrefabs;

        internal static void Bind(ConfigFile config)
        {
            rangedPrefabs = config.Bind(
                "Enemy Modifiers",
                "RangedPrefabs",
                DefaultPrefabs,
                "Comma-separated enemy prefab IDs eligible for Volley and Quickshot. Entries still require a projectile attack.");
            rangedPrefabs.SettingChanged += (sender, args) => Refresh();
            Refresh();
        }

        private static void Refresh()
        {
            Prefabs.Clear();
            foreach (string entry in rangedPrefabs.Value.Split(','))
            {
                string prefab = entry.Trim();
                if (prefab.Length > 0)
                {
                    Prefabs.Add(prefab);
                }
            }
        }

        internal static bool IsRanged(GameObject enemy)
        {
            return enemy != null && Prefabs.Contains(Utils.GetPrefabName(enemy));
        }
    }
}
