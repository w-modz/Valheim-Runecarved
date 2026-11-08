using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Runecarved
{
    internal static class SpawnEnemyCompletion
    {
        internal static bool TryGetOptions(Terminal terminal, out string word, out List<string> options)
        {
            word = null;
            options = null;
            if (terminal != global::Console.instance || terminal.m_input == null)
            {
                return false;
            }

            string text = terminal.m_input.text;
            string[] parts = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !parts[0].Equals("spawn_mod", StringComparison.OrdinalIgnoreCase) ||
                text.IndexOf(' ') < 0)
            {
                return false;
            }

            bool newArgument = text.EndsWith(" ", StringComparison.Ordinal);
            int position = newArgument ? parts.Length : parts.Length - 1;
            word = newArgument ? "" : parts[parts.Length - 1];
            options = new List<string>();
            if (position == 1 && ZNetScene.instance != null)
            {
                foreach (string name in ZNetScene.instance.GetPrefabNames())
                {
                    GameObject prefab = ZNetScene.instance.GetPrefab(name);
                    if (prefab != null && EnemyModifiers.IsEligiblePrefab(prefab))
                    {
                        options.Add(name);
                    }
                }
            }
            else if (position >= 2 && position <= EnemyModifiers.MaxCommandModifiers + 1)
            {
                GameObject prefab = ZNetScene.instance != null && parts.Length > 1
                    ? ZNetScene.instance.GetPrefab(parts[1])
                    : null;
                foreach (EnemyModifierDefinition definition in EnemyModifierCatalog.All)
                {
                    if (definition.Group != EnemyModifierGroup.Boss &&
                        (prefab == null || EnemyModifiers.IsCompatible(definition, prefab)) &&
                        (position != 3 || !definition.Id.Equals(parts[2], StringComparison.OrdinalIgnoreCase)))
                    {
                        options.Add(definition.Id);
                    }
                }
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Terminal), "tabCycle")]
    internal static class SpawnEnemyTabCompletionPatch
    {
        private static bool Prefix(Terminal __instance, ref string word, ref List<string> options)
        {
            if (!SpawnEnemyCompletion.TryGetOptions(__instance, out string currentWord, out List<string> currentOptions))
            {
                return true;
            }
            if (__instance.m_input.caretPosition != __instance.m_input.text.Length)
            {
                return false;
            }
            word = currentWord;
            options = currentOptions;
            return true;
        }
    }

    [HarmonyPatch(typeof(Terminal), "updateSearch")]
    internal static class SpawnEnemySearchCompletionPatch
    {
        private static void Prefix(Terminal __instance, ref string word, ref List<string> options)
        {
            if (SpawnEnemyCompletion.TryGetOptions(__instance, out string currentWord, out List<string> currentOptions))
            {
                word = currentWord;
                options = currentOptions;
            }
        }
    }
}
