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
    /// Resolves simple dynamic tokens to catch config-driven farm mods.
    /// Handles comma-separated CP targets (e.g., "Maps/Farm,Maps").
    /// Also detects whether each mod edits Maps/FarmHouse.
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
                    "Pathoschild.ContentPatcher",
                    StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip our own mod just in case
                if (mod.Manifest.UniqueID.Equals(
                    "tbonehunter.CPFarmRegistrar",
                    StringComparison.OrdinalIgnoreCase))
                    continue;

                var scanResult = ScanContentPack(mod);
                if (scanResult.FarmTargets.Count > 0)
                {
                    foreach (var target in scanResult.FarmTargets)
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
                            ContentPackDirectory = GetContentPackDirectory(mod),
                            EditsFarmHouse = scanResult.EditsFarmHouse
                        };

                        detected.Add(entry);
                        Monitor.Log(
                            $"Detected CP farm mod: {entry}" +
                            (entry.EditsFarmHouse
                                ? " (also edits FarmHouse)"
                                : ""),
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
        /// Result of scanning a single CP content pack.
        /// </summary>
        private class ScanResult
        {
            public List<string> FarmTargets { get; set; } = new();
            public bool EditsFarmHouse { get; set; }
        }

        /// <summary>
        /// Reads a CP content pack's content.json and looks for Load actions
        /// targeting vanilla farm maps. Skips mods that already register
        /// themselves in Data/AdditionalFarms. Resolves simple dynamic tokens.
        /// Splits comma-separated targets to handle multi-target patches.
        /// Also checks whether the mod edits Maps/FarmHouse.
        /// </summary>
        private ScanResult ScanContentPack(IModInfo mod)
        {
            var result = new ScanResult();

            string contentPackDir = GetContentPackDirectory(mod);
            if (contentPackDir == null)
                return result;

            string contentJsonPath = Path.Combine(contentPackDir, "content.json");
            if (!File.Exists(contentJsonPath))
                return result;

            try
            {
                string json = File.ReadAllText(contentJsonPath);
                JObject root = JObject.Parse(json);

                JArray changes = root["Changes"] as JArray;
                if (changes == null)
                    return result;

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
                        if (editTarget != null)
                        {
                            // Check each part of a comma-separated target
                            foreach (string part in SplitTargets(editTarget))
                            {
                                if (part.Equals(
                                    "Data/AdditionalFarms",
                                    StringComparison.OrdinalIgnoreCase))
                                {
                                    Monitor.Log(
                                        $"  {mod.Manifest.Name} self-registers in " +
                                        $"Data/AdditionalFarms. Skipping.",
                                        LogLevel.Trace);
                                    return result;
                                }
                            }
                        }
                    }
                }

                // Build a token resolution table from ConfigSchema defaults
                // and DynamicTokens.
                var tokenDefaults = ResolveTokenDefaults(root);

                // Second pass: scan for Load actions targeting vanilla farm maps,
                // and check for any edits to Maps/FarmHouse.
                foreach (JToken change in changes)
                {
                    string action = change["Action"]?.ToString();
                    if (action == null)
                        continue;

                    string target = change["Target"]?.ToString();
                    if (target == null)
                        continue;

                    // Resolve tokens in the target
                    string resolved = ResolveTokens(target, tokenDefaults);

                    // Split comma-separated targets and process each
                    foreach (string rawPart in SplitTargets(resolved))
                    {
                        string part = rawPart;

                        // Check for FarmHouse edits (Load or EditMap)
                        if (action.Equals("Load", StringComparison.OrdinalIgnoreCase) ||
                            action.Equals("EditMap", StringComparison.OrdinalIgnoreCase))
                        {
                            string normalizedPart =
                                VanillaFarmMap.Normalize(part);

                            if (normalizedPart.Equals("Maps/FarmHouse",
                                StringComparison.OrdinalIgnoreCase))
                            {
                                result.EditsFarmHouse = true;
                            }
                        }

                        // Only look for Load actions for farm map detection
                        if (!action.Equals("Load", StringComparison.OrdinalIgnoreCase))
                            continue;

                        // Skip parts with unresolved tokens
                        if (part.Contains("{{"))
                        {
                            Monitor.Log(
                                $"  Skipping unresolvable target '{part}' in " +
                                $"{mod.Manifest.Name} - could not resolve all tokens.",
                                LogLevel.Trace);
                            continue;
                        }

                        string normalized = VanillaFarmMap.Normalize(part);
                        if (VanillaFarmMap.IsVanillaFarmMap(normalized))
                        {
                            JToken when = change["When"];
                            if (when != null)
                            {
                                Monitor.Log(
                                    $"  Found conditional Load targeting " +
                                    $"'{normalized}' in {mod.Manifest.Name} " +
                                    $"(has When block - may be partially scoped).",
                                    LogLevel.Trace);
                            }

                            if (!result.FarmTargets.Contains(normalized))
                                result.FarmTargets.Add(normalized);
                        }
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

            return result;
        }

        /// <summary>
        /// Splits a CP target string on commas and trims each part.
        /// CP supports comma-separated targets like "Maps/Farm,Maps"
        /// which means the asset is loaded to both "Maps/Farm" and "Maps".
        /// Some mods use this as an aliasing mechanism.
        /// </summary>
        private IEnumerable<string> SplitTargets(string target)
        {
            if (!target.Contains(","))
            {
                yield return target.Trim();
                yield break;
            }

            foreach (string part in target.Split(','))
            {
                string trimmed = part.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    yield return trimmed;
            }
        }

        /// <summary>
        /// Builds a dictionary of token name -> default value by reading
        /// ConfigSchema defaults and then resolving DynamicTokens that
        /// depend on those config values.
        /// </summary>
        private Dictionary<string, string> ResolveTokenDefaults(JObject root)
        {
            var defaults = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

            // Step 1: Read ConfigSchema defaults
            JObject configSchema = root["ConfigSchema"] as JObject;
            if (configSchema != null)
            {
                foreach (JProperty prop in configSchema.Properties())
                {
                    string defaultValue = prop.Value?["Default"]?.ToString();
                    if (defaultValue != null)
                    {
                        defaults[prop.Name] = defaultValue;
                    }
                }
            }

            // Step 2: Resolve DynamicTokens using config defaults.
            JArray dynamicTokens = root["DynamicTokens"] as JArray;
            if (dynamicTokens != null)
            {
                foreach (JToken token in dynamicTokens)
                {
                    string name = token["Name"]?.ToString();
                    string value = token["Value"]?.ToString();
                    if (name == null || value == null)
                        continue;

                    JObject when = token["When"] as JObject;
                    if (when == null)
                    {
                        // Unconditional dynamic token — always applies
                        defaults[name] = value;
                        continue;
                    }

                    // Check if all When conditions match against our
                    // resolved defaults.
                    bool allMatch = true;
                    foreach (JProperty condition in when.Properties())
                    {
                        string conditionKey = condition.Name;
                        string conditionValue = condition.Value?.ToString();

                        if (defaults.TryGetValue(conditionKey, out string currentValue))
                        {
                            if (!string.Equals(currentValue, conditionValue,
                                StringComparison.OrdinalIgnoreCase))
                            {
                                allMatch = false;
                                break;
                            }
                        }
                        else
                        {
                            allMatch = false;
                            break;
                        }
                    }

                    if (allMatch)
                    {
                        defaults[name] = value;
                    }
                }
            }

            return defaults;
        }

        /// <summary>
        /// Replaces {{tokenName}} placeholders in a string with resolved values.
        /// </summary>
        private string ResolveTokens(
            string input,
            Dictionary<string, string> tokenDefaults)
        {
            if (!input.Contains("{{"))
                return input;

            string result = input;
            foreach (var kvp in tokenDefaults)
            {
                result = result.Replace(
                    $"{{{{{kvp.Key}}}}}",
                    kvp.Value,
                    StringComparison.OrdinalIgnoreCase);
            }

            return result;
        }

        /// <summary>
        /// Gets the directory path for a mod from the mod registry.
        /// </summary>
        private string GetContentPackDirectory(IModInfo mod)
        {
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
