using System;
using System.Collections.Generic;
using DLS.Game;
using DLS.Game.Inspect;
using DLS.Game.Probes;
using Seb.Helpers;
using Seb.Types;
using Seb.Vis;
using Seb.Vis.UI;
using UnityEngine;

namespace DLS.Graphics
{
	// Docked above the bottom bar. It is not a MenuType, so the canvas above it stays usable:
	// UI.DrawPanel marks only the panel's area as UI, which blocks world clicks and camera zoom under it.
	public static class TimingDiagramPanel
	{
		const float HeaderHeight = 2.6f;
		const float RowHeight = 2.2f;
		const float MaxHeightFraction = 0.45f;
		const float Pad = 0.6f;
		const float NameColumnWidth = 14;
		const float LineThickness = 0.12f;
		const float BusSlant = 0.3f;
		const float MinLabelWidthPx = 24;
		const float MinSpanPx = 3; // value changes closer together than this are drawn as one dense band
		const float PanPxPerWheelNotch = 40;
		const float RowFontSize = UIThemeLibrary.FontSizeSmall;
		const string EmptyHint = "Right-click a pin or wire → PROBE";

		static readonly Color headerCol = new(0.08f, 0.08f, 0.08f);
		static readonly Color rowDividerCol = new(1, 1, 1, 0.06f);
		static readonly Color floatingCol = new(0.5f, 0.5f, 0.5f);
		static readonly Color hintCol = new(0.6f, 0.6f, 0.6f);
		static readonly Color cursorCol = new(1, 1, 1, 0.45f);

		public static bool IsOpen { get; private set; }

		static float pxPerStep = WaveformView.DefaultPxPerStep;
		static double panSteps; // steps back from the newest sample; 0 follows the live simulation

		// Reused every frame
		static readonly long[] steps = new long[ProbeSet.HistoryLength];
		static readonly uint[][] samples = new uint[ProbeSet.HistoryLength][];
		static readonly uint[] column = new uint[ProbeSet.HistoryLength];
		static readonly List<WaveSegment> segments = new();
		static readonly List<WaveSpan> spans = new();
		static readonly Dictionary<ulong, string> hexCache = new();
		static readonly Dictionary<ulong, string> cursorTextCache = new();
		static long stepTextStep = long.MinValue;
		static string stepText;

		// Newest sample seen last frame, so stepping while panned back keeps the view still
		static SampleRing lastRing;
		static long lastNewest = long.MaxValue;

		public static void Toggle() => IsOpen = !IsOpen;
		public static void Open() => IsOpen = true;

		public static void Reset()
		{
			IsOpen = false;
			pxPerStep = WaveformView.DefaultPxPerStep;
			panSteps = 0;
		}

