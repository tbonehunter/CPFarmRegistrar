// ModEntry.cs
using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData;

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
        private SaveRescue Rescue;
        private List<DetectedCPFarm> DetectedFarms;
        private ModConfig Config;

        public override void Entry(IModHelper helper)
        {
            Config = helper.ReadConfig<ModConfig>();
            Detector = new CPFarmDetector(Monitor, Helper);

            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
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

            // Register the save rescue console command
            Rescue = new SaveRescue(Monitor, Helper, DetectedFarms);
            Rescue.RegisterCommand();

            // Register the pre-select command for pre-CPFR saves
            Helper.ConsoleCommands.Add(
                "cpfr_select",
                "Pre-select a CP farm mod to use when loading a pre-CPFR save.\n" +
                "Usage:\n" +
                "  cpfr_select          - Lists available CP farms\n" +
                "  cpfr_select <number> - Pre-selects the farm by number\n" +
                "Run this BEFORE loading a save that was created without CPFR.",
                OnCpfrSelect);

            Monitor.Log(
                $"CP Farm Registrar initialized. " +
                $"Registered {DetectedFarms.Count} CP farm(s) as selectable types.",
                LogLevel.Info);

            // Register with GMCM if available
            RegisterGmcm();

            // Apply config-based pre-selection if set
            ApplyConfigPreSelection();

            // Prompt the player to pre-select a farm for pre-CPFR saves
            Monitor.Log(
                "If loading a save created without CPFR, select a CP farm first:",
                LogLevel.Info);
            for (int i = 0; i < DetectedFarms.Count; i++)
            {
                Monitor.Log(
                    $"  {i + 1}. {DetectedFarms[i].ModName} " +
                    $"(replaces {DetectedFarms[i].ReplacedFarmName} farm)",
                    LogLevel.Info);
            }
            Monitor.Log(
                "Use: cpfr_select <number>  (e.g., cpfr_select 1)",
                LogLevel.Info);
            Monitor.Log(
                "Or use the Mod Config Menu (GMCM) to select a farm.",
                LogLevel.Info);
        }

        private void RegisterGmcm()
        {
            var gmcmApi = Helper.ModRegistry.GetApi
                <IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");

            if (gmcmApi == null)
            {
                Monitor.Log(
                    "Generic Mod Config Menu not found. " +
                    "Use the cpfr_select console command instead.",
                    LogLevel.Debug);
                return;
            }

            gmcmApi.Register(
                mod: ModManifest,
                reset: () => Config = new ModConfig(),
                save: () =>
                {
                    Helper.WriteConfig(Config);
                    ApplyConfigPreSelection();
                });

            gmcmApi.AddParagraph(
                mod: ModManifest,
                text: () =>
                    $"{DetectedFarms.Count} CP farm mod(s) detected. " +
                    $"Scroll the dropdown to see all options.");

            // Build dropdown choices: "None" + each detected farm
            var choices = new List<string> { "None" };
            choices.AddRange(DetectedFarms.Select(f => f.UniqueModId));
            string[] allowedValues = choices.ToArray();

            gmcmApi.AddTextOption(
                mod: ModManifest,
                name: () => "Pre-Select CP Farm",
                tooltip: () =>
                    "Choose a CP farm mod to use when loading a save " +
                    "created before CPFR was installed. After loading " +
                    "and saving, this resets to 'None' automatically.",
                getValue: () => Config.PreSelectedFarmModId,
                setValue: value => Config.PreSelectedFarmModId = value,
                allowedValues: allowedValues,
                formatAllowedValue: value =>
                {
                    if (value == "None")
                        return "None (use save's farm type)";
                    var farm = DetectedFarms.FirstOrDefault(
                        f => f.UniqueModId == value);
                    return farm != null
                        ? $"{farm.ModName} (replaces {farm.ReplacedFarmName})"
                        : value;
                });

            Monitor.Log(
                "GMCM integration registered.",
                LogLevel.Debug);
        }

        private void ApplyConfigPreSelection()
        {
            if (Config.PreSelectedFarmModId == "None" ||
                string.IsNullOrEmpty(Config.PreSelectedFarmModId))
            {
                FarmRegistrar.PreSelectedFarm = null;
                return;
            }

            var farm = DetectedFarms?.FirstOrDefault(
                f => f.UniqueModId.Equals(
                    Config.PreSelectedFarmModId,
                    StringComparison.OrdinalIgnoreCase));

            if (farm != null)
            {
                FarmRegistrar.PreSelectedFarm = farm;
                Monitor.Log(
                    $"Config pre-selected '{farm.ModName}' for next " +
                    $"pre-CPFR save load.",
                    LogLevel.Info);
            }
            else
            {
                Monitor.Log(
                    $"Config references unknown farm mod " +
                    $"'{Config.PreSelectedFarmModId}'. Ignoring.",
                    LogLevel.Warn);
                FarmRegistrar.PreSelectedFarm = null;
            }
        }

        private void OnCpfrSelect(string command, string[] args)
        {
            if (DetectedFarms == null || DetectedFarms.Count == 0)
            {
                Monitor.Log("No CP farm mods detected.", LogLevel.Info);
                return;
            }

            if (args.Length == 0)
            {
                Monitor.Log("Available CP farm mods:", LogLevel.Info);
                for (int i = 0; i < DetectedFarms.Count; i++)
                {
                    Monitor.Log(
                        $"  {i + 1}. {DetectedFarms[i].ModName} " +
                        $"(replaces {DetectedFarms[i].ReplacedFarmName} farm)",
                        LogLevel.Info);
                }
                Monitor.Log(
                    "Use: cpfr_select <number>  (e.g., cpfr_select 1)",
                    LogLevel.Info);
                return;
            }

            if (!int.TryParse(args[0], out int selection) ||
                selection < 1 || selection > DetectedFarms.Count)
            {
                Monitor.Log(
                    $"Invalid selection. Enter a number between 1 and " +
                    $"{DetectedFarms.Count}.",
                    LogLevel.Error);
                return;
            }

            var farm = DetectedFarms[selection - 1];
            FarmRegistrar.PreSelectedFarm = farm;

            Monitor.Log(
                $"Pre-selected '{farm.ModName}' ({farm.RegisteredFarmId}). " +
                $"Now load your save — the CP farm map will be used instead of " +
                $"the vanilla {farm.ReplacedFarmName} farm.",
                LogLevel.Info);
            Monitor.Log(
                "After loading, save the game to make the change permanent.",
                LogLevel.Alert);
        }

        private void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
        {
            if (FarmRegistrar.PreSelectedFarm == null)
                return;

            if (!Context.IsMainPlayer)
                return;

            // If save already uses a CPFR farm, no rescue needed
            if (Game1.whichFarm == 7)
            {
                string currentId = Game1.GetFarmTypeID();
                if (currentId != null &&
                    currentId.StartsWith("CPFarmRegistrar/",
                        StringComparison.OrdinalIgnoreCase))
                {
                    Monitor.Log(
                        $"Save already uses CPFR farm '{currentId}'. " +
                        $"Clearing pre-selection.",
                        LogLevel.Debug);
                    FarmRegistrar.PreSelectedFarm = null;
                    return;
                }
            }

            var farm = FarmRegistrar.PreSelectedFarm;

            try
            {
                // Set the farm type so saving persists it
                Game1.whichFarm = 7;

                var additionalFarms = Game1.content.Load<List<ModFarmType>>(
                    "Data\\AdditionalFarms");

                var modFarmType = additionalFarms.FirstOrDefault(
                    f => f.Id == farm.RegisteredFarmId);

                if (modFarmType != null)
                {
                    Game1.whichModFarm = modFarmType;
                    Monitor.Log(
                        $"Applied pre-selected farm '{farm.ModName}' " +
                        $"({farm.RegisteredFarmId}) to loaded save.",
                        LogLevel.Info);
                    Monitor.Log(
                        "Save your game to make this change permanent.",
                        LogLevel.Alert);
                }
                else
                {
                    Monitor.Log(
                        $"Could not find '{farm.RegisteredFarmId}' in " +
                        $"Data/AdditionalFarms.",
                        LogLevel.Error);
                }
            }
            catch (Exception ex)
            {
                Monitor.Log(
                    $"Failed to apply pre-selected farm: {ex.Message}",
                    LogLevel.Error);
            }

            // Clear pre-selection and reset config so it only applies once
            FarmRegistrar.PreSelectedFarm = null;
            Config.PreSelectedFarmModId = "None";
            Helper.WriteConfig(Config);
        }

        private void OnReturnedToTitle(object sender, ReturnedToTitleEventArgs e)
        {
            FarmRegistrar.PreSelectedFarm = null;
        }
    }
}
