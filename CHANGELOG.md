# Changelog

Changes in this fork ([osama1998H/Digital-Logic-Sim](https://github.com/osama1998H/Digital-Logic-Sim)) compared to the original project, [SebLague/Digital-Logic-Sim](https://github.com/SebLague/Digital-Logic-Sim) (`main` at `7aeb66d`, "Merge branch 'Dev'").

## Editor tools

### Working with many selected items
- **Right-click a selection:** right-clicking one of several selected items keeps the selection and opens a menu for all of them: **Rename… / Label…**, **Replace…**, **Arrange >** and **Delete**.
- **Sequence renaming:** select several input pins (or several output pins), choose **Rename…**, type a name such as `A` and pick **Sequence**. The pins are named `A0`, `A1`, … from the bottom up, so the top pin gets the highest number. This matches the bit order of multi-bit pins and the split/merge chips. **Same name** gives every pin the same name instead.
- **Sequence labels:** the same works for chip labels. For example, 8 full adders can be labelled `FA0` … `FA7`.
- **Undo:** renaming a pin and labelling a chip can now be undone, whether done one at a time or in a batch.

### Align and distribute
Select several items, then use **Arrange >** in the right-click menu or these shortcuts:

| Shortcut | Action |
|---|---|
| Ctrl+Shift+Left / Right | Align left / right edges |
| Ctrl+Shift+Up / Down | Align top / bottom edges |
| Ctrl+Shift+C | Align centres (left to right) |
| Ctrl+Shift+M | Align middles (top to bottom) |
| Ctrl+Shift+H | Space evenly across |
| Ctrl+Shift+V | Space evenly down |

- Items snap to the grid when snapping is on.
- One undo step reverses the whole arrangement.
- If an arrangement would make items overlap, or would change the order of the chip's input/output pins, nothing moves and the selection outline turns red.

### Replace chips
- **Replace…** (right-click a chip) swaps it for another chip with the same number of input and output pins at the same bit widths. Wires stay connected.
- **Replace all…** swaps every chip of that type in the open chip.
- **Select same** selects every chip of the clicked type. Replacing that selection swaps just those chips.
- **How wires are kept:** copies of a chip (made with Save As or Duplicate) keep each wire on the same pin. For other chips, pins are matched top to bottom.
- Replacing can be undone in one step.
- Key, ROM, LED, Pulse and bus chips can't be replaced.

### Duplicate a chip
- A **DUPLICATE** button in the chip library saves a copy of the selected chip under a new name (suggested as `<NAME> COPY`). The copy is placed next to the original and starred.
- The copy has the same pins as the original, so it can be swapped in with Replace (for example, build AND-8, duplicate it as OR-8, then edit the copy).

### Tidy Wires and Organize All
- **Tidy Wires (Ctrl+T)** reroutes wires as tidy horizontal and vertical lines. Branch wires connect at T-junctions, wires of different widths are kept apart, and fewer wires cross.
- **Organize All (Ctrl+Shift+T)** lays out the chips in columns and then tidies the wires. Input pins stay where they are, and the chip's pin order never changes.
- **Organize on save** (a preference) runs Organize All when you open the save menu. Cancelling the save undoes it.
- Each of these can be undone in one step.

### Split / merge pin names
Split and merge chip pins are named by bit number (`IN 7` … `IN 0`, `OUT 7-4`, `OUT 3-0`, `OUT 15-8`, …) instead of letters (`IN H` … `IN A`). The top pin is the most significant bit. How the chips work is unchanged.

## Study tools

### Chip statistics
Total NAND gates across all levels, longest path in gate delays, and a breakdown of chips used. Right-click a chip and choose **Statistics**, or use **STATISTICS** in the bottom-bar menu for the chip you're editing.

### Truth table
Right-click a chip and choose **Truth Table**, or press **Ctrl+Shift+Y** for the chip you're editing. Copy or save the table as CSV, and get a simplified expression for 1-bit outputs.

### Test bench
Right-click a chip and choose **Test Bench**, or press **Ctrl+B** for the chip you're editing, to check it against a list of test lines. Each line sets inputs and, after `->`, says what the outputs should be, for example `A=1 B=0 -> SUM=1`. Lines with no `->` only set inputs, so you can clock a sequential chip step by step; inputs start at 0 and keep their value from line to line. Pins are referenced by name or by position (`in1`, `out2`), values can be decimal, negative, `0b1010` or `0xFF`, and an expected value can be `x` (don't care) or `z` (floating). `#` starts a comment. Tests are saved per chip in the project's `Tests` folder. **FILL FROM CURRENT** writes one line per input combination of a combinational chip (up to 8 input bits), using its current outputs.

### Value tooltips
Hover any wire or pin to see its current value in binary, decimal, signed and hex. Turn it off with **Value tooltips** in Preferences.

## New chips and pins (merged from upstream pull requests)
- **16-bit pins and chips** ([#571](https://github.com/SebLague/Digital-Logic-Sim/pull/571)): 16-bit input and output pins, plus 16-bit split and merge chips (16→8, 16→1, 8→16, 1→16).
- **RTC (real-time clock) chip** ([#499](https://github.com/SebLague/Digital-Logic-Sim/pull/499)).
- **Buzzer display** ([#476](https://github.com/SebLague/Digital-Logic-Sim/pull/476)): the buzzer looks like a speaker and shows its actual frequency, also inside subchips.
- **Bus snapping** ([#497](https://github.com/SebLague/Digital-Logic-Sim/pull/497)): bus chips snap to the grid by their centre, so they stay aligned when flipped.
- **Chip caching** ([#507](https://github.com/SebLague/Digital-Logic-Sim/pull/507)): small chips made only of logic gates (no memory or clock) are simulated with a precomputed lookup table, which is much faster. Larger chips can be cached manually in the chip customization menu. A popup shows progress while a cache is built.

## Fixes
- **Simulation could freeze while caching.** Caches are now cleared safely, cache building pauses while a menu is open, and the 24-input-bit limit for manually cached chips is enforced. Unexpected errors in the simulation are logged instead of silently stopping it.
- **Caching fixes:** chips containing an RTC are no longer cached, and the new 16-bit split/merge chips are handled correctly.
- **CPU use:** the simulation sleeps between steps instead of keeping a CPU core fully busy.
- **Wires on Save As copies:** removing a pin from a chip no longer deletes wires connected to the same pin on a Save As copy of that chip.
- **16-bit pin wires** now connect at the correct position on the pin.

## Development
- Upgraded to Unity **6000.0.85f1** (LTS), which includes the fix for CVE-2025-59489.
- Command-line macOS build:
  `Unity -batchmode -quit -projectPath . -executeMethod MacBuild.Build`
  The build is written to `Builds/macOS/Digital-Logic-Sim.app`.
- Unit tests for the layout and editing code, which doesn't depend on Unity: `dotnet test Tools/LayoutTests`.
- Helper scripts in `Tools/`: `verify-compile.sh` (compile-check the game code with dotnet), `make-metas.sh` (create missing Unity `.meta` files) and `install-app.sh` (build the macOS app and install it to `/Applications`, keeping one backup).
- Design and implementation notes are in `docs/superpowers/plans/`.