		public static void Draw(Project project)
		{
			// The panel can stay open while viewing a subchip; probes always belong to the edited (root) chip
			if (project.CanEditViewedChip) ProbeSet.PruneMissing(project.ViewedChip);

			IReadOnlyList<Probe> probes = ProbeSet.Probes;
			int probeCount = probes.Count;
			DrawSettings.UIThemeDLS theme = DrawSettings.ActiveUITheme;

			float height = Mathf.Min(HeaderHeight + RowHeight * probeCount, UI.Height * MaxHeightFraction);
			float rowHeight = probeCount > 0 ? (height - HeaderHeight) / probeCount : RowHeight;
			float bottom = BottomBarUI.barHeight;
			float top = bottom + height;
			Bounds2D panelBounds = new(new Vector2(0, bottom), new Vector2(UI.Width, top));

			UI.DrawPanel(panelBounds, theme.MenuPanelCol);
			UI.DrawPanel(new Vector2(0, top), new Vector2(UI.Width, HeaderHeight), headerCol, Anchor.TopLeft);

			// The bottom-bar popup and context menu draw over the panel, so don't let clicks fall through them
			bool ignoreInputs = UIDrawer.ActiveMenu != UIDrawer.MenuType.None || ContextMenu.HasFocus() || BottomBarUI.CollectionPopupIsOpen;
			bool mouseOverPanel = UI.MouseInsideBounds(panelBounds) && !ignoreInputs;
			bool paused = project.simPaused;

			// ---- Samples ----
			SampleRing ring = ProbeSet.Ring;
			int count = ring != null && ring.ProbeCount == probeCount ? ring.CopyLatest(ProbeSet.HistoryLength, steps, samples) : 0;
			int first = WaveformView.MonotonicStart(steps, count);
			bool hasSamples = count > first;

			// ---- Window ----
			float waveLeft = NameColumnWidth;
			float waveRight = UI.Width - Pad;
			float pxPerUI = UI.UIToScreenSpace(Vector2.right).x - UI.UIToScreenSpace(Vector2.zero).x;

			if (mouseOverPanel)
			{
				Vector2 scroll = InputHelper.MouseScrollDelta;
				if (InputHelper.ShiftIsHeld)
				{
					// macOS turns Shift + wheel into horizontal scrolling, so take whichever axis moved
					if (paused) panSteps += Maths.AbsoluteMax(scroll.x, scroll.y) * PanPxPerWheelNotch / pxPerStep;
				}
				else if (scroll.y != 0)
				{
					pxPerStep = WaveformView.Zoom(pxPerStep, scroll.y);
				}
			}

			float stepWidth = pxPerStep / pxPerUI;
			long visibleSteps = Math.Max(1, (long)Math.Ceiling((waveRight - waveLeft) / stepWidth));
			long newest = hasSamples ? steps[count - 1] : 0;
			long oldest = hasSamples ? steps[first] : 0;
			if (!paused) panSteps = 0;
			else if (panSteps > 0 && ring == lastRing && hasSamples && newest > lastNewest) panSteps += newest - lastNewest;
			lastRing = ring;
			lastNewest = hasSamples ? newest : long.MaxValue;
			panSteps = Math.Clamp(panSteps, 0, WaveformView.ClampPan(long.MaxValue, oldest, newest, visibleSteps));
			long rightStep = newest - (long)Math.Round(panSteps);
			long leftStep = rightStep - visibleSteps + 1;

			// Samples in the window (one extra on the left for the partly visible step)
			int visStart = hasSamples ? WaveformView.LowerBound(steps, first, count, leftStep - 1) : 0;
			int visEnd = hasSamples ? WaveformView.LowerBound(steps, first, count, rightStep + 1) : 0;

			// ---- Cursor ----
			Vector2 mouse = UI.ScreenToUISpace(InputHelper.MousePos);
			bool cursorActive = mouseOverPanel && hasSamples && mouse.x >= waveLeft && mouse.x <= waveRight && mouse.y < top - HeaderHeight;
			long cursorStep = cursorActive ? WaveformView.StepAtDistanceFromRight(rightStep, waveRight - mouse.x, stepWidth) : rightStep;
			// Without a cursor, the names show the value at the right edge (the newest visible step)
			int valueIndex = hasSamples ? cursorActive ? WaveformView.IndexOfStep(steps, first, count, cursorStep) : visEnd - 1 : -1;
			if (valueIndex < first) valueIndex = -1;

			// ---- Header ----
			float headerY = top - HeaderHeight / 2;
			UI.DrawText("TIMING DIAGRAM", theme.FontBold, theme.FontSizeRegular, new Vector2(Pad, headerY), Anchor.TextCentreLeft, Color.white);
			float infoX = UI.PrevBounds.Right + 1.5f;

			ButtonTheme buttonTheme = theme.ChipButton;
			Vector2 buttonPad = new(0.8f, 0);
			float buttonHeight = HeaderHeight - 0.6f;
			if (UI.Button("CLOSE", buttonTheme, new Vector2(UI.Width - Pad, headerY), new Vector2(buttonPad.x, buttonHeight), true, true, false, Anchor.CentreRight, ignoreInputs: ignoreInputs))
			{
				IsOpen = false;
			}

			float closeLeft = UI.PrevBounds.Left;
			if (UI.Button("CLEAR", buttonTheme, new Vector2(closeLeft - BottomBarUI.buttonSpacing * 2, headerY), new Vector2(buttonPad.x, buttonHeight), probeCount > 0, true, false, Anchor.CentreRight, ignoreInputs: ignoreInputs))
			{
				ProbeSet.ClearHistory();
				panSteps = 0;
			}

			string info;
			if (probeCount == 0) info = EmptyHint;
			else if (cursorActive) info = StepText(cursorStep);
			else if (paused) info = "Shift+wheel: pan   wheel: zoom   Space: step";
			else info = "wheel: zoom   Ctrl+Space: pause";
			UI.DrawText(info, theme.FontRegular, RowFontSize, new Vector2(infoX, headerY), Anchor.TextCentreLeft, hintCol);

			// ---- Rows ----
			float rowsTop = top - HeaderHeight;
			for (int p = 0; p < probeCount; p++)
			{
				Probe probe = probes[p];
				float rowTop = rowsTop - p * rowHeight;
				float rowCentre = rowTop - rowHeight / 2;
				Color col = DrawSettings.GetStateColour(true, (uint)probe.Colour);

				if (p > 0) UI.DrawLine(new Vector2(0, rowTop), new Vector2(UI.Width, rowTop), 0.05f, rowDividerCol);

				// Name and value
				string valueText = valueIndex >= 0 ? FormatCursorValue(samples[valueIndex][p], probe.BitCount) : string.Empty;
				float nameRight = waveLeft - Pad;
				if (valueText.Length > 0)
				{
					UI.DrawText(valueText, theme.FontBold, RowFontSize, new Vector2(nameRight, rowCentre), Anchor.TextCentreRight, col);
					nameRight = UI.PrevBounds.Left - 0.4f;
				}

				// A very long value can leave no room for the name; never let the mask invert
				if (nameRight > Pad)
				{
					using (UI.CreateMaskScopeMinMax(new Vector2(Pad, rowTop - rowHeight), new Vector2(nameRight, rowTop)))
					{
						UI.DrawText(probe.Name, theme.FontRegular, RowFontSize, new Vector2(Pad, rowCentre), Anchor.TextCentreLeft, Color.white);
					}
				}

				if (visEnd <= visStart) continue;

				// Waveform
				for (int i = visStart; i < visEnd; i++) column[i] = samples[i][p];
				WaveformSegments.Build(steps, column, visStart, visEnd - visStart, segments);
				WaveformView.BuildSpans(segments, leftStep, rightStep, pxPerStep, MinSpanPx, spans);

				float amplitude = rowHeight * 0.3f;
				using (UI.CreateMaskScopeMinMax(new Vector2(waveLeft, rowTop - rowHeight), new Vector2(waveRight, rowTop)))
				{
					if (probe.BitCount <= 1) DrawBitWave(rowCentre, amplitude, col);
					else DrawBusWave(rowCentre, amplitude, col, probe.BitCount, pxPerUI, theme.FontRegular);
				}
			}

			if (cursorActive)
			{
				float x = StepX(cursorStep) + stepWidth / 2;
				UI.DrawLine(new Vector2(x, bottom), new Vector2(x, rowsTop), 0.08f, cursorCol);
			}

			// A layer draws all its shapes before its text, so start a new one: otherwise the panel's text
			// would show through the bottom-bar popups drawn after it
			UI.StartNewLayer();

			// Left edge of a step; the right edge of rightStep is waveRight
			float StepX(long step) => waveRight - (rightStep + 1 - step) * stepWidth;

			float ClampX(float x) => Mathf.Clamp(x, waveLeft, waveRight);

			void DrawBitWave(float centreY, float amp, Color col)
			{
				float yHigh = centreY + amp;
				float yLow = centreY - amp;
				Color fillCol = new(col.r, col.g, col.b, 0.15f);
				Color denseCol = new(col.r, col.g, col.b, 0.55f);
				float prevY = float.NaN;
				long prevEnd = long.MinValue;

				// Spans are already clipped to the window
				foreach (WaveSpan span in spans)
				{
					float x0 = ClampX(StepX(span.StartStep));
					float x1 = ClampX(StepX(span.EndStep));

					if (span.Dense)
					{
						DrawQuad(new Vector2(x0, yLow), new Vector2(x1, yHigh), denseCol);
						prevY = float.NaN;
						prevEnd = span.EndStep;
						continue;
					}

					bool floating = (span.Value & 0x10000) != 0;
					bool high = !floating && (span.Value & 1) != 0;
					float y = floating ? centreY : high ? yHigh : yLow;

					if (high) DrawQuad(new Vector2(x0, yLow), new Vector2(x1, yHigh), fillCol);
					if (prevEnd == span.StartStep && !float.IsNaN(prevY) && prevY != y) UI.DrawLine(new Vector2(x0, prevY), new Vector2(x0, y), LineThickness, col);
					UI.DrawLine(new Vector2(x0, y), new Vector2(x1, y), LineThickness, floating ? floatingCol : col);

					prevY = y;
					prevEnd = span.EndStep;
				}
			}

			void DrawBusWave(float centreY, float amp, Color col, int bitCount, float pxPerUnit, FontType font)
			{
				uint mask = bitCount >= 16 ? 0xFFFFu : (1u << bitCount) - 1;
				float yTop = centreY + amp;
				float yBottom = centreY - amp;
				Color denseCol = new(col.r, col.g, col.b, 0.35f);

				foreach (WaveSpan span in spans)
				{
					float x0 = ClampX(StepX(span.StartStep));
					float x1 = ClampX(StepX(span.EndStep));
					if (x1 <= x0) continue;

					if (span.Dense)
					{
						DrawQuad(new Vector2(x0, yBottom), new Vector2(x1, yTop), denseCol);
						continue;
					}

					if (((span.Value >> 16) & mask) == mask)
					{
						UI.DrawLine(new Vector2(x0, centreY), new Vector2(x1, centreY), LineThickness, floatingCol);
						continue;
					}

					// A box whose ends narrow to a point at each value change (not where the window cuts it off)
					bool changesAtStart = span.StartStep > leftStep;
					bool changesAtEnd = span.EndStep <= rightStep;
					float slant = Mathf.Min(BusSlant, (x1 - x0) / 2);
					float a = changesAtStart ? x0 + slant : x0;
					float b = changesAtEnd ? x1 - slant : x1;
					UI.DrawLine(new Vector2(a, yTop), new Vector2(b, yTop), LineThickness, col);
					UI.DrawLine(new Vector2(a, yBottom), new Vector2(b, yBottom), LineThickness, col);
					if (changesAtStart)
					{
						UI.DrawLine(new Vector2(x0, centreY), new Vector2(a, yTop), LineThickness, col);
						UI.DrawLine(new Vector2(x0, centreY), new Vector2(a, yBottom), LineThickness, col);
					}

					if (changesAtEnd)
					{
						UI.DrawLine(new Vector2(b, yTop), new Vector2(x1, centreY), LineThickness, col);
						UI.DrawLine(new Vector2(b, yBottom), new Vector2(x1, centreY), LineThickness, col);
					}

					float widthPx = (x1 - x0) * pxPerUnit;
					if (widthPx < MinLabelWidthPx) continue;
					string label = HexText(span.Value, bitCount);
					float labelWidth = UI.CalculateTextSize(label, RowFontSize, font).x;
					if (labelWidth + slant * 2 > x1 - x0) continue;
					UI.DrawText(label, font, RowFontSize, new Vector2((x0 + x1) / 2, centreY), Anchor.TextCentre, Color.white);
				}
			}
		}

