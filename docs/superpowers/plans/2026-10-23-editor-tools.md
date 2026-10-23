# Editor tools: sequence rename, align/distribute, replace, duplicate + sim fixes

## Context
The user (CS student designing gate-level circuits) works with many 8-bit inputs/outputs and repeated subchips. Today every pin must be renamed one by one (A0…A7, B0…B7), subchips labelled one by one (FA0…FA7), pins lined up by hand, and turning AND8 into OR8 means deleting and re-wiring every gate. Research also checked their report that 8-bit input + merge/split "gave wrong values".

**Findings that shaped the plan**
- Already exists: multi-place (click a chip button repeatedly → stacked copies), Shift/Alt+D duplicate of placed elements + wires, Save As (copies a chip in the library). Missing: batch rename, align/distribute, rename/label undo, replace, library Duplicate button.
- Merge/split is **not buggy**: every merge/split, 8-bit pin grid and value display uses top pin = MSB, pin "A" = bit 0. Likely cause of "wrong values" = wiring A0-on-top into a merge → bit-reversed. User: "if reversed fix it, if not leave it" → semantics unchanged; we add bit-number labels and make sequence naming put A0 at the bottom.
- Real bugs found: chip-cache thread race and unbounded user-cache size (both silently freeze the sim); Save-As copies share pin IDs and `DeleteWiresAttachedToPinOfSubChip` ignores chip name (editing AND-8 pins cuts OR-8 wires in parents).

**User decisions:** bottom pin = A0 (top = A(N-1)); Replace = single + "replace all of this type"; include bit-number labels, caching fixes, Save-As wire fix, library DUPLICATE button.

## Conventions
- Branch `feature/editor-tools` off `fix/sim-thread-power`; one commit per task (tasks 1–3 are standalone fixes, cherry-pickable).
- Follow "Global constraints" in `docs/superpowers/plans/2026-10-23-organize-layout.md`: tabs, block namespaces, Unity-free C# 9 core, deterministic ordering (never iterate Dictionary/HashSet for order), never call `DevChipInstance.DeleteWire` when swapping wires.
- Pure logic goes in new `Assets/Scripts/Game/Editing/Core/` (namespace `DLS.Game.Editing`, own plain structs — `DLS.Description` uses UnityEngine.Vector2), compiled into `Tools/LayoutTests/LayoutTests.csproj` and unit-tested.
- Commit Unity-generated `.meta` files for new scripts.
- Save this plan as `docs/superpowers/plans/2026-10-23-editor-tools.md` in the first commit.

## Tasks (order: 0 → 9, 10 optional)

### 0. Branch + test harness
Add `<Compile Include="../../Assets/Scripts/Game/Editing/Core/**/*.cs" />` to `Tools/LayoutTests/LayoutTests.csproj`; create `Game/Editing/Core/EditingTypes.cs` (`ElementBox {Id, MinX, MinY, MaxX, MaxY, AnchorX, AnchorY}`, `enum ArrangeOp {AlignLeft, AlignHCentre, AlignRight, AlignTop, AlignVCentre, AlignBottom, DistributeH, DistributeV}`, `CorePin {Id, Bits}`); smoke test.

### 1. Sim-thread robustness + thread-safe cache clear
- `Simulation/Simulator.cs`: `static volatile bool clearCachesRequested` + `RequestClearCaches()`; honour it at top of `ApplyModifications` (clear both cache collections, `needsOrderPass = true`). Make `useCaching`/`isCreatingACache` volatile; add `volatile bool cacheCreationSuspended` (main thread writes) gating `RecalculateCachedLUTs` in `StepChip` and cancelling inside it. try/catch per command in `ApplyModifications`.
- `Simulation/SimChip.cs` `RemoveConnection`: tolerate missing pin like `AddConnection`.
- `DevChipInstance.NotifySaved` (~215-218), `SaveSystem/Loader.cs` (~29-30): `.Clear()` → `Simulator.RequestClearCaches()`.
- `Graphics/UI/UIDrawer.cs:82`: `Simulator.cacheCreationSuspended = aMenuIsOpen;` (stops cache-build restart churn while a menu is open).
- `Project.cs` `SimThread` loop: wrap body in try/catch → rate-limited `Debug.LogException`, `needsOrderPass = true`, `Thread.Sleep(1)`.

### 2. Cache input-bit limit
`Simulator.RecalculateCachedLUTs` (~221): `limit = shouldBeCached ? MAX_..._USER_CACHING : MAX_..._AUTO_CACHING`; reject if bits > limit. `ChipCustomizationMenu.cs` (~90-108): clear `ShouldBeCached` in the ≤12-bit / too-many-bits / non-combinational branches.

