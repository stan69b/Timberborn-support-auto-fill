# Changelog

All notable changes to this project will be documented in this file.

## [1.0.3] - 2026-06-07

- Fixed a startup issue caused by local test binaries being picked up as mod assemblies.
- Improved support planning so existing world objects are validated as potential support bases instead of always truncating the stack.
- Added regression tests for support-template detection and validator-driven support placement selection.

## [1.0.2] - 2026-06-07

- Added a lightweight automated test runner under `src/tests`.
- Added regression coverage for faction parsing, support template ordering, support placement selection, and bottom-up support ordering rules.

## [1.0.0] - 2026-06-07

Version 1 establishes the core Platform Autofill feature set:

- Added an in-tool `Platform Autofill` toggle for supported drag-placeable block tools.
- Added automatic support generation for elevated drag placements.
- Added support stacking with `TriplePlatform`, `DoublePlatform`, and `Platform` pieces to minimize block count.
- Added live support previews while dragging so the final structure is visible before placement.
- Added support for draggable vanilla block types such as platforms, paths, and overhangs.
- Added compatibility for modded draggable blocks that follow Timberborn's faction-based template naming.
- Added faction-aware support selection so generated supports match the active faction.
- Added placement validation bypass logic for supported autofill placements so elevated drags can be placed cleanly.
- Added deferred support placement to avoid conflicting with Timberborn's active placement flow.
- Added Harmony-based integration so autofill hooks into placement, preview, and validation behavior at runtime.
