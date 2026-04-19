// FarmRegistrar.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData;

namespace CPFarmRegistrar
{
    /// <summary>
    /// Registers detected CP farm mods as selectable farm types via Data/AdditionalFarms,
    /// uses Harmony to filter CP's Load and Edit patches so only the selected CP farm's
    /// patches are applied, and spoofs Game1.whichFarm during loadForNewGame so vanilla
    /// furniture and starter items are placed correctly.
    /// </summary>
    public class FarmRegistrar
    {
        private readonly IMonitor Monitor;
        private readonly IModHelper Helper;
        private readonly List<DetectedCPFarm> DetectedFarms;

        /// <summary>
        /// Tracks which vanilla map assets are claimed by detected CP farms.
        /// Key: normalized map asset name (e.g., "Maps/Farm_Foraging")
        /// Value: list of DetectedCPFarms targeting that map
        /// </summary>
        private readonly Dictionary<string, List<DetectedCPFarm>> ClaimedAssets = new();

        /// <summary>
        /// Static reference used by the Harmony postfixes to access instance data.
        /// </summary>
        private static FarmRegistrar Instance;

        /// <summary>
        /// When non-null, we are inside a loadForNewGame spoof and this is the
        /// farm that was selected before the spoof. The patch filter uses this
        /// instead of querying Game1.whichFarm / GetFarmTypeID() during the spoof.
        /// </summary>
        private static DetectedCPFarm SpoofedSelectedFarm = null;

        /// <summary>
        /// When non-null, a farm has been pre-selected via cpfr_select for
        /// loading a pre-CPFR save. The patch filter allows this farm's
        /// patches through even when Game1.whichFarm is a vanilla value.
        /// </summary>
        public static DetectedCPFarm PreSelectedFarm = null;

        public FarmRegistrar(
            IMonitor monitor,
            IModHelper helper,
            List<DetectedCPFarm> detectedFarms)
        {
            Monitor = monitor;
            Helper = helper;
            DetectedFarms = detectedFarms;
            Instance = this;

            foreach (var farm in detectedFarms)
            {
                if (!ClaimedAssets.ContainsKey(farm.TargetMapAsset))
                    ClaimedAssets[farm.TargetMapAsset] = new List<DetectedCPFarm>();

                ClaimedAssets[farm.TargetMapAsset].Add(farm);
            }

            foreach (var kvp in ClaimedAssets)
            {
                if (kvp.Value.Count > 1)
                {
                    string modNames = string.Join(", ",
                        kvp.Value.Select(f => $"'{f.ModName}'"));
                    Monitor.Log(
                        $"Multiple CP farm mods target {kvp.Key}: {modNames}. " +
                        $"Each will be selectable independently.",
                        LogLevel.Info);
                }
            }
        }

        /// <summary>
        /// Hooks into SMAPI's AssetRequested event for farm registration
        /// and applies Harmony patches.
        /// </summary>
        public void Initialize(string modUniqueId)
        {
            Helper.Events.Content.AssetRequested += OnAssetRequested;

            ApplyHarmonyPatches(modUniqueId);
        }

        /// <summary>
        /// Applies Harmony patches to:
        /// 1. CP's PatchManager.GetCurrentLoaders — filter Load patches
        /// 2. CP's PatchManager.GetCurrentEditors — filter Edit patches
        /// 3. Game1.loadForNewGame — spoof whichFarm for vanilla furniture
        /// </summary>
        private void ApplyHarmonyPatches(string modUniqueId)
        {
            var harmony = new Harmony(modUniqueId);

            // --- CP PatchManager patches ---

            Type patchManagerType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                patchManagerType = assembly.GetType(
                    "ContentPatcher.Framework.PatchManager");
                if (patchManagerType != null)
                    break;
            }

            if (patchManagerType == null)
            {
                Monitor.Log(
                    "Could not find ContentPatcher.Framework.PatchManager. " +
                    "Harmony patches not applied — CP farm filtering will not work.",
                    LogLevel.Error);
                return;
            }

            MethodInfo getLoaders = patchManagerType.GetMethod(
                "GetCurrentLoaders",
                BindingFlags.Public | BindingFlags.Instance);

            if (getLoaders != null)
            {
                harmony.Patch(
                    original: getLoaders,
                    postfix: new HarmonyMethod(
                        typeof(FarmRegistrar),
                        nameof(GetCurrentLoaders_Postfix)));

                Monitor.Log(
                    "Harmony postfix applied to PatchManager.GetCurrentLoaders.",
                    LogLevel.Trace);
            }
            else
            {
                Monitor.Log(
                    "Could not find PatchManager.GetCurrentLoaders method.",
                    LogLevel.Error);
            }

            MethodInfo getEditors = patchManagerType.GetMethod(
                "GetCurrentEditors",
                BindingFlags.Public | BindingFlags.Instance);

