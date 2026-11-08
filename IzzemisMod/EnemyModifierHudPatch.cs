using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace Runecarved
{
    [HarmonyPatch(typeof(EnemyHud), "ShowHud")]
    internal static class EnemyModifierHudPatch
    {
        private const string LabelName = "RunecarvedEnemyModifiers";
        private static bool missingHealthWarningLogged;
        private static bool missingStarsWarningLogged;
        internal static readonly FieldInfo HudsField = AccessTools.Field(typeof(EnemyHud), "m_huds");
        internal static readonly FieldInfo GuiField =
            AccessTools.Field(HudsField.FieldType.GetGenericArguments()[1], "m_gui");

        private static void Postfix(EnemyHud __instance, Character c, bool isMount)
        {
            if (c == null || c.IsPlayer() || isMount)
            {
                return;
            }

            var huds = (IDictionary)HudsField.GetValue(__instance);
            object hud = huds[c];
            if (hud == null)
            {
                return;
            }

            var gui = (GameObject)GuiField.GetValue(hud);
            IReadOnlyList<EnemyModifierDefinition> modifiers = EnemyModifiers.GetModifiers(c);
            Transform health = gui.transform.Find("Health");
            if (health == null)
            {
                if (!missingHealthWarningLogged && modifiers.Count > 0)
                {
                    Jotunn.Logger.LogWarning("Cannot display enemy modifiers: health bar is missing.");
                    missingHealthWarningLogged = true;
                }
                return;
            }

            StackStars(gui.transform);
            int row = 0;
            foreach (EnemyModifierDefinition modifier in modifiers)
            {
                UpdateLabel(gui.transform, health, row++, modifier.DisplayToken);
            }
            // Reuse label objects across HUD refreshes and hide any rows left over from a larger modifier set
            for (; row < EnemyModifierCatalog.All.Count; row++)
            {
                Transform unused = health.Find(LabelName + row);
                if (unused != null)
                {
                    unused.gameObject.SetActive(false);
                }
            }
        }

        private static void UpdateLabel(Transform gui, Transform health, int row, string token)
        {
            string labelName = LabelName + row;
            Transform existing = health.Find(labelName);
            if (existing == null)
            {
                var name = gui.Find("Name").GetComponent<TextMeshProUGUI>();
                TextMeshProUGUI label = Object.Instantiate(name, health, false);
                label.name = labelName;
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.enableAutoSizing = false;
                label.fontSize = name.fontSize * 0.8f;
                label.enableVertexGradient = false;
                label.color = new Color(0.35f, 0.85f, 1f, 1f);
                label.raycastTarget = false;
                RectTransform rect = label.rectTransform;
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(80f, 20f);
                rect.anchoredPosition = new Vector2(20f, -2f - row * 16f);
                existing = label.transform;
            }

            existing.gameObject.SetActive(true);
            existing.GetComponent<TextMeshProUGUI>().text = Localization.instance.Localize(token);
        }

        internal static void StackStars(Transform gui, int extraStars = 0)
        {
            Transform level = gui.Find("level_3");
            if (level == null)
            {
                return;
            }

            RectTransform first = level.Find("star") as RectTransform;
            RectTransform second = level.Find("star (1)") as RectTransform;
            if (first == null || second == null)
            {
                if (!missingStarsWarningLogged)
                {
                    Jotunn.Logger.LogWarning("Cannot stack enemy stars: star icons are missing.");
                    missingStarsWarningLogged = true;
                }
                return;
            }

            second.anchoredPosition = first.anchoredPosition -
                new Vector2(0f, Mathf.Max(first.rect.height, second.rect.height) + 1f);

            float spacing = Mathf.Max(first.rect.height, second.rect.height) + 1f;
            for (int i = 0; i < 6; i++)
            {
                // Extra icons are reused as enemy levels change instead of being recreated every HUD update
                string name = "RunecarvedExtraStar" + i;
                Transform extra = level.Find(name);
                if (extra == null && i < extraStars)
                {
                    RectTransform clone = Object.Instantiate(first, level, false);
                    clone.name = name;
                    clone.anchoredPosition = first.anchoredPosition - new Vector2(0f, spacing * (i + 2));
                    extra = clone;
                }
                if (extra != null)
                {
                    extra.gameObject.SetActive(i < extraStars);
                }
            }
        }
    }

    [HarmonyPatch(typeof(EnemyHud), "UpdateHuds")]
    internal static class EnemyModifierHighLevelHudPatch
    {
        private static void Postfix(EnemyHud __instance)
        {
            var huds = (IDictionary)EnemyModifierHudPatch.HudsField.GetValue(__instance);
            foreach (DictionaryEntry entry in huds)
            {
                var character = (Character)entry.Key;
                if (character == null || character.IsPlayer())
                {
                    continue;
                }
                var gui = (GameObject)EnemyModifierHudPatch.GuiField.GetValue(entry.Value);
                Transform level = gui.transform.Find("level_3");
                if (level == null)
                {
                    continue;
                }
                int stars = character.GetLevel() - 1;
                bool showExtra = stars > 2 && EnemyModifiers.GetModifiers(character).Count > 0;
                EnemyModifierHudPatch.StackStars(gui.transform, showExtra ? Mathf.Min(stars - 2, 6) : 0);
                if (showExtra)
                {
                    level.gameObject.SetActive(true);
                }
            }
        }
    }
}
