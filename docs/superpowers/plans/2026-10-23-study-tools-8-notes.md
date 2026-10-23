# Plan 8: Text Notes on the Canvas

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user place coloured sticky-note text boxes inside a chip ("carry chain", "Y = A·B + C"). Notes can be moved, selected, duplicated, deleted, edited and undone like other elements, and are saved with the chip.

**Architecture:**
- **New element:** `NoteInstance : IMoveable` joins `DevChipInstance.Elements`, so selection, box-select, moving, arranging and undo-by-ID work automatically.
- **Code that switches on element type** (listed in Task 2) gets an explicit Note branch.
- **Saving:** notes are stored in a new optional `ChipDescription.Notes` array. It is written as null when empty, so existing chips don't show spurious unsaved changes.
- **Editing:** text is edited in a dedicated multi-row editor (one input field per line, like `RomEditMenu`), because the UI has no multi-line text field.
- **Layout and overlap:** notes never block moves or placement, are not wire-routing obstacles, and are left alone by Organize All.

**Tech Stack:** C# 9, Unity 6000.0.85f1, Newtonsoft JSON, NUnit.

**Spec:** `docs/superpowers/plans/2026-10-23-study-tools-roadmap.md` (feature #5).

## Decisions (planner's calls)
- **Data:** `NoteDescription { int ID; Vector2 Position; string Text; NoteColour Colour }` with `enum NoteColour { Yellow, Blue, Green, Pink, Grey }`. Text has at most 12 lines of at most 40 characters, with `\n` line breaks.
- **Size:** calculated from the text (monospace font): width = longest line × character width + padding, height = lines × line height + padding. There are no resize handles.
- **Adding a note:** bottom bar "ADD NOTE" or Ctrl+Shift+N creates a note that follows the cursor, like placing a chip. Clicking places it and opens the editor straight away. If the user confirms empty text, the note is removed and no undo step is recorded.
- **Right-click menu:** EDIT and DELETE. EDIT opens the editor with a colour wheel.
- **Drawing:** notes are drawn under the chips (before subchips in `DrawMoveableElements`), as a rounded quad in the note colour at 85% alpha with dark text in `FontRegular`.
- **Save version:** `Main.DLSVersion` becomes 2.1.7. Older builds ignore the `Notes` field (Newtonsoft default).

## Global Constraints
See the roadmap. Branch: `feature/study-tools-notes`. New Core folder: `Assets/Scripts/Game/Notes/Core/`, with a `<Compile Include>` line.

## Review Focus
- **Opening a chip saved before notes existed:** no unsaved-changes prompt. `Notes` stays null and is written as null when empty. Manual check plus Task 1 test `Normalize_EmptyTextIsEmpty`.
- **A parent chip re-saved by `UpdateAndSaveAffectedChips`** (after editing a subchip): the parent's notes survive. `LoadFromDescriptionTest` must load notes. Manual check: put a note in PARENT, edit a child chip's pins, reopen PARENT.
- **Undo after deleting a selection that includes notes:** the notes come back with their text and colour. Manual check.
- **Duplicate (Shift+D) of a selection including a note:** the copy keeps its text and colour. This path currently casts non-pins to `SubChipInstance` and would crash; Task 2 fixes it.
- **Very long pasted text:** clamped to 12 × 40 characters, not overflowing. Test `Normalize_ClampsLinesAndLength`.

---

### Task 1: Note text and layout (core)

**Files:**
- Create: `Assets/Scripts/Game/Notes/Core/NoteText.cs`
- Modify: `Tools/LayoutTests/LayoutTests.csproj`
- Test: `Tools/LayoutTests/NoteTextTests.cs`

**Interfaces:**
- Produces (namespace `DLS.Game.Notes`): `static class NoteText`:
  - `const int MaxLines = 12`; `const int MaxLineLength = 40`.
  - `string Normalize(string text)`: converts `\r\n` to `\n`, trims trailing whitespace on each line, removes trailing empty lines, and clamps the line count and line length.
  - `string[] Lines(string text)`.
  - `(float width, float height) Measure(string text, float charWidth, float lineHeight, float padding)`. Empty text measures as one line of 8 characters.
- [ ] **Step 1: Write failing tests:**
  - `Normalize_ClampsLinesAndLength`: 20 lines of 60 characters give 12 lines of 40.
  - `Normalize_TrimsTrailingEmptyLines`
  - `Normalize_EmptyTextIsEmpty`
  - `Measure_UsesLongestLine`: `"ab\nabcd"` with charWidth 1, lineHeight 2, padding 0.5 gives (5, 5).
- [ ] **Step 2: Run tests.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run tests.** Expected: PASS.
- [ ] **Step 5: Commit** (`Add note text helpers`).

### Task 2: Note element, saving and all element-type branches

**Files:**
- Create: `Assets/Scripts/Description/Types/SubTypes/NoteDescription.cs` (struct and enum).
- Modify: `Assets/Scripts/Description/Types/ChipDescription.cs`: `[JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public NoteDescription[] Notes;`.
- Create: `Assets/Scripts/Game/Elements/NoteInstance.cs`: implements `IMoveable`.
  - `Position` is the top-left corner. `BoundingBox` comes from `NoteText.Measure` using `DrawSettings` font metrics.
  - `SnapPoint` = `Position`.
  - `ShouldBeIncludedInSelectionBox` uses overlap, as dev pins do.
  - `string Text; NoteColour Colour`.
- Modify: `Assets/Scripts/Game/Project/DevChipInstance.cs`: `AddNewNote(NoteInstance)` and `DeleteNote(NoteInstance)` (no simulator calls). Load notes in `LoadFromDescriptionTest`.
- Modify: `Assets/Scripts/SaveSystem/DescriptionCreator.cs`:
  - `CreateChipDescription` sets `Notes` = notes ordered by ID, or null if there are none.
  - Add `CreateNoteDescription(NoteInstance)`.
- Modify: `Assets/Scripts/Game/Main/Main.cs`: `DLSVersion = new(2, 1, 7)`.
- Modify: `Assets/Scripts/Game/Interaction/ChipInteractionController.cs`:
  - `DeleteElements`: delete branch.
  - `FinishPlacingNewElements`: add branch.
  - `StartPlacing(IMoveable…)`: don't cast notes to `SubChipInstance`.
  - `CreateElementFromDuplicationSource`: Note branch, with a new ID.
  - Obstacles: `ActiveDevChip.Elements.Where(e => !e.IsSelected && e is not NoteInstance)`. Notes also skip the overlap test as moved elements.
  - Add `StartPlacingNote()`.
- Modify: `Assets/Scripts/Game/Interaction/UndoController.cs`:
  - `RecordAddOrDeleteElements` and `ElementExistenceAction` gain `NoteDescription[] noteDescriptions` (add/remove like pins).
  - New `RecordNoteEdit(int id, string textBefore, NoteColour colourBefore, string textAfter, NoteColour colourAfter)` with a `NoteEditUndoAction` branch in `UndoRedo`.
- Modify: `Assets/Scripts/Game/Layout/LayoutProblemBuilder.cs`: skip `NoteInstance` when adding routing obstacles.
- Modify: `Assets/Scripts/Game/Editing/SelectionArranger.cs`: exclude notes from `otherBoxes`.
- Modify: `Assets/Scripts/Graphics/World/DevSceneDrawer.cs`:
  - `DrawMoveableElements`: draw notes first, then the rest.
  - `DrawNote(NoteInstance)` draws the quad, the selection outline and the text, and calls `InteractionState.NotifyElementUnderMouse(note)` when the mouse is inside.
- Modify: `Assets/Scripts/Graphics/UI/Menus/BatchRenameMenu.cs`: notes are not renamable (the multi-select RENAME/LABEL rules already exclude mixed selections; make sure a note-only selection falls back to `entries_multiMixed`).

- [ ] **Step 1: Implement.**
- [ ] **Step 2: Run `Tools/verify-compile.sh` and `dotnet test Tools/LayoutTests`.** Expected: 0 errors, all tests pass.
- [ ] **Step 3: Commit** (`Add note element with saving, undo and duplication`).

### Task 3: Note editor, menu entries and shortcut

**Files:**
- Create: `Assets/Scripts/Graphics/UI/Menus/NoteEditMenu.cs`:
  - 12 `UIHandle("NoteEdit_Line", i)` fields (validation ≤ 40 chars).
  - Enter moves to the next line; Up/Down move between lines.
  - A colour wheel; CONFIRM / CANCEL. Ctrl+Enter confirms.
  - Confirming records `RecordNoteEdit`; confirming empty text on a just-placed note deletes it with no undo step.
- Modify: `UIDrawer.cs` (`MenuType.NoteEdit`), `ContextMenu.cs` (an `openNoteContextMenu` branch with `entries_note = { EDIT, DELETE }`), `BottomBarUI.cs` (`$"ADD NOTE     {shortcutTextCol}Ctrl+Shift+N"`), `KeyboardShortcuts.cs` (`AddNoteShortcutTriggered => CtrlShiftShortcutTriggered(KeyCode.N)`).
- [ ] **Step 1: Implement.**
- [ ] **Step 2: Run `Tools/verify-compile.sh`.** Expected: `0 Error(s)`.
- [ ] **Step 3: Manual check:**
  - Ctrl+Shift+N, place, type 3 lines, pick Blue, confirm. Move it over a chip: allowed.
  - Box-select it with chips and Shift+D: the copy is identical.
  - Delete, then Ctrl+Z: restored.
  - Save and reopen: the note is there.
  - Open an old chip: no unsaved prompt.
  - Edit a child chip used by a parent that has a note: the parent's note survives.
- [ ] **Step 4: Commit** (`Add note editor and placement`).

### Task 4: Ship
- [ ] Add a "Notes" bullet under `## Study tools` in `CHANGELOG.md`.
- [ ] Run the tests and compile check; merge into `main`; `git push origin main`; `Tools/install-app.sh`.