            if (getEditors != null)
            {
                harmony.Patch(
                    original: getEditors,
                    postfix: new HarmonyMethod(
                        typeof(FarmRegistrar),
                        nameof(GetCurrentEditors_Postfix)));

                Monitor.Log(
                    "Harmony postfix applied to PatchManager.GetCurrentEditors.",
                    LogLevel.Trace);
            }
            else
            {
                Monitor.Log(
                    "Could not find PatchManager.GetCurrentEditors method.",
                    LogLevel.Error);
            }

            // --- Game1.loadForNewGame patches ---

            MethodInfo loadForNewGame = typeof(Game1).GetMethod(
                "loadForNewGame",
                BindingFlags.Public | BindingFlags.Instance);

            if (loadForNewGame != null)
            {
                harmony.Patch(
                    original: loadForNewGame,
                    prefix: new HarmonyMethod(
                        typeof(FarmRegistrar),
                        nameof(LoadForNewGame_Prefix)),
                    postfix: new HarmonyMethod(
                        typeof(FarmRegistrar),
                        nameof(LoadForNewGame_Postfix)));

                Monitor.Log(
                    "Harmony prefix/postfix applied to Game1.loadForNewGame.",
                    LogLevel.Trace);
            }
            else
            {
                Monitor.Log(
                    "Could not find Game1.loadForNewGame method. " +
                    "Starting furniture may not be placed for CP farms.",
                    LogLevel.Warn);
            }
        }

        // =====================================================================
        // Harmony: CP Patch Filtering
        // =====================================================================

        private static void GetCurrentLoaders_Postfix(ref object __result)
        {
            if (Instance == null || __result == null)
                return;

            try
            {
                Instance.FilterPatches(ref __result, "Load");
            }
            catch (Exception ex)
            {
                Instance.Monitor.LogOnce(
                    $"Error in GetCurrentLoaders postfix: {ex.Message}",
                    LogLevel.Error);
            }
        }

        private static void GetCurrentEditors_Postfix(ref object __result)
        {
            if (Instance == null || __result == null)
                return;

            try
            {
                Instance.FilterPatches(ref __result, "Edit");
            }
            catch (Exception ex)
            {
                Instance.Monitor.LogOnce(
                    $"Error in GetCurrentEditors postfix: {ex.Message}",
                    LogLevel.Error);
            }
        }

        private void FilterPatches(ref object result, string patchKind)
        {
            var enumerable = result as IEnumerable;
            if (enumerable == null)
                return;

            var allPatches = new List<object>();
            Type patchType = null;

            foreach (object patch in enumerable)
            {
                allPatches.Add(patch);
                if (patchType == null)
                    patchType = patch.GetType();
            }

            if (allPatches.Count == 0)
                return;

            bool needsFiltering = false;
            foreach (object patch in allPatches)
            {
                IAssetName targetAsset = GetTargetAsset(patch);
                if (targetAsset == null)
                    continue;

                string normalized = VanillaFarmMap.Normalize(
                    targetAsset.BaseName);

                if (ClaimedAssets.ContainsKey(normalized))
                {
                    needsFiltering = true;
                    break;
                }
            }

            if (!needsFiltering)
                return;

            var filtered = new List<object>();
            foreach (object patch in allPatches)
            {
                if (ShouldAllowPatch(patch, patchKind))
                    filtered.Add(patch);
            }

            if (filtered.Count == allPatches.Count)
                return;

            Type elementType = FindEnumerableElementType(result.GetType())
                ?? patchType;

            if (elementType != null)
            {
                Type listType = typeof(List<>).MakeGenericType(elementType);
                IList typedList = (IList)Activator.CreateInstance(listType);

                foreach (object item in filtered)
                    typedList.Add(item);

                result = typedList;
            }
        }

