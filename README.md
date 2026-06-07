# Platform Autofill

A Timberborn mod that adds an **Autofill** toggle to every drag-placeable tool (platforms, paths, overhangs, and similar blocks). When active, dragging across a height change automatically inserts support columns underneath to keep the top surface level with your drag start point.

## How it works

Whenever you use a block tool that supports drag-placement (platforms, paths, ramps, etc.), a **Platform Autofill — ON/OFF** button appears in the tool panel.

- **OFF (default):** the tool behaves exactly as vanilla — placement is blocked if the terrain is lower than expected.
- **ON:** the mod intercepts each placed block and calculates the vertical gap between the terrain and the bottom of your block. It then fills that gap automatically with support columns (Platform, DoublePlatform, or TriplePlatform) matching your current faction, stacking from largest to smallest to minimize piece count.

Support previews are shown in real time while you drag, so you can see exactly what will be placed before you commit.

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

- The autofill only triggers during drag-placement. Single-click placements are not affected.
- Supports are placed for the faction active at the time of placement. Switching factions mid-drag is not supported.
- Placement validation bypass is limited to blocks that are almost-valid (e.g. floating in air above terrain); blocks that overlap existing structures are still rejected.
