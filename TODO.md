# TODO

Tick items off as they are verified **in-game** (not just written).

## 1. Verify in-game (highest priority)

None of the changes below have been built or played yet.

- [ ] `dotnet build` succeeds (watch for `IBlockService.GetObjectsAt` in `AnyWorldObjectAt`)
- [ ] `dotnet test src/tests/PlatformAutofill.Tests` passes
- [ ] Dragging platforms/paths over a drop: no floating blocks, previews always render
- [ ] Dragging a path over existing objects: nothing disappears
- [ ] Dragging next to / over impermeable floors: no crash, no supports on top of them
- [ ] Placing buildings with autofill ON: game's normal validation is unchanged
- [ ] Long drag session: no growing lag (preview pooling)
- [ ] `Max 3/2/1` button changes which support pieces are used
- [ ] ON/OFF and Max size are remembered after loading another save / restarting
- [ ] Button still appears after loading a second save in the same session

## 2. Done in code (1.0.5 – 1.1.0)

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

## 3. Next up (small, well understood)

- [ ] **Hotkey to toggle autofill.** Needs research: Timberborn key bindings are declared as specs (see how other mods add a `KeyBindingSpec`); avoid `UnityEngine.Input`, which may not work with the game's input system.
- [ ] **Show support count / cost in the panel** while dragging (e.g. "+12 supports, 36 logs"). The service already knows the planned columns in `UpdateSupportPreviews`; expose a count and refresh the label.
- [ ] **Explain why a spot is red.** When a column can't be completed, show a short reason in the tool panel (blocked cell, unsupported base, no fitting piece).
- [ ] **Diagnostic logging as a setting** instead of a code constant.
- [ ] **Localization.** Labels are hard-coded English; move them to the game's localization files (at least EN + FR).

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
- [ ] 🟢 **Tooltip on the toggle** explaining what autofill does.
- [ ] 🟡 **Preview colour for supports** distinct from the dragged block (e.g. tinted), so it's clear what is auto-added.
- [ ] 🟡 **Mod settings page** (via a mod-settings mod) for defaults and logging.

### Project
- [ ] 🟢 Build script / CI that compiles against the game DLLs and runs the tests.
- [ ] 🟢 Mod page text / screenshots for the Workshop / mod.io release.
