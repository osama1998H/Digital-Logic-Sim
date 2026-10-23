# Organize Layout (Tidy Wires + Organize All) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add one-click commands that turn a messy chip into a clean, circuit-board-style layout:
- **Tidy Wires** (Ctrl+T) re-routes every wire as right-angle traces that avoid chips, keep apart from each other, and join at T-junctions.
- **Organize All** (Ctrl+Shift+T) also arranges the subchips in signal-flow columns, then tidies the wires.

Both are a single Undo step.

**Architecture:**
- An engine-free layout core (plain C#, no `UnityEngine`) solves two abstract problems:
  - orthogonal A* wire routing on a lattice;
  - layered (Sugiyama-style) chip placement.
- The core is unit-tested with `dotnet test`, so it can be built and tested without Unity.
- A thin Unity adapter converts the open `DevChipInstance` into a problem and writes the result back as new `WireDescription`s and positions. It goes through the existing undo and wire-loading machinery and never touches the simulator, because the set of pin-to-pin connections doesn't change.

**Tech Stack:**
- C# 9 (Unity 6000.0 Mono)
- NUnit via a standalone `dotnet` (SDK 9) test project
- Unity 6000.0.85f1 for integration

**Spec:** None. The user asked to skip design questions. The design decisions are recorded here under **Decisions** and are the planner's calls.

## Decisions (planner's calls)

**Goals, in priority order:**
1. **Tidy Wires.** Right-angle routing, wires spaced by their bit width, fan-out as T-junction branches, bus taps re-attached to their bus line.
2. **Organize All.** Columns by signal depth, inputs left and outputs right, then Tidy Wires.
3. **Preference "Organize on save":** Off / Tidy wires / Organize all.

**Non-goals:**
- Organizing only the current selection.
- 45° traces.
- Moving input/output dev pins vertically.
- Running organize in the background.
- Changing how wires are drawn.

**Anti-goals (a failure even if everything else works):**
- Any change to circuit behaviour.
- Reordering the chip's external pins.
- A crash or a lost wire on an unroutable layout.
- A result that differs between two runs on the same chip.

**Fallback:**
- A wire the router can't route keeps its original description.
- If a bus trunk can't be routed, the trunk and all its taps keep their original descriptions.

## Global Constraints

- **Branch:** `feature/organize-layout`, created from `upstream-prs`. Never commit to `main`.
- **C# version:** code under `Assets/Scripts/Game/Layout/Core/` must compile as **C# 9** with **no `using UnityEngine`** and no references to `DLS.Game`/`DLS.Graphics`. The dotnet test project enforces this (`<LangVersion>9.0</LangVersion>`).
- **Determinism:** never iterate a `Dictionary`/`HashSet` to decide an order. Sort connections, nets and nodes by integer key and break ties by key.
- **Geometry constants** are passed to the core in `RouterSettings`, never read from `DrawSettings` inside the core:
  - `GridSize = 0.125`
  - `WireThickness = 0.025`
  - Per-bit wire offset `= WireThickness * 2 * 0.925`, so a wire of `b` bits is `b * 0.04625` wide.
- **Wire replacement** must never call `DevChipInstance.DeleteWire`. It calls `Simulator.RemoveConnection` and cascade-deletes bus taps. Load replacement wires with `AddWire(wire, isLoading: true)`.
- **Code style** follows the repo:
  - tabs
  - file-scoped namespaces are NOT used (block namespaces)
  - `PascalCase` public members
  - comments sparse

## Review Focus

1. **Enclosed or unroutable pin.** Example: a target boxed in by chips. Expected: that one wire keeps its old shape, the others are tidied, and nothing throws. Test: `Route_EnclosedTarget_ReportsFailureWithoutThrowing` (Task 2).
2. **Feedback loops** (D-LATCH, FLIP-FLOP). Expected: the placer terminates, and both chips in the loop get placed without overlap. Test: `Place_Cycle_TerminatesAndPlacesAll` (Task 5).
3. **Bus writers** (BUS BUFFER). Expected: each writer still ends on the bus line itself, never on a reader branch. Test: `Route_BusWriter_AttachesOnlyToTrunk` (Task 3).
4. **Mixed widths.** Example: a 16-bit wire running next to a 1-bit wire. Expected: they are visibly separated. Test: `Route_ParallelNets_RespectWidthClearance` (Task 4).
5. **Degenerate chips.** Examples: a chip with no wires, or subchips with no connections. Expected: Organize is a no-op for wires, and unconnected chips are parked in a row below without overlap. Tests: `Route_EmptyProblem_ReturnsEmpty` (Task 2) and `Place_UnconnectedChips_ParkedBelow` (Task 5).

## File Structure

**Engine-free core:** `Assets/Scripts/Game/Layout/Core/`, namespace `DLS.Game.Layout`.

| File | Responsibility |
|---|---|
| `LayoutTypes.cs` | `LayoutPoint`, `LayoutRect`, `LayoutPin`, `LayoutConnection`, `LayoutProblem`, `RouterSettings`, `RoutedConnection`, `LayoutResult`, `AttachKind` |
| `RoutingLattice.cs` | Non-uniform orthogonal lattice: grid lines plus every pin's y; obstacle mask; pin escape nodes |
| `WireRouter.cs` | A* with bend, crossing and clearance costs; nets as trees; bus taps; simplification; improvement pass |
| `ChipPlacer.cs` | `PlaceNode`, `PlaceEdge`, `PlacementInput`; cycle breaking, layering, barycenter ordering, coordinates, snapping |

**Unity adapter:** `Assets/Scripts/Game/Layout/`, namespace `DLS.Game.Layout`.

| File | Responsibility |
|---|---|
| `LayoutProblemBuilder.cs` | `DevChipInstance` → `LayoutProblem` / `PlacementInput`; keeps the key ↔ wire/pin maps |
| `LayoutApplier.cs` | `LayoutResult` → ordered `WireDescription[]`; positions → elements |
| `ChipOrganizer.cs` | Public entry points `TidyWires` / `OrganizeAll`, plus undo recording |

**Modified:**
- `Assets/Scripts/Game/Project/DevChipInstance.cs`: add `ReplaceAllWires`.
- `Assets/Scripts/Game/Interaction/UndoController.cs`: add `RecordOrganize` and `OrganizeUndoAction`.
- `Assets/Scripts/Game/Interaction/KeyboardShortcuts.cs`
- `Assets/Scripts/Graphics/UI/Menus/BottomBarUI.cs`
- `Assets/Scripts/Description/Types/ProjectDescription.cs`
- `Assets/Scripts/Graphics/UI/Menus/PreferencesMenu.cs`
- `Assets/Scripts/Graphics/UI/Menus/ChipSaveMenu.cs`

**Tests:** `Tools/LayoutTests/` (outside `Assets/`, so Unity ignores it).
- `LayoutTests.csproj`
- `RoutingLatticeTests.cs`
- `WireRouterTests.cs`
- `ChipPlacerTests.cs`
- `TestGeometry.cs`: helpers such as `AllAxisAligned` and `SegmentsOverlapCollinear`.

Unity generates `.meta` files for new `Assets/` files the first time the editor opens the project. Commit them in Task 6.

---

### Task 0: Branch and test harness

**Files:**
- Create: `Tools/LayoutTests/LayoutTests.csproj`
- Create: `Tools/LayoutTests/TestGeometry.cs`
- Create: `Assets/Scripts/Game/Layout/Core/LayoutTypes.cs`

**Interfaces (Produces):** all of these are used by later tasks.
```csharp
namespace DLS.Game.Layout
{
	public readonly struct LayoutPoint { public readonly float X, Y; public LayoutPoint(float x, float y); }
	public readonly struct LayoutRect { public readonly int OwnerID; public readonly float MinX, MinY, MaxX, MaxY; public LayoutRect(int ownerID, float minX, float minY, float maxX, float maxY); public bool Contains(LayoutPoint p); }
	public struct LayoutPin { public int Key; public int OwnerID; public LayoutPoint Position; public int ExitDirX; /* +1 or -1 */ public int BitCount; }
	public class LayoutConnection
	{
		public int Key;                 // caller's id (the adapter uses the current wire index)
		public int SourcePinKey;
		public int TargetPinKey;
		public int BitCount;
		public bool IsBusTrunk;         // bus origin -> bus terminus wire
		public int TargetAttachToKey = -1; // bus writer: target end must end on the connection with this key (the trunk)
	}
	public class LayoutProblem { public List<LayoutRect> Obstacles = new(); public Dictionary<int, LayoutPin> Pins = new(); public List<LayoutConnection> Connections = new(); }
	public class RouterSettings
	{
		public float GridSize = 0.125f, WireThickness = 0.025f, MinGap = 0.05f;
		public float BendCost = 3, CrossCost = 2, ClearanceViolationCost = 30;
		public int LatticeMarginCells = 12, WindowMarginCells = 16, ImprovementPasses = 1;
		public float HalfWidth(int bitCount) => bitCount * WireThickness * 2 * 0.925f / 2;
	}
	public enum AttachKind { Pin, Wire }
	public class RoutedConnection
	{
		public int Key; public bool Routed;
		public List<LayoutPoint> Points;  // source end -> target end, corners only, first/last = exact end coordinates
		public AttachKind SourceAttach, TargetAttach;
		public int AttachedToKey = -1;    // connection whose wire this one attaches to (at whichever end is AttachKind.Wire)
		public int AttachedSegmentIndex = -1; // segment i = Points[i]..Points[i+1] of the parent's FINAL Points
	}
	public class LayoutResult { public List<RoutedConnection> Connections = new(); /* parents before children */ public int FailedCount; public float TotalCost; /* sum of A* costs of routed connections */ }
}
```

- [ ] **Step 1: Create the branch.**
  ```bash
  git checkout upstream-prs
  git checkout -b feature/organize-layout
  ```
  Expected: `Switched to a new branch`. `Assets/Editor/MacBuild.cs` may be untracked in the working tree; leave it alone.
- [ ] **Step 2: Write `LayoutTypes.cs`** exactly as in the Interfaces block. Use `using System.Collections.Generic;` and nothing else.
- [ ] **Step 3: Write `LayoutTests.csproj`.** It targets `net9.0` with `<LangVersion>9.0</LangVersion>` and `<Nullable>disable</Nullable>`, and uses the packages `NUnit`, `NUnit3TestAdapter` and `Microsoft.NET.Test.Sdk`. Include `<Compile Include="../../Assets/Scripts/Game/Layout/Core/**/*.cs" />` so the core compiles straight from `Assets/`.
- [ ] **Step 4: Write `TestGeometry.cs`** with these helpers:
  - `static bool AllAxisAligned(List<LayoutPoint> pts)`: every consecutive pair shares X or Y within 1e-4.
  - `static bool SegmentIntersectsRect(LayoutPoint a, LayoutPoint b, LayoutRect r)`
  - `static bool SegmentsOverlapCollinear(LayoutPoint a1, LayoutPoint a2, LayoutPoint b1, LayoutPoint b2, float minGap)`: true if the segments are parallel, closer than `minGap` perpendicular, and overlap in projection.
  - `static bool PointOnSegment(LayoutPoint p, LayoutPoint a, LayoutPoint b)`
- [ ] **Step 5: Add a smoke test** `LayoutPoint_Constructs` that asserts `new LayoutPoint(1,2).X == 1`.
- [ ] **Step 6:** Run `dotnet test Tools/LayoutTests`. Expected: `Passed: 1`.
- [ ] **Step 7: Commit.**
  ```bash
  git add Tools/LayoutTests Assets/Scripts/Game/Layout/Core/LayoutTypes.cs
  git commit -m "Add engine-free layout core types and dotnet test harness"
  ```
  Use the repo's commit trailer (Co-Authored-By line) on every commit in this plan.

### Task 1: Routing lattice

**Files:**
- Create: `Assets/Scripts/Game/Layout/Core/RoutingLattice.cs`
- Test: `Tools/LayoutTests/RoutingLatticeTests.cs`

**Interfaces (Produces):**
```csharp
public class RoutingLattice
{
	public readonly float[] Xs, Ys;              // sorted ascending, deduplicated within 1e-4
	public int Width => Xs.Length; public int Height => Ys.Length;
	public static RoutingLattice Build(LayoutProblem problem, RouterSettings settings);
	public bool IsBlocked(int ix, int iy);       // inside any obstacle, or reserved escape node of another pin (see ReservedPinKey)
	public int ReservedPinKey(int ix, int iy);   // pin key whose escape node this is, else -1
	public int IndexOfX(float x); public int IndexOfY(float y); // exact match within 1e-4, else -1
	public (int ix, int iy) EscapeNode(LayoutPin pin); // first node from the pin along ExitDirX that is >= GridSize from the pin and outside every obstacle
	public LayoutPoint Point(int ix, int iy);
}
```

Lattice rules:
- `Xs` are the multiples of `GridSize` across the bounds of all obstacles and pins, plus `LatticeMarginCells` on each side.
- `Ys` are those grid multiples plus every pin's exact `Position.Y`, so the first segment from any pin is perfectly horizontal.
- A node is blocked if it lies strictly inside an obstacle expanded by `GridSize / 2`.
- Each pin's escape node is reserved for that pin's net.

Tests (write them first, run and see them fail, implement, run and see them pass):
- `Build_AddsOffGridPinRow`: a pin at y = 0.06 → `IndexOfY(0.06f) >= 0`.
- `Build_GridRowsAreGridMultiples`: every `Ys` value apart from pin rows has `y / 0.125` within 1e-4 of an integer.
- `IsBlocked_InsideObstacleTrue_OutsideFalse`: obstacle (0,0)-(1,1). The node nearest (0.5, 0.5) is blocked; the node nearest (2, 2) is not.
- `EscapeNode_IsOutsideOwnObstacleOnExitSide`: a pin at (1.01, 0.5) with `ExitDirX = +1` and owner obstacle (0,0)-(1.01,1). The escape node's X is > 1.01 + 0.0625, and its Y equals 0.5.
- `EscapeNode_ReservedForItsPin`: `ReservedPinKey` at the escape node returns that pin's key.

Commit: `"Add routing lattice for orthogonal wire layout"`.

### Task 2: Single-connection routing

**Files:**
- Create: `Assets/Scripts/Game/Layout/Core/WireRouter.cs`
- Test: `Tools/LayoutTests/WireRouterTests.cs`

**Interfaces (Produces):** `public static class WireRouter { public static LayoutResult Route(LayoutProblem problem, RouterSettings settings); }`

Approach:
- A* state is `(ix, iy, dir)` with `dir ∈ {+x, −x, +y, −y}`.
- Step cost = world distance / `GridSize`.
- `BendCost` applies when `dir` changes.
- The heuristic is Manhattan distance / `GridSize`.
- Blocked nodes are impassable, except the start node and the goal node of the current connection.
- The start is the source pin's escape node with `dir = ExitDirX`.
- The goal is the target pin's escape node. Arriving with a direction other than `−target.ExitDirX` adds `BendCost`.
- The final `Points` are `[sourcePin.Position, sourceEscape, …corners…, targetEscape, targetPin.Position]`, with collinear points removed.
- **Windowed search:** first search inside the bounding box of start and goal expanded by `WindowMarginCells`. If that finds nothing, search the full lattice. If that also fails, set `Routed = false`, increment `FailedCount`, and leave `Points = null`.
- In this task every connection is `AttachKind.Pin` at both ends. Nets and taps come in Task 3.

Tests:
- `Route_EmptyProblem_ReturnsEmpty`: no connections → `Connections.Count == 0` and `FailedCount == 0`.
- `Route_FacingPinsSameRow_IsStraight`: source (0,0) exit +1, target (2,0) exit −1, no obstacles → `Points.Count == 2` and both points have Y == 0.
- `Route_AvoidsObstacle`: same pins with obstacle (0.8,−0.5)-(1.2,0.5) between them → `AllAxisAligned` holds, and no segment `SegmentIntersectsRect` the obstacle.
- `Route_OffsetRows_TwoBends`: source (0,0), target (2,0.5) → exactly 4 points (two bends).
- `Route_EnclosedTarget_ReportsFailureWithoutThrowing`: four obstacles box the target's escape area → `Routed == false` and `FailedCount == 1`.
- `Route_IsDeterministic`: route the same problem twice → identical `Points` sequences.

Commit: `"Add A* orthogonal router for single connections"`.

### Task 3: Nets as trees, and bus taps

**Files:**
- Modify: `Assets/Scripts/Game/Layout/Core/WireRouter.cs`
- Test: `Tools/LayoutTests/WireRouterTests.cs`

**Nets.**
- Group connections by `SourcePinKey`. Process nets in this order:
  1. nets containing an `IsBusTrunk` connection, by key;
  2. all other nets, by ascending half-perimeter of their pins' bounding box, then by key.
- Within a net:
  1. Route the trunk first (if any). Otherwise route first the target whose escape node is nearest the source (Manhattan distance, ties by key).
  2. Each further target is a **multi-source** A*. The start set is every lattice node on the net's already-routed paths (the source's escape node included), with no initial bend cost.
  3. If the chosen start node lies on connection `P`'s path, then:
     - `SourceAttach = Wire`
     - `AttachedToKey = P.Key`
     - `AttachedSegmentIndex` = the index of the segment of `P`'s **final simplified** `Points` containing that node
     - `Points[0]` = that node's exact coordinates
  4. If the chosen start is the source escape node itself, keep `SourceAttach = Pin`.
