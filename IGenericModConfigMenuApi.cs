// IGenericModConfigMenuApi.cs
using System;
using StardewModdingAPI;

namespace CPFarmRegistrar
{
    /// <summary>
    /// Minimal API interface for Generic Mod Config Menu integration.
    /// </summary>
    public interface IGenericModConfigMenuApi
    {
        void Register(
            IManifest mod,
            Action reset,
            Action save,
            bool titleScreenOnly = false);

        void AddParagraph(
            IManifest mod,
            Func<string> text);

        void AddTextOption(
            IManifest mod,
            Func<string> getValue,
            Action<string> setValue,
            Func<string> name,
            Func<string> tooltip = null,
            string[] allowedValues = null,
            Func<string, string> formatAllowedValue = null,
            string fieldId = null);
    }
}
