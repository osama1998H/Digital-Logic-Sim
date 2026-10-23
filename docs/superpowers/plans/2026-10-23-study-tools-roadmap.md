# Study Tools Roadmap (9 features)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement each plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Digital Logic Sim useful for coursework and deeper simulation work: check circuits automatically, see signals over time, measure designs, document them, and generate circuits from expressions.

**Architecture:** Nine features, split into ten plans that each ship working software on their own. Pure logic (parsing, minimisation, formatting, graph analysis, layout maths) lives in engine-free `Core` folders that compile under `dotnet test`. Unity-side adapters and menus sit next to them. Anything that steps a simulated chip runs as a job on the simulation thread (Plan 1).

**Tech Stack:** Unity 6000.0.85f1, C# 9, Seb.Vis UI/Draw, Newtonsoft JSON, NUnit 4.2.2 (`Tools/LayoutTests`).

**Spec:** No separate spec. The user skipped the questions ("skip the questions and plan all nine"). Feature intent comes from the assistant's suggestion list in the session, and every default below is the planner's call. Plans 1 to 10 each start with their own "Decisions" block.

## Plans and order

| # | Plan file | Feature(s) | Depends on |
|---|---|---|---|
| 0 | `2026-10-23-study-tools-0-tooling.md` | Build / compile / meta / install scripts | none |
| 1 | `2026-10-23-study-tools-1-evaluator-truth-table.md` | Simulation-thread evaluation jobs; **Truth table** (#1); **Truth table → simplified expression** (#7b) | 0 |
| 2 | `2026-10-23-study-tools-2-test-bench.md` | **Test bench** (#2) | 1 |
| 3 | `2026-10-23-study-tools-3-expression-to-circuit.md` | **Boolean expression → circuit** (#7a) | 1 (gate verification) |
| 4 | `2026-10-23-study-tools-4-wire-hover.md` | **Wire/pin value on hover** (#9) | 0 |
| 5 | `2026-10-23-study-tools-5-chip-stats.md` | **Chip statistics** (#4) | 0 |
| 6 | `2026-10-23-study-tools-6-timing-diagram.md` | **Timing diagram / probes** (#3) | 4 (value formatting) |
| 7 | `2026-10-23-study-tools-7-ram-chip.md` | **RAM chip** (#8) | 0 |
| 8 | `2026-10-23-study-tools-8-notes.md` | **Canvas text notes** (#5) | 0 |
| 9 | `2026-10-23-study-tools-9-export-image.md` | **Export circuit image** (#6) | 0 |

**Recommended execution order:** 0, 4, 5, 1, 2, 3, 6, 7, 8, 9. Small, independent wins come first. The evaluator comes before everything that uses it. Notes and export come last because they touch the most shared code.

## Global Constraints (apply to every plan)

- **Branching:** each plan runs on its own branch `feature/<plan-slug>` created from `main`. When a plan is finished, fast-forward or merge it into local `main` and run `git push origin main`. **Never open a pull request** (user instruction).
- **Fork:** pushes go only to `origin` = `https://github.com/osama1998H/Digital-Logic-Sim.git`. Never push to or open anything on `SebLague/Digital-Logic-Sim`.
- **Changelog:** every plan's final task adds its entry to `CHANGELOG.md` under the matching section ("Editor tools", "Fixes", "Development", or a new "Study tools" section).
- **App install:** every plan's final task runs `Tools/install-app.sh` (Plan 0). It builds the macOS app and replaces `/Applications/Digital-Logic-Sim.app`, keeping one backup. The user runs the app from `/Applications`.
- **C# version:** code under any `Core` folder must compile as **C# 9** with **no `using UnityEngine`** and no references to `DLS.Game`/`DLS.Graphics`/`DLS.Description`/`DLS.Simulation` types. Each new Core folder gets a `<Compile Include>` line in `Tools/LayoutTests/LayoutTests.csproj`.
- **Determinism:** never iterate a `Dictionary`/`HashSet` to decide an order; sort by integer key or ordinal string and break ties by key.
- **Threads:** never step a `SimChip` outside the simulation thread. Use evaluation jobs (Plan 1). Main-thread reads of `SimPin.State` / `InternalState` are allowed only for display. The live display already does this.
- **Wire replacement** must never call `DevChipInstance.DeleteWire` (it cascades). Use `ReplaceAllWires` / `ReplaceSubChips`.
- **Code style:** tabs; block namespaces (no file-scoped); `PascalCase` public members; sparse comments explaining *why*; match neighbouring code.
- **Unity metas:** every new `.cs` file and folder under `Assets/` gets a `.meta` (run `Tools/make-metas.sh`, Plan 0) committed with it.
- **Verification per task:** `dotnet test Tools/LayoutTests` (all pass) and `Tools/verify-compile.sh` (prints `0 Error(s)`). Final task of each plan: `Tools/install-app.sh` prints `MacBuild: Succeeded (0 errors)`.
- **Shortcuts:** Ctrl (not Cmd) like existing shortcuts. Unmodified letters/digits are reserved for Key chips. New shortcuts used across plans: `Ctrl+Shift+Y` truth table, `Ctrl+B` test bench, `Ctrl+Shift+W` timing diagram, `Ctrl+E` export image, `Ctrl+Shift+N` add note, `Ctrl+Shift+G` expression → circuit. All are currently unused (`Assets/Scripts/Game/Interaction/KeyboardShortcuts.cs`).
- **Bottom-bar menu:** new commands are added to `BottomBarUI.menuButtonNames` with their shortcut hint, in the existing `NAME         {shortcutTextCol}Ctrl+X` format, before `LIBRARY`.
- **Commits** end with `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`.

## Shared vocabulary (types several plans use)

- `NumberFormat { Binary, Decimal, Hex, Signed }`, `PinValueFormat` (Plan 4, `Assets/Scripts/Game/Inspect/Core/PinValueFormat.cs`). These are used by the hover tooltip, truth table, test bench and timing diagram. **Plan 1 also uses them, so if Plan 1 runs before Plan 4, Plan 1's Task 1 creates this file exactly as specified in Plan 4 Task 1, and Plan 4 skips that task.**
- `EvaluationJob` (abstract), `Simulator.EnqueueEvaluationJob`, `Simulator.RunEvaluationJobs`, `SimEvaluator` (Plan 1, `Assets/Scripts/Simulation/`).
- `SavePaths.GetExportsPath`, `Main.OpenFolderInFileBrowser` (Plan 1; Plan 9 adds them if Plan 1 hasn't run).
- `TruthTable`, `TruthPin` (Plan 1, `Assets/Scripts/Game/Logic/Core/`).
- `BoolExpr` AST and `ExprParser` (Plan 3, same folder); `SopFormatter` (Plan 1) outputs text that `ExprParser` accepts.