		static void DrawQuad(Vector2 min, Vector2 max, Color col)
		{
			Vector2 a = UI.UIToScreenSpace(min);
			Vector2 b = UI.UIToScreenSpace(max);
			Seb.Vis.Draw.Quad((a + b) / 2, b - a, col);
		}

		static string FormatCursorValue(uint state, int bitCount)
		{
			ulong key = ((ulong)(uint)bitCount << 32) | state;
			if (!cursorTextCache.TryGetValue(key, out string text))
			{
				if (cursorTextCache.Count > 4096) cursorTextCache.Clear();
				if (bitCount <= 1) text = PinValueFormat.FormatValue(state, 1, NumberFormat.Binary);
				else
				{
					string hex = PinValueFormat.FormatValue(state, bitCount, NumberFormat.Hex);
					text = hex == "Z" ? hex : "0x" + hex;
				}

				cursorTextCache.Add(key, text);
			}

			return text;
		}

		static string StepText(long step)
		{
			if (step != stepTextStep)
			{
				stepTextStep = step;
				stepText = "step " + step;
			}

			return stepText;
		}

		// Labels repeat every frame, so cache them rather than allocating new strings
		static string HexText(uint state, int bitCount)
		{
			ulong key = ((ulong)(uint)bitCount << 32) | state;
			if (!hexCache.TryGetValue(key, out string text))
			{
				if (hexCache.Count > 4096) hexCache.Clear();
				text = PinValueFormat.FormatValue(state, bitCount, NumberFormat.Hex);
				hexCache.Add(key, text);
			}

			return text;
		}
	}
}