- **Simplify first:** simplify a connection's `Points` before any later connection computes its segment index.

**Bus writers** (`TargetAttachToKey >= 0`).
- The goal set is every lattice node on the trunk connection's path, and nothing else.
- On arrival:
  - `TargetAttach = Wire`
  - `AttachedToKey = trunk key`
  - `AttachedSegmentIndex` = the trunk segment containing the goal node
  - last point = that node
- Route writers after their trunk's net.
- If the trunk is unrouted, mark the writer unrouted.

**Output order:** connections in the order they were routed (so parents come before children), then unrouted ones.

Tests:
- `Route_Fanout_SecondTargetBranchesFromFirst`: one source, two targets to the right at different heights. Exactly one of the two results has `SourceAttach == Wire`. Its `AttachedToKey` is the other connection's key, and its `Points[0]` satisfies `PointOnSegment` on that parent's segment `AttachedSegmentIndex`.
- `Route_ParentsPrecedeChildren`: for every result with `AttachedToKey >= 0`, the parent's index in `Connections` is lower.
- `Route_BusWriter_AttachesOnlyToTrunk`: a trunk, one reader (same source pin as the trunk) and one writer (`TargetAttachToKey = trunk.Key`). The writer's `TargetAttach == Wire` and `AttachedToKey == trunk.Key`, never the reader's key.
- `Route_BusTrunkUnroutable_WriterMarkedUnrouted`: the trunk's target is enclosed, so both the trunk and the writer have `Routed == false`.

