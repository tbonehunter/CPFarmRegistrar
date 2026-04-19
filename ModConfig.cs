// ModConfig.cs

namespace CPFarmRegistrar
{
    /// <summary>
    /// Configuration for CP Farm Registrar, managed via GMCM.
    /// </summary>
    public class ModConfig
    {
        /// <summary>
        /// The unique mod ID of the CP farm to pre-select for loading
        /// pre-CPFR saves. Set to "None" when no pre-selection is active.
        /// Cleared automatically after the save is converted.
        /// </summary>
        public string PreSelectedFarmModId { get; set; } = "None";
    }
}
