# Platform Autofill

A Timberborn mod that adds an **Autofill** toggle to every drag-placeable tool (platforms, paths, overhangs, and similar blocks). When active, dragging across a height change automatically inserts support columns underneath to keep the top surface level with your drag start point.

See [CHANGELOG.md](CHANGELOG.md) for version history and the `1.0.0` release summary.

## How it works

Whenever you use a block tool that supports drag-placement (platforms, paths, ramps, etc.), a **Platform Autofill** row appears in the tool panel with two buttons:

- **ON / OFF:**
  - **OFF (default):** the tool behaves exactly as vanilla — placement is blocked if the terrain is lower than expected.
  - **ON:** the mod intercepts each placed block and calculates the vertical gap between the terrain (or the existing object below) and the bottom of your block. It then fills that gap automatically with support columns (Platform, DoublePlatform, or TriplePlatform) matching your current faction, stacking from largest to smallest to minimize piece count.
- **Max 3 / Max 2 / Max 1:** the largest support piece the mod may use. Click to cycle. `Max 1` builds columns only from single platforms.

Both settings are remembered between games.

Support previews are shown in real time while you drag, so you can see exactly what will be placed before you commit. A column is only planned when it can reach all the way down; otherwise the spot is left to the game's normal rules (shown in red).

## Supported block types

The autofill toggle appears for any tool whose layout is draggable (line or area) or that uses the path system. This includes:

- Platforms and platform variants
- Paths
- Overhangs
- Any mod-added draggable block that uses the same faction naming convention (`BlockName.FactionId`)

## Support column selection

The mod tries the **largest** available support size first (TriplePlatform → DoublePlatform → Platform) and picks the one that fills the gap exactly, working down from the top. This minimises the number of blocks placed for tall drops.

## Requirements

- **Timberborn** 1.0.13.1 or later
- **Harmony** mod (listed as a required dependency)

## Installation

Place the `Platform Autofill` folder (containing `manifest.json` and `PlatformAutofill.dll`) inside your Timberborn mods directory, then enable the mod in the in-game mod manager.

## Known limitations

- The autofill only triggers for drag-placeable tools. Single-footprint buildings are not supported.
- Autofill never places anything into a cell that is already occupied, and never lets a dragged block overlap an existing object.
- The bottom support of each column must be valid on its own (on terrain or on an object that can carry it); if not, no column is built for that spot.
- Supports are placed as regular construction sites; they are not removed automatically if you delete the block above them.

See [TODO.md](TODO.md) for planned work and feature ideas.