Commit: `"Route nets as trees with T-junction branches and bus taps"`.

### Task 4: Clearance, crossings, improvement pass, and performance

**Files:**
- Modify: `Assets/Scripts/Game/Layout/Core/WireRouter.cs`
- Test: `Tools/LayoutTests/WireRouterTests.cs`

**Clearance rule.** Two parallel segments from different nets must be at least `HalfWidth(a) + HalfWidth(b) + MinGap` apart, measured perpendicular to the segments. Each lattice step that violates this costs `ClearanceViolationCost`.

**Occupancy.**
- Keep an occupancy record per lattice node and axis (H/V): the owning net and the maximum half-width of committed paths that reach that node.
- When a path is committed, mark the nodes within `HalfWidth(bits) + MinGap` of it on its perpendicular axis.
- Entering a node occupied on the **perpendicular** axis by another net (a crossing) costs `CrossCost`.
- Nodes occupied by the same net cost nothing (trees share their own trunk).
- The data structure is the implementer's choice.

**Improvement pass** (`ImprovementPasses` times, in the same net order):
1. Lift a net's occupancy.
2. Reroute all of its connections.
3. Keep the new routing only if its total A* cost is ≤ the old cost. Otherwise restore the old routing and re-mark it.

Tests:
- `Route_ParallelNets_RespectWidthClearance`: an 8-bit net and a 1-bit net whose shortest paths would share a row, with 16-bit and 1-bit variants via `[TestCase(8)]` and `[TestCase(16)]`. No pair of segments from different nets is `SegmentsOverlapCollinear` with `minGap = HalfWidth(bitsA) + HalfWidth(bitsB) + MinGap - 1e-3`.
- `Route_DifferentNetsNeverShareCollinearSegments`: on a 6-pin ladder (three nets crossing), no two segments of different nets overlap collinearly (`minGap = 1e-3`).
- `Route_ImprovementPassNeverIncreasesCost`: `Route(p, passes = 1).TotalCost <= Route(p, passes = 0).TotalCost`.
- `Route_200Connections_UnderTwoSeconds`: a deterministic grid of 20 chips by 10 connections (seeded `System.Random(1)`). The `Stopwatch` time is < 2000 ms and `FailedCount == 0`.

