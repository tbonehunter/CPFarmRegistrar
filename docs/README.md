<!-- README.md -->

# CP Farm Registrar

A [SMAPI](https://smapi.io/) mod for [Stardew Valley](https://www.stardewvalley.net/) that detects Content Patcher farm mods which silently replace vanilla farm maps and registers them as proper selectable farm types in the character creation screen, giving the player the ability to select either the vanilla or the modded replacement farm without changing configuration.

## The Problems

1: Many Content Patcher farm mods work by unconditionally replacing a vanilla farm map (e.g., the Forest farm) with their custom map. When you install one of these mods, the vanilla farm it replaces becomes unavailable — and there's no indication in the farm selector that anything has changed. You select "Forest farm" and get a completely different map. Usually the mod creator will specify somewhere in their documentation which farm map their CP replaces, but not always. And if you don't know which vanilla farm a CP mod replaces, you have to dig through SMAPI logs or your CP Farm mods' `content.json`files to figure out which farm mod to disable. 

2: When multiple CP farm mods target the same vanilla farm, they conflict with each other and neither loads correctly. 

## The Solution

CP Farm Registrar scans all installed Content Patcher content packs at startup and identifies any that replace vanilla farm maps via `Load` actions. For each one found, it:

1. **Registers the mod as its own selectable farm type** in the character creation farm selector, using the mod's name and description from its manifest.
2. **Suppresses CP patches from non-selected farm mods** using Harmony, so only the chosen farm's map replacement is applied. All other CP farm mods targeting the same vanilla map are silently filtered out.
3. **Restores the vanilla farm map** when no CP farm is selected for that map type, so the original farm remains fully playable.
4. **Places vanilla starting furniture** when a CP farm is selected, matching the furniture layout of the vanilla farm type being replaced. This is skipped if the CP farm mod provides its own farmhouse customization.

Multiple CP farm mods can target the same vanilla farm type and each will appear as an independent, selectable option.

Mods that already register themselves properly in `Data/AdditionalFarms` are detected and skipped to avoid duplicate entries.

## Requirements

- [SMAPI](https://smapi.io/) 4.1.3 or later
- [Content Patcher](https://www.nexusmods.com/stardewvalley/mods/1915) 2.0.0 or later

## Installation

1. Install SMAPI and Content Patcher if you haven't already.
2. Download CP Farm Registrar and unzip it into your `Stardew Valley/Mods` folder.
3. Launch the game. Any detected CP farm mods will appear in the farm selector with their proper names.

## Compatibility

- Works with the vanilla farm selector.
- Works alongside [Custom Farm Loader](https://www.nexusmods.com/stardewvalley/mods/13804) (CFL). CFL is not required — CP Farm Registrar operates independently. If both are loaded, CPFR will defer to CFL's Farm Selector menu but will insist that selection of the vanilla farm loads the vanilla farm rather than the CP Farm mod trying to replace it. 
- Detects CP farm mods that target any of the eight vanilla farm maps: Standard, Riverland, Forest, Hill-top, Wilderness, Four Corners, Beach, and Meadowlands.
- Resolves simple dynamic tokens in CP content packs (e.g., config-driven targets like `Maps/{{file}}`) using `ConfigSchema` defaults and `DynamicTokens`. This catches mods that use a configurable farm type.
- Skips CP mods that have unresolvable tokenized targets containing complex or nested tokens.
- Skips CP mods that already self-register in `Data/AdditionalFarms`.

## How It Works

On game launch, the mod:

1. Scans all installed CP content packs for `Load` actions targeting vanilla farm map assets. Resolves simple dynamic tokens by reading `ConfigSchema` defaults and `DynamicTokens` from the mod's `content.json`.
2. Checks whether each mod already self-registers in `Data/AdditionalFarms` — if so, skips it.
3. Checks whether each mod also edits `Maps/FarmHouse` — this determines whether we inject vanilla furniture on new game creation.
4. For each detected silent replacement, injects a `ModFarmType` entry into `Data/AdditionalFarms` with the mod's name, description, and an icon matching the vanilla farm it replaces.
5. Applies Harmony postfixes to Content Patcher's internal `PatchManager.GetCurrentLoaders` and `PatchManager.GetCurrentEditors` methods. These postfixes filter CP's patch lists at the source: only the selected CP farm's Load and Edit patches are allowed through. All others are suppressed before they reach SMAPI's asset pipeline, preventing conflicts between competing farm mods.
6. Applies a Harmony prefix/postfix to `Game1.loadForNewGame`. When a registered CP farm is selected and the mod doesn't provide its own farmhouse edits, the prefix temporarily sets `Game1.whichFarm` to the vanilla farm integer so the game's built-in furniture and starter item placement runs correctly. The postfix restores it afterward.

## Source

Source code is available on [GitHub](https://github.com/tbonehunter/CPFarmRegistrar).

## License

MIT

## Credits

- **tbonehunter** — Author
- Thanks to [DeLiXxN](https://www.nexusmods.com/stardewvalley/users/39574502) for [Custom Farm Loader](https://www.nexusmods.com/stardewvalley/mods/13804), whose approach to farm type registration via `Data/AdditionalFarms` helped clarify how the game's farm selection system works.
