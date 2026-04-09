// CPFarmDetector.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StardewModdingAPI;

namespace CPFarmRegistrar
{
    /// <summary>
    /// Scans installed Content Patcher content packs to detect mods that
    /// silently replace vanilla farm maps via Load actions.
    /// Skips mods that already self-register in Data/AdditionalFarms.
    /// </summary>
    public class CPFarmDetector
    {
        private readonly IMonitor Monitor;
        private readonly IModHelper Helper;

        public CPFarmDetector(IMonitor monitor, IModHelper helper)
        {
            Monitor = monitor;
            Helper = helper;
        }

        /// <summary>
        /// Scans all installed CP content packs and returns a list of detected
        /// farm map replacements. Mods that self-register in Data/AdditionalFarms
        /// are excluded.
        /// </summary>
        public List<DetectedCPFarm> DetectCPFarmMods()
        {
            var detected = new List<DetectedCPFarm>();
            var modRegistry = Helper.ModRegistry;

            foreach (var mod in modRegistry.GetAll())
            {
                // Skip non-CP content packs
                if (mod.Manifest.ContentPackFor?.UniqueID == null)
                    continue;

                if (!mod.Manifest.ContentPackFor.UniqueID.Equals(
                    "Pathoschild.ContentPatcher", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip our own mod just in case
                if (mod.Manifest.UniqueID.Equals(
                    "tbonehunter.CPFarmRegistrar", StringComparison.OrdinalIgnoreCase))
                    continue;

                var farmTargets = ScanContentPack(mod);
                if (farmTargets.Count > 0)
                {
                    foreach (var target in farmTargets)
                    {
                        string normalized = VanillaFarmMap.Normalize(target);

                        if (!VanillaFarmMap.AssetToDisplayName.TryGetValue(
                            normalized, out string replacedName))
                            continue;

                        var entry = new DetectedCPFarm
                        {
                            UniqueModId = mod.Manifest.UniqueID,
                            ModName = mod.Manifest.Name,
                            Author = mod.Manifest.Author,
                            Description = mod.Manifest.Description
                                ?? $"Custom {replacedName} farm replacement.",
                            TargetMapAsset = normalized,
                            ReplacedFarmName = replacedName,
                            ContentPackDirectory = GetContentPackDirectory(mod)
                        };

                        detected.Add(entry);
                        Monitor.Log(
                            $"Detected CP farm mod: {entry}",
                            LogLevel.Info);
                    }
                }
            }

            if (detected.Count == 0)
                Monitor.Log(
                    "No CP farm map replacements detected.",
                    LogLevel.Info);
            else
                Monitor.Log(
                    $"Detected {detected.Count} CP farm map replacement(s).",
                    LogLevel.Info);

            return detected;
        }

        /// <summary>
        /// Reads a CP content pack's content.json and looks for Load actions
        /// targeting vanilla farm maps. Skips mods that already register
        /// themselves in Data/AdditionalFarms.
        /// </summary>
        private List<string> ScanContentPack(IModInfo mod)
        {
            var farmTargets = new List<string>();

            string contentPackDir = GetContentPackDirectory(mod);
            if (contentPackDir == null)
                return farmTargets;

            string contentJsonPath = Path.Combine(contentPackDir, "content.json");
            if (!File.Exists(contentJsonPath))
                return farmTargets;

            try
            {
                string json = File.ReadAllText(contentJsonPath);
                JObject root = JObject.Parse(json);

                JArray changes = root["Changes"] as JArray;
                if (changes == null)
                    return farmTargets;

                // First pass: check if this mod self-registers in
                // Data/AdditionalFarms. If so, it handles its own farm
                // selector entry and doesn't need us.
                foreach (JToken change in changes)
                {
                    string action = change["Action"]?.ToString();
                    if (action == null)
                        continue;

                    if (action.Equals("EditData", StringComparison.OrdinalIgnoreCase))
                    {
                        string editTarget = change["Target"]?.ToString();
                        if (editTarget != null && editTarget.Equals(
                            "Data/AdditionalFarms",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            Monitor.Log(
                                $"  {mod.Manifest.Name} self-registers in " +
                                $"Data/AdditionalFarms. Skipping.",
                                LogLevel.Trace);
                            return farmTargets;
                        }
                    }
                }

                // Second pass: scan for Load actions targeting vanilla farm maps.
                foreach (JToken change in changes)
                {
                    string action = change["Action"]?.ToString();
                    if (action == null)
                        continue;

                    // Only look for Load actions — these are full map replacements.
                    // EditMap actions modify existing maps and are less likely to be
                    // full farm replacements, but we could extend detection later.
                    if (!action.Equals("Load", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string target = change["Target"]?.ToString();
                    if (target == null)
                        continue;

                    // Skip targets that use CP tokens (e.g., "Maps/Farm_{{something}}")
                    // We can't resolve these at scan time.
                    if (target.Contains("{{"))
                    {
                        Monitor.Log(
                            $"  Skipping tokenized target '{target}' in " +
                            $"{mod.Manifest.Name} - cannot resolve CP tokens " +
                            $"at scan time.",
                            LogLevel.Trace);
                        continue;
                    }

                    string normalized = VanillaFarmMap.Normalize(target);
                    if (VanillaFarmMap.IsVanillaFarmMap(normalized))
                    {
                        // Check if this patch has a When condition that already
                        // limits it to a specific context. Log it for visibility
                        // but still register it, since the vanilla map is still
                        // replaced when the condition is met.
                        JToken when = change["When"];
                        if (when != null)
                        {
                            Monitor.Log(
                                $"  Found conditional Load targeting '{normalized}' " +
                                $"in {mod.Manifest.Name} (has When block - may be " +
                                $"partially scoped).",
                                LogLevel.Trace);
                        }

                        if (!farmTargets.Contains(normalized))
                            farmTargets.Add(normalized);
                    }
                }
            }
            catch (Exception ex)
            {
                Monitor.Log(
                    $"Failed to parse content.json for {mod.Manifest.Name}: " +
                    $"{ex.Message}",
                    LogLevel.Warn);
            }

            return farmTargets;
        }

        /// <summary>
        /// Gets the directory path for a mod from the mod registry.
        /// </summary>
        private string GetContentPackDirectory(IModInfo mod)
        {
            // SMAPI's IModInfo implementation has a DirectoryPath property
            // that isn't on the interface. Access it via reflection.
            try
            {
                var directoryPath = mod.GetType()
                    .GetProperty("DirectoryPath")?
                    .GetValue(mod) as string;

                return directoryPath;
            }
            catch
            {
                Monitor.Log(
                    $"Could not determine directory path for {mod.Manifest.Name}.",
                    LogLevel.Warn);
                return null;
            }
        }
    }
}
