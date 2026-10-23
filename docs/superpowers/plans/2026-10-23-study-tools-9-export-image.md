# Plan 9: Export Circuit Image

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Save the chip being edited as a clean PNG for reports. The whole circuit is framed, with no menus, selection or hover effects, optionally with all pin names shown. The file goes to the project's Exports folder, which then opens.

**Architecture:**
- **Capture:** an end-of-frame screen capture driven by a coroutine on `UnityMain.instance`. For two frames it hides the UI (`Main.Update` skips `UIDrawer.Draw`), clears the selection, suppresses hover drawing, forces pin names if chosen, and frames the camera on the chip. It then calls `ScreenCapture.CaptureScreenshotAsTexture(scale)`, encodes PNG, writes the file, and restores everything.
- **Testable pieces:** framing maths and file naming are engine-free.

**Tech Stack:** C# 9, Unity 6000.0.85f1 (Built-in pipeline; `com.unity.modules.screencapture` and `imageconversion` are present), NUnit.

**Spec:** `docs/superpowers/plans/2026-10-23-study-tools-roadmap.md` (feature #6).

## Decisions (planner's calls)
- **Opening:** Ctrl+E or bottom bar "EXPORT IMAGE" opens a small options popup:
  - Scale: 1×, 2× (default) or 4×.
  - Pin names: All (default) or As shown.
  - Grid: Hide (default) or Show.
  - EXPORT / CANCEL buttons.
- **Framing:** the chip's element bounds plus wire points (the same sources as `CameraController.GetViewForChip`), with 6% padding, fitted to the window aspect. There is no bottom-bar offset because the UI is hidden.
- **File:** `<project folder>/Exports/<chip name> <yyyy-MM-dd HH-mm-ss>.png`. Characters invalid in file names are replaced with `_`. After saving, the folder opens through `Main.OpenFolderInFileBrowser` (created in Plan 1; if Plan 1 hasn't run, this plan's Task 2 extracts it as described there). A 4-second banner "Image saved: <file name>" appears at the top.
- **Empty chip:** EXPORT is disabled with the message "Nothing to export".

## Global Constraints
See the roadmap. Branch: `feature/study-tools-export`. New Core folder: `Assets/Scripts/Game/Export/Core/`, with a `<Compile Include>` line.

## Review Focus
- **Very wide or very tall circuits:** framed by the limiting dimension, with nothing cut off. Tests `Fit_WideBoundsLimitedByWidth` and `Fit_TallBoundsLimitedByHeight`.
- **Chip names with `/` or `:`:** the file is still written. Test `FileName_ReplacesInvalidChars`.
- **The user's view and selection after export:** exactly restored (camera position and zoom). Manual check.
- **Exporting while the sim runs:** wire colours reflect the state at capture time, with no crash. Manual check.
- **An unsaved new chip with no name:** the file name uses "Untitled". Test `FileName_EmptyChipName`.

---

### Task 1: Framing and naming (core)

**Files:**
- Create: `Assets/Scripts/Game/Export/Core/ExportFraming.cs`
- Create: `Assets/Scripts/Game/Export/Core/ExportNaming.cs`
- Modify: `Tools/LayoutTests/LayoutTests.csproj`
- Test: `Tools/LayoutTests/ExportCoreTests.cs`

**Interfaces:**
- Produces (namespace `DLS.Game.Export`):
  - `static class ExportFraming`: `(float centreX, float centreY, float orthoSize) Fit(float minX, float minY, float maxX, float maxY, float aspect, float paddingFraction)`. `orthoSize` is the half-height, as for Unity orthographic cameras; `aspect` = width / height. It is the smallest half-height that fits both dimensions, then multiplied by `(1 + paddingFraction)`.
  - `static class ExportNaming`: `string FileName(string chipName, DateTime time)` returns e.g. `"AND-8 2026-10-23 14-05-09.png"`.
- [ ] **Step 1: Write failing tests**
```csharp
[Test] public void Fit_WideBoundsLimitedByWidth()
{
	var f = ExportFraming.Fit(0, 0, 32, 2, 16f / 9f, 0);
	Assert.That(f.orthoSize, Is.EqualTo(9f).Within(1e-4)); // half-width 16 / aspect
	Assert.That(f.centreX, Is.EqualTo(16f)); Assert.That(f.centreY, Is.EqualTo(1f));
}
[Test] public void Fit_TallBoundsLimitedByHeight() => Assert.That(ExportFraming.Fit(0, 0, 2, 10, 16f / 9f, 0).orthoSize, Is.EqualTo(5f).Within(1e-4));
[Test] public void Fit_AppliesPadding() => Assert.That(ExportFraming.Fit(0, 0, 2, 10, 16f / 9f, 0.1f).orthoSize, Is.EqualTo(5.5f).Within(1e-4));
[Test] public void FileName_Format() => Assert.That(ExportNaming.FileName("AND-8", new DateTime(2026, 10, 23, 14, 5, 9)), Is.EqualTo("AND-8 2026-10-23 14-05-09.png"));
[Test] public void FileName_ReplacesInvalidChars() => Assert.That(ExportNaming.FileName("A/B:C", new DateTime(2026, 1, 2, 3, 4, 5)), Is.EqualTo("A_B_C 2026-01-02 03-04-05.png"));
[Test] public void FileName_EmptyChipName() => Assert.That(ExportNaming.FileName("", new DateTime(2026, 1, 2, 3, 4, 5)), Does.StartWith("Untitled "));
```
- [ ] **Step 2: Run tests.** Expected: FAIL.
- [ ] **Step 3: Implement.** Use a fixed list of invalid characters (`/ \ : * ? " < > |` plus control characters) so the result is the same on every platform.
- [ ] **Step 4: Run tests.** Expected: PASS.
- [ ] **Step 5: Commit** (`Add export framing and file naming`).

### Task 2: Capture, options popup and shortcut

**Files:**
- Create: `Assets/Scripts/Game/Export/ImageExporter.cs`:
  - `static bool IsCapturing`; `static void Export(ExportOptions options)`.
  - It starts a coroutine via `UnityMain.instance.StartCoroutine`.
  - `ExportOptions { int Scale; bool AllPinNames; bool ShowGrid; }`.
- Create: `Assets/Scripts/Graphics/UI/Menus/ExportImageMenu.cs` (`MenuType.ExportImage`).
- Create: `Assets/Scripts/Graphics/UI/StatusBanner.cs`: `Show(string text, float seconds)`; drawn in `UIDrawer.Draw` when visible.
- Modify: `Assets/Scripts/Game/Main/Main.cs`: skip `UIDrawer.Draw()` while `ImageExporter.IsCapturing`. Add `OpenFolderInFileBrowser(string path)` if Plan 1 hasn't.
- Modify: `Assets/Scripts/Graphics/World/DevSceneDrawer.cs`: when `ImageExporter.IsCapturing`:
  - skip the hover pin label, wire hover highlight and selection box;
  - use "always" for pin-name display if `AllPinNames`;
  - only draw the grid if `ShowGrid`.
- Modify: `Assets/Scripts/Game/Interaction/CameraController.cs`: add `public static ViewState CaptureView()` and `public static void RestoreView(ViewState v)`, and expose bounds collection as `public static Bounds2D GetChipBounds(DevChipInstance chip)`, extracted from `GetViewForChip`.
- Modify: `UIDrawer.cs`, `BottomBarUI.cs` (`$"EXPORT IMAGE {shortcutTextCol}Ctrl+E"`), `KeyboardShortcuts.cs` (`ExportImageShortcutTriggered => CtrlShortcutTriggered(KeyCode.E)`).

**Interfaces:**
- Consumes: `ExportFraming.Fit`, `ExportNaming.FileName`, `SavePaths.GetExportsPath` (Plan 1; add it here if missing).
- Coroutine order:
  1. `controller.CancelEverything()`, save the view, set `IsCapturing = true`.
  2. Fit and apply the camera, `yield return null`, `yield return new WaitForEndOfFrame()`.
  3. `ScreenCapture.CaptureScreenshotAsTexture(options.Scale)`, `EncodeToPNG`, `File.WriteAllBytes`, `Object.Destroy(texture)`.
  4. In a `finally`-style cleanup: restore the view and `IsCapturing = false`.
  5. Show the banner and open the folder.
- [ ] **Step 1: Implement.**
- [ ] **Step 2: Run `Tools/verify-compile.sh`.** Expected: `0 Error(s)`.
- [ ] **Step 3: Manual check (built app via `Tools/install-app.sh`, since capture behaviour can differ in the Editor):**
  - Export a large chip at 2×: the PNG contains the whole circuit with all pin names, and no bottom bar, menus or selection.
  - The view returns to exactly where it was.
  - The Exports folder opens and the banner shows.
- [ ] **Step 4: Commit** (`Add circuit image export`).

### Task 3: Ship
- [ ] Add an "Export image" bullet under `## Study tools` in `CHANGELOG.md`.
- [ ] Run the tests and compile check; merge into `main`; `git push origin main`; `Tools/install-app.sh`.
