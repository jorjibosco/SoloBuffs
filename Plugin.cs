using BepInEx;
using BepInEx.Configuration;
using BepInEx.Core.Logging.Interpolation;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using VampireCommandFramework;

namespace SoloBuffs
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("gg.deca.VampireCommandFramework")]
    public class Plugin : BasePlugin
    {
        public const string PluginGuid = "SoloBuffs";
        public const string PluginName = "SoloBuffs";
        public const string PluginVersion = "1.0.0";

        internal static Plugin? Instance;

        // Combat Stats
        internal static ConfigEntry<float>? SoloAttackSpeedPercent;
        internal static ConfigEntry<int>? SoloAttackSpeedType;
        internal static ConfigEntry<float>? SoloDamagePercent;
        internal static ConfigEntry<int>? SoloDamageType;
        internal static ConfigEntry<float>? SoloSpellDamagePercent;
        internal static ConfigEntry<int>? SoloSpellDamageType;
        internal static ConfigEntry<float>? SoloCritChancePercent;
        internal static ConfigEntry<int>? SoloCritChanceType;
        internal static ConfigEntry<float>? SoloCritDamagePercent;
        internal static ConfigEntry<int>? SoloCritDamageType;

        // Survivability
        internal static ConfigEntry<float>? SoloHealthPercent;
        internal static ConfigEntry<int>? SoloHealthType;
        internal static ConfigEntry<float>? SoloPhysicalLeechPercent;
        internal static ConfigEntry<int>? SoloPhysicalLeechType;
        internal static ConfigEntry<float>? SoloSpellLeechPercent;
        internal static ConfigEntry<int>? SoloSpellLeechType;
        internal static ConfigEntry<float>? SoloPhysicalResistancePercent;
        internal static ConfigEntry<int>? SoloPhysicalResistanceType;
        internal static ConfigEntry<float>? SoloSpellResistancePercent;
        internal static ConfigEntry<int>? SoloSpellResistanceType;

        // Mobility & Utility
        internal static ConfigEntry<float>? SoloMoveSpeedPercent;
        internal static ConfigEntry<float>? SoloResourceYieldPercent;
        internal static ConfigEntry<int>? SoloResourceYieldType;

        // Clan Settings
        internal static ConfigEntry<int>? SoloClanOfflineThresholdMinutes;

        // Visual buff, not configurable: the move speed taper comes from this prefab
        internal const int VisualBuffGUID = 178225731;

        public override void Load()
        {
            Instance = this;
            BindConfig();

            if (!ClassInjector.IsTypeRegisteredInIl2Cpp<SoloBuffsBehaviour>())
                ClassInjector.RegisterTypeInIl2Cpp<SoloBuffsBehaviour>();

            var go = new GameObject("SoloBuffsBehaviour");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<SoloBuffsBehaviour>();

            CommandRegistry.RegisterAll();

            bool enabled;
            var handler = new BepInExInfoLogInterpolatedStringHandler(11, 2, out enabled);
            if (enabled)
            {
                handler.AppendLiteral("[");
                handler.AppendFormatted(PluginName);
                handler.AppendLiteral("] ");
                handler.AppendFormatted(PluginVersion);
                handler.AppendLiteral(" loaded.");
            }
            Log.LogInfo(handler);
        }

        private void BindConfig()
        {
            SoloAttackSpeedPercent = Config.Bind("1. Combat Stats", "AttackSpeedValue", 0.10f,
                "Attack speed bonus when solo. Multiply: 0.10 = +10%. Add: 0.10 = +0.10 flat. Set to 0 to disable.");
            SoloAttackSpeedType = Config.Bind("1. Combat Stats", "AttackSpeedType", 0,
                "Modification type: 0 = Multiply (scales with gear), 1 = Add (flat value).");

            SoloDamagePercent = Config.Bind("1. Combat Stats", "PhysicalDamageValue", 0.10f,
                "Physical damage bonus when solo. Multiply: 0.10 = +10%. Set to 0 to disable.");
            SoloDamageType = Config.Bind("1. Combat Stats", "PhysicalDamageType", 0,
                "Modification type: 0 = Multiply (scales with gear), 1 = Add (flat value).");

            SoloSpellDamagePercent = Config.Bind("1. Combat Stats", "SpellDamageValue", 0.10f,
                "Spell damage bonus when solo. Multiply: 0.10 = +10%. Set to 0 to disable.");
            SoloSpellDamageType = Config.Bind("1. Combat Stats", "SpellDamageType", 0,
                "Modification type: 0 = Multiply (scales with gear), 1 = Add (flat value).");

            SoloCritChancePercent = Config.Bind("1. Combat Stats", "CritChanceValue", 0.10f,
                "Crit chance bonus when solo (physical + spell). Set to 0 to disable.");
            SoloCritChanceType = Config.Bind("1. Combat Stats", "CritChanceType", 0,
                "Modification type: 0 = Multiply (scales with gear), 1 = Add (flat value).");

            SoloCritDamagePercent = Config.Bind("1. Combat Stats", "CritDamageValue", 0.10f,
                "Crit damage bonus when solo (physical + spell). Set to 0 to disable.");
            SoloCritDamageType = Config.Bind("1. Combat Stats", "CritDamageType", 0,
                "Modification type: 0 = Multiply (scales with gear), 1 = Add (flat value).");

            SoloHealthPercent = Config.Bind("2. Survivability", "HealthValue", 0.10f,
                "Max health bonus when solo. Multiply: 0.10 = +10%. Add: 100 = +100 flat HP. Set to 0 to disable.");
            SoloHealthType = Config.Bind("2. Survivability", "HealthType", 0,
                "Modification type: 0 = Multiply (scales with gear), 1 = Add (flat value).");

            SoloPhysicalLeechPercent = Config.Bind("2. Survivability", "PhysicalLeechValue", 0.10f,
                "Physical lifesteal when solo. Set to 0 to disable.");
            SoloPhysicalLeechType = Config.Bind("2. Survivability", "PhysicalLeechType", 1,
                "Modification type: 0 = Multiply (scales with gear), 1 = Add (flat value).");

            SoloSpellLeechPercent = Config.Bind("2. Survivability", "SpellLeechValue", 0.10f,
                "Spell lifesteal when solo. Set to 0 to disable.");
            SoloSpellLeechType = Config.Bind("2. Survivability", "SpellLeechType", 1,
                "Modification type: 0 = Multiply (scales with gear), 1 = Add (flat value).");

            SoloPhysicalResistancePercent = Config.Bind("2. Survivability", "PhysicalResistanceValue", 0.0f,
                "Physical damage reduction when solo. Set to 0 to disable.");
            SoloPhysicalResistanceType = Config.Bind("2. Survivability", "PhysicalResistanceType", 1,
                "Modification type: 0 = Multiply (scales with gear), 1 = Add (flat value).");

            SoloSpellResistancePercent = Config.Bind("2. Survivability", "SpellResistanceValue", 0.0f,
                "Spell damage reduction when solo. Set to 0 to disable.");
            SoloSpellResistanceType = Config.Bind("2. Survivability", "SpellResistanceType", 1,
                "Modification type: 0 = Multiply (scales with gear), 1 = Add (flat value).");

            SoloMoveSpeedPercent = Config.Bind("3. Mobility & Utility", "MoveSpeedValue", 0.20f,
                "Movement speed is tuned to scale less with higher item bonuses for balance - " +
                "low-speed builds benefit more, high-speed builds benefit less. " +
                "Do not set below 0.20, which gives no bonus speed at capped speed, or the buff will act as a nerf for players. ");

            SoloResourceYieldPercent = Config.Bind("3. Mobility & Utility", "ResourceYieldValue", 0.10f,
                "Resource gathering bonus when solo. Set to 0 to disable.");
            SoloResourceYieldType = Config.Bind("3. Mobility & Utility", "ResourceYieldType", 0,
                "Modification type: 0 = Multiply (scales with gear), 1 = Add (flat value).");

            SoloClanOfflineThresholdMinutes = Config.Bind("4. Clan Settings", "ClanOfflineThresholdMinutes", 30,
                "Minutes other clan members must be offline before you are treated as solo. Set to 0 for instant.");
        }

        internal static void ReloadConfig()
        {
            if (Instance == null) return;
            Instance.Config.Reload();
            Instance.Config.Save();
            Instance.Log.LogInfo("[SoloBuffs] Config reloaded from disk.");

            SoloBuffsLogic.RefreshAllBuffedCharacters();
        }
    }
}