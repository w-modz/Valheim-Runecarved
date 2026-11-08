using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace Runecarved
{
    internal static class EnemyModifierAllowlist
    {
        // Keys must match Utils.GetPrefabName output; DefaultEnabled sets only the initial config value
        private static readonly Dictionary<string, (string Biome, bool DefaultEnabled)> Enemies =
            new Dictionary<string, (string, bool)>(StringComparer.Ordinal)
        {
            // Meadows
            { "Boar", ("Meadows", false) },
            { "Neck", ("Meadows", false) },
            { "Greyling", ("Meadows", false) },

            // Black Forest
            { "Greydwarf", ("Black Forest", true) },
            { "Greydwarf_Shaman", ("Black Forest", true) },
            { "Greydwarf_Elite", ("Black Forest", true) },
            { "Skeleton", ("Black Forest", true) },
            { "Skeleton_Poison", ("Black Forest", true) },
            { "Ghost", ("Black Forest", true) },
            { "Troll", ("Black Forest", true) },

            // Swamp
            { "Blob", ("Swamp", true) },
            { "BlobElite", ("Swamp", true) },
            { "Leech", ("Swamp", false) },
            { "Draugr", ("Swamp", true) },
            { "Draugr_Ranged", ("Swamp", true) },
            { "Draugr_Elite", ("Swamp", true) },
            { "Surtling", ("Swamp", false) },
            { "Wraith", ("Swamp", true) },
            { "Abomination", ("Swamp", true) },

            // Mountain
            { "Wolf", ("Mountain", true) },
            { "Fenring", ("Mountain", true) },
            { "Fenring_Cultist", ("Mountain", true) },
            { "Hatchling", ("Mountain", false) },
            { "Bat", ("Mountain", true) },
            { "Ulv", ("Mountain", true) },
            { "StoneGolem", ("Mountain", true) },

            // Plains
            { "Goblin", ("Plains", true) },
            { "GoblinArcher", ("Plains", true) },
            { "GoblinShaman", ("Plains", true) },
            { "GoblinBrute", ("Plains", true) },
            { "Deathsquito", ("Plains", false) },
            { "Lox", ("Plains", true) },

            // Mistlands
            { "Tick", ("Mistlands", false) },
            { "Seeker", ("Mistlands", true) },
            { "SeekerBrood", ("Mistlands", false) },
            { "SeekerBrute", ("Mistlands", true) },
            { "Gjall", ("Mistlands", true) },
            { "Dverger", ("Mistlands", true) },
            { "DvergerMage", ("Mistlands", true) },
            { "DvergerMageFire", ("Mistlands", true) },
            { "DvergerMageIce", ("Mistlands", true) },
            { "DvergerMageSupport", ("Mistlands", true) },

            // Ashlands
            { "Charred_Melee", ("Ashlands", true) },
            { "Charred_Archer", ("Ashlands", true) },
            { "Charred_Mage", ("Ashlands", true) },
            { "Charred_Twitcher", ("Ashlands", true) },
            { "Asksvin", ("Ashlands", true) },
            { "Morgen", ("Ashlands", true) },
            { "FallenValkyrie", ("Ashlands", true) },
            { "Volture", ("Ashlands", false) },
            { "BlobLava", ("Ashlands", false) }
        };

        private static readonly Dictionary<string, ConfigEntry<bool>> Allowed =
            new Dictionary<string, ConfigEntry<bool>>(StringComparer.Ordinal);

        internal static void Bind(ConfigFile config)
        {
            foreach (var enemy in Enemies)
            {
                Allowed.Add(enemy.Key, config.Bind(
                    "Enemy Modifiers - " + enemy.Value.Biome,
                    enemy.Key,
                    enemy.Value.DefaultEnabled,
                    "Allow this enemy prefab to receive new enemy modifiers. Existing modifiers remain active."));
            }
        }

        internal static bool IsAllowed(GameObject enemy)
        {
            string prefabName = Utils.GetPrefabName(enemy);
            // Unknown prefab names are not allowed to receive new modifiers
            return Allowed.TryGetValue(prefabName, out ConfigEntry<bool> enabled) && enabled.Value;
        }
    }
}