### 3. Save-As shared pin-ID wire bug
`DevChipInstance.DeleteWiresAttachedToPinOfSubChip(string subChipName, int pinID)` also matches `SubChipInstance.Description.NameMatch(subChipName)`; pass `root_desc.Name` from `Project.UpdateAndSaveAffectedChips` (~450, 455). Also `WireInstance.RemoveConnectionDependency` takes the owning `DevChipInstance` instead of `Project.ActiveProject.ViewedChip`.

### 4. Bit-numbered merge/split pin labels
Core `BitPinNames.ForConversionChip(isInput, pinIndex, pinCount, bitsPerPin)` → single wide pin keeps "IN"/"OUT"; else "IN 7" (1-bit) or "OUT 7-4" (multi-bit), top index = highest bits. Use in `BuiltinChipCreator.CreateBitConversionChip`, delete `GetPinName`. Built-ins are rebuilt on load and pin IDs unchanged → existing projects pick it up, no migration.

### 5. Multi-selection context menu
- `ChipInteractionController`: `IsPartOfMultiSelection(IInteractable)`; `HandleRightMouseDown` skips `ClearSelection()` when right-clicking inside a multi-selection; expose `DeleteSelectedElements()`.
- `Project`: `SnapPreferenceEnabled` (preference only — Ctrl held forces `ShouldSnapToGrid`).
- `ContextMenu.cs`: when multi-selected, show "N SELECTED" menu — input-only / output-only pins: RENAME…, ARRANGE ›, DELETE; subchips: LABEL…, REPLACE…, SELECT SAME, ARRANGE ›, DELETE; mixed: ARRANGE ›, DELETE. ARRANGE › pages to `entries_arrange` (Align Left / H-Centre / Right, Top / V-Centre / Bottom, Distribute H / V). Menu width = widest entry, not `menuEntries[0]`.
- New menus must receive targets *before* `SetActiveMenu` (opening an input-blocking menu calls `CancelEverything()`, clearing the selection).

### 6. Batch sequence rename / label (+ rename undo)
- Core `SequenceNaming`: order by Y ascending (bottom first), ties X descending then ID → index 0 = bottom (exact reverse of `DescriptionCreator.OrderPins`); `Names(base, n)` → base+i; `Fits(base, n, maxLen)` (16 pins, 18 labels).
- `UndoController`: `RecordRename(ids, before, after, isPinName)` + `RenameUndoAction` (sets `devPin.Pin.Name` / `subChip.Label`) + branch in `UndoRedo`. Also record from `PinEditMenu.Confirm` and `ChipLabelMenu.Confirm` (give it `SetTargets`).
- New `Graphics/UI/Menus/BatchRenameMenu.cs` (`MenuType.BatchRename` in `UIDrawer` enum/draw/`OnMenuOpened`): base-name field, SEQUENCE / SAME NAME wheel, preview "A0 (bottom) … A7 (top)", CANCEL/CONFIRM; one undo step; re-select targets afterwards.

### 7. Align & distribute
- Core `ArrangeMath.ComputeDeltas(boxes, op, snapX, snapY)`: align to selection bounds edge/centre (snapped once); distribute (n ≥ 3) keeps ends fixed, equal gaps, sorted by centre then ID. `AnyOverlap(moved, others)` (inclusive, like `Bounds2D.Overlaps`); `PinOrder` replicates `OrderPins`.
- New `Game/Editing/SelectionArranger.Apply(controller, op, out reason)`: build boxes from `BoundingBox`/`SnapPoint`, snap only if `SnapPreferenceEnabled` (x step `GridSize`, y step `GridSize/2`); **refuse whole op** if result overlaps anything or changes input/output pin order (protects the chip's external pin order — blocks e.g. align-top on stacked pins); no-op if all deltas zero; else existing move recipe (`MoveStartPosition = Position`, move, `wire.MoveOffset` for wires with both ends selected, `RecordMoveElements`, `ApplyMoveOffset`). On refusal mark elements `IsValidMovePos = false` (red outline).
- Shortcuts (`KeyboardShortcuts.cs`, handled in `HandleKeyboardInput` when >1 selected and not placing/moving): **Ctrl+Shift+← / → / ↑ / ↓** align left/right/top/bottom; **Ctrl+Shift+C** align horizontal centres; **Ctrl+Shift+M** align vertical centres; **Ctrl+Shift+H / V** distribute horizontally / vertically. (Ctrl, not Cmd, on macOS — matches existing shortcuts.)

### 8. DUPLICATE in chip library
- `Project.DuplicateChip(source, newName)`: `Saver.CloneChipDescription` → rename → ensure min size → `Saver.SaveChip` → `chipLibrary.NotifyChipSaved` → insert in source's collection after source → star → `UpdateAndSaveProjectDescription()` → `SearchPopup.AddRecentChip`. (Can't reuse `SaveFromDescription(SaveAs)` — it retargets the viewed chip.)
- `ChipSaveMenu`: `IsValidNewChipName`, unique "<NAME> COPY [n]" helper.
- `ChipLibraryMenu`: DUPLICATE row under USE/OPEN/DELETE (disabled for built-ins), inline name input prefilled with unique name, CANCEL/DUPLICATE.

