# Plan 4: Value Tooltip on Wires and Pins

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Hovering any wire or pin shows a small tooltip next to the cursor with its name and current value. 1-bit values show as 0, 1 or Z. Multi-bit values show binary, decimal (unsigned and signed) and hex.

**Architecture:** Engine-free `PinValueFormat` turns raw pin states into text (other plans reuse it). A Unity-side `PinValueSource` works out which state to show for a hovered element. `ValueTooltip` draws with raw `Draw.*` calls in a screen-space layer at the end of `UIDrawer.Draw`. It deliberately avoids `UI.*` widgets, so it never sets `MouseIsOverUI` or blocks clicks.

**Tech Stack:** C# 9, Unity 6000.0.85f1, Seb.Vis Draw, NUnit.

**Spec:** `docs/superpowers/plans/2026-10-23-study-tools-roadmap.md` (feature #9).

## Decisions (planner's calls)
- **When it shows:** whenever a pin or wire is under the mouse, no input-blocking menu is open, the context menu is closed, and nothing is being placed, moved or wired.
- **Position:** offset (+16 px, -16 px) from the cursor, clamped inside the window.
- **Preference:** "Value Tooltips: On/Off" in Preferences, default On. It is stored as `Prefs_HideValueTooltips` (int, 0 = show), so older project files default to showing.
- **Which state is shown:**
  - Source pins (outputs and input dev pins) show their own `State`; an input dev pin in an editable chip shows `PlayerInputState`.
  - Subchip input pins don't receive sim state on the main thread, so they show the source pin state of the wire feeding them. With no wire they show "not connected".
  - Wires show their `SourcePin` state.
- **Tooltip text:** the name line is the pin name (or `wire: <source pin name>`), plus the bit width when > 1, e.g. `SUM (8-bit)`.

## Global Constraints
See the roadmap. Branch: `feature/study-tools-hover`. New Core folder: `Assets/Scripts/Game/Inspect/Core/`. Add its `<Compile Include>` to `Tools/LayoutTests/LayoutTests.csproj`.

## Review Focus
- **Partially floating multi-bit value:** binary shows `Z` per floating bit; decimal and hex show `?`. Test `PartiallyFloating`.
- **Fully floating value:** shows `Z (floating)`. Test `FullyFloating`.
- **16-bit value with the top bit set:** signed decimal is negative. Test `Signed16`.
- **Tooltip near the window edge:** stays fully visible (clamped). Manual check.
- **Tooltip must not steal clicks:** clicking a pin under the tooltip still starts a wire. Manual check; the design avoids `UI.*`.

---

### Task 1: `PinValueFormat` core

**Files:**
- Create: `Assets/Scripts/Game/Inspect/Core/PinValueFormat.cs`
- Modify: `Tools/LayoutTests/LayoutTests.csproj` (Inspect/Core include)
- Test: `Tools/LayoutTests/PinValueFormatTests.cs`

**Interfaces:**
- Produces (namespace `DLS.Game.Inspect`):
  - `enum NumberFormat { Binary, Decimal, Hex, Signed }`.
  - `static class PinValueFormat`. In all methods, `state` = bits in the low 16 bits and floating flags in the high 16 bits.
    - `string FormatValue(uint state, int bitCount, NumberFormat format)`:
      - **Binary:** MSB first, `Z` for floating bits, no prefix, no separators.
      - **Decimal / Signed:** `Z` if all bits are floating, `?` if some are.
      - **Hex:** uppercase, padded to `ceil(bits/4)` digits, `Z` if all floating, `?` for a nibble containing any floating bit.
    - `string[] TooltipLines(string name, uint state, int bitCount)`:
      - 1-bit: `{ name, "1" | "0" | "Z (floating)" }`.
      - Multi-bit: `{ $"{name} ({bitCount}-bit)", "bin " + binary in groups of 4 separated by '_', "dec " + unsigned + "  (signed " + signed + ")", "hex " + hex }`.
      - A fully floating multi-bit value: `{ title, "Z (floating)" }`.

- [ ] **Step 1: Write failing tests**
```csharp
[Test] public void Binary() => Assert.That(PinValueFormat.FormatValue(0b0101, 4, NumberFormat.Binary), Is.EqualTo("0101"));
[Test] public void Hex_Padded() => Assert.That(PinValueFormat.FormatValue(0x5, 8, NumberFormat.Hex), Is.EqualTo("05"));
[Test] public void Signed8() => Assert.That(PinValueFormat.FormatValue(0xFF, 8, NumberFormat.Signed), Is.EqualTo("-1"));
[Test] public void Signed16() => Assert.That(PinValueFormat.FormatValue(0x8000, 16, NumberFormat.Signed), Is.EqualTo("-32768"));
[Test] public void PartiallyFloating()
{
	uint s = 0b0100 | (1u << 16); // bit 0 floating
	Assert.That(PinValueFormat.FormatValue(s, 4, NumberFormat.Binary), Is.EqualTo("010Z"));
	Assert.That(PinValueFormat.FormatValue(s, 4, NumberFormat.Decimal), Is.EqualTo("?"));
	Assert.That(PinValueFormat.FormatValue(s, 8, NumberFormat.Hex), Is.EqualTo("0?"));
}
[Test] public void FullyFloating() => Assert.That(PinValueFormat.TooltipLines("Y", 1u << 16, 1), Is.EqualTo(new[] { "Y", "Z (floating)" }));
[Test] public void TooltipMultiBit() => Assert.That(PinValueFormat.TooltipLines("SUM", 0xA5, 8),
	Is.EqualTo(new[] { "SUM (8-bit)", "bin 1010_0101", "dec 165  (signed -91)", "hex A5" }));
[Test] public void TooltipOneBit() => Assert.That(PinValueFormat.TooltipLines("C", 1, 1), Is.EqualTo(new[] { "C", "1" }));
```
- [ ] **Step 2: Run `dotnet test Tools/LayoutTests`.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run tests.** Expected: PASS.
- [ ] **Step 5: Commit** (`Add pin value formatting`).

### Task 2: Tooltip drawing and preference

**Files:**
- Create: `Assets/Scripts/Game/Inspect/PinValueSource.cs`
- Create: `Assets/Scripts/Graphics/UI/ValueTooltip.cs`
- Modify: `Assets/Scripts/Graphics/UI/UIDrawer.cs`: call `ValueTooltip.Draw(project)` at the end of `Draw()`, after the fixed-aspect scope and after `ContextMenu.Update()`.
- Modify: `Assets/Scripts/Description/Types/ProjectDescription.cs`: `public int Prefs_HideValueTooltips;`
- Modify: `Assets/Scripts/Graphics/UI/Menus/PreferencesMenu.cs`: add an "Value Tooltips" On/Off wheel (index 0 = On), following the existing wheels and their save path.

**Interfaces:**
- Consumes: `PinValueFormat.TooltipLines`.
- Produces:
  - `PinValueSource.TryGetHoverInfo(IInteractable element, DevChipInstance chip, bool chipEditable, out string name, out uint state, out int bitCount) -> bool`.
    - Feeding wire lookup: the first wire in `chip.Wires` whose `TargetPin == pin`.
    - For bus targets, compare `TargetPin_BusCorrected`.
  - `ValueTooltip.Draw(Project project)`:
    - `Draw.StartLayer(Vector2.zero, 1, true)` (screen space, pixels).
    - Background `Draw.Quad` in `ActiveTheme.PinLabelCol`, then text lines with `FontBold` for the first line and `FontRegular` for the rest.
    - Font size 14 px × `Screen.height / 1080f`; padding 6 px.
- [ ] **Step 1: Implement.**
- [ ] **Step 2: Run `Tools/verify-compile.sh`.** Expected: `0 Error(s)`.
- [ ] **Step 3: Manual check:**
  - Hover an 8-bit input set to 165: four lines with `-91` signed.
  - Hover a subchip input pin fed by a wire: the source value.
  - Hover an unconnected subchip input: "not connected".
  - Turn the preference Off: no tooltip.
  - Hover near the right and bottom edges: the tooltip stays visible.
  - Click a pin while the tooltip shows: a wire starts.
- [ ] **Step 4: Commit** (`Show wire and pin values on hover`).

### Task 3: Ship
- [ ] Add a "Value tooltips" bullet under `## Study tools` in `CHANGELOG.md`.
- [ ] Run the tests and compile check; merge into `main`; `git push origin main`; `Tools/install-app.sh`.