Commit: `"Add width-aware clearance, crossing cost and improvement pass to router"`.

### Task 5: Chip placer

**Files:**
- Create: `Assets/Scripts/Game/Layout/Core/ChipPlacer.cs`
- Test: `Tools/LayoutTests/ChipPlacerTests.cs`

**Interfaces (Produces):**
```csharp
public enum PlaceKind { Chip, InputPin, OutputPin }
public struct PlaceNode { public int ID; public PlaceKind Kind; public LayoutPoint Position; public float BoxWidth, BoxHeight; public LayoutPoint BoxOffset; /* bbox centre - Position */ public LayoutPoint SnapOffset; /* SnapPoint - Position */ }
public struct PlaceEdge { public int FromID, ToID; }
public class PlacementInput { public List<PlaceNode> Nodes = new(); public List<PlaceEdge> Edges = new(); public float GridSize = 0.125f; }
public static class ChipPlacer { public static Dictionary<int, LayoutPoint> Place(PlacementInput input); } // new Position for every Chip and OutputPin; InputPins absent (never move)
```

Algorithm:
1. **Edges.** Deduplicate edges and drop self-edges.
2. **Break cycles.** Run a DFS in ascending ID order: first from nodes with no incoming edges, then from any unvisited node. Reverse the back edges.
3. **Layers.**
   - `InputPin` = layer 0.
   - A chip's layer = 1 + the maximum layer of its predecessors.
   - Chips with no predecessors but some successors = layer 1.
   - Chips with no edges at all are "unconnected".
