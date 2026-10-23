using System;
using System.Collections.Generic;
using System.Text;
using DLS.Description;
using DLS.Game;
using DLS.Game.Logic;
using DLS.Simulation;
using Seb.Helpers;
using Seb.Types;
using Seb.Vis;
using Seb.Vis.UI;
using UnityEngine;
using static DLS.Graphics.DrawSettings;

namespace DLS.Graphics
{
	public static class TestBenchMenu
	{
		const string Header = "# A=1 B=0 -> Y=1   (inputs -> expected outputs; lines without -> only set inputs)";
		const string FillConfirmText = "CONFIRM REPLACE?";
		const int MaxLineLength = 120;
		const int MaxLines = 500;
		const int MaxFillInputBits = 8;
		const float RowHeight = 2.2f;

		static readonly UIHandle ID_Scrollbar = new("TestBenchMenu_Scrollbar");
		static readonly UI.ScrollViewDrawElementFunc drawRow = DrawRow;
		static readonly Func<string, bool> lineValidator = ValidateLine;
		static readonly Color passCol = new(0.53f, 0.8f, 0.57f);
		static readonly Color failCol = new(1f, 0.42f, 0.42f);
		static readonly Color dimCol = new(0.7f, 0.7f, 0.7f);

		static bool isOpen;
		static ChipDescription chip;
		static string chipName;
		static string projectName;
		static string pinSummary;
		static string statusText;
		static int rowCount;
		static int focusedRow;
		static int pendingScrollRow;
		static bool fillConfirmPending;
		// Set when the saved test file couldn't be read: nothing is saved until the lines differ from what was shown on opening
		static bool loadFailed;
		static string loadedSnapshot;
		static Bounds2D scrollViewBounds;
		static float statusCellWidth;

		static TestBenchJob job;
		static TruthTableJob fillJob;
		// What the last finished run reported, with the lines it ran, so stale results can be hidden after an edit
		static string[] resultLines;
		static string[] resultTexts;
		static int resultPassed;
		static int resultFailed;
		static int resultErrors;
		static string resultWarning;

		static bool CanEdit => !string.IsNullOrWhiteSpace(chipName);
		static bool Busy => job != null || fillJob != null;
		static UIHandle LineHandle(int i) => new("TestBench_Line", i);

		public static void Open(ChipDescription chipDescription)
		{
			CancelJobs();

			chip = chipDescription;
			chipName = chip.Name;
			projectName = Project.ActiveProject.description.ProjectName;
			statusText = string.Empty;
			resultTexts = null;
			resultLines = null;
			fillConfirmPending = false;
			loadFailed = false;
			loadedSnapshot = null;
			focusedRow = -1;
			rowCount = 0;
			pinSummary = DescribePins(chip);
			isOpen = true;

			if (CanEdit)
			{
				List<string> lines = new();
				try
				{
					lines.AddRange(TestBenchStore.Load(projectName, chipName));
				}
				catch (Exception e)
				{
					loadFailed = true;
					lines.Clear();
					statusText = "Could not read the test file (" + e.Message + "); it will not be overwritten unless you edit the lines";
				}

				if (lines.Count == 0)
				{
					lines.Add(Header);
					lines.Add(string.Empty);
				}

				WriteLines(lines, lines.Count - 1);
				loadedSnapshot = string.Join("\n", ReadLines());
			}

			UIDrawer.SetActiveMenu(UIDrawer.MenuType.TestBench);
		}

		public static void OnMenuOpened()
		{
			UI.GetScrollbarState(ID_Scrollbar).scrollY = 0;
			pendingScrollRow = focusedRow;
		}

		// Called by UIDrawer whenever the menu stops being the active one, whatever the reason
		public static void OnMenuClosed()
		{
			CancelJobs();
			if (!isOpen) return;
			isOpen = false;
			SaveLines(ReadLines());
		}

		static void CancelJobs()
		{
			job?.Cancel();
			job = null;
			fillJob?.Cancel();
			fillJob = null;
		}

