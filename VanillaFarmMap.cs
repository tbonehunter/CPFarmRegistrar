// VanillaFarmMap.cs
using System.Collections.Generic;

namespace CPFarmRegistrar
{
    /// <summary>
    /// Maps vanilla farm types to their map asset names and whichFarm integer values.
    /// Used to detect which vanilla farm a CP mod is replacing.
    /// </summary>
    public static class VanillaFarmMap
    {
        /// <summary>
        /// Map asset name (as targeted by CP) -> vanilla farm display name.
        /// </summary>
        public static readonly Dictionary<string, string> AssetToDisplayName = new()
        {
            { "Maps/Farm",              "Standard" },
            { "Maps/Farm_Fishing",      "Riverland" },
            { "Maps/Farm_Foraging",     "Forest" },
            { "Maps/Farm_Mining",       "Hill-top" },
            { "Maps/Farm_Combat",       "Wilderness" },
            { "Maps/Farm_FourCorners",  "Four Corners" },
            { "Maps/Farm_Beach",        "Beach" },
            { "Maps/Farm_Ranching",     "Meadowlands" }
        };

        /// <summary>
        /// Map asset name -> the whichFarm integer the game uses for this farm type.
        /// Used for determining which vanilla icon to fall back to.
        /// </summary>
        public static readonly Dictionary<string, int> AssetToWhichFarm = new()
        {
            { "Maps/Farm",              0 },
            { "Maps/Farm_Fishing",      1 },
            { "Maps/Farm_Foraging",     2 },
            { "Maps/Farm_Mining",       3 },
            { "Maps/Farm_Combat",       4 },
            { "Maps/Farm_FourCorners",  5 },
            { "Maps/Farm_Beach",        6 },
            { "Maps/Farm_Ranching",     7 }
        };

        /// <summary>
        /// Checks if a given asset name (case-insensitive) is a vanilla farm map.
        /// Normalizes path separators before matching.
        /// </summary>
        public static bool IsVanillaFarmMap(string assetName)
        {
            string normalized = assetName.Replace("\\", "/").Trim();
            return AssetToDisplayName.ContainsKey(normalized);
        }

        /// <summary>
        /// Normalizes a CP target path to the canonical format used in our dictionaries.
        /// </summary>
        public static string Normalize(string assetName)
        {
            return assetName.Replace("\\", "/").Trim();
        }
    }
}
