# Plan 1: Evaluation Jobs, Truth Table, Truth Table → Expression

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show the full truth table of any combinational chip (copy it or save it as CSV), and derive a simplified sum-of-products expression for each 1-bit output. This is built on a reusable way to run a chip in isolation on the simulation thread.

**Architecture:** `Simulator` becomes `partial`. A new `Simulator.Evaluation.cs` adds a job queue that the simulation thread drains between live steps. It also adds an isolated stepping routine that saves and restores the global stepping statics and bypasses the chip caches. Truth-table storage, CSV output and Quine–McCluskey minimisation are engine-free code in `Game/Logic/Core`. `TruthTableMenu` is a full-screen popup.

**Tech Stack:** C# 9, Unity 6000.0.85f1, Seb.Vis UI, NUnit.

**Spec:** `docs/superpowers/plans/2026-10-23-study-tools-roadmap.md` (features #1 and #7b).

## Decisions (planner's calls)
- **Supported chips:** only chips whose freshly built sim tree is combinational (`SimChip.IsCombinational()`) with ≤ 16 total input bits. Otherwise show "This chip has memory, a clock or feedback; use the Test Bench" or "Too many input bits (N > 16)".
- **Row order:** the top input pin is the most significant part of the row number (textbook order: A B C → row 1 = A0 B0 C1). Columns are inputs top-to-bottom, then outputs top-to-bottom.
- **Floating output bits** display as `Z`. The minimiser treats a floating 1-bit output as don't-care.
- **Expression syntax** (shared with Plan 3): AND `*`, OR ` + `, NOT postfix `'`, e.g. `A'*B + A*B'`. Constant outputs give `0` or `1`.
- **Expression availability:** only when every input is 1-bit and there are ≤ 8 inputs; per 1-bit output.
- **Chips that can be tabled:**
  - **From the right-click menu (TRUTH TABLE):** the subchip's saved library description.
  - **From the bottom-bar command (Ctrl+Shift+Y):** the currently edited chip, including unsaved changes, via `DescriptionCreator.CreateChipDescription(ViewedChip)`.
- **CSV files** are saved to `<project folder>/Exports/<chip name> truth table.csv`.

## Global Constraints
See the roadmap. Branch: `feature/study-tools-truth-table`. New Core folder: `Assets/Scripts/Game/Logic/Core/`. Add `<Compile Include="../../Assets/Scripts/Game/Logic/Core/**/*.cs" />` to `Tools/LayoutTests/LayoutTests.csproj`.

## Review Focus
- **Disconnected input pin inside the chip** (an input not wired to anything): outputs depending on it show `Z` or `0` per NAND semantics, and nothing crashes. Covered by a Task 4 manual check.
- **Chip with zero input pins** (e.g. constant outputs): exactly 1 row. Test `TruthTable_NoInputs_HasOneRow`.
- **Duplicate or invalid pin names** (many chips name both inputs `IN`): the expression must still be unambiguous. Test `MakeVariableNames_DuplicatesFallBackToLetters`.
- **Closing the menu while the job is still running:** the job must be cancelled and its result ignored. Handled in Task 5; check by closing mid-way on a 16-bit chip.
- **Live simulation keeps running correctly during a long evaluation:** the job is time-sliced to 4 ms per sim loop. Check that clocks keep ticking on screen while a 16-bit table computes.

---

### Task 1: Shared value formatting (only if Plan 4 has not run)
If `Assets/Scripts/Game/Inspect/Core/PinValueFormat.cs` exists, skip this task. Otherwise implement Plan 4 → Task 1 exactly as written there (same files, tests and signatures) and commit.

### Task 2: `TruthTable` core and CSV

**Files:**
- Create: `Assets/Scripts/Game/Logic/Core/TruthTable.cs`
- Create: `Assets/Scripts/Game/Logic/Core/TruthTableCsv.cs`
- Modify: `Tools/LayoutTests/LayoutTests.csproj` (add the Logic/Core include)
- Test: `Tools/LayoutTests/TruthTableTests.cs`

**Interfaces:**
- Consumes: `PinValueFormat.FormatValue(uint state, int bitCount, NumberFormat format) -> string` (Plan 4).
- Produces (namespace `DLS.Game.Logic`):
  - `readonly struct TruthPin(string name, int bitCount)` with `Name`, `BitCount`.
  - `sealed class TruthTable`:
    - `TruthTable(TruthPin[] inputs, TruthPin[] outputs)`, `const int MaxInputBits = 16`, `TruthPin[] Inputs`, `TruthPin[] Outputs`, `int TotalInputBits`, `int RowCount`.
    - `uint GetInputValue(int row, int inputIndex)`.
    - `void SetOutputStates(int row, uint[] states)`, `uint GetOutputState(int row, int outputIndex)`.
    - `int RowsFilled`. Outputs are stored as raw pin states (bits | tristate flags << 16).
  - `static class TruthTableCsv`: `string ToCsv(TruthTable table, NumberFormat format)` writes a header of pin names, one line per row, comma-separated, `\n` line ends, no trailing newline.

- [ ] **Step 1: Write failing tests**
```csharp
[Test] public void TwoOneBitInputs_TopPinIsMostSignificant()
{
	var t = new TruthTable(new[] { new TruthPin("A", 1), new TruthPin("B", 1) }, new[] { new TruthPin("Y", 1) });
	Assert.That(t.RowCount, Is.EqualTo(4));
	Assert.That(t.GetInputValue(2, 0), Is.EqualTo(1u)); // row 2 = A1 B0
	Assert.That(t.GetInputValue(2, 1), Is.EqualTo(0u));
}
[Test] public void MixedWidths_SplitRowIntoPinValues()
{
	var t = new TruthTable(new[] { new TruthPin("A", 4), new TruthPin("B", 1) }, new TruthPin[0]);
	Assert.That(t.GetInputValue(0b10011, 0), Is.EqualTo(9u));
	Assert.That(t.GetInputValue(0b10011, 1), Is.EqualTo(1u));
}
[Test] public void TruthTable_NoInputs_HasOneRow() => Assert.That(new TruthTable(new TruthPin[0], new[] { new TruthPin("Y", 1) }).RowCount, Is.EqualTo(1));
[Test] public void HalfAdder_Csv()
{
	var t = new TruthTable(new[] { new TruthPin("A", 1), new TruthPin("B", 1) }, new[] { new TruthPin("S", 1), new TruthPin("C", 1) });
	uint[][] outs = { new[] { 0u, 0u }, new[] { 1u, 0u }, new[] { 1u, 0u }, new[] { 0u, 1u } };
	for (int r = 0; r < 4; r++) t.SetOutputStates(r, outs[r]);
	Assert.That(TruthTableCsv.ToCsv(t, NumberFormat.Binary), Is.EqualTo("A,B,S,C\n0,0,0,0\n0,1,1,0\n1,0,1,0\n1,1,0,1"));
}
[Test] public void FloatingOutput_ShowsZ()
{
	var t = new TruthTable(new TruthPin[0], new[] { new TruthPin("Y", 1) });
	t.SetOutputStates(0, new[] { 1u << 16 });
	Assert.That(TruthTableCsv.ToCsv(t, NumberFormat.Binary), Is.EqualTo("Y\nZ"));
}
```
- [ ] **Step 2: Run `dotnet test Tools/LayoutTests`.** Expected: FAIL (types missing).
- [ ] **Step 3: Implement both files.** `GetInputValue`: shift = the sum of the bit counts of the inputs after `inputIndex`; return `(row >> shift) & ((1u << bits) - 1)`. Throw `ArgumentException` from the constructor if `TotalInputBits > MaxInputBits`.
- [ ] **Step 4: Run tests.** Expected: PASS.
- [ ] **Step 5: Commit** (`Add truth table core and CSV export`, with metas via `Tools/make-metas.sh`).

### Task 3: Quine–McCluskey minimiser and SOP text

**Files:**
- Create: `Assets/Scripts/Game/Logic/Core/LogicMinimizer.cs`
- Create: `Assets/Scripts/Game/Logic/Core/SopFormatter.cs`
- Test: `Tools/LayoutTests/LogicMinimizerTests.cs`

**Interfaces:**
- Produces:
  - `readonly struct Implicant(uint value, uint mask)`. Mask bit = 1 means the variable appears in the term. Variable 0 is the most significant bit of a minterm index (matches truth-table row order).
  - `static class LogicMinimizer`: `List<Implicant> Minimize(int variableCount, IReadOnlyCollection<int> minterms, IReadOnlyCollection<int> dontCares)`, with `const int MaxVariables = 8`.
  - `static class SopFormatter`:
    - `string Format(IReadOnlyList<Implicant> terms, string[] variableNames)`.
    - `string[] MakeVariableNames(string[] pinNames)`. Keep each name if it matches `^[A-Za-z_][A-Za-z0-9_]*$` and all names are unique case-insensitively; otherwise use `A`, `B`, `C`… by position.

- [ ] **Step 1: Write failing tests**
```csharp
static string Sop(int vars, int[] on, int[] dc = null) =>
	SopFormatter.Format(LogicMinimizer.Minimize(vars, on, dc ?? new int[0]), new[] { "A", "B", "C", "D" }.Take(vars).ToArray());
[Test] public void Xor() => Assert.That(Sop(2, new[] { 1, 2 }), Is.EqualTo("A'*B + A*B'"));
[Test] public void Majority() => Assert.That(Sop(3, new[] { 3, 5, 6, 7 }), Is.EqualTo("A*B + A*C + B*C"));
[Test] public void AlwaysOne() => Assert.That(Sop(2, new[] { 0, 1, 2, 3 }), Is.EqualTo("1"));
[Test] public void AlwaysZero() => Assert.That(Sop(2, new int[0]), Is.EqualTo("0"));
[Test] public void DontCaresAreUsed() => Assert.That(Sop(3, new[] { 1, 3 }, new[] { 5, 7 }), Is.EqualTo("C"));
[Test] public void MakeVariableNames_DuplicatesFallBackToLetters() =>
	Assert.That(SopFormatter.MakeVariableNames(new[] { "IN", "IN" }), Is.EqualTo(new[] { "A", "B" }));
[Test] public void MakeVariableNames_KeepsValidNames() =>
	Assert.That(SopFormatter.MakeVariableNames(new[] { "CIN", "X1" }), Is.EqualTo(new[] { "CIN", "X1" }));
```
- [ ] **Step 2: Run tests.** Expected: FAIL.
- [ ] **Step 3: Implement.**
  - Standard tabular QM: group by popcount, merge pairs that differ in one cared bit, and collect primes.
  - Cover selection: first take essential primes. Then pick greedily the prime covering the most uncovered minterms; ties go to fewer literals, then lower `(mask, value)`.
  - Output terms are sorted by literal count, then by the term's string. Literals within a term are in variable order.
- [ ] **Step 4: Run tests.** Expected: PASS.
- [ ] **Step 5: Commit** (`Add Quine-McCluskey minimiser and SOP formatter`).

### Task 4: Evaluation jobs on the simulation thread

**Files:**
- Modify: `Assets/Scripts/Simulation/Simulator.cs`: change `public static class Simulator` to `public static partial class Simulator`. In `StepChip` (the cached-chip and cache-creation branches, ~lines 163-176), add `&& !bypassCaches` to both `useCaching` conditions.
- Create: `Assets/Scripts/Simulation/Simulator.Evaluation.cs`
- Create: `Assets/Scripts/Simulation/SimEvaluator.cs`
- Modify: `Assets/Scripts/Game/Project/Project.cs`: in `SimThread`, call `Simulator.RunEvaluationJobs(4)` directly after `Simulator.ApplyModifications();` (inside the try, before the pause branch). In `Debug_RunMainThreadSimStep`, call it after `ApplyModifications` too.

**Interfaces:**
- Produces (namespace `DLS.Simulation`):
  - `public abstract class EvaluationJob`:
    - Fields: `public volatile bool IsFinished`, `public volatile bool IsCancelled`, `public volatile float Progress`, `public volatile string Error`.
    - `protected internal abstract bool RunSlice()`, called repeatedly on the sim thread until it returns true.
    - `public void Cancel()`.
  - `Simulator.EnqueueEvaluationJob(EvaluationJob job)` (main thread).
  - `Simulator.RunEvaluationJobs(double maxMilliseconds)` (sim thread only). It runs the head job's `RunSlice` repeatedly until the time budget is used up. When a job finishes, throws (sets `Error = e.Message`) or is cancelled, it sets `IsFinished` and dequeues it.
  - `public sealed class SimEvaluator`:
    - `SimEvaluator(SimChip root)`, `SimChip Root`.
    - `void SetInput(int inputIndex, uint bits)` sets `State` to the bits masked to the pin width, with no tristate flags.
    - `uint GetOutput(int outputIndex)` returns the raw state.
    - `void Step()`.
    - `int StepUntilStable(int stableSteps, int maxSteps)` returns the steps taken, or -1 if the outputs never stayed unchanged for `stableSteps` consecutive steps.
  - `internal static void Simulator.StepIsolated(SimChip root, bool firstStep, int frame)`:
    1. Save `simulationFrame`, `canDynamicReorderThisFrame` and `pcg_rngState`.
    2. Set `simulationFrame = frame`, `canDynamicReorderThisFrame = false`, `pcg_rngState = (uint)frame * 747796405u + 1`, `bypassCaches = true`.
    3. Call `root.ResetReceivedFlagsOnAllPins()`, then `StepChipReorder(root)` if `firstStep` else `StepChip(root)`.
    4. Restore everything in a `finally`.
  - `SimEvaluator` keeps its own frame counter starting at `1 << 30`.

- [ ] **Step 1: Implement the changes above.** `bypassCaches` is `static bool` (sim thread only).
- [ ] **Step 2: Run `Tools/verify-compile.sh`.** Expected: `0 Error(s)`.
- [ ] **Step 3: Commit** (`Run isolated chip evaluations as jobs on the sim thread`).

(Behaviour is verified end-to-end in Task 5, where the truth table is checked against known chips.)

### Task 5: Truth table job and menu

**Files:**
- Create: `Assets/Scripts/Game/Logic/TruthTableJob.cs`
- Create: `Assets/Scripts/Graphics/UI/Menus/TruthTableMenu.cs`
- Modify: `Assets/Scripts/Graphics/UI/UIDrawer.cs`: `MenuType.TruthTable`, draw branch, `OnMenuOpened` branch.
- Modify: `Assets/Scripts/Graphics/UI/Menus/ContextMenu.cs`: add `new(Format("TRUTH TABLE"), OpenTruthTableForSubChip, () => true)` to `entries_customSubchip` and `entries_builtinSubchip` (before the first divider).
- Modify: `Assets/Scripts/Graphics/UI/Menus/BottomBarUI.cs`: add `$"TRUTH TABLE  {shortcutTextCol}Ctrl+Shift+Y"` to the menu (update button index constants) and handle `KeyboardShortcuts.TruthTableShortcutTriggered`.
- Modify: `Assets/Scripts/Game/Interaction/KeyboardShortcuts.cs`: `TruthTableShortcutTriggered => CtrlShiftShortcutTriggered(KeyCode.Y)`.
- Modify: `Assets/Scripts/SaveSystem/SavePaths.cs`: `public static string GetExportsPath(string projectName) => Path.Combine(GetProjectPath(projectName), "Exports")`.
- Modify: `Assets/Scripts/Game/Main/Main.cs`: extract `public static void OpenFolderInFileBrowser(string path)` from `OpenSaveDataFolderInFileBrowser` (which then calls it).

**Interfaces:**
- Consumes: Tasks 2 to 4.
- Produces:
  - `public sealed class TruthTableJob : EvaluationJob` (namespace `DLS.Game.Logic`). Construct it with `TruthTableJob(ChipDescription chip, ChipLibrary library)` on the main thread: it builds the tree via `Simulator.BuildSimChip`, and `CanRun(out string reason)` checks `IsCombinational()` and the bit limit.
  - `TruthTable Table`. Each slice fills up to 256 rows: `SetInput` for every input, then `Step()` (the first row passes `firstStep: true`), then read the outputs.
  - `TruthTableMenu.Open(ChipDescription chip)`.

- [ ] **Step 1: Build the menu.**
  - Layout: a title (chip name) and a progress line while running. When done, a scroll view of rows: a sticky header row with column names; input cells in white, output cells in `ActiveUITheme` green. Rows are skipped when off-screen, as in `RomEditMenu.DrawScrollEntry`.
  - Format wheel: `Binary/Decimal/Hex`, default Binary.
  - Buttons:
    - **COPY CSV:** `InputHelper.CopyToClipboard`.
    - **SAVE CSV:** writes to `GetExportsPath(...)/<chip> truth table.csv` and shows "Saved to …".
    - **EXPRESSION:** shown when allowed. It shows `Y = <SOP>` lines per 1-bit output, using `MakeVariableNames`. If names were replaced, it adds a line "A = first input, B = second …". A COPY button copies these lines.
    - **CLOSE / Esc:** calls `job.Cancel()`.
- [ ] **Step 2: Run `Tools/verify-compile.sh`.** Expected: `0 Error(s)`.
- [ ] **Step 3: Manual check in the Unity Editor (TestData project).** Each of these must hold:
  - **NAND:** 4 rows, Y = 1,1,1,0. Expression `A' + B'`, or the equivalent with letter names because both pins are named `IN`.
  - **XOR (custom chip):** 0,1,1,0.
  - **AND-8:** Decimal format shows output = A & B, e.g. row A=255, B=15 → 15. EXPRESSION disabled, reason "inputs must be 1-bit".
  - **A chip with a CLOCK:** shows the "use the Test Bench" message.
  - **Closing on a 16-bit-input chip mid-way:** no error, and the live simulation keeps ticking throughout.
- [ ] **Step 4: Commit** (`Add truth table viewer with CSV export and simplified expressions`).

### Task 6: Ship
- [ ] Add a "Truth table" bullet under a new `## Study tools` section in `CHANGELOG.md`: right-click a chip or press Ctrl+Shift+Y; copy or save as CSV; simplified expression for 1-bit outputs.
- [ ] `dotnet test Tools/LayoutTests` passes. `Tools/verify-compile.sh` reports 0 errors.
- [ ] Commit, merge into `main`, `git push origin main`, and run `Tools/install-app.sh` (expected: `MacBuild: Succeeded (0 errors)`).