### 9. Replace subchip / Replace all of type
- Core `PinMatcher.TryMatch(oldIn, oldOut, newIn, newOut)`: same input/output counts and widths in order; map by ID if the ID sets match (Save-As / Duplicate siblings like AND-8 ↔ OR-8), else by position; null if incompatible.
- `DevChipInstance.ReplaceSubChips(newDescs, wiresAfter)` (same IDs): remove sim connections touching replaced IDs → per chip `Simulator.RemoveSubChip`, swap `Elements[i]` with new `SubChipInstance`, `Simulator.AddSubChip` → `ReplaceAllWires(wiresAfter)` → `Simulator.AddConnection` for touched wires. Wire order/`ConnectedWireIndex` unchanged, so branch wires survive.
- New `Game/Editing/SubChipReplacer`: `CanReplace` (editable, not bus/Key/ROM/Pulse/LED i.e. no default InternalData, not referenced by `Displays`); `CompatibleChipNames` (library minus hidden/self, `CanAddSubchip`, `PinMatcher` ok); `Replace(chip, ids, newName)` snapshots before, remaps wire pin IDs + `OutputPinColourInfo.PinID`, keeps ID/label/position, applies, records undo, re-selects. Size changes may leave overlaps — user can Ctrl+T.
- `UndoController`: `RecordReplaceSubChips` + `ReplaceSubChipsUndoAction`.
- New `Graphics/UI/Menus/ReplaceChipPopup.cs` (`MenuType.ReplaceChip`), modelled on `SearchPopup` (extract its filter into a shared helper); lists only compatible chips.
- `ContextMenu`: REPLACE and REPLACE ALL (all of that type in this chip) on subchip menus; multi-select REPLACE… when all selected share one replaceable type.

### 10. (Small extra) SELECT SAME
Subchip context entry selecting every subchip with the same name → straight into LABEL sequence / ARRANGE / REPLACE.

## Critical files
`Graphics/UI/Menus/ContextMenu.cs`, `Game/Interaction/ChipInteractionController.cs`, `Game/Interaction/UndoController.cs`, `Game/Interaction/KeyboardShortcuts.cs`, `Game/Project/DevChipInstance.cs`, `Game/Project/Project.cs`, `Game/Project/BuiltinChipCreator.cs`, `Simulation/Simulator.cs`, `Simulation/SimChip.cs`, `Graphics/UI/UIDrawer.cs`, `Graphics/UI/Menus/{PinEditMenu,ChipLabelMenu,ChipLibraryMenu,ChipSaveMenu,ChipCustomizationMenu,SearchPopup}.cs`, `Tools/LayoutTests/LayoutTests.csproj` (all under `Assets/Scripts/` except the csproj).
Reuse: `ChipOrganizer.Organize` (snapshot/replace-wires pattern), `DuplicateElements` (PinAddress remapping), `Saver.CloneChipDescription`, `DescriptionCreator.CreateSubChipDescription`, `GridHelper`, `CanAddSubchip`.

## Verification
- `dotnet test Tools/LayoutTests` after each core task (SequenceNaming, ArrangeMath, BitPinNames, PinMatcher, existing layout tests).
- Unity Editor (6000.0.85f1) compile + manual checks:
  - Select 8 stacked inputs → RENAME "A" sequence → bottom A0, top A7; save; chip used as subchip shows A7..A0 top→bottom; wire into MERGE 1-8 ("IN 7".."IN 0") straight across → 8-bit output shows correct value; Ctrl+Z restores names.
  - 8 full adders → SELECT SAME → LABEL "FA" → FA0..FA7.
  - Scattered pins → Align right + Distribute V → even, on grid, wires attached, single undo; align-top on a stacked column refused (red), nothing moves. Check Ctrl+Shift+arrows reach the app on macOS build.
  - Library: DUPLICATE AND-8 → "AND-8 COPY" persists after restart; original unchanged.
  - Parent with 8 wired AND-8 → REPLACE ALL → OR-8: wires preserved, output now OR; undo/redo; save/reload; branch wires survive; REPLACE disabled on ROM/Key/bus; parent chip not offered.
  - Save-As bug: AND-8 + OR-8 (Save As) both wired in PARENT; delete an AND-8 pin → only AND-8's wire removed.
  - Caching: repeated saves while a 12-bit chip caches → sim keeps running; menus open during caching → no memory churn; user-cached chip grown past 24 bits → no freeze, flag cleared.