        private Type FindEnumerableElementType(Type type)
        {
            foreach (Type iface in type.GetInterfaces())
            {
                if (iface.IsGenericType &&
                    iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                {
                    return iface.GetGenericArguments()[0];
                }
            }
            return null;
        }

        private bool ShouldAllowPatch(object patch, string patchKind)
        {
            IAssetName targetAsset = GetTargetAsset(patch);
            IContentPack contentPack = GetContentPack(patch);

            if (targetAsset == null || contentPack == null)
                return true;

            string normalized = VanillaFarmMap.Normalize(
                targetAsset.BaseName);

            if (!ClaimedAssets.ContainsKey(normalized))
                return true;

            string patchModId = contentPack.Manifest.UniqueID;
            var claimingFarms = ClaimedAssets[normalized];

            var matchingFarm = claimingFarms.FirstOrDefault(
                f => f.UniqueModId.Equals(patchModId,
                    StringComparison.OrdinalIgnoreCase));

            if (matchingFarm == null)
                return true;

            if (IsFarmSelected(matchingFarm))
            {
                Monitor.LogOnce(
                    $"Allowing {patchKind} patch from '{matchingFarm.ModName}' " +
                    $"for {normalized} (selected farm).",
                    LogLevel.Trace);
                return true;
            }
            else
            {
                Monitor.LogOnce(
                    $"Suppressing {patchKind} patch from '{matchingFarm.ModName}' " +
                    $"for {normalized} (not selected).",
                    LogLevel.Trace);
                return false;
            }
        }

        // =====================================================================
        // Harmony: loadForNewGame whichFarm Spoofing
        // =====================================================================

        /// <summary>
        /// Harmony prefix for Game1.loadForNewGame.
        /// If a CPFR farm is selected and the mod doesn't edit FarmHouse,
        /// store the selected farm reference for the filter to use during
        /// the spoof, then set Game1.whichFarm to the vanilla integer.
        /// </summary>
        private static void LoadForNewGame_Prefix()
        {
            if (Instance == null)
                return;

            try
            {
                SpoofedSelectedFarm = null;

                if (Game1.whichFarm != 7)
                    return;

                string currentFarmId = Game1.GetFarmTypeID();
                var selectedFarm = Instance.DetectedFarms.FirstOrDefault(
                    f => f.RegisteredFarmId == currentFarmId);

                if (selectedFarm == null)
                    return;

                if (selectedFarm.EditsFarmHouse)
                {
                    Instance.Monitor.Log(
                        $"'{selectedFarm.ModName}' edits FarmHouse — " +
                        $"skipping vanilla furniture injection.",
                        LogLevel.Trace);
                    return;
                }

                if (!VanillaFarmMap.AssetToWhichFarm.TryGetValue(
                    selectedFarm.TargetMapAsset, out int vanillaWhichFarm))
                    return;

                // Store the selected farm BEFORE spoofing so the patch
                // filter knows which farm is active during the spoof.
                SpoofedSelectedFarm = selectedFarm;

                Game1.whichFarm = vanillaWhichFarm;

                Instance.Monitor.Log(
                    $"Spoofing whichFarm to {vanillaWhichFarm} " +
                    $"({selectedFarm.ReplacedFarmName}) for " +
                    $"'{selectedFarm.ModName}' during loadForNewGame.",
                    LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Instance.Monitor.LogOnce(
                    $"Error in loadForNewGame prefix: {ex.Message}",
                    LogLevel.Error);
            }
        }

        /// <summary>
        /// Harmony postfix for Game1.loadForNewGame.
        /// Restores Game1.whichFarm to 7 and clears the spoof reference.
        /// </summary>
        private static void LoadForNewGame_Postfix()
        {
            if (Instance == null || SpoofedSelectedFarm == null)
                return;

            try
            {
                Game1.whichFarm = 7;

                Instance.Monitor.Log(
                    $"Restored whichFarm to 7 after loadForNewGame.",
                    LogLevel.Trace);

                SpoofedSelectedFarm = null;
            }
            catch (Exception ex)
            {
                Instance.Monitor.LogOnce(
                    $"Error in loadForNewGame postfix: {ex.Message}",
                    LogLevel.Error);
            }
        }

        // =====================================================================
        // Farm Selection Check
        // =====================================================================

        /// <summary>
        /// Checks whether the given farm is the currently selected farm.
        /// Handles both normal state and the loadForNewGame spoof state.
        /// During a spoof, Game1.whichFarm is temporarily a vanilla value
        /// and GetFarmTypeID() won't return our ID, so we check against
        /// the stored SpoofedSelectedFarm reference instead.
        /// </summary>
        private static bool IsFarmSelected(DetectedCPFarm farm)
        {
            // During a loadForNewGame spoof, use the stored reference
            if (SpoofedSelectedFarm != null)
            {
                return farm.UniqueModId.Equals(
                    SpoofedSelectedFarm.UniqueModId,
                    StringComparison.OrdinalIgnoreCase);
            }

            // Normal state: check Game1 directly
            if (Game1.whichFarm == 7)
            {
                string currentFarmId = Game1.GetFarmTypeID();
                return currentFarmId == farm.RegisteredFarmId;
            }

            // Pre-selected farm for loading a pre-CPFR save
            if (PreSelectedFarm != null)
            {
                return farm.UniqueModId.Equals(
                    PreSelectedFarm.UniqueModId,
                    StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        // =====================================================================
        // Reflection Helpers
        // =====================================================================

        private IAssetName GetTargetAsset(object patch)
        {
            try
            {
                return patch.GetType()
                    .GetProperty("TargetAsset")?
                    .GetValue(patch) as IAssetName;
            }
            catch
            {
                return null;
            }
        }

        private IContentPack GetContentPack(object patch)
        {
            try
            {
                return patch.GetType()
                    .GetProperty("ContentPack")?
                    .GetValue(patch) as IContentPack;
            }
            catch
            {
                return null;
            }
        }

        // =====================================================================
        // SMAPI AssetRequested: Farm Registration
        // =====================================================================

        private void OnAssetRequested(object sender, AssetRequestedEventArgs e)
        {
            if (e.NameWithoutLocale.IsEquivalentTo("Data/AdditionalFarms"))
            {
                e.Edit(edit =>
                {
                    var data = edit.GetData<List<ModFarmType>>();

                    foreach (var farm in DetectedFarms)
                    {
                        if (data.Any(f => f.Id == farm.RegisteredFarmId))
                            continue;

                        var farmType = new ModFarmType
                        {
                            Id = farm.RegisteredFarmId,
                            MapName = farm.TargetMapAsset.Replace("Maps/", ""),
                            TooltipStringPath =
                                $"Strings/UI:CPFarmRegistrar_Desc/{farm.UniqueModId}",
                            IconTexture =
                                $"CPFarmRegistrar_Icon/{farm.UniqueModId}",
                            WorldMapTexture = null,
                            ModData = new Dictionary<string, string>
                            {
                                { "CPFarmRegistrar.TargetMap", farm.TargetMapAsset },
                                { "CPFarmRegistrar.OriginalModId", farm.UniqueModId }
                            }
                        };

                        data.Add(farmType);

                        Monitor.Log(
                            $"Registered '{farm.ModName}' as selectable farm type " +
                            $"(ID: {farm.RegisteredFarmId}).",
                            LogLevel.Info);
                    }
                }, AssetEditPriority.Default);

                return;
            }

            if (e.NameWithoutLocale.BaseName.Contains("Strings/UI"))
            {
                e.Edit(edit =>
                {
                    var data = edit.AsDictionary<string, string>().Data;

                    foreach (var farm in DetectedFarms)
                    {
                        string key =
                            $"CPFarmRegistrar_Desc/{farm.UniqueModId}";
                        if (!data.ContainsKey(key))
                        {
                            string desc = !string.IsNullOrEmpty(farm.Description)
                                ? farm.Description
                                : $"A custom {farm.ReplacedFarmName} farm replacement.";
                            string description =
                                $"{farm.ModName}_{desc}";
                            data[key] = description;
                        }
                    }
                }, AssetEditPriority.Default);

                return;
            }

            if (e.NameWithoutLocale.BaseName.StartsWith("CPFarmRegistrar_Icon/"))
            {
                string modId = e.NameWithoutLocale.BaseName
                    .Split("CPFarmRegistrar_Icon/")[1];

                var farm = DetectedFarms.FirstOrDefault(
                    f => f.UniqueModId == modId);
                if (farm == null)
                    return;

                e.LoadFrom(
                    () => GetFallbackIcon(farm),
                    AssetLoadPriority.Low);

                return;
            }
        }

        // =====================================================================
        // Icon Helpers
        // =====================================================================

        private Texture2D GetFallbackIcon(DetectedCPFarm farm)
        {
            if (!VanillaFarmMap.AssetToWhichFarm.TryGetValue(
                farm.TargetMapAsset, out int whichFarm))
            {
                return GetPlaceholderIcon();
            }

            if (Game1.mouseCursors == null)
                Game1.mouseCursors = Game1.content.Load<Texture2D>(
                    "LooseSprites\\Cursors");

            Rectangle sourceRect = whichFarm switch
            {
                0 => new Rectangle(2, 324, 18, 20),
                1 => new Rectangle(24, 324, 19, 20),
                2 => new Rectangle(46, 324, 18, 20),
                3 => new Rectangle(68, 324, 18, 20),
                4 => new Rectangle(90, 324, 18, 20),
                5 => new Rectangle(2, 345, 18, 20),
                6 => new Rectangle(24, 345, 18, 20),
                _ => new Rectangle(2, 324, 18, 20)
            };

            return CreateSubTexture(Game1.mouseCursors, sourceRect);
        }

        private Texture2D CreateSubTexture(
            Texture2D source, Rectangle sourceRect)
        {
            Color[] data = new Color[sourceRect.Width * sourceRect.Height];
            source.GetData(0, sourceRect, data, 0, data.Length);

            Texture2D result = new Texture2D(
                Game1.graphics.GraphicsDevice,
                sourceRect.Width,
                sourceRect.Height);
            result.SetData(data);

            return result;
        }

        private Texture2D GetPlaceholderIcon()
        {
            Texture2D placeholder = new Texture2D(
                Game1.graphics.GraphicsDevice, 18, 20);
            Color[] data = new Color[18 * 20];
            Array.Fill(data, Color.Gray);
            placeholder.SetData(data);
            return placeholder;
        }
    }
}
