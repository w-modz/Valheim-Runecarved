using BepInEx;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;

namespace Runecarved
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Patch)]
    internal partial class Runecarved : BaseUnityPlugin
    {
        public const string PluginGUID = "com.izzemi.izzemismod";
        public const string PluginName = "Runecarved";
        public const string PluginVersion = "1.0.0";
        public static CustomLocalization ModLocalization = LocalizationManager.Instance.GetLocalization();

        private void Awake()
        {
            EnemyModifierCatalog.RegisterLocalization(ModLocalization);
            EnemyModifierAllowlist.Bind(Config);
            EnemyModifierRangedList.Bind(Config);
            EnemyModifierSummoner.Bind(Config);
            new Harmony(PluginGUID).PatchAll();

            ModCommands.Register();
            RuneItems.Register();
            EnemyModifierFiretread.Register();
            EnemyModifierCombatAbilities.Register();
            EnemySummonIndicator.Register();
            RuneGui.Bind(Config);
            RuneDrops.Bind(Config);
        }

        private void Update()
        {
            // Disabled for now: the hotkey also fired while typing in chat and search bars.
            // RuneGui.HandleHotkey();
        }
    }
}