# Plan 7: RAM Chip

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a proper, visible built-in RAM chip (256 × 8-bit, clocked writes). Its starting contents can be edited and are saved with the chip, and it has an editor that can also show the live memory.

**Architecture:**
- **New type:** `ChipType.Ram_256x8` is appended to the enum and wired through the built-in chip pipeline (creator, names, collections, simulation, combinational check, default instance data).
- **Memory logic:** RAM behaviour is a small engine-free `RamLogic` used by both the new RAM and the hidden `dev_Ram_8Bit`. This also fixes the dev RAM leaking random bits into bits 8-15.
- **Editor:** the existing `RomEditMenu` is generalised to edit RAM contents (8-bit words) and gains a "LOAD LIVE" button for RAM.

**Tech Stack:** C# 9, Unity 6000.0.85f1, NUnit.

**Spec:** `docs/superpowers/plans/2026-10-23-study-tools-roadmap.md` (feature #8).

## Decisions (planner's calls)
- **Name and pins:** the chip is named `RAM 256×8`, using the same `×` character as `ROM 256×16`. Its pins match `dev.RAM-8`:
  - Inputs: `ADDRESS` (id 0, 8-bit), `DATA` (1, 8-bit), `WRITE` (2), `RESET` (3), `CLOCK` (4).
  - Output: `OUT` (5, 8-bit).
- **Behaviour:**
  - Reads are asynchronous: `OUT = mem[ADDRESS]` every step.
  - On a rising `CLOCK` edge: if `RESET`, every cell becomes 0; else if `WRITE`, `mem[ADDRESS] = DATA`.
  - Floating inputs read as 0.
- **Saved data:** `InternalData = uint[257]`. Indices 0-255 hold the starting contents (saved with the parent chip); index 256 holds the previous clock level (always saved as 0).
  - At runtime, writes change only the live sim state, never the saved starting contents.
  - Editing the contents in the editor resets the live memory to the edited values (same as ROM's `NotifyRomContentsEdited`).
- **Collections:** new projects get the chip in MEMORY. Existing projects get it added to their "MEMORY" collection once, on load, if it isn't in any collection yet. Otherwise it falls back to "OTHER" through the existing library logic.
- **Editor:**
  - Right-click → `EDIT` opens the generalised editor: 256 rows, 8-bit values, display modes as in the ROM editor.
  - `LOAD LIVE` copies the current simulated memory into the fields (main-thread read of `SimChip.InternalState`, for display only).
  - Not available in view mode (`CanEditCurrentChip`).

## Global Constraints
See the roadmap. Branch: `feature/study-tools-ram`. New Core folder: `Assets/Scripts/Game/Memory/Core/`, with a `<Compile Include>` line.

## Review Focus
- **Writing while the clock stays high** for many steps: exactly one write per rising edge. Test `WriteOnlyOnRisingEdge`.
- **RESET and WRITE high on the same edge:** RESET wins. Test `ResetBeatsWrite`.
- **DATA wider than 8 bits** can't happen at the pin level, but the stored values must be masked to 8 bits. Test `StoredValuesAreMasked`.
- **Old projects with no RAM entry in collections:** it appears in MEMORY once and isn't duplicated on every load. Manual check: open twice.
- **Saving a chip containing a RAM:** no "unsaved changes" right after the save. The saved index 256 is always 0, even though the live clock state differs. Manual check.

---

### Task 1: `RamLogic` core

**Files:**
- Create: `Assets/Scripts/Game/Memory/Core/RamLogic.cs`
- Modify: `Tools/LayoutTests/LayoutTests.csproj`
- Test: `Tools/LayoutTests/RamLogicTests.cs`

**Interfaces:**
- Produces (namespace `DLS.Game.Memory`): `static class RamLogic`:
  - `const int WordCount = 256`; `const int PrevClockIndex = 256`; `const int StateLength = 257`.
  - `uint Step(uint[] state, uint address, uint data, bool write, bool reset, bool clock, int dataBits)`. It updates `state` and returns the output value, masked to `dataBits`.

- [ ] **Step 1: Write failing tests:** `ReadIsAsynchronous`, `WriteOnlyOnRisingEdge`, `ResetBeatsWrite`, `StoredValuesAreMasked`, `NoWriteWhenWriteLow`. Each one calls `Step` with an explicit sequence and asserts the exact returned values, e.g.:
```csharp
var s = new uint[RamLogic.StateLength];
RamLogic.Step(s, 3, 0xAB, true, false, false, 8);
Assert.That(RamLogic.Step(s, 3, 0xAB, true, false, true, 8), Is.EqualTo(0xABu)); // rising edge writes, read sees new value
RamLogic.Step(s, 3, 0x11, true, false, true, 8); // clock still high: no write
Assert.That(s[3], Is.EqualTo(0xABu));
```
- [ ] **Step 2: Run tests.** Expected: FAIL.
- [ ] **Step 3: Implement.** On a rising edge, apply the write or reset first, then read.
- [ ] **Step 4: Run tests.** Expected: PASS.
- [ ] **Step 5: Commit** (`Add RAM logic core`).

### Task 2: Built-in RAM chip and simulation

**Files:**
- Modify: `Assets/Scripts/Description/Types/SubTypes/ChipTypes.cs`: append `Ram_256x8` at the very end of the enum.
- Modify: `Assets/Scripts/Description/Helpers/ChipTypeHelper.cs`: `Names` gets `{ ChipType.Ram_256x8, "RAM 256×8" }`, plus `IsRamType(ChipType)` (true for `Ram_256x8` and `dev_Ram_8Bit`).
- Modify: `Assets/Scripts/Game/Project/BuiltinChipCreator.cs`: `CreateRAM_256x8()` with the pins above, the same size and colour style as `CreateROM_8()`, added to `CreateAllBuiltinChipDescriptions()`.
- Modify: `Assets/Scripts/Game/Project/BuiltinCollectionCreator.cs`: add it to MEMORY.
- Modify: `Assets/Scripts/SaveSystem/DescriptionCreator.cs`: `CreateDefaultInstanceData` gives `Ram_256x8 => new uint[RamLogic.StateLength]`. `CreateSubChipDescription` sets index 256 of a RAM's cloned data to 0 before saving.
- Modify: `Assets/Scripts/Simulation/Simulator.cs`, `ProcessBuiltinChip`:
  - New `case ChipType.Ram_256x8` calling `RamLogic.Step(chip.InternalState, …, 8)`.
  - The `dev_Ram_8Bit` case also calls `RamLogic.Step` (this fixes the 16-bit garbage).
- Modify: `Assets/Scripts/Simulation/SimChip.cs`: add `Ram_256x8` to the `IsCombinational` `return false` list.
- Modify: `Assets/Scripts/Game/Project/Project.cs`: after load, `EnsureBuiltinInDefaultCollection("RAM 256×8", "MEMORY")`. It adds the chip to the collection named MEMORY only if no collection contains it, then saves the project description.

- [ ] **Step 1: Implement.**
- [ ] **Step 2: Run `Tools/verify-compile.sh` and `dotnet test Tools/LayoutTests`.** Expected: 0 errors, all tests pass.
- [ ] **Step 3: Manual check:**
  - In a test chip, wire 8-bit inputs to ADDRESS and DATA, and 1-bit inputs to WRITE, RESET and CLOCK.
  - Toggle CLOCK with WRITE=1: OUT shows DATA at that address. Changing ADDRESS and back shows the value is kept.
  - RESET clears the memory.
  - Save, reopen: the starting contents are all 0. There is no unsaved-changes prompt right after saving.
- [ ] **Step 4: Commit** (`Add RAM 256x8 built-in chip`).

### Task 3: RAM editor

**Files:**
- Modify: `Assets/Scripts/Graphics/UI/Menus/RomEditMenu.cs`: the bit count comes from the chip type (ROM 16, RAM 8); the row count is 256. Title "EDIT RAM CONTENTS" or "EDIT ROM CONTENTS". On save, write only indices 0-255. Add a `LOAD LIVE` button (RAM only) that fills the fields from `Project.ActiveProject.ViewedChip.SimChip.GetSubChipFromID(id).InternalState[0..255]`.
- Modify: `Assets/Scripts/Graphics/UI/Menus/ContextMenu.cs`: `entries_builtinRamSubchip = { EDIT (OpenRomEditMenu), LABEL, DELETE }`, selected when `ChipTypeHelper.IsRamType` is true for `Ram_256x8`.
- [ ] **Step 1: Implement.**
- [ ] **Step 2: Run `Tools/verify-compile.sh`.** Expected: `0 Error(s)`.
- [ ] **Step 3: Manual check:**
  - Set row 5 to 42 in the editor and confirm: OUT at address 5 reads 42.
  - Write 7 to address 6 through the circuit, then press LOAD LIVE: row 6 shows 7.
  - The ROM editor still works for ROM chips: 16-bit values, no LOAD LIVE button.
- [ ] **Step 4: Commit** (`Add RAM contents editor`).

### Task 4: Ship
- [ ] Add a "RAM chip" bullet under `## Study tools` in `CHANGELOG.md`, and add "dev RAM no longer outputs random high bits" under Fixes.
- [ ] Run the tests and compile check; merge into `main`; `git push origin main`; `Tools/install-app.sh`.
