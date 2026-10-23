# Plan 3: Boolean Expression → Circuit

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The user types one or more equations (e.g. `S = A ^ B; C = A*B`). The app creates input pins, gates and output pins, wires them, lays them out, and records it all as one undo step.

**Architecture:** An engine-free parser produces a `BoolExpr` tree. An engine-free `NetlistBuilder` turns the trees into a shared gate netlist, using only the gate kinds that are available (anything missing is rewritten into NAND). A Unity-side `CircuitGenerator` maps the netlist to library chips, adds the elements and wires, then runs the existing placer and router pieces (`ChipPlacer`, `WireRouter`, `LayoutApplier`) before recording `RecordAddElements`.

**Tech Stack:** C# 9, Unity 6000.0.85f1, existing `Game/Layout/Core`, NUnit.

**Spec:** `docs/superpowers/plans/2026-10-23-study-tools-roadmap.md` (feature #7a).

## Decisions (planner's calls)
- **Syntax:**
  - Equations are separated by `;` or new lines. Each is `Name = expr`, or just `expr` (the output is named `Y`, then `Y2`, `Y3`…).
  - Identifiers match `[A-Za-z_][A-Za-z0-9_]*`. Constants are `0` and `1`.
  - Operators, from highest to lowest precedence:
    1. NOT: prefix `!` or `~`, or postfix `'`
    2. AND: `*`, `&`, `.`, `·`
    3. XOR: `^`
    4. OR: `+`, `|`
  - Parentheses group terms. There is no implicit AND (`AB` is the variable named `AB`). The error message hints: "use A*B for AND".
  - This accepts every string produced by Plan 1's `SopFormatter`.
- **Input pins:** one per distinct variable, sorted alphabetically (ordinal, ignoring case), with A at the top. Output pins are in equation order, top to bottom. All are 1-bit.
- **Limits:** ≤ 16 inputs, ≤ 8 outputs, ≤ 300 gates after expansion. Beyond these, show an error and don't generate.
- **Gates:**
  - The wheel offers "Use my gates" (default) or "NAND only".
  - "Use my gates" looks for library chips named exactly `NOT`, `AND`, `OR` and `XOR` (case-insensitive). Each must have the right shape (NOT: 1-in/1-out; others: 2-in/1-out, all 1-bit) and its truth table must match. This is checked with a `TruthTableJob` (Plan 1) before generating.
  - A gate kind that is missing or fails the check is built from the builtin `NAND`.
- **Target chip:** if the chip being edited is empty, generate into it. Otherwise first create a new blank chip, going through the same unsaved-changes prompt as NEW CHIP (`BottomBarUI.CreateNewChip`).
- **Sharing:** identical sub-expressions become one gate (structural hashing on `(kind, sorted input nets)`).
- **Constants:**
  - `1` is built as `NAND(x, NOT x)` from the first input; `0` as `NOT` of that.
  - An equation set with no variables at all is an error: "needs at least one input".

## Global Constraints
See the roadmap. Branch: `feature/study-tools-expr-circuit`. Requires Plan 1 to be merged.

## Review Focus
- **Same variable in different case** (`a` and `A`): one pin, with the name as first written. Test `VariablesAreCaseInsensitive`.
- **Typing errors** (`A*`, `(A+B`, `A B`): a clear message with the character position, and nothing generated. Tests `Parse_ReportsErrorPosition_*`.
- **XOR built from NAND only:** must still compute XOR. Test `NandOnly_PreservesTruthTable` checks every expression against its netlist for all input combinations.
- **Duplicate output names** (`Y = A; Y = B`): error "output Y defined twice". Test `DuplicateOutput_IsError`.
- **Output equal to an input** (`Y = A`): the output pin is wired straight to the input pin, with no gate. Test `IdentityOutput_HasNoGates`.

---

### Task 1: Expression parser

**Files:**
- Create: `Assets/Scripts/Game/Logic/Core/BoolExpr.cs`
- Create: `Assets/Scripts/Game/Logic/Core/ExprParser.cs`
- Test: `Tools/LayoutTests/ExprParserTests.cs`

**Interfaces:**
- Produces (namespace `DLS.Game.Logic`):
  - `abstract class BoolExpr` with subclasses `VarExpr(string Name)`, `ConstExpr(bool Value)`, `NotExpr(BoolExpr Operand)`, `AndExpr(List<BoolExpr> Terms)`, `OrExpr(List<BoolExpr> Terms)`, `XorExpr(List<BoolExpr> Terms)`. Members:
    - `bool Evaluate(IReadOnlyDictionary<string, bool> values)` (keys case-insensitive);
    - `void CollectVariables(ISet<string> into)`.
  - `sealed class Equation { string Output; BoolExpr Expr; }`.
  - `sealed class ParseResult { List<Equation> Equations; List<string> Inputs /*sorted*/; string Error; int ErrorIndex; bool Success => Error == null; }`.
  - `static class ExprParser`: `ParseResult Parse(string text)`.

- [ ] **Step 1: Write failing tests**
```csharp
static bool Eval(string expr, string assignment) // assignment like "A=1,B=0"
{
	var r = ExprParser.Parse(expr);
	var d = assignment.Split(',').Select(p => p.Split('=')).ToDictionary(p => p[0], p => p[1] == "1", StringComparer.OrdinalIgnoreCase);
	return r.Equations[0].Expr.Evaluate(d);
}
[Test] public void Precedence_NotAndXorOr() { Assert.That(Eval("A + B*C", "A=0,B=1,C=0"), Is.False); Assert.That(Eval("A ^ B*C", "A=1,B=1,C=1"), Is.False); }
[Test] public void PostfixNot() => Assert.That(Eval("A'*B", "A=0,B=1"), Is.True);
[Test] public void AlternativeOperators() => Assert.That(Eval("!(a & b) | 0", "A=1,B=1"), Is.False);
[Test] public void ParsesSopFormatterOutput() => Assert.That(ExprParser.Parse("Y = A'*B + A*B'").Success, Is.True);
[Test] public void MultipleEquations_AndDefaultNames()
{
	var r = ExprParser.Parse("S = A ^ B; A*B");
	Assert.That(r.Equations.Select(e => e.Output), Is.EqualTo(new[] { "S", "Y" }));
	Assert.That(r.Inputs, Is.EqualTo(new[] { "A", "B" }));
}
[Test] public void VariablesAreCaseInsensitive() => Assert.That(ExprParser.Parse("Y = a + A").Inputs, Is.EqualTo(new[] { "a" }));
[Test] public void Parse_ReportsErrorPosition_TrailingOperator() { var r = ExprParser.Parse("Y = A*"); Assert.That(r.Success, Is.False); Assert.That(r.ErrorIndex, Is.EqualTo(6)); }
[Test] public void Parse_ReportsErrorPosition_Unclosed() => Assert.That(ExprParser.Parse("(A+B").Success, Is.False);
[Test] public void Parse_ImplicitAnd_IsErrorWithHint() { var r = ExprParser.Parse("A B"); Assert.That(r.Error, Does.Contain("A*B")); }
[Test] public void DuplicateOutput_IsError() => Assert.That(ExprParser.Parse("Y=A; Y=B").Error, Does.Contain("Y"));
[Test] public void NoVariables_IsError() => Assert.That(ExprParser.Parse("Y = 1").Success, Is.False);
```
- [ ] **Step 2: Run tests.** Expected: FAIL.
- [ ] **Step 3: Implement** a recursive-descent parser over a tokenizer that records each token's start index. `ErrorIndex` is the index of the offending token, or the text length at end of input.
- [ ] **Step 4: Run tests.** Expected: PASS.
- [ ] **Step 5: Commit** (`Add boolean expression parser`).

### Task 2: Netlist builder

**Files:**
- Create: `Assets/Scripts/Game/Logic/Core/Netlist.cs`
- Test: `Tools/LayoutTests/NetlistTests.cs`

**Interfaces:**
- Consumes: `ParseResult`, `BoolExpr`.
- Produces:
  - `enum GateKind { Not, And, Or, Xor, Nand }`. `[Flags] enum GateSet { None = 0, Not = 1, And = 2, Or = 4, Xor = 8 }` (NAND is always available).
  - `sealed class Gate { GateKind Kind; int[] InputNets; int OutputNet; }`.
  - `sealed class Netlist`:
    - `string[] InputNames` (nets `0..n-1`), `string[] OutputNames`, `int[] OutputNets`, `List<Gate> Gates` (in topological order);
    - `bool[] Evaluate(bool[] inputs)`.
  - `static class NetlistBuilder`: `Netlist Build(ParseResult parsed, GateSet available)`.
    - n-ary AND/OR/XOR become left-to-right chains of 2-input gates.
    - Missing kinds are rewritten into NAND: `Not a = Nand(a,a)`; `And = Not(Nand)`; `Or(a,b) = Nand(Not a, Not b)`; `Xor(a,b)`: `t = Nand(a,b)`, `Nand(Nand(a,t), Nand(b,t))`.
    - Gates are shared through hashing on `(kind, sorted inputs)`.

- [ ] **Step 1: Write failing tests**
```csharp
[TestCase("Y = A ^ B")] [TestCase("Y = A'*B + A*C'")] [TestCase("Y = !(A+B+C) ^ D")] [TestCase("S = A ^ B ^ C; C = A*B + C*(A ^ B)")] [TestCase("Y = 1; Z = A*0")]
public void NandOnly_PreservesTruthTable(string text) => AssertEquivalent(text, GateSet.None);
[TestCase("S = A ^ B ^ C; C = A*B + C*(A ^ B)")]
public void AllGates_PreservesTruthTable(string text) => AssertEquivalent(text, GateSet.Not | GateSet.And | GateSet.Or | GateSet.Xor);
static void AssertEquivalent(string text, GateSet gates)
{
	var p = ExprParser.Parse(text); var n = NetlistBuilder.Build(p, gates);
	int k = p.Inputs.Count;
	for (int row = 0; row < 1 << k; row++)
	{
		bool[] ins = Enumerable.Range(0, k).Select(i => ((row >> (k - 1 - i)) & 1) == 1).ToArray();
		var d = p.Inputs.Select((name, i) => (name, ins[i])).ToDictionary(t => t.name, t => t.Item2, StringComparer.OrdinalIgnoreCase);
		bool[] outs = n.Evaluate(ins);
		for (int o = 0; o < p.Equations.Count; o++) Assert.That(outs[o], Is.EqualTo(p.Equations[o].Expr.Evaluate(d)), $"{text} row {row}");
	}
}
[Test] public void NandOnly_UsesOnlyNand() => Assert.That(NetlistBuilder.Build(ExprParser.Parse("Y=A*B+C"), GateSet.None).Gates.All(g => g.Kind == GateKind.Nand), Is.True);
[Test] public void SharedSubexpressions_AreBuiltOnce() => Assert.That(NetlistBuilder.Build(ExprParser.Parse("X = A*B; Y = A*B + C"), GateSet.And | GateSet.Or).Gates.Count(g => g.Kind == GateKind.And), Is.EqualTo(1));
[Test] public void IdentityOutput_HasNoGates() { var n = NetlistBuilder.Build(ExprParser.Parse("Y = A"), GateSet.None); Assert.That(n.Gates, Is.Empty); Assert.That(n.OutputNets[0], Is.EqualTo(0)); }
```
- [ ] **Step 2: Run tests.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run tests.** Expected: PASS.
- [ ] **Step 5: Commit** (`Add gate netlist builder with NAND fallback`).

### Task 3: Circuit generator and menu

**Files:**
- Create: `Assets/Scripts/Game/Logic/GateLibrary.cs`
- Create: `Assets/Scripts/Game/Logic/CircuitGenerator.cs`
- Create: `Assets/Scripts/Graphics/UI/Menus/ExpressionMenu.cs`
- Modify: `UIDrawer.cs` (`MenuType.Expression`), `BottomBarUI.cs` (`$"FROM EXPRESSION {shortcutTextCol}Ctrl+Shift+G"`), `KeyboardShortcuts.cs` (`ExpressionShortcutTriggered => CtrlShiftShortcutTriggered(KeyCode.G)`).

**Interfaces:**
- Consumes: `NetlistBuilder`, `TruthTableJob`, `IDGenerator.GenerateNewElementID`, `DevChipInstance.AddNewDevPin/AddNewSubChip/AddWire`, `DescriptionCreator.CreateBuiltinSubChipDescriptionForPlacement`, and from `Game/Layout`: `LayoutProblemBuilder.BuildPlacementInput`, `ChipPlacer.Place`, `LayoutProblemBuilder.ToVector`, `LayoutProblemBuilder.BuildRoutingProblem`, `WireRouter.Route`, `LayoutApplier.BuildWireDescriptions`, `DevChipInstance.ReplaceAllWires`, and `UndoController.RecordAddElements(List<IMoveable>, bool hasWires)`.
- Produces:
  - `GateLibrary`:
    - `static GateLibrary.VerifyAsync(ChipLibrary lib)` starts `TruthTableJob`s for the candidate chips and returns a `GateLibrary` instance.
    - Instance members: `bool IsReady`, `GateSet Available`, `string ChipNameFor(GateKind kind)`, and `int[] InputPinIds(GateKind)` / `int OutputPinId(GateKind)`.
    - Builtin NAND: inputs `{0, 1}` (`IN B`, `IN A`), output `2`.
  - `CircuitGenerator.Generate(Netlist netlist, GateLibrary gates, DevChipInstance chip)`:
    - Input pins named by `InputNames`, at x = 0, stacked top-down 0.5 apart starting at y = (n-1)*0.25. Output pins at x = 6, the same way.
    - Subchips at temporary positions.
    - Wires as `WireInstance` pin-to-pin with `new Vector2[2]`.
    - Then placement and routing, then a single `RecordAddElements(allNewElements, hasWires: true)`.
- [ ] **Step 1: Implement GateLibrary, CircuitGenerator and ExpressionMenu.**
  - The menu has a wide single-line input field (≤ 200 chars) and the gate wheel.
  - Live preview text:
    - On success: `Inputs: A, B, C | Outputs: S, C | Gates: 2 XOR, 2 AND, 1 OR`.
    - On error: the error in red with a `^` marker under the column.
  - Buttons GENERATE (disabled until the gate check is ready and the parse succeeded) and CANCEL.
- [ ] **Step 2: Run `Tools/verify-compile.sh`.** Expected: `0 Error(s)`.
- [ ] **Step 3: Manual check:**
  - **Full adder in a new project (NAND only):** `S = A ^ B ^ CIN; COUT = A*B + CIN*(A ^ B)` generates a laid-out circuit. The truth table (Plan 1) matches a full adder. One Ctrl+Z removes everything.
  - **TestData project:** the same input uses AND/OR/XOR chips.
  - **Bad input** (`A B`): shows an error, and GENERATE is disabled.
- [ ] **Step 4: Commit** (`Generate circuits from boolean expressions`).

### Task 4: Ship
- [ ] Add an "Expression to circuit" bullet under `## Study tools` in `CHANGELOG.md`, with the syntax table.
- [ ] Run the tests and compile check; merge into `main`; `git push origin main`; `Tools/install-app.sh`.
