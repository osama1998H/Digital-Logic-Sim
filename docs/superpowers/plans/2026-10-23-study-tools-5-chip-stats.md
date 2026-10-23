# Plan 5: Chip Statistics

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** For any chip, show its total NAND count (all nesting levels), its longest gate path (critical path in NAND delays), and a breakdown of the chips it uses. This lets designs such as ripple-carry and carry-lookahead adders be compared with numbers.

**Architecture:** An engine-free calculator works on a small chip graph model (`StatsChipDef`) and memoises results per chip name. A Unity adapter converts `ChipDescription`s from the `ChipLibrary` (plus the unsaved description of the chip being edited). `ChipStatsMenu` shows the results. No simulation is involved.

**Tech Stack:** C# 9, Unity 6000.0.85f1, NUnit.

**Spec:** `docs/superpowers/plans/2026-10-23-study-tools-roadmap.md` (feature #4).

## Decisions (planner's calls)
- **Gate delays:**

  | Category | Chip types | NAND count | Delay |
  |---|---|---|---|
  | NAND | `ChipType.Nand` | 1 | 1 from each input to the output |
  | Buffer | `TriStateBuffer` | 0 | 1 |
  | Pass-through | Merge/split, bus origin/terminus, IN/OUT pin types | 0 | 0 from every input to every output |
  | Opaque | CLOCK, PULSE, KEY, ROM, RAM (both), displays, BUZZER, RTC | 0 | none: inputs are sinks, outputs are sources |

  Opaque parts are listed separately as "Other parts: 2 × CLOCK, 1 × ROM 256×16".
- **Longest path:** the maximum delay from any of the chip's input pins to any of its output pins. With no input→output path it is 0.
- **Feedback loops** (latches, flip-flops, anywhere inside): the longest path is shown as "not defined: contains a feedback loop (memory)". The NAND count is still shown.
- **Breakdown:** every chip type used at any depth (excluding NAND and pass-through types), with the total number of instances and the NANDs per instance, sorted by total NANDs (descending), then name.
- **Entry points:**
  - Right-click a subchip → STATISTICS (its library description).
  - Bottom-bar "STATISTICS" (no shortcut) for the chip being edited, using `DescriptionCreator.CreateChipDescription(ViewedChip)` so unsaved changes count.

## Global Constraints
See the roadmap. Branch: `feature/study-tools-stats`. New Core folder: `Assets/Scripts/Game/Stats/Core/`, with a `<Compile Include>` line.

## Review Focus
- **The same chip name with different case in references** (`and` vs `AND`): memoised once. Test `NameLookupIsCaseInsensitive`.
- **Deep nesting** (e.g. CPU → ALU → adders, 6+ levels): computed with memoisation and no recursion explosion. Test `DeepNesting_IsMemoised`, which counts library `Get` calls.
- **A chip whose library description is missing** (deleted subchip type): counted as opaque under the name "missing: X", without throwing. Test `MissingChip_IsOpaque`.
- **Wire from an output pin straight to an output pin, or input to output:** delay 0 is handled. Test `InputWiredToOutput_DepthZero`.
- **A feedback loop deep inside a subchip:** the parent reports feedback too. Test `FeedbackInsideSubChip_PropagatesToParent`.

---

### Task 1: Statistics calculator (core)

**Files:**
- Create: `Assets/Scripts/Game/Stats/Core/ChipStats.cs`
- Modify: `Tools/LayoutTests/LayoutTests.csproj`
- Test: `Tools/LayoutTests/ChipStatsTests.cs`

**Interfaces:**
- Produces (namespace `DLS.Game.Stats`):
  - `enum StatsKind { Custom, Nand, Buffer, PassThrough, Opaque }`.
  - `readonly struct StatsPin(int ownerId, int pinId)`. A chip's own pins use `(pinId, 0)`, matching the game's dev-pin addressing; subchip pins use `(subChipId, pinId)`.
  - `sealed class StatsChipDef`:
    - `string Name; StatsKind Kind; int[] InputPinIds; int[] OutputPinIds;`
    - `(int Id, string ChipName)[] SubChips;`
    - `(StatsPin From, StatsPin To)[] Wires;`
  - `interface IStatsLibrary { StatsChipDef Get(string name); }` returns null if missing.
  - `sealed class ChipStatsResult`:
    - `long TotalNands; int? LongestPath; bool HasFeedback;`
    - `List<(string Name, long Count, long NandsEach)> Breakdown;`
    - `List<(string Name, long Count)> OtherParts;`
  - `static class ChipStatsCalculator`: `ChipStatsResult Calculate(StatsChipDef root, IStatsLibrary library)`. The root is passed directly so unsaved chips work.
  - Per chip, `delay[input i][output j]` is computed by longest path over a DAG of pins, using topological order with an on-stack/visited cycle check. It is memoised per name with `StringComparer.OrdinalIgnoreCase`.

- [ ] **Step 1: Write failing tests.** Use a test helper `TestLib : IStatsLibrary` that builds chips from compact specs. NAND is a `Nand` def with inputs `{0,1}` and output `{2}`. Tests:
  - `Nand_IsOneGateDepthOne`
  - `And_FromNandAndNot`: AND = NAND → NOT(NAND), so 2 NANDs and depth 2.
  - `Xor_FourNands_DepthThree`
  - `ParallelGates_DepthIsMax`: three ANDs side by side gives 6 NANDs, depth 2.
  - `RippleChain_DepthAdds`: two ANDs in series gives depth 4.
  - `PassThroughSplitMerge_AddsZero`
  - `SrLatch_HasFeedback`: two cross-coupled NANDs give `HasFeedback == true`, `LongestPath == null`, `TotalNands == 2`.
  - `FeedbackInsideSubChip_PropagatesToParent`
  - `OpaqueParts_ListedNotCounted`: a CLOCK gives `OtherParts == [("CLOCK", 1)]`, 0 NANDs.
  - `Breakdown_SortedByTotalNands`
  - `NameLookupIsCaseInsensitive`
  - `DeepNesting_IsMemoised`: 8 levels, each with 2 of the previous. `TotalNands == 2^8` and each name's `Get` is called at most once.
  - `MissingChip_IsOpaque`
  - `InputWiredToOutput_DepthZero`

  Assertions use exact numbers, e.g. `Assert.That(r.TotalNands, Is.EqualTo(4)); Assert.That(r.LongestPath, Is.EqualTo(3));`.
- [ ] **Step 2: Run tests.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run tests.** Expected: PASS.
- [ ] **Step 5: Commit** (`Add chip statistics calculator`).

### Task 2: Adapter and menu

**Files:**
- Create: `Assets/Scripts/Game/Stats/ChipStatsAdapter.cs`
- Create: `Assets/Scripts/Graphics/UI/Menus/ChipStatsMenu.cs`
- Modify: `UIDrawer.cs` (`MenuType.ChipStats`), `ContextMenu.cs` (`STATISTICS` on `entries_customSubchip` and `entries_builtinSubchip`), `BottomBarUI.cs` (`"STATISTICS"`).

**Interfaces:**
- Consumes: `ChipStatsCalculator`.
- Produces:
  - `ChipStatsAdapter.ToDef(ChipDescription desc) -> StatsChipDef` maps the `ChipType` according to the Decisions table.
  - `ChipStatsAdapter.Library(ChipLibrary lib) : IStatsLibrary`.
  - `ChipStatsMenu.Open(ChipDescription chip)`.
- [ ] **Step 1: Implement.** The menu shows:
  - the title;
  - "NAND gates: 1,234" (thousands separator);
  - "Longest path: 12 gate delays", or the feedback message;
  - a scrolling breakdown table (name | count | NANDs each | NANDs total);
  - an "Other parts" line;
  - COPY (copies the panel as text) and CLOSE buttons.
- [ ] **Step 2: Run `Tools/verify-compile.sh`.** Expected: `0 Error(s)`.
- [ ] **Step 3: Manual check (TestData project):**
  - XOR → 6 NANDs (NAND 1 + OR 3 + AND 2).
  - AND-8 → 16 NANDs, longest path 2.
  - A latch chip → the feedback message.
- [ ] **Step 4: Commit** (`Add chip statistics panel`).

### Task 3: Ship
- [ ] Add a "Chip statistics" bullet under `## Study tools` in `CHANGELOG.md`.
- [ ] Run the tests and compile check; merge into `main`; `git push origin main`; `Tools/install-app.sh`.
