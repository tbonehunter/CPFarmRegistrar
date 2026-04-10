// ModEntry.cs
using System.Collections.Generic;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace CPFarmRegistrar
{
    /// <summary>
    /// SMAPI mod entry point for CP Farm Registrar.
    /// 
    /// Detects Content Patcher farm mods that silently replace vanilla farm maps,
    /// registers them as proper selectable farm types via Data/AdditionalFarms,
    /// and uses Harmony to filter CP's Load patches so only the selected farm's
    /// map replacement is applied.
    /// 
    /// Requires: Content Patcher
    /// </summary>
    public class ModEntry : Mod
    {
        private CPFarmDetector Detector;
        private FarmRegistrar Registrar;
        private List<DetectedCPFarm> DetectedFarms;

        public override void Entry(IModHelper helper)
        {
            Detector = new CPFarmDetector(Monitor, Helper);

            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        }

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            // Verify dependencies are loaded
            if (!Helper.ModRegistry.IsLoaded("Pathoschild.ContentPatcher"))
            {
                Monitor.Log(
                    "Content Patcher is not installed. " +
                    "CP Farm Registrar requires Content Patcher to function.",
                    LogLevel.Error);
                return;
            }

            // Scan for CP farm mods that replace vanilla maps
            DetectedFarms = Detector.DetectCPFarmMods();

            if (DetectedFarms.Count == 0)
            {
                Monitor.Log(
                    "No CP farm map replacements found. " +
                    "Nothing to register.",
                    LogLevel.Info);
                return;
            }

            // Initialize the registrar and apply Harmony patches
            Registrar = new FarmRegistrar(Monitor, Helper, DetectedFarms);
            Registrar.Initialize(ModManifest.UniqueID);

            Monitor.Log(
                $"CP Farm Registrar initialized. " +
                $"Registered {DetectedFarms.Count} CP farm(s) as selectable types.",
                LogLevel.Info);
        }
    }
}
