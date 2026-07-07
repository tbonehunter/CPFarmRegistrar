// CharacterCustomizationHighlightFix.cs
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley.Menus;

namespace CPFarmRegistrar
{
    /// <summary>
    /// Harmony transpiler patch that corrects a vanilla bug in
    /// CharacterCustomization.draw (Stardew Valley 1.6.15).
    ///
    /// The farm selection menu paginates when the total farm count exceeds 12
    /// (a 2 x 6 grid). Pagination arithmetic elsewhere in the vanilla menu
    /// correctly uses "_currentFarmPage * 12" — see the _farmPages calculation
    /// at line 1186 and RefreshFarmTypeButtons at line 1194 of the decompiled
    /// source. However, the selection-highlight code inside the draw method
    /// contains a copy-paste error at line 2963:
    ///
    ///     int index = i + _currentFarmPage * 6;   // should be * 12
    ///
    /// The consequence: on any page past the first, the highlight overlay looks
    /// up the wrong entry in farmTypeButtonNames and is drawn on the wrong slot.
    /// The click handler (lines 2071-2082) uses the button's own name property
    /// and is not affected by this bug, so the correct farm is still loaded on
    /// click — only the visual highlight is misplaced. The bug is only
    /// reachable when a mod pushes the farm count past 12 entries, which is why
    /// CPFarmRegistrar users encounter it and vanilla players do not.
    ///
    /// This transpiler locates the "ldfld _currentFarmPage; ldc.i4.6; mul"
    /// sequence in the draw method's IL and rewrites the constant from 6 to 12,
    /// leaving every other instruction untouched. If the pattern is not found —
    /// for example because ConcernedApe fixes this bug in a future release, or
    /// another mod has already transpiled the same arithmetic — the method IL
    /// is returned unchanged and a warning is logged. The transpiler never
    /// throws and never applies partial changes.
    /// </summary>
    public static class CharacterCustomizationHighlightFix
    {
        private static IMonitor Monitor;

        /// <summary>
        /// Applies the transpiler patch. Intended to be called once during mod
        /// initialization. Creates its own Harmony instance under the supplied
        /// mod id so that this fix can be enabled or unpatched independently
        /// of other CPFarmRegistrar patches.
        /// </summary>
        /// <param name="harmonyId">
        /// A unique Harmony id — normally the mod's manifest UniqueID with a
        /// suffix, e.g. "tbonehunter.CPFarmRegistrar.HighlightFix".
        /// </param>
        /// <param name="monitor">SMAPI monitor for diagnostic logging.</param>
        public static void Apply(string harmonyId, IMonitor monitor)
        {
            Monitor = monitor;

            MethodInfo drawMethod = AccessTools.Method(
                typeof(CharacterCustomization),
                nameof(CharacterCustomization.draw),
                new[] { typeof(SpriteBatch) });

            if (drawMethod == null)
            {
                Monitor.Log(
                    "Could not find CharacterCustomization.draw(SpriteBatch). " +
                    "Farm selection highlight fix not applied.",
                    LogLevel.Error);
                return;
            }

            try
            {
                var harmony = new Harmony(harmonyId);

                harmony.Patch(
                    original: drawMethod,
                    transpiler: new HarmonyMethod(
                        typeof(CharacterCustomizationHighlightFix),
                        nameof(Transpiler)));

                Monitor.Log(
                    "Harmony transpiler applied to CharacterCustomization.draw " +
                    "(farm-selection highlight fix).",
                    LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log(
                    $"Failed to apply CharacterCustomization.draw transpiler: " +
                    $"{ex.Message}",
                    LogLevel.Error);
            }
        }

        /// <summary>
        /// Locates the "_currentFarmPage * 6" arithmetic in the draw method's
        /// IL and rewrites the constant to 12. The match window is
        /// [ldfld _currentFarmPage; ldc.i4.6; mul]; on match, the ldc.i4.6
        /// instruction is mutated in place (preserving any attached labels and
        /// exception-handling block metadata) into ldc.i4.s with operand 12.
        /// </summary>
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);

            FieldInfo currentFarmPageField = AccessTools.Field(
                typeof(CharacterCustomization), "_currentFarmPage");

            if (currentFarmPageField == null)
            {
                Monitor?.Log(
                    "CharacterCustomization._currentFarmPage field not found. " +
                    "Highlight fix transpiler making no changes.",
                    LogLevel.Warn);
                return codes;
            }

            bool patched = false;

            for (int i = 0; i < codes.Count - 2; i++)
            {
                bool matches =
                    codes[i].opcode == OpCodes.Ldfld
                    && codes[i].operand is FieldInfo field
                    && field == currentFarmPageField
                    && codes[i + 1].opcode == OpCodes.Ldc_I4_6
                    && codes[i + 2].opcode == OpCodes.Mul;

                if (matches)
                {
                    // Mutate in place so any labels or block metadata attached
                    // to this instruction remain attached to the replacement.
                    codes[i + 1].opcode = OpCodes.Ldc_I4_S;
                    codes[i + 1].operand = (sbyte)12;
                    patched = true;
                    break;
                }
            }

            if (patched)
            {
                Monitor?.Log(
                    "Rewrote '_currentFarmPage * 6' to '_currentFarmPage * 12' " +
                    "in CharacterCustomization.draw.",
                    LogLevel.Trace);
            }
            else
            {
                Monitor?.Log(
                    "Highlight fix transpiler did not find the expected IL " +
                    "pattern in CharacterCustomization.draw. The vanilla bug " +
                    "may have been fixed by ConcernedApe, or another mod may " +
                    "have already patched the same arithmetic. No changes made.",
                    LogLevel.Warn);
            }

            return codes;
        }
    }
}
