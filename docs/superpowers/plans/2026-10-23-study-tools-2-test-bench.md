# Plan 2: Test Bench

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user write expected results for a chip (one line per step, e.g. `A=5 B=3 -> SUM=8`), run them all with one button, and see pass/fail per line. Sequential circuits work by toggling a clock input across lines.

**Architecture:** Test lines are parsed and checked by engine-free code (`Game/Logic/Core`). A `TestBenchJob` (an `EvaluationJob` from Plan 1) runs the lines in order on one isolated `SimEvaluator`, so state carries over between lines. Tests are saved as plain text per chip. `TestBenchMenu` is a scrolling list of one-line input fields, modelled on `RomEditMenu`.

**Tech Stack:** C# 9, Unity 6000.0.85f1, Seb.Vis UI, NUnit.

**Spec:** `docs/superpowers/plans/2026-10-23-study-tools-roadmap.md` (feature #2).

## Decisions (planner's calls)
- **Line syntax:** `<assignments> [-> <expectations>]`. `#` starts a comment, and blank lines are ignored.
  - Assignments and expectations are space-separated `pin=value`.
  - A line with no `->` only sets inputs (setup/clock lines) and is not checked.
- **Pin references:** a pin name (case-insensitive, must be unique on that side) or a position: `in1..inN` / `out1..outN`, 1-based, top to bottom.
- **Values:**
  - decimal; negative decimal (two's complement at the pin width); `0b1010` (underscores allowed); `0xFF`;
  - in expectations only: `x` (don't care) and `z` (expect floating).
  - A value too wide for the pin is a parse error.
- **Running:** inputs start at 0 and keep their last value between lines. For each line: set the given inputs, then `StepUntilStable(stableSteps: 3, maxSteps: 1000)`. -1 means FAIL "outputs did not settle (oscillation?)". Then compare the expectations.
- **Chips containing CLOCK, KEY, RTC or PULSE:** still run, with the warning "contains CLOCK: its output depends on time; drive clocks from an input pin for repeatable tests".
- **Storage:** `<project folder>/Tests/<chip name>.txt`. It is saved when the menu closes and when RUN is pressed. Chips that were never saved can't have tests ("Save the chip first").
- **FILL FROM CURRENT:** only for combinational chips with ≤ 8 input bits. It replaces the lines with one per truth-table row, using the chip's current outputs (to lock in known-good behaviour).

## Global Constraints
See the roadmap. Branch: `feature/study-tools-test-bench`. Requires Plan 1 to be merged.

## Review Focus
- **Two pins with the same name** (`IN`, `IN`): naming `IN` is an error that says "ambiguous; use in1/in2". Test `AmbiguousName_IsError`.
- **Value wider than the pin** (`A=16` on a 4-bit pin): parse error, not silent truncation. Test `ValueTooWide_IsError`.
- **Lowercase/uppercase pin names and hex digits:** accepted. Test `CaseInsensitive`.
- **Partially floating multi-bit output against a numeric expectation:** FAIL showing `Z` bits in binary. Test `FloatingBits_FailNumericExpectation`.
- **Renaming a pin after tests were written:** lines report "unknown pin 'A'" and are not silently skipped. Covered by the unknown-pin test.

---

### Task 1: Test line parsing, checking and writing (core)

**Files:**
- Create: `Assets/Scripts/Game/Logic/Core/TestVector.cs` (types and parser)
- Create: `Assets/Scripts/Game/Logic/Core/TestVectorChecker.cs`
- Test: `Tools/LayoutTests/TestVectorTests.cs`

**Interfaces:**
- Consumes: `TruthPin`, `TruthTable` (Plan 1); `PinValueFormat`, `NumberFormat` (Plan 4).
- Produces (namespace `DLS.Game.Logic`):
  - `enum ExpectKind { Value, Floating }` (don't-care entries are simply omitted).
  - `sealed class TestVector`:
    - `int LineIndex`;
    - `List<(int PinIndex, uint Value)> Inputs`;
    - `List<(int PinIndex, ExpectKind Kind, uint Value)> Expectations`;
    - `bool HasCheck`.
  - `static class TestVectorParser`: `bool TryParse(string line, int lineIndex, IReadOnlyList<TruthPin> inputs, IReadOnlyList<TruthPin> outputs, out TestVector vector, out string error)`. A blank or comment line returns true with `vector == null`.
  - `static class TestVectorChecker`: `string Check(TestVector vector, uint[] outputStates, IReadOnlyList<TruthPin> outputs)` returns null on pass, or e.g. `"SUM=7 (expected 8)"`. Multiple mismatches are joined by `", "`. Actual values are formatted in decimal, or as `"0b" + PinValueFormat.FormatValue(state, bits, NumberFormat.Binary)` when any bit is floating.
  - `static class TestVectorWriter`: `string FromTruthTableRow(TruthTable table, int row)` returns e.g. `"A=0 B=1 -> S=1 C=0"`, using `in1`/`out1` positions for pins whose names are ambiguous or contain spaces.

- [ ] **Step 1: Write failing tests**
```csharp
static readonly TruthPin[] Ins = { new("A", 4), new("B", 4) };
static readonly TruthPin[] Outs = { new("SUM", 5) };
[Test] public void ParsesAssignmentsAndExpectations()
{
	Assert.That(TestVectorParser.TryParse("A=5 B=0x3 -> SUM=8", 0, Ins, Outs, out var v, out _), Is.True);
	Assert.That(v.Inputs, Is.EqualTo(new[] { (0, 5u), (1, 3u) }));
	Assert.That(v.Expectations[0], Is.EqualTo((0, ExpectKind.Value, 8u)));
}
[Test] public void CommentAndBlank_ReturnNull()
{
	Assert.That(TestVectorParser.TryParse("  # setup", 0, Ins, Outs, out var v, out _), Is.True);
	Assert.That(v, Is.Null);
}
[Test] public void SetupLine_HasNoCheck()
{
	TestVectorParser.TryParse("A=1", 0, Ins, Outs, out var v, out _);
	Assert.That(v.HasCheck, Is.False);
}
[Test] public void NegativeAndBinaryValues()
{
	TestVectorParser.TryParse("A=-1 B=0b0_1_1", 0, Ins, Outs, out var v, out _);
	Assert.That(v.Inputs, Is.EqualTo(new[] { (0, 15u), (1, 3u) }));
}
[Test] public void ValueTooWide_IsError()
{
	Assert.That(TestVectorParser.TryParse("A=16", 0, Ins, Outs, out _, out string e), Is.False);
	Assert.That(e, Does.Contain("A"));
}
[Test] public void UnknownPin_IsError() => Assert.That(TestVectorParser.TryParse("Q=1", 0, Ins, Outs, out _, out _), Is.False);
[Test] public void AmbiguousName_IsError()
{
	TruthPin[] dup = { new("IN", 1), new("IN", 1) };
	Assert.That(TestVectorParser.TryParse("IN=1", 0, dup, Outs, out _, out string e), Is.False);
	Assert.That(e, Does.Contain("in1"));
	Assert.That(TestVectorParser.TryParse("in2=1", 0, dup, Outs, out var v, out _), Is.True);
	Assert.That(v.Inputs[0], Is.EqualTo((1, 1u)));
}
[Test] public void CaseInsensitive() => Assert.That(TestVectorParser.TryParse("a=0XF -> sum=x", 0, Ins, Outs, out _, out _), Is.True);
[Test] public void Check_PassAndFail()
{
	TestVectorParser.TryParse("A=5 B=3 -> SUM=8", 0, Ins, Outs, out var v, out _);
	Assert.That(TestVectorChecker.Check(v, new[] { 8u }, Outs), Is.Null);
	Assert.That(TestVectorChecker.Check(v, new[] { 7u }, Outs), Is.EqualTo("SUM=7 (expected 8)"));
}
[Test] public void FloatingBits_FailNumericExpectation()
{
	TestVectorParser.TryParse("-> SUM=8", 0, Ins, Outs, out var v, out _);
	Assert.That(TestVectorChecker.Check(v, new[] { 8u | (1u << 16) }, Outs), Is.EqualTo("SUM=0b0100Z (expected 8)"));
}
[Test] public void ExpectFloating()
{
	TestVectorParser.TryParse("-> SUM=z", 0, Ins, Outs, out var v, out _);
	Assert.That(TestVectorChecker.Check(v, new[] { 0x1Fu << 16 }, Outs), Is.Null);
}
```
- [ ] **Step 2: Run `dotnet test Tools/LayoutTests`.** Expected: FAIL.
- [ ] **Step 3: Implement.** `TestVectorWriter` gets its own test, `Writer_UsesNamesOrPositions`: the half-adder row 3 gives `"A=1 B=1 -> S=0 C=1"`; duplicate names give `"in1=1 in2=1 -> out1=0"`.
- [ ] **Step 4: Run tests.** Expected: PASS.
- [ ] **Step 5: Commit** (`Add test bench line parser and checker`).

### Task 2: Test bench job, storage and menu

**Files:**
- Create: `Assets/Scripts/Game/Logic/TestBenchJob.cs`
- Create: `Assets/Scripts/Game/Logic/TestBenchStore.cs`
- Create: `Assets/Scripts/Graphics/UI/Menus/TestBenchMenu.cs`
- Modify: `Assets/Scripts/SaveSystem/SavePaths.cs`: `GetTestsPath(string projectName) => Path.Combine(GetProjectPath(projectName), "Tests")`.
- Modify: `UIDrawer.cs`: `MenuType.TestBench` plus draw/opened branches.
- Modify: `ContextMenu.cs`: `TEST BENCH` entry on `entries_customSubchip`, next to TRUTH TABLE.
- Modify: `BottomBarUI.cs`: `$"TEST BENCH   {shortcutTextCol}Ctrl+B"`.
- Modify: `KeyboardShortcuts.cs`: `TestBenchShortcutTriggered => CtrlShortcutTriggered(KeyCode.B)`.

**Interfaces:**
- Consumes: `EvaluationJob`, `SimEvaluator`, `Simulator.BuildSimChip`, `TruthTableJob` (for FILL FROM CURRENT).
- Produces:
  - `TestBenchStore`: `string[] Load(string projectName, string chipName)` (empty array if no file) and `void Save(string projectName, string chipName, IReadOnlyList<string> lines)` (writes `\n`-joined text and creates the folder).
  - `TestBenchJob(ChipDescription chip, ChipLibrary library, IReadOnlyList<string> lines) : EvaluationJob`:
    - `string[] LineResults` (null = no check, `"PASS"`, `"FAIL: …"` or `"ERROR: …"` per line);
    - `int Passed`, `int Failed`, `string Warning`.
    - Parse errors are reported per line, and the remaining lines still run.
  - `TestBenchMenu.Open(ChipDescription chip)`.

- [ ] **Step 1: Implement the job, store and menu.**
  - Menu layout:
    - One `UIHandle("TestBench_Line", i)` input field per line (validation: ≤ 120 chars), in a scroll view with off-screen skipping as in `RomEditMenu`.
    - A status cell to the right of each line: green PASS, red FAIL/ERROR text, truncated with the full text shown in the footer when the line is focused.
    - Enter adds a line below and focuses it; Up/Down move between lines.
    - A summary line "N passed, M failed" plus any warning.
  - Buttons: RUN, ADD LINE, DELETE LINE, COPY ALL, PASTE ALL (splits the clipboard on newlines), FILL FROM CURRENT (asks for confirmation if there are existing lines), CLOSE.
  - A new test file starts with the comment `# A=1 B=0 -> Y=1   (inputs -> expected outputs; lines without -> only set inputs)`.
- [ ] **Step 2: Run `Tools/verify-compile.sh`.** Expected: `0 Error(s)`.
- [ ] **Step 3: Manual check (TestData project):**
  - **XOR:** `in1=0 in2=1 -> OUT=1` PASS; `-> OUT=0` FAIL "OUT=1 (expected 0)".
  - **D-LATCH or another sequential chip:** a toggle sequence through an input clock behaves as expected.
  - **FILL FROM CURRENT on AND:** 4 lines, all PASS on RUN.
  - **Persistence:** after closing and reopening, the lines are still there and `Tests/AND.txt` exists.
- [ ] **Step 4: Commit** (`Add test bench for chips`).

### Task 3: Ship
- [ ] Add a "Test bench" bullet under `## Study tools` in `CHANGELOG.md`, including the syntax summary.
- [ ] Run the tests and compile check; commit; merge into `main`; `git push origin main`; `Tools/install-app.sh`.
