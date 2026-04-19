// SaveRescue.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData;

namespace CPFarmRegistrar
{
    /// <summary>
    /// Provides a console command to rescue saves that were created before
    /// CPFR was installed. Changes the save's farm type from a vanilla
    /// integer to a CPFR-registered farm ID so the CP farm mod's patches
    /// are allowed through on load.
    /// 
    /// Usage: cpfr_rescue
    ///   Lists detected CP farm mods and their IDs.
    /// 
    /// Usage: cpfr_rescue &lt;mod_id&gt;
    ///   Patches the currently loaded save to use the specified CP farm.
    ///   The game must be loaded into the save you want to rescue.
    ///   After running the command, save the game normally.
    /// </summary>
    public class SaveRescue
    {
        private readonly IMonitor Monitor;
        private readonly IModHelper Helper;
        private readonly List<DetectedCPFarm> DetectedFarms;

        public SaveRescue(
            IMonitor monitor,
            IModHelper helper,
            List<DetectedCPFarm> detectedFarms)
        {
            Monitor = monitor;
            Helper = helper;
            DetectedFarms = detectedFarms;
        }

        /// <summary>
        /// Registers the cpfr_rescue console command.
        /// </summary>
        public void RegisterCommand()
        {
            Helper.ConsoleCommands.Add(
                "cpfr_rescue",
                "Rescues a pre-CPFR save by changing its farm type to a " +
                "CPFR-registered CP farm.\n" +
                "Usage:\n" +
                "  cpfr_rescue          - Lists available CP farm IDs\n" +
                "  cpfr_rescue <mod_id> - Patches the current save to use " +
                "the specified CP farm\n" +
                "Example:\n" +
                "  cpfr_rescue DaisyNiko.OvergrownGardenFarm",
                OnCommand);
        }

        private void OnCommand(string command, string[] args)
        {
            // No arguments: list available farms
            if (args.Length == 0)
            {
                ListAvailableFarms();
                return;
            }

            string modId = args[0];

            // Find the matching detected farm
            var farm = DetectedFarms.FirstOrDefault(
                f => f.UniqueModId.Equals(modId,
                    StringComparison.OrdinalIgnoreCase));

            if (farm == null)
            {
                Monitor.Log(
                    $"No detected CP farm found with ID '{modId}'.",
                    LogLevel.Error);
                Monitor.Log(
                    "Use 'cpfr_rescue' with no arguments to list available IDs.",
                    LogLevel.Info);
                return;
            }

            // Verify a save is loaded
            if (!Context.IsWorldReady)
            {
                Monitor.Log(
                    "No save is loaded. Load the save you want to rescue " +
                    "first, then run this command.",
                    LogLevel.Error);
                return;
            }

            // Verify this is the main player
            if (!Context.IsMainPlayer)
            {
                Monitor.Log(
                    "Only the main player can rescue a save.",
                    LogLevel.Error);
                return;
            }

            // Check if the save is already using a CPFR farm
            if (Game1.whichFarm == 7)
            {
                string currentId = Game1.GetFarmTypeID();
                if (currentId.StartsWith("CPFarmRegistrar/"))
                {
                    Monitor.Log(
                        $"This save is already using CPFR farm '{currentId}'. " +
                        $"No rescue needed.",
                        LogLevel.Info);
                    return;
                }
            }

            // Perform the rescue
            RescueSave(farm);
        }

        /// <summary>
        /// Lists all detected CP farm mods and their IDs for use with
        /// the rescue command.
        /// </summary>
        private void ListAvailableFarms()
        {
            if (DetectedFarms.Count == 0)
            {
                Monitor.Log(
                    "No CP farm mods detected. Nothing to rescue to.",
                    LogLevel.Info);
                return;
            }

            Monitor.Log(
                "Available CP farm mods for rescue:",
                LogLevel.Info);

            foreach (var farm in DetectedFarms)
            {
                Monitor.Log(
                    $"  {farm.UniqueModId} - {farm.ModName} " +
                    $"(replaces {farm.ReplacedFarmName} farm)",
                    LogLevel.Info);
            }

            Monitor.Log(
                "\nUsage: cpfr_rescue <mod_id>",
                LogLevel.Info);
            Monitor.Log(
                "Example: cpfr_rescue DaisyNiko.OvergrownGardenFarm",
                LogLevel.Info);
            Monitor.Log(
                "\nLoad the save you want to rescue first, " +
                "then run the command, then save the game.",
                LogLevel.Info);
        }

        /// <summary>
        /// Patches the currently loaded save's farm type to use the
        /// specified CPFR-registered farm. Changes Game1.whichFarm to 7
        /// and sets the farm type ID to our registered ID.
        /// The player must save the game afterward for changes to persist.
        /// </summary>
        private void RescueSave(DetectedCPFarm farm)
        {
            string previousFarmType = Game1.whichFarm == 7
                ? Game1.GetFarmTypeID()
                : $"vanilla type {Game1.whichFarm}";

            try
            {
                // Change the in-memory game state
                Game1.whichFarm = 7;

                // Set the mod farm type. In SV 1.6, the farm type ID for
                // mod farms is stored via Game1.whichModFarm which is a
                // ModFarmType reference. We need to find our registered
                // ModFarmType in Data/AdditionalFarms and set it.
                var additionalFarms = Game1.content.Load<List<ModFarmType>>(
                    "Data\\AdditionalFarms");

                var modFarmType = additionalFarms.FirstOrDefault(
                    f => f.Id == farm.RegisteredFarmId);

                if (modFarmType == null)
                {
                    Monitor.Log(
                        $"Could not find registered farm type " +
                        $"'{farm.RegisteredFarmId}' in Data/AdditionalFarms. " +
                        $"Is CPFR properly initialized?",
                        LogLevel.Error);
                    return;
                }

                Game1.whichModFarm = modFarmType;

                // Invalidate the farm map cache so the correct map loads
                Helper.GameContent.InvalidateCache("Maps/Farm");
                Helper.GameContent.InvalidateCache(
                    $"Maps/{farm.TargetMapAsset.Replace("Maps/", "")}");

                Monitor.Log(
                    $"Save rescued! Farm type changed from {previousFarmType} " +
                    $"to '{farm.ModName}' ({farm.RegisteredFarmId}).",
                    LogLevel.Info);
                Monitor.Log(
                    "IMPORTANT: Save your game now for the change to persist. " +
                    "The farm map will update on the next day or reload.",
                    LogLevel.Alert);
            }
            catch (Exception ex)
            {
                Monitor.Log(
                    $"Failed to rescue save: {ex.Message}",
                    LogLevel.Error);
            }
        }
    }
}