		public static void DrawMenu()
		{
			MenuHelper.DrawBackgroundOverlay();
			Draw.ID panelID = UI.ReservePanel();
			PollJobs();

			using (UI.BeginBoundsScope(true))
			{
				float width = UI.Width * 0.7f;
				Vector2 topLeft = new(UI.Centre.x - width / 2, UI.Height * 0.92f);

				UI.DrawText("TEST BENCH: " + (CanEdit ? chipName : "Untitled"), ActiveUITheme.FontBold, ActiveUITheme.FontSizeRegular, topLeft, Anchor.TopLeft, Color.white);
				topLeft = UI.PrevBounds.BottomLeft + Vector2.down * 1f;

				if (!CanEdit)
				{
					UI.DrawText("Save the chip first", ActiveUITheme.FontRegular, ActiveUITheme.FontSizeRegular, topLeft, Anchor.TopLeft, Color.white);
					topLeft = UI.PrevBounds.BottomLeft + Vector2.down * 1.5f;
					bool closeOnly = UI.Button("CLOSE", MenuHelper.Theme.ButtonTheme, topLeft, new Vector2(width, 0), true, false, true, Anchor.TopLeft);
					MenuHelper.DrawReservedMenuPanel(panelID, UI.GetCurrentBoundsScope());
					if (closeOnly || KeyboardShortcuts.CancelShortcutTriggered) UIDrawer.SetActiveMenu(UIDrawer.MenuType.None);
					return;
				}

				UI.DrawText(Truncate(pinSummary, width), ActiveUITheme.FontRegular, ActiveUITheme.FontSizeRegular, topLeft, Anchor.TopLeft, dimCol);
				topLeft = UI.PrevBounds.BottomLeft + Vector2.down * 0.8f;

				UI.DrawText(Truncate(GetSummaryLine(), width), ActiveUITheme.FontRegular, ActiveUITheme.FontSizeRegular, topLeft, Anchor.TopLeft, Color.white);
				topLeft = UI.PrevBounds.BottomLeft + Vector2.down * 0.8f;

				HandleLineKeys();

				Vector2 scrollSize = new(width, UI.Height * 0.42f);
				scrollViewBounds = Bounds2D.CreateFromTopLeftAndSize(topLeft, scrollSize);
				statusCellWidth = width * 0.3f;
				ScrollToPendingRow(scrollSize.y);
				UI.DrawScrollView(ID_Scrollbar, topLeft, scrollSize, 0, Anchor.TopLeft, ActiveUITheme.ScrollTheme, drawRow, rowCount);
				topLeft = UI.PrevBounds.BottomLeft + Vector2.down * 0.8f;

				string footer = GetFocusedResultText();
				UI.DrawText(Truncate(footer, width), ActiveUITheme.FontRegular, ActiveUITheme.FontSizeRegular, topLeft, Anchor.TopLeft, footer.StartsWith("PASS") ? passCol : failCol);
				topLeft = UI.PrevBounds.BottomLeft + Vector2.down * 1f;

				bool busy = Busy;
				int row1 = MenuHelper.DrawButtonTriplet("RUN", "ADD LINE", "DELETE LINE", topLeft, width, false, !busy, !busy, !busy && rowCount > 0);
				topLeft = UI.PrevBounds.BottomLeft + Vector2.down * 0.5f;
				int row2 = MenuHelper.DrawButtonTriplet("COPY ALL", "PASTE ALL", fillConfirmPending ? FillConfirmText : "FILL FROM CURRENT", topLeft, width, false, true, !busy, !busy);
				topLeft = UI.PrevBounds.BottomLeft + Vector2.down * 0.5f;
				bool close = UI.Button("CLOSE", MenuHelper.Theme.ButtonTheme, topLeft, new Vector2(width, 0), true, false, true, Anchor.TopLeft);

				MenuHelper.DrawReservedMenuPanel(panelID, UI.GetCurrentBoundsScope());

				if (row1 >= 0 || row2 >= 0)
				{
					if (row2 != 2) fillConfirmPending = false;
					if (row1 == 0) Run();
					else if (row1 == 1) AddLine();
					else if (row1 == 2) DeleteLine();
					else if (row2 == 0) CopyAll();
					else if (row2 == 1) PasteAll();
					else if (row2 == 2) FillFromCurrent();
				}

				if (close || KeyboardShortcuts.CancelShortcutTriggered) UIDrawer.SetActiveMenu(UIDrawer.MenuType.None);
			}
		}