4. **Columns.**
   - Column 1's left edge = rightmost input-pin bbox edge + gap. If there are no input pins, use the leftmost current chip bbox edge.
   - Each column's width = its widest chip's bbox.
   - The gap after a column = `(6 + 2 * edgesSpanningThisGap) * GridSize`, capped at `40 * GridSize`.
   - Chips are centred horizontally in their column.
5. **Y positions,** column by column from left to right:
   - Desired y = the mean y of the chip's predecessors' current positions (input pins, or chips already placed). With no predecessors, keep its current y.
   - Sort the column by desired y, ties by ID.
   - Stack top-down with `3 * GridSize` between bboxes, pushing chips down to resolve overlaps.
   - Then shift the whole column by `mean(desired − actual)`.
6. **Snapping.** For every placed chip, `Position = SnapToGrid(Position + SnapOffset) − SnapOffset`. `SnapToGrid` rounds each coordinate to the nearest multiple of `GridSize`.
7. **Output pins.** Translate **all output pins by the same dx**, so their leftmost bbox edge = the rightmost chip bbox edge + `6 * GridSize`. Y is unchanged. This preserves the `OrderPins` order (y descending, then x) even when y values tie. If there are no chips, the output pins don't move.
8. **Unconnected chips.** Place them left to right in a row whose top is `4 * GridSize` below the lowest bbox placed so far, starting at column 1's left edge, with `3 * GridSize` gaps.

