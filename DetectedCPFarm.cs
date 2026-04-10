// DetectedCPFarm.cs

namespace CPFarmRegistrar
{
    /// <summary>
    /// Represents a Content Patcher mod that was detected replacing a vanilla farm map.
    /// </summary>
    public class DetectedCPFarm
    {
        /// <summary>The unique mod ID from the CP content pack's manifest.</summary>
        public string UniqueModId { get; set; }

        /// <summary>The display name from the CP content pack's manifest.</summary>
        public string ModName { get; set; }

        /// <summary>The author from the CP content pack's manifest.</summary>
        public string Author { get; set; }

        /// <summary>The description from the CP content pack's manifest.</summary>
        public string Description { get; set; }

        /// <summary>
        /// The vanilla farm map asset this CP mod replaces.
        /// E.g., "Maps/Farm_Foraging".
        /// </summary>
        public string TargetMapAsset { get; set; }

        /// <summary>
        /// The display name of the vanilla farm being replaced.
        /// E.g., "Forest".
        /// </summary>
        public string ReplacedFarmName { get; set; }

        /// <summary>
        /// The ID we register this farm under in Data/AdditionalFarms.
        /// Format: "CPFarmRegistrar/{UniqueModId}"
        /// </summary>
        public string RegisteredFarmId => $"CPFarmRegistrar/{UniqueModId}";

        /// <summary>
        /// The absolute path to the CP content pack's directory.
        /// Used for locating icon/preview assets if available.
        /// </summary>
        public string ContentPackDirectory { get; set; }

        /// <summary>
        /// Whether this CP mod also edits Maps/FarmHouse. If true, we skip
        /// the vanilla furniture injection to avoid conflicting with the
        /// mod's own farmhouse customization.
        /// </summary>
        public bool EditsFarmHouse { get; set; }

        public override string ToString()
        {
            return $"{ModName} by {Author} (replaces {ReplacedFarmName} farm via {TargetMapAsset})";
        }
    }
}
