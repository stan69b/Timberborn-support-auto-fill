# TODO

Tick items off as they are verified **in-game** (not just written).

## 1. Verify in-game (highest priority)

1.1.0 builds; nothing has been played in-game yet.

- [x] `dotnet build` succeeds (1.1.0, after adding the `Timberborn.Common` reference)
- [ ] `dotnet build` succeeds for 1.2.0 (new `Timberborn.Localization` reference)
- [ ] Game still loads with the new `Localizations/` folder; labels show in English and French
- [ ] Summary line under the buttons shows "+N supports" while dragging and the orange "can't be supported" part when some spots have no full column
- [ ] `dotnet test src/tests/PlatformAutofill.Tests` passes
- [ ] Dragging platforms/paths over a drop: no floating blocks, previews always render
- [ ] Dragging a path over existing objects: nothing disappears
- [ ] Dragging next to / over impermeable floors: no crash, no supports on top of them
- [ ] Placing buildings with autofill ON: game's normal validation is unchanged
- [ ] Long drag session: no growing lag (preview pooling)
- [ ] `Max 3/2/1` button changes which support pieces are used
- [ ] ON/OFF and Max size are remembered after loading another save / restarting
- [ ] Button still appears after loading a second save in the same session

## 2. Done in code (1.0.5 – 1.2.0)

- [x] Only build complete support columns (no partial/floating stacks)
- [x] Skip the rest of a column when a lower piece fails or its cell is taken
- [x] Never overlap existing objects (paths no longer make things disappear)
- [x] Bottom support must pass the game's own validation (impermeable floors etc.)
- [x] Validation override limited to autofill placements
- [x] Preview object pooling (memory leak)
- [x] Per-component placement dedup
- [x] Max support size button (`SetMaxSupport` was never wired to the UI)
- [x] Remember ON/OFF and max size between games
- [x] Removed empty `OnBeforePlace` hook
- [x] Diagnostic logging off by default
- [x] Column planning moved to `PlatformAutofillRules` with unit tests
- [x] Fixed button missing after loading a second save (process-wide "UI created" flag)
- [x] README updated
- [x] Support count in the panel while dragging ("+N supports") (1.2.0)
- [x] Count of spots that can't be supported, shown in orange (1.2.0)
- [x] Tooltips on both buttons (1.2.0)
- [x] Localization: English + French (`Localizations/`), English fallback if missing (1.2.0)

## 3. Next up (small, well understood)

- [ ] **Hotkey to toggle autofill.** Needs research: Timberborn key bindings are declared as specs (see how other mods add a `KeyBindingSpec`); avoid `UnityEngine.Input`, which may not work with the game's input system. A decompiled look at `Timberborn.InputSystem` (or another mod that adds a hotkey) would make this quick.
- [ ] **Show material cost** next to the support count (e.g. "36 logs"). Needs the construction cost spec of the support templates.
- [ ] **Explain *why* a spot can't be supported** (blocked cell, base can't carry it, no fitting piece) instead of only counting them.
- [ ] **Diagnostic logging as a setting** instead of a code constant (needs a settings UI, see "Mod settings page" below).

## 4. Feature ideas (from the feature sweep)

Rough value / effort estimate: 🟢 easy · 🟡 medium · 🔴 hard/uncertain.

### Placement
- [ ] 🟡 **Level mode:** lock the top surface to the height where the drag started, so a drag across a valley builds a flat bridge instead of following the terrain.
- [ ] 🟡 **Fill down to water surface only** (option): stop columns at the water line, or skip spots that are under water.
- [ ] 🟡 **Choose the support family:** use other stackable blocks than platforms when a faction or mod offers them.
- [ ] 🔴 **Single-footprint buildings:** fill under every cell of a multi-cell building, not just drag tools.
- [ ] 🔴 **Stairs / ramps:** autofill under stairs so a raised path network can be dragged in one go.

### Construction
- [ ] 🟡 **Build supports first:** give autofilled supports a higher construction priority than the block on top, so builders don't wait on an unsupported site.
- [ ] 🟡 **Remove orphan supports:** when you delete a block that autofill supported, offer to delete the column under it (track which supports were auto-placed).
- [ ] 🔴 **Undo last autofill** as one action.

### UI / quality of life
- [ ] 🟢 **Per-tool memory:** remember ON/OFF separately for paths vs platforms.
- [x] 🟢 **Tooltip on the toggle** explaining what autofill does.
- [ ] 🟡 **Preview colour for supports** distinct from the dragged block (e.g. tinted), so it's clear what is auto-added.
- [ ] 🟡 **Mod settings page** (via a mod-settings mod) for defaults and logging.

### Project
- [ ] 🟢 Build script / CI that compiles against the game DLLs and runs the tests.
- [ ] 🟢 Mod page text / screenshots for the Workshop / mod.io release.