Tests:
- `Place_Chain_ColumnsIncreaseLeftToRight`: input → A → B → C → output gives x(A) < x(B) < x(C) < x(output).
- `Place_NoChipBoundingBoxesOverlap`: fan-out of 1 input into 5 chips feeding 2 outputs → no two placed chip bboxes intersect.
- `Place_Cycle_TerminatesAndPlacesAll`: A→B, B→A, input→A → both A and B are in the result and don't overlap.
- `Place_SnapPointsLandOnGrid`: for every chip, `(Position + SnapOffset) / 0.125` is within 1e-4 of an integer on both axes.
- `Place_OutputPinsKeepYAndRelativeX`: two output pins with equal y and different x keep the same y and the same x difference.
- `Place_InputPinsNotInResult`
- `Place_UnconnectedChips_ParkedBelow`: an unconnected chip's bbox top is below every connected chip's bbox bottom.

Commit: `"Add layered chip placer"`.

### Task 6: Unity adapter, wire replacement, and undo

**Files:**
- Create: `Assets/Scripts/Game/Layout/LayoutProblemBuilder.cs`
- Create: `Assets/Scripts/Game/Layout/LayoutApplier.cs`
- Create: `Assets/Scripts/Game/Layout/ChipOrganizer.cs`
- Modify: `Assets/Scripts/Game/Project/DevChipInstance.cs`, adding a method next to `AddWire`
- Modify: `Assets/Scripts/Game/Interaction/UndoController.cs`

**Interfaces:**
- Consumes: `WireRouter.Route`, `ChipPlacer.Place` and every core type.
- Produces:
```csharp
// DevChipInstance
public bool ReplaceAllWires(WireDescription[] descriptions); // Wires.Clear(); load each with TryLoadWireFromDescription(desc, i, this, loadedWithOriginalIndices) exactly like LoadFromDescriptionTest's loop; AddWire(wire, isLoading: true). Returns true if any failed. Never calls Simulator.
// UndoController
public void RecordOrganize(int[] elementIDs, Vector2[] positionsBefore, Vector2[] positionsAfter, WireDescription[] wiresBefore, WireDescription[] wiresAfter);
// ChipOrganizer
public struct OrganizeReport { public int WiresRouted, WiresFailed, ChipsMoved; }
public static class ChipOrganizer
{
	public static OrganizeReport TidyWires(DevChipInstance chip);
	public static OrganizeReport OrganizeAll(DevChipInstance chip);
}
```

**`LayoutProblemBuilder`**
- Obstacles: every element's `BoundingBox`, with `OwnerID = element.ID`.
- Pins:
  - Create one per distinct `PinAddress` used by any wire, keyed by a dense int assigned in order of first appearance while iterating `Wires` by index.
  - `Position = pin.GetWorldPos()`
  - `ExitDirX = (int)pin.ForwardDir.x`. This already accounts for bus flip.
  - `BitCount = (int)pin.bitCount`