		static void DrawRow(Vector2 topLeft, float width, int index, bool isLayoutPass)
		{
			Vector2 size = new(width, RowHeight);
			Bounds2D bounds = Bounds2D.CreateFromTopLeftAndSize(topLeft, size);

			if (!isLayoutPass && bounds.Overlaps(scrollViewBounds))
			{
				InputFieldState state = UI.GetInputFieldState(LineHandle(index));
				if (state.focused) focusedRow = index;

				float fieldWidth = width - statusCellWidth;
				InputFieldTheme theme = MenuHelper.Theme.ChipNameInputField;
				theme.fontSize = MenuHelper.Theme.FontSizeRegular;
				theme.bgCol = state.focused ? new Color(0.25f, 0.4f, 0.27f) : (index % 2 == 0 ? ColHelper.MakeCol(0.17f) : ColHelper.MakeCol(0.13f));
				theme.focusBorderCol = Color.clear;
				string textBefore = state.text;
				UI.InputField(LineHandle(index), theme, topLeft, new Vector2(fieldWidth, RowHeight), string.Empty, Anchor.TopLeft, 0.6f, lineValidator);
				if (state.text != textBefore)
				{
					// Edits are ignored while a fill is running (its result would replace them)
					if (fillJob != null) state.SetText(textBefore, state.focused);
					else fillConfirmPending = false;
				}

				string result = GetResultFor(index, state.text);
				if (result != null)
				{
					bool pass = result == "PASS";
					Vector2 textPos = topLeft + new Vector2(fieldWidth + 0.7f, -RowHeight / 2);
					UI.DrawText(TruncateChars(result, statusCellWidth - 1f), ActiveUITheme.FontRegular, ActiveUITheme.FontSizeRegular, textPos, Anchor.TextCentreLeft, pass ? passCol : failCol);
				}
			}

			UI.OverridePreviousBounds(bounds);
		}

		static bool ValidateLine(string text)
		{
			if (text.Length > MaxLineLength) return false;
			foreach (char c in text)
			{
				if (char.IsControl(c)) return false;
			}

			return true;
		}

		// Up/Down move between lines, Enter adds a line below. Only while a line is focused (so typing never triggers anything else)
		static void HandleLineKeys()
		{
			if (focusedRow < 0 || focusedRow >= rowCount || !UI.GetInputFieldState(LineHandle(focusedRow)).focused)
			{
				focusedRow = -1;
				return;
			}

			if (KeyboardShortcuts.ConfirmShortcutTriggered && !Busy)
			{
				AddLine();
				return;
			}

			int move = 0;
			if (InputHelper.IsKeyDownThisFrame(KeyCode.UpArrow)) move = -1;
			else if (InputHelper.IsKeyDownThisFrame(KeyCode.DownArrow)) move = 1;

			int target = focusedRow + move;
			if (move != 0 && target >= 0 && target < rowCount)
			{
				UI.GetInputFieldState(LineHandle(focusedRow)).SetFocus(false);
				UI.GetInputFieldState(LineHandle(target)).SetFocus(true);
				focusedRow = target;
				pendingScrollRow = target;
			}
		}

		static void ScrollToPendingRow(float viewHeight)
		{
			if (pendingScrollRow < 0) return;

			float visibleHeight = viewHeight - ActiveUITheme.ScrollTheme.padding * 2;
			float top = pendingScrollRow * RowHeight;
			ScrollBarState scroll = UI.GetScrollbarState(ID_Scrollbar);
			if (top < scroll.scrollY) scroll.scrollY = top;
			else if (top + RowHeight > scroll.scrollY + visibleHeight) scroll.scrollY = top + RowHeight - visibleHeight;
			pendingScrollRow = -1;
		}

		// ---- Lines ----

		static List<string> ReadLines()
		{
			List<string> lines = new(rowCount);
			for (int i = 0; i < rowCount; i++) lines.Add(UI.GetInputFieldState(LineHandle(i)).text);
			return lines;
		}

