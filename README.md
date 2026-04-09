<!-- README.md -->

# CP Farm Registrar

A [SMAPI](https://smapi.io/) mod for [Stardew Valley](https://www.stardewvalley.net/) that detects Content Patcher farm mods which silently replace vanilla farm maps and registers them as proper selectable farm types in the character creation screen.

## The Problem

Some Content Patcher farm mods work by unconditionally replacing a vanilla farm map (e.g., the Forest farm) with their custom map. When you install one of these mods, the vanilla farm it replaces becomes unavailable — and there's no indication in the farm selector that anything has changed. Some mod authors document their replacement process, some do not. You might select "Forest farm" and get a completely different map with no explanation. Switching back to the vanilla farm requires going into your Mods folder and removing or disabling the CP mod.

## The Solution

CP Farm Registrar scans all installed Content Patcher content packs at startup and identifies any that replace vanilla farm maps via `Load` actions. For each one found, it:

1. **Registers the mod as its own selectable farm type** in the character creation farm selector, using the mod's name and description from its manifest.
2. **Restores the vanilla farm map** when the CP farm mod is not selected, so the original farm type remains playable.

Mods that already register themselves properly in `Data/AdditionalFarms` (like Immersive Farm 2) are detected and skipped to avoid duplicate entries.

## Requirements

- [SMAPI](https://smapi.io/) 4.1.3 or later
- [Content Patcher](https://www.nexusmods.com/stardewvalley/mods/1915) 2.0.0 or later

## Installation

1. Install SMAPI and Content Patcher if you haven't already.
2. Download CP Farm Registrar and unzip it into your `Stardew Valley/Mods` folder.
3. Launch the game. Any detected CP farm mods will appear in the farm selector with their proper names.

## Compatibility

- Works with the vanilla farm selector.
- Works alongside [Custom Farm Loader](https://www.nexusmods.com/stardewvalley/mods/13804) (CFL). CFL is not required — CP Farm Registrar operates independently. When both are installed, registered farms appear in both the vanilla selector and CFL's custom farm menu.
- Detects CP farm mods that target any of the eight vanilla farm maps: Standard, Riverland, Forest, Hill-top, Wilderness, Four Corners, Beach, and Meadowlands.
- Skips CP mods that use tokenized target paths (e.g., `Maps/{{variable}}`) since these can't be resolved at scan time.
- Skips CP mods that already self-register in `Data/AdditionalFarms`.

## How It Works

On game launch, the mod:

1. Scans all installed CP content packs for `Load` actions targeting vanilla farm map assets.
2. Checks whether each mod already self-registers in `Data/AdditionalFarms` — if so, skips it.
3. For each detected silent replacement, injects a `ModFarmType` entry into `Data/AdditionalFarms` with the mod's name, description, and an icon matching the vanilla farm it replaces.
4. Caches the original vanilla farm maps by loading them from the game's content files through a raw content manager that bypasses SMAPI and Content Patcher.
5. When a farm map asset is requested and the corresponding CP farm is not the selected farm type, edits the asset to replace CP's modded map with the cached vanilla version.

## Source

Source code is available on [GitHub](https://github.com/tbonehunter/CPFarmRegistrar).

## License

MIT

## Credits

- **tbonehunter** — Author
- Thanks to [DeLiXxN](https://www.nexusmods.com/stardewvalley/users/39574502) for [Custom Farm Loader](https://www.nexusmods.com/stardewvalley/mods/13804), whose approach to farm type registration via `Data/AdditionalFarms` helped clarify how the game's farm selection system works.