- Connections:
  - One per wire, with `Key = wire index` (call `UpdateWireIndicesForDescriptionCreation` first).
  - `IsBusTrunk = wire.IsBusWire`
  - `TargetAttachToKey = wire.TargetConnectionInfo.IsConnectedAtWire && wire.ConnectedWire.IsBusWire ? wire.ConnectedWire.descriptionCreator_wireIndex : -1`
- `PlacementInput`:
  - Nodes are the elements. `SubChipInstance` → `Chip`; `DevPinInstance` → `InputPin` or `OutputPin`.
  - Box fields come from `BoundingBox`, and `SnapOffset = SnapPoint − Position`.
  - Edges run from `wire.SourcePin.Address.PinOwnerID` to `wire.TargetPin.Address.PinOwnerID`.

**`LayoutApplier`** builds the new `WireDescription[]` in `LayoutResult` order:
- Routed connections:
  - Source and target addresses come from the original wire.
  - `ConnectionType` is `ToWireSource` if `SourceAttach == Wire`, `ToWireTarget` if `TargetAttach == Wire`, otherwise `ToPins`.
  - `ConnectedWireIndex` = the **new** index of `AttachedToKey`.
  - `ConnectedWireSegmentIndex = AttachedSegmentIndex`.
  - `Points` = the converted points. The wire-attached end must be the exact attach coordinate, because `WireInstance` takes `originalWireConnectionPoint` from it.
- Unrouted connections:
  - Use the original `CreateWireDescription(wire)`.
  - If the original was `ToWireSource` attached to a wire that is now at a different index, remap `ConnectedWireIndex` to the new index.
  - If that parent is itself unrouted or missing, convert the wire to `ToPins` with its original interior points.
  - Writers of an unrouted trunk keep `ToWireTarget` pointing at the trunk's new index.
- Invariants that `TryLoadWireFromDescription` enforces, and that the applier must keep:
  - `ToWireSource` parents have the same `SourcePin.Address`.
  - `ToWireTarget` parents are bus wires.

**`ChipOrganizer`**
- Return a zero report unless `Project.ActiveProject.CanEditViewedChip`.
- `controller.CancelEverything()`.
- Snapshot:
  - `UpdateWireIndicesForDescriptionCreation`
  - `wiresBefore = Wires.Select(CreateWireDescription)`
  - `positionsBefore` for all elements
- `OrganizeAll` only: run the placer and set positions.
- Build the problem, then route with `new RouterSettings()`. The defaults match `DrawSettings.GridSize` and `WireThickness`; assert that in a comment and don't read `DrawSettings` in the core.
- Apply the result with `ReplaceAllWires`.
- Snapshot the after-state.
- Call `UndoController.RecordOrganize(...)` once.
- `Debug.Log` the report when `Application.isEditor`.
- If there are zero wires and no positions changed, record nothing.

**`OrganizeUndoAction`**
- `Trigger(undo, devChip)` sets element positions by ID (before or after), then calls `ReplaceAllWires` (before or after).
- Do **not** select elements.
- Add an `else if (action is OrganizeUndoAction organize) organize.Trigger(undo, devChip);` branch in `UndoRedo`.

**Steps:**
- [ ] Write the three files and the two modifications.
- [ ] Compile in Unity. This needs the license to be active.
  ```bash
  /Applications/Unity/Hub/Editor/6000.0.85f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath . -logFile /tmp/organize-compile.log
  grep -c "error CS" /tmp/organize-compile.log
  ```
  Expected: `0`. The project must not be open in the editor at the same time.
- [ ] Run `dotnet test Tools/LayoutTests`. Expected: all pass. This step guards against core regressions.
- [ ] Commit the new files and their Unity-generated `.meta` files.
  ```bash
  git add Assets/Scripts/Game/Layout Assets/Scripts/Game/Project/DevChipInstance.cs Assets/Scripts/Game/Interaction/UndoController.cs
  git commit -m "Add Unity adapter and single-step undo for organize"
  ```

### Task 7: Commands — shortcuts and menu

**Files:**
- Modify: `Assets/Scripts/Game/Interaction/KeyboardShortcuts.cs`
- Modify: `Assets/Scripts/Graphics/UI/Menus/BottomBarUI.cs`

Changes:
- In `KeyboardShortcuts`:
  - `public static bool TidyWiresShortcutTriggered => CtrlShortcutTriggered(KeyCode.T);`
  - `public static bool OrganizeAllShortcutTriggered => CtrlShiftShortcutTriggered(KeyCode.T);`
  - Ctrl+T and Ctrl+Shift+T are unused today. `KeyCode.T` only appears in the key-chip binding list.