		static void WriteLines(List<string> lines, int focusIndex)
		{
			rowCount = lines.Count;
			for (int i = 0; i < rowCount; i++) UI.GetInputFieldState(LineHandle(i)).SetText(lines[i], i == focusIndex);
			focusedRow = focusIndex;
			pendingScrollRow = focusIndex;
			ClearResults();
		}

		static void AddLine()
		{
			if (rowCount >= MaxLines)
			{
				statusText = $"At most {MaxLines} lines";
				return;
			}

			List<string> lines = ReadLines();
			int at = focusedRow >= 0 ? focusedRow + 1 : lines.Count;
			lines.Insert(at, string.Empty);
			WriteLines(lines, at);
		}

		static void DeleteLine()
		{
			List<string> lines = ReadLines();
			if (lines.Count == 0) return;
			int at = focusedRow >= 0 && focusedRow < lines.Count ? focusedRow : lines.Count - 1;
			lines.RemoveAt(at);
			WriteLines(lines, lines.Count == 0 ? -1 : Mathf.Min(at, lines.Count - 1));
		}

		static void CopyAll()
		{
			InputHelper.CopyToClipboard(string.Join("\n", ReadLines()));
			statusText = "Copied all lines";
		}

		static void PasteAll()
		{
			List<string> lines = new();
			foreach (string line in StringHelper.SplitByLine(InputHelper.GetClipboardContents() ?? string.Empty))
			{
				string clean = line.Length > MaxLineLength ? line.Substring(0, MaxLineLength) : line;
				lines.Add(clean.Replace('\t', ' '));
			}

			while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
			if (lines.Count == 0)
			{
				statusText = "The clipboard is empty";
				return;
			}

			if (lines.Count > MaxLines) lines.RemoveRange(MaxLines, lines.Count - MaxLines);
			WriteLines(lines, -1);
			statusText = $"Pasted {lines.Count} lines";
		}

		static void SaveLines(List<string> lines)
		{
			if (!CanEdit) return;
			if (loadFailed && string.Join("\n", lines) == loadedSnapshot) return;
			try
			{
				TestBenchStore.Save(projectName, chipName, lines);
				loadFailed = false;
			}
			catch (Exception e)
			{
				statusText = "Could not save tests: " + e.Message;
			}
		}

		// ---- Running ----

		static void Run()
		{
			List<string> lines = ReadLines();
			SaveLines(lines);
			ClearResults();
			statusText = string.Empty;

			try
			{
				TestBenchJob newJob = new(chip, Project.ActiveProject.chipLibrary, lines);
				if (!newJob.CanRun(out string reason))
				{
					statusText = reason;
					return;
				}

				resultLines = lines.ToArray();
				job = newJob;
				Simulator.EnqueueEvaluationJob(newJob);
			}
			catch (Exception e)
			{
				statusText = "Can't run the tests: " + e.Message;
			}
		}

		static void FillFromCurrent()
		{
			statusText = string.Empty;
			if ((loadFailed || HasExistingLines()) && !fillConfirmPending)
			{
				fillConfirmPending = true;
				statusText = loadFailed
					? "CONFIRM: the test file couldn't be read, so FILL would replace it. Press again to confirm"
					: "This replaces the existing lines. Press again to confirm";
				return;
			}

			fillConfirmPending = false;

			int inputBits = 0;
			foreach (PinDescription pin in chip.InputPins) inputBits += (int)pin.BitCount;
			if (inputBits > MaxFillInputBits)
			{
				statusText = $"FILL FROM CURRENT needs at most {MaxFillInputBits} input bits (this chip has {inputBits})";
				return;
			}

			try
			{
				TruthTableJob newJob = new(chip, Project.ActiveProject.chipLibrary);
				if (!newJob.CanRun(out string reason))
				{
					statusText = reason.StartsWith("This chip has memory") ? "FILL FROM CURRENT only works for combinational chips" : reason;
					return;
				}

				fillJob = newJob;
				Simulator.EnqueueEvaluationJob(newJob);
			}
			catch (Exception e)
			{
				statusText = "Can't fill from the chip: " + e.Message;
			}
		}

