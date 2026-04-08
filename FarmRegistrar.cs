// FarmRegistrar.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData;

namespace CPFarmRegistrar
{
    /// <summary>
    /// Registers detected CP farm mods as selectable farm types via Data/AdditionalFarms
    /// and handles restoring vanilla maps when a CP farm is not selected.
    /// </summary>
    public class FarmRegistrar
    {
        private readonly IMonitor Monitor;
        private readonly IModHelper Helper;
        private readonly List<DetectedCPFarm> DetectedFarms;

        /// <summary>
        /// Tracks which vanilla map assets are claimed by detected CP farms.
        /// Key: normalized map asset name (e.g., "Maps/Farm_Foraging")
        /// Value: the DetectedCPFarm that targets it
        /// </summary>
        private readonly Dictionary<string, DetectedCPFarm> ClaimedAssets = new();

        /// <summary>
        /// Cached vanilla map data loaded via a raw content manager that
        /// bypasses SMAPI/CP, giving us untouched vanilla maps from XNB files.
        /// Key: normalized map asset name
        /// Value: the raw vanilla map
        /// </summary>
        private readonly Dictionary<string, xTile.Map> VanillaMapCache = new();

        public FarmRegistrar(
            IMonitor monitor,
            IModHelper helper,
            List<DetectedCPFarm> detectedFarms)
        {
            Monitor = monitor;
            Helper = helper;
            DetectedFarms = detectedFarms;

            foreach (var farm in detectedFarms)
            {
                if (ClaimedAssets.ContainsKey(farm.TargetMapAsset))
                {
                    var existing = ClaimedAssets[farm.TargetMapAsset];
                    Monitor.Log(
                        $"Warning: Both '{existing.ModName}' and '{farm.ModName}' " +
                        $"replace {farm.TargetMapAsset}. Only one can be active at a time.",
                        LogLevel.Warn);
                }
                ClaimedAssets[farm.TargetMapAsset] = farm;
            }
        }

        /// <summary>
        /// Hooks into SMAPI's AssetRequested event.
        /// </summary>
        public void Initialize()
        {
            Helper.Events.Content.AssetRequested += OnAssetRequested;
        }

        /// <summary>
        /// Caches vanilla map data by loading maps through a raw
        /// LocalizedContentManager that bypasses SMAPI and Content Patcher.
        /// This gives us the untouched vanilla XNB maps.
        /// Must be called during GameLaunched.
        /// </summary>
        public void CacheVanillaMaps()
        {
            // Create a raw content manager that talks directly to the game's
            // Content folder, completely bypassing SMAPI's content interception
            // and therefore any CP patches.
            LocalizedContentManager rawContentManager = null;

            try
            {
                rawContentManager = new LocalizedContentManager(
                    Game1.content.ServiceProvider,
                    Game1.content.RootDirectory);

                foreach (var mapAsset in ClaimedAssets.Keys)
                {
                    try
                    {
                        // Load the map directly from the XNB file.
                        // The asset name is something like "Maps/Farm_Foraging"
                        // and the content manager will find and load
                        // Content/Maps/Farm_Foraging.xnb automatically.
                        var map = rawContentManager.Load<xTile.Map>(mapAsset);

                        if (map != null)
                        {
                            VanillaMapCache[mapAsset] = map;
                            Monitor.Log(
                                $"Cached vanilla map: {mapAsset}",
                                LogLevel.Trace);
                        }
                        else
                        {
                            Monitor.Log(
                                $"Loaded null map for {mapAsset}.",
                                LogLevel.Warn);
                        }
                    }
                    catch (Exception ex)
                    {
                        Monitor.Log(
                            $"Failed to cache vanilla map {mapAsset}: {ex.Message}",
                            LogLevel.Warn);
                    }
                }
            }
            catch (Exception ex)
            {
                Monitor.Log(
                    $"Failed to create raw content manager: {ex.Message}",
                    LogLevel.Error);
            }
            finally
            {
                // Dispose the raw content manager but keep the loaded maps.
                // The maps are xTile objects in memory and don't depend on
                // the content manager staying alive.
                // 
                // NOTE: Disposing the content manager may unload the maps
                // from its internal cache, but the xTile.Map objects
                // themselves should remain valid since they're managed
                // references. If this causes issues, we can keep the
                // content manager alive as a field instead.
                //
                // For safety, keep it alive:
                // rawContentManager?.Dispose();
                //
                // We intentionally do NOT dispose here. The raw content manager
                // must stay alive to keep tilesheet texture references valid.
                // It will be collected when the mod is unloaded.
            }
        }

        private void OnAssetRequested(object sender, AssetRequestedEventArgs e)
        {
            // Inject our detected farms into the additional farms list
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

            // Provide tooltip/description strings for our registered farms
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

            // Provide icon textures for our registered farms
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

            // Core logic: restore vanilla maps when our CP farm isn't selected
            HandleVanillaMapRestoration(e);
        }

/// <summary>
        /// If this asset request is for a vanilla farm map that a detected CP farm
        /// is replacing, and that CP farm is NOT the currently selected farm type,
        /// edit the asset after CP loads it to swap in the cached vanilla map.
        /// Using Edit instead of LoadFrom avoids a two-loader conflict with CP.
        /// </summary>
        private void HandleVanillaMapRestoration(AssetRequestedEventArgs e)
        {
            string normalized = VanillaFarmMap.Normalize(
                e.NameWithoutLocale.BaseName);

            if (!ClaimedAssets.TryGetValue(normalized, out var claimingFarm))
                return;

            // If the registered CP farm IS selected, let CP do its thing
            if (IsRegisteredCPFarmSelected(claimingFarm))
            {
                Monitor.LogOnce(
                    $"CP farm '{claimingFarm.ModName}' is selected. " +
                    $"Allowing CP to replace {normalized}.",
                    LogLevel.Trace);
                return;
            }

            // CP farm is NOT selected — restore vanilla map
            if (!VanillaMapCache.TryGetValue(normalized, out var vanillaMap))
            {
                Monitor.LogOnce(
                    $"Cannot restore vanilla map for {normalized}: " +
                    $"no cached copy available.",
                    LogLevel.Warn);
                return;
            }

            Monitor.LogOnce(
                $"Restoring vanilla {claimingFarm.ReplacedFarmName} farm map " +
                $"(CP farm '{claimingFarm.ModName}' is not selected).",
                LogLevel.Trace);

            // Edit at Late priority so CP's Load runs first, then we
            // replace the map content with the cached vanilla version.
            // This avoids competing LoadFrom providers.
            e.Edit(asset =>
            {
                var mapAsset = asset.AsMap();
                mapAsset.ReplaceWith(vanillaMap);
            }, AssetEditPriority.Late);
        }

        /// <summary>
        /// Checks whether the currently selected farm type is our registered
        /// version of the given CP farm.
        /// </summary>
        private bool IsRegisteredCPFarmSelected(DetectedCPFarm farm)
        {
            if (Game1.whichFarm != 7)
                return false;

            string currentFarmId = Game1.GetFarmTypeID();
            return currentFarmId == farm.RegisteredFarmId;
        }

        /// <summary>
        /// Gets a fallback icon using the vanilla farm icon for the farm
        /// type being replaced.
        /// </summary>
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

        /// <summary>
        /// Creates a new texture from a sub-rectangle of a source texture.
        /// </summary>
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

        /// <summary>
        /// Returns a simple placeholder icon texture.
        /// </summary>
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
