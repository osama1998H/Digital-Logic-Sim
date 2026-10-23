# Plan 6: Timing Diagram (Probes)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user mark pins and wires as probes and see their values over time as waveforms in a panel docked above the bottom bar. The panel can be zoomed, panned while paused, and inspected with a cursor. It is meant for latches, flip-flops, counters and state machines.

**Architecture:**
- **Recording:** the simulation thread records one sample per step for every probe into a lock-free ring buffer (`SampleRing`, engine-free) right after `RunSimulationStep`.
- **Reading:** the main thread reads a snapshot and turns it into value segments (`WaveformSegments`, engine-free).
- **Display:** `TimingDiagramPanel` draws them. It is not a `MenuType`, so the world stays interactive.
- **Probe changes:** the probe list is an immutable `ProbeRecording` object that the main thread swaps in atomically. The recorder never shares a mutable list with the main thread.

**Tech Stack:** C# 9, Unity 6000.0.85f1, Seb.Vis Draw/UI, NUnit.

**Spec:** `docs/superpowers/plans/2026-10-23-study-tools-roadmap.md` (feature #3).

## Decisions (planner's calls)
- **Adding probes:** a "PROBE" / "REMOVE PROBE" entry in the right-click menus of dev pins, wires (probes the wire's source pin), subchip output pins (`entries_subChipOutput`), and subchip input pins. Subchip input pins get a new probe-only menu, since today `openSubchipOutputPinContextMenu` requires `IsSourcePin`.
- **Probe limits:** at most 12 probes. The default name is the pin name, or `<chip label or name>.<pin name>` for subchip pins. Colours cycle through `PinColour` values.
- **Lifetime:** probes belong to the chip being edited. They are kept in memory per chip name for the session (not saved) and cleared when a probed element is deleted.
- **History:** the last 4096 steps. The panel shows the newest steps, so it scrolls live.
  - When the simulation is paused, Shift + mouse wheel pans back and plain wheel zooms (1 to 64 px per step). While running, the wheel only zooms.
- **Drawing:**
  - 1-bit signals: a line, high/low; floating as a grey mid-line.
  - Multi-bit signals: bus "boxes" with the hex value drawn when the segment is ≥ 24 px wide.
  - Moving the mouse over the panel shows a vertical cursor and the value at that step next to each name, using `PinValueFormat` with Hex for multi-bit values.
- **Opening the panel:** Ctrl+Shift+W or bottom bar "TIMING DIAGRAM". The panel header has CLEAR (resets history) and CLOSE. With no probes, it shows the hint "Right-click a pin or wire → PROBE".
- **Panel height:** `2.6 + 2.2 × probeCount` UI units, at most 45% of the screen height. It sits directly above the bottom bar (`BottomBarUI.barHeight`).

## Global Constraints
See the roadmap. Branch: `feature/study-tools-timing`. New Core folder: `Assets/Scripts/Game/Probes/Core/`, with a `<Compile Include>` line. Requires Plan 4 (`PinValueFormat`).

## Review Focus
- **Probes changed while the sim thread is recording:** no exceptions and no torn reads. Handled by the immutable `ProbeRecording` swap plus `volatile` reference; Test `Ring_ReaderNeverSeesHalfWrittenSlot` simulates interleaving.
- **More than 4096 steps:** the ring wraps, and the oldest samples drop off cleanly. Test `Ring_WrapsAndKeepsNewest`.
- **A probed element is deleted,** or the user switches chips: the probe is removed and no stale address is read (`GetSimPinFromAddress` failure → floating sample). Manual check plus try/catch in the recorder.
- **Step-by-step stepping while paused** (Space): each single step adds exactly one sample. Manual check.
- **A value that doesn't change for thousands of steps:** a single segment is drawn efficiently. Test `Segments_MergeEqualRuns`.

---

### Task 1: Ring buffer and segments (core)

**Files:**
- Create: `Assets/Scripts/Game/Probes/Core/SampleRing.cs`
- Create: `Assets/Scripts/Game/Probes/Core/WaveformSegments.cs`
- Modify: `Tools/LayoutTests/LayoutTests.csproj`
- Test: `Tools/LayoutTests/ProbeCoreTests.cs`

**Interfaces:**
- Produces (namespace `DLS.Game.Probes`):
  - `sealed class SampleRing`:
    - `SampleRing(int probeCount, int capacity)`; `int ProbeCount`; `int Capacity`.
    - `void Write(long step, uint[] values)` (single writer).
    - `int CopyLatest(int maxCount, long[] stepsOut, uint[][] valuesOut)`: reader, oldest first, returns the count. It reads at most `Capacity - 64` slots back, so a concurrent writer can't overwrite what is being read.
    - `long LatestStep`; `void Clear()`. `Clear` is requested by the main thread and honoured by the writer before its next write, via a `volatile bool`.
  - `readonly struct WaveSegment(long startStep, long endStep, uint value)`. `endStep` is exclusive.
  - `static class WaveformSegments`: `List<WaveSegment> Build(long[] steps, uint[] values, int count)`. Consecutive equal values merge; a gap in step numbers starts a new segment.

- [ ] **Step 1: Write failing tests:**
  - `Ring_KeepsWrittenOrder`
  - `Ring_WrapsAndKeepsNewest`: capacity 128, write 1000 steps, `CopyLatest(64, …)` returns steps 936 to 999.
  - `Ring_ReaderNeverSeesHalfWrittenSlot`: interleave `Write` / `CopyLatest` on two threads for 200 ms and assert every copied sample's values all equal its step (values written as `{step, step}`).
  - `Ring_ClearDropsHistory`
  - `Segments_MergeEqualRuns`: values 0,0,1,1,1,0 at steps 10 to 15 give `[(10,12,0),(12,15,1),(15,16,0)]`.
  - `Segments_GapStartsNewSegment`
- [ ] **Step 2: Run tests.** Expected: FAIL.
- [ ] **Step 3: Implement.** `SampleRing` stores the steps in a `long[]`, the values flattened in a `uint[capacity*probeCount]`, and a `volatile int writeCount`. The writer fills a slot, then increments `writeCount`; the reader takes a snapshot of `writeCount` first.
- [ ] **Step 4: Run tests.** Expected: PASS.
- [ ] **Step 5: Commit** (`Add probe sample ring and waveform segments`).

### Task 2: Probe set and recording on the sim thread

**Files:**
- Create: `Assets/Scripts/Game/Probes/ProbeSet.cs` (main thread)
- Create: `Assets/Scripts/Simulation/ProbeRecorder.cs` (sim thread)
- Modify: `Assets/Scripts/Game/Project/Project.cs`: in `SimThread`, call `ProbeRecorder.Record(simChip, Simulator.simulationFrame)` right after `Simulator.RunSimulationStep(...)`; do the same in `Debug_RunMainThreadSimStep`. In `LoadDevChipOrCreateNewIfDoesntExist`, call `ProbeSet.OnViewedChipChanged(newChipName)`.
- Modify: `Assets/Scripts/Game/Interaction/ChipInteractionController.cs`: in `DeleteElements`, call `ProbeSet.RemoveProbesOwnedBy(element.ID)` for each deleted element.

**Interfaces:**
- Produces:
  - `sealed class Probe { string Name; PinAddress Address; int BitCount; PinColour Colour; }`.
  - `static class ProbeSet`:
    - `IReadOnlyList<Probe> Probes`; `const int MaxProbes = 12`.
    - `bool Toggle(PinInstance pin)` returns whether it is now probed; `bool IsProbed(PinInstance pin)`.
    - `void RemoveProbesOwnedBy(int ownerId)`; `void OnViewedChipChanged(string chipName)`; `void ClearHistory()`.
    - `SampleRing Ring` (current).
    - Every change builds a new `ProbeRecording(PinAddress[] addresses, SampleRing ring)` and assigns it to `ProbeRecorder.Current` (`volatile`).
  - `static class ProbeRecorder`: `volatile ProbeRecording Current`; `void Record(SimChip root, long step)`. For each address it reads `root.GetSimPinFromAddress(addr).State` inside a try/catch; on failure it records `0xFFFF0000` (floating).
- [ ] **Step 1: Implement.**
- [ ] **Step 2: Run `Tools/verify-compile.sh`.** Expected: `0 Error(s)`.
- [ ] **Step 3: Commit** (`Record probe samples on the simulation thread`).

### Task 3: Panel, menu entries and shortcut

**Files:**
- Create: `Assets/Scripts/Graphics/UI/TimingDiagramPanel.cs`
- Modify: `UIDrawer.cs`: draw the panel in `DrawProjectMenus` when `TimingDiagramPanel.IsOpen` and the active menu is `None` or `BottomBarMenuPopup`. Draw it before `BottomBarUI` so menus overlay it.
- Modify: `ContextMenu.cs`: probe entries as in Decisions. `MenuEntry` text is dynamic, so add a `Func<string>` overload or two entries with `IsEnabled` toggled by `ProbeSet.IsProbed`.
- Modify: `BottomBarUI.cs` (`$"TIMING DIAGRAM {shortcutTextCol}Ctrl+Shift+W"`), `KeyboardShortcuts.cs` (`TimingDiagramShortcutTriggered => CtrlShiftShortcutTriggered(KeyCode.W)`).

**Interfaces:**
- Consumes: `ProbeSet`, `WaveformSegments`, `PinValueFormat`.
- Produces: `TimingDiagramPanel.IsOpen` and `TimingDiagramPanel.Toggle()`.
- [ ] **Step 1: Implement.**
  - Use `UI.DrawPanel` for the background so world clicks are blocked only under the panel.
  - Use `Draw.Line` / `Draw.Quad` / `Draw.Text` in the UI layer for waveforms.
  - Mouse wheel input over the panel is consumed so the camera doesn't zoom. Check `InteractionState.MouseIsOverUI`, as `BottomBarUI.MouseIsOverBar` does.
- [ ] **Step 2: Run `Tools/verify-compile.sh`.** Expected: `0 Error(s)`.
- [ ] **Step 3: Manual check:**
  - Probe a CLOCK output and a counter's 4-bit output (TestData chips): the square wave and counter values scroll live.
  - Pause and step with Space: one new sample per press.
  - Shift + wheel pans; wheel zooms.
  - Delete a probed chip: its probe row disappears.
  - Switch chips: the probes reset.
  - Clicking the canvas above the panel still works.
- [ ] **Step 4: Commit** (`Add timing diagram panel with probes`).

### Task 4: Ship
- [ ] Add a "Timing diagram" bullet under `## Study tools` in `CHANGELOG.md`.
- [ ] Run the tests and compile check; merge into `main`; `git push origin main`; `Tools/install-app.sh`.