		static bool HasExistingLines()
		{
			foreach (string line in ReadLines())
			{
				string trimmed = line.Trim();
				if (trimmed.Length > 0 && trimmed != Header) return true;
			}

			return false;
		}

		static void PollJobs()
		{
			if (job is { IsFinished: true })
			{
				TestBenchJob finished = job;
				job = null;
				if (finished.IsCancelled) ClearResults();
				else if (finished.Error != null)
				{
					ClearResults();
					statusText = "Could not run the tests: " + finished.Error;
				}
				else
				{
					resultTexts = finished.LineResults;
					resultPassed = finished.Passed;
					resultFailed = finished.Failed;
					resultWarning = finished.Warning;
					resultErrors = 0;
					foreach (string r in resultTexts)
					{
						if (r != null && r.StartsWith("ERROR")) resultErrors++;
					}
				}
			}

			if (fillJob is { IsFinished: true })
			{
				TruthTableJob finished = fillJob;
				fillJob = null;
				if (finished.IsCancelled) return;

				if (finished.Error != null || finished.Table.RowsFilled != finished.Table.RowCount)
				{
					statusText = "Could not evaluate the chip: " + (finished.Error ?? "incomplete");
					return;
				}

				List<string> lines = new(finished.Table.RowCount);
				for (int row = 0; row < finished.Table.RowCount; row++) lines.Add(TestVectorWriter.FromTruthTableRow(finished.Table, row));
				WriteLines(lines, -1);
				loadFailed = false; // the user confirmed replacing the unreadable file
				SaveLines(lines);
				statusText = $"Filled {lines.Count} lines from the chip's current outputs";
			}
		}

		static void ClearResults()
		{
			resultTexts = null;
			resultWarning = null;
		}

		static string GetResultFor(int index, string currentText)
		{
			if (resultTexts == null || resultLines == null || index >= resultTexts.Length || index >= resultLines.Length) return null;
			return resultLines[index] == currentText ? resultTexts[index] : null;
		}

		static string GetFocusedResultText()
		{
			if (focusedRow < 0 || focusedRow >= rowCount) return string.Empty;
			return GetResultFor(focusedRow, UI.GetInputFieldState(LineHandle(focusedRow)).text) ?? string.Empty;
		}

		static string GetSummaryLine()
		{
			if (job != null) return $"Running... {Mathf.RoundToInt(job.Progress * 100)}%";
			if (fillJob != null) return $"Filling... {Mathf.RoundToInt(fillJob.Progress * 100)}%";

			StringBuilder sb = new();
			if (resultTexts != null)
			{
				sb.Append($"{resultPassed} passed, {resultFailed} failed");
				if (resultErrors > 0) sb.Append($", {resultErrors} with errors");
				if (resultWarning != null) sb.Append("   Warning: ").Append(resultWarning);
			}

			if (statusText.Length > 0)
			{
				if (sb.Length > 0) sb.Append("   ");
				sb.Append(statusText);
			}

			return sb.ToString();
		}

		static string DescribePins(ChipDescription desc)
		{
			StringBuilder sb = new("Inputs: ");
			AppendPins(sb, desc.InputPins);
			sb.Append("   Outputs: ");
			AppendPins(sb, desc.OutputPins);
			sb.Append("   (or in1.. / out1..)");
			return sb.ToString();
		}

		static void AppendPins(StringBuilder sb, PinDescription[] pins)
		{
			for (int i = 0; i < pins.Length; i++)
			{
				if (i > 0) sb.Append(", ");
				sb.Append(pins[i].Name);
				if (pins[i].BitCount != PinBitCount.Bit1) sb.Append('[').Append((int)pins[i].BitCount).Append(']');
			}

			if (pins.Length == 0) sb.Append('-');
		}

		static string Truncate(string text, float width) => TruncateChars(text, width);

		static string TruncateChars(string text, float width)
		{
			int maxChars = Mathf.Max(4, Mathf.FloorToInt(width / (ActiveUITheme.FontSizeRegular * 0.55f)));
			return text.Length <= maxChars ? text : text.Substring(0, maxChars - 3) + "...";
		}
	}
}