- In `BottomBarUI`:
  - Insert `$"TIDY WIRES   {shortcutTextCol}Ctrl+T"` and `$"ORGANIZE ALL {shortcutTextCol}Ctrl+Shift+T"` after `FIND CHIP` in `menuButtonNames`.
  - Renumber the index constants and route them in `ButtonPressed`.
  - In `HandleKeyboardShortcuts`, inside the `MenuButtonsAndShortcutsEnabled` block, call `ChipOrganizer.TidyWires(Project.ActiveProject.ViewedChip)` and `ChipOrganizer.OrganizeAll(...)`.
  - Make sure the popup menu width still fits the longest label. The width comes from `menuButtonNames[0]`, so measure the longest label instead if it is wider.

Steps:
- [ ] Implement the changes, then compile (Task 6 command). Expected: `0` errors.
- [ ] **Manual verification.** Build with `-executeMethod MacBuild.Build` or use the editor's Play mode, and open the TestData `MainTest` project. Run each check below on these fixtures:
  - **XOR**: the user's original complaint.
  - **ADDER-4**: fan-out.
  - **BUS BUFFER**: bus taps.
  - **D-LATCH**: feedback loop.
  - **ALU-8** and **CONTROL UNIT**: size and performance.

  Checks:
  1. Ctrl+T: all wires are right-angle, none cross a chip body, fan-outs are T-junctions, bus taps end on the bus line.
  2. Toggle the inputs: outputs behave exactly as before.
  3. Ctrl+Z restores the original exactly, and Ctrl+Shift+Z re-applies.
  4. Save, quit to the menu, and reopen: the layout persists, with no "unsaved changes" prompt right after reopening.
  5. Ctrl+Shift+T: chips are arranged in columns, the external pin order is unchanged (compare the chip's pins in a parent chip before and after), and Undo restores the positions.
  6. Ctrl+T on CONTROL UNIT finishes in under 2 seconds.

  If any check fails, fix it before committing.
- [ ] Commit: `"Add Tidy Wires and Organize All commands"`.

### Task 8: "Organize on save" preference

**Files:**
- Modify: `Assets/Scripts/Description/Types/ProjectDescription.cs`
- Modify: `Assets/Scripts/Graphics/UI/Menus/PreferencesMenu.cs`
- Modify: `Assets/Scripts/Graphics/UI/Menus/ChipSaveMenu.cs`

Changes:
- `ProjectDescription`: add `public int Prefs_OrganizeOnSave;` after `Prefs_StraightWires`. Old saves deserialize it as 0, which means Off.
- `PreferencesMenu`:
  - Add `static readonly string[] OrganizeOnSaveOptions = { "Off", "Tidy wires", "Organize all" };` and `ID_OrganizeOnSave`.
  - Add a `DrawNextWheel("Organize on save", …)` row under EDITING, after "Straight wires".
  - Assign it alongside the other prefs, and initialize it in the wheel-state init block (where `ID_StraightWires` is initialized).
- `ChipSaveMenu.OnMenuOpened`, first statement: read `Project.ActiveProject.description.Prefs_OrganizeOnSave`. If 1, call `ChipOrganizer.TidyWires(ViewedChip)`; if 2, call `ChipOrganizer.OrganizeAll(ViewedChip)`. This runs before the customization description is created, so the saved description includes the organized layout. It is undoable after saving like any other edit.

Steps:
- [ ] Implement the changes, then compile. Expected: `0` errors.
- [ ] **Manual check:**
  1. Set the preference to "Tidy wires", mess up the XOR wires, press Ctrl+S, and save.
  2. Reopen the chip: the wires are tidy.
  3. Set the preference to Off: saving leaves the layout alone.
  4. An old project without the field opens with the preference at Off.
- [ ] Commit: `"Add Organize on save preference"`.

---

## Self-review notes

- **Coverage:** every goal maps to a task.
  - Tidy Wires: Tasks 1–4, 6 and 7.
  - Organize All: Tasks 5–7.
  - On-save preference: Task 8.
  - The anti-goals are covered as follows:
    - behaviour change: no simulator calls (Task 6), and the manual check toggles inputs;
    - pin reorder: uniform dx for output pins, and input pins never move (Task 5);
    - crash or lost wire: fallback tests (Tasks 2–3) and the applier rules (Task 6);
    - nondeterminism: the determinism test (Task 2) and Global Constraints.
- **Names used across tasks:**
  - `LayoutProblem`, `LayoutConnection.TargetAttachToKey`, `RoutedConnection.AttachedToKey` / `AttachedSegmentIndex`, `ReplaceAllWires`, `RecordOrganize`, `ChipOrganizer.TidyWires` / `OrganizeAll`.
  - These are defined once and used with the same spelling throughout.
