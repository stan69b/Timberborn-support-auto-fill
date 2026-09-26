# Changelog

All notable changes to this project will be documented in this file.

## [1.0.5] - 2026-09-26

- Fixed floating elements: a support column is now only planned, previewed and placed when it reaches all the way down to the ground or an existing base. Partial columns are no longer built.
- Fixed floating supports when a lower support of a column fails to place: the pieces above it are now skipped.
- Fixed objects disappearing when dragging paths (roads) or other blocks over existing objects: autofill no longer lets a placement into cells that are already occupied, and supports are never placed into occupied cells.
- Fixed a likely crash when supports were stacked on objects that cannot carry them (e.g. impermeable floors): the bottom support now has to pass the game's own validation.
- The validation override no longer affects unrelated tools (buildings, floors, …) while the toggle is on.
- Fixed a memory leak where preview objects were created on every mouse move and validation check; previews are now reused. This also reduces lag and stale/ghost previews while dragging.
- Fixed supports sometimes not being generated when placing again at the same spot as the previous placement.

## [1.0.4] - 2026-06-07

- Fixed an autofill bug where supports could be planned through already occupied cells below the dragged structure.
- Prevented auto-generated supports from being injected under existing structures such as water flow regulators, which could cause invalid placements after reload.

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
