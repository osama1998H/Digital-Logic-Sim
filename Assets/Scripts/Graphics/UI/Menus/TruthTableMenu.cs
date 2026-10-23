using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DLS.Description;
using DLS.Game;
using DLS.Game.Inspect;
using DLS.Game.Logic;
using DLS.SaveSystem;
using DLS.Simulation;
using Seb.Helpers;
using Seb.Types;
using Seb.Vis;
using Seb.Vis.UI;
using UnityEngine;
using static DLS.Graphics.DrawSettings;

namespace DLS.Graphics
{
	public static class TruthTableMenu
	{
		static readonly UIHandle ID_Scrollbar = new("TruthTableMenu_Scrollbar");
		static readonly UIHandle ID_Format = new("TruthTableMenu_Format");
		static readonly UI.ScrollViewDrawElementFunc drawRow = DrawRow;
		static readonly string[] formatOptions = { "Binary", "Decimal", "Hex" };
		static readonly Color outputCol = new(0.53f, 0.8f, 0.57f);
		static readonly Color headerCol = new(0.7f, 0.7f, 0.7f);
		const float RowHeight = 1.8f;

		static string chipName;
		static string cannotRunReason;
		static TruthTableJob job;
		static TruthTable table;
		static bool resultsPrepared;
		static NumberFormat format;
		static string statusLine;
		static bool showExpression;
		static string expressionUnavailableReason;
		static string expressionText;
		static float cellWidth;

		public static void Open(ChipDescription chip)
		{
			CancelJob();

			chipName = string.IsNullOrWhiteSpace(chip.Name) ? "Untitled" : chip.Name;
			statusLine = string.Empty;
			showExpression = false;
			resultsPrepared = false;
			expressionText = null;
			expressionUnavailableReason = null;
			table = null;

			job = new TruthTableJob(chip, Project.ActiveProject.chipLibrary);
			if (job.CanRun(out cannotRunReason))
			{
				cannotRunReason = null;
				table = job.Table;
				Simulator.EnqueueEvaluationJob(job);
			}
			else
			{
				job = null;
			}

			UIDrawer.SetActiveMenu(UIDrawer.MenuType.TruthTable);
		}

		public static void OnMenuOpened()
		{
			UI.GetScrollbarState(ID_Scrollbar).scrollY = 0;
			UI.GetWheelSelectorState(ID_Format).index = 0;
		}

		// Called by UIDrawer whenever the menu stops being the active one, whatever the reason
		public static void OnMenuClosed() => CancelJob();

		static void CancelJob()
		{
			job?.Cancel();
			job = null;
		}

		static bool IsDone => job is { IsFinished: true, IsCancelled: false, Error: null } && table.RowsFilled == table.RowCount;

		public static void DrawMenu()
		{
			MenuHelper.DrawBackgroundOverlay();
			Draw.ID panelID = UI.ReservePanel();

			if (IsDone && !resultsPrepared) PrepareResults();

			using (UI.BeginBoundsScope(true))
			{
				float width = UI.Width * 0.6f;
				Vector2 topLeft = new(UI.Centre.x - width / 2, UI.Height * 0.9f);

				UI.DrawText("TRUTH TABLE: " + chipName, ActiveUITheme.FontBold, ActiveUITheme.FontSizeRegular, topLeft, Anchor.TopLeft, Color.white);
				topLeft = UI.PrevBounds.BottomLeft + Vector2.down * 1.5f;

				string line = GetStatusLine();
				if (line.Length > 0)
				{
					UI.DrawText(line, ActiveUITheme.FontRegular, ActiveUITheme.FontSizeRegular, topLeft, Anchor.TopLeft, Color.white);
					topLeft = UI.PrevBounds.BottomLeft + Vector2.down * 1f;
				}

				bool done = IsDone;
				if (done)
				{
					format = (NumberFormat)UI.WheelSelector(ID_Format, formatOptions, topLeft, new Vector2(width, SelectorWheelHeight), MenuHelper.Theme.OptionsWheel, Anchor.TopLeft);
					topLeft = UI.PrevBounds.BottomLeft + Vector2.down * 1f;

					if (showExpression) DrawExpression(topLeft, width);
					else DrawTable(topLeft, width);

					topLeft = UI.PrevBounds.BottomLeft + Vector2.down * 1f;
				}
				else
				{
					topLeft += Vector2.down * 1f;
				}

				int triplet = MenuHelper.DrawButtonTriplet("COPY CSV", "SAVE CSV", showExpression ? "TABLE" : "EXPRESSION", topLeft, width, false, done, done, done && expressionUnavailableReason == null);
				topLeft = UI.PrevBounds.BottomLeft + Vector2.down * 0.5f;

				if (done && expressionUnavailableReason != null)
				{
					UI.DrawText("Expression unavailable: " + expressionUnavailableReason, ActiveUITheme.FontRegular, ActiveUITheme.FontSizeRegular, topLeft, Anchor.TopLeft, headerCol);
					topLeft = UI.PrevBounds.BottomLeft + Vector2.down * 1f;
				}

				int pair = MenuHelper.DrawButtonPair("COPY EXPRESSION", "CLOSE", topLeft, width, false, done && showExpression);

				MenuHelper.DrawReservedMenuPanel(panelID, UI.GetCurrentBoundsScope());

				if (triplet == 0) InputHelper.CopyToClipboard(TruthTableCsv.ToCsv(table, format));
				else if (triplet == 1) SaveCsv();
				else if (triplet == 2) showExpression = !showExpression;

				if (pair == 0) InputHelper.CopyToClipboard(expressionText);

				if (pair == 1 || KeyboardShortcuts.CancelShortcutTriggered)
				{
					CancelJob();
					UIDrawer.SetActiveMenu(UIDrawer.MenuType.None);
				}
			}
		}

		static string GetStatusLine()
		{
			if (cannotRunReason != null) return cannotRunReason;
			if (job == null) return statusLine;
			if (job.Error != null) return "Could not evaluate chip: " + job.Error;
			if (!job.IsFinished) return $"Evaluating... {Mathf.RoundToInt(job.Progress * 100)}% ({table.RowsFilled:N0} / {table.RowCount:N0} rows)";
			return statusLine;
		}

		static void DrawTable(Vector2 topLeft, float width)
		{
			int columns = table.Inputs.Length + table.Outputs.Length;
			cellWidth = width / Mathf.Max(1, columns);

			if (columns == 0) return;

			Vector2 pos = topLeft;
			for (int c = 0; c < columns; c++)
			{
				bool isInput = c < table.Inputs.Length;
				string name = isInput ? table.Inputs[c].Name : table.Outputs[c - table.Inputs.Length].Name;
				UI.DrawText(name, ActiveUITheme.FontBold, ActiveUITheme.FontSizeRegular, pos + Vector2.right * (cellWidth * (c + 1) - 0.5f), Anchor.TopRight, isInput ? Color.white : outputCol);
			}

			UI.OverridePreviousBounds(Bounds2D.CreateFromTopLeftAndSize(pos, new Vector2(width, RowHeight)));
			pos = UI.PrevBounds.BottomLeft + Vector2.down * 0.3f;
			UI.DrawScrollView(ID_Scrollbar, pos, new Vector2(width, UI.Height * 0.45f), 0, Anchor.TopLeft, ActiveUITheme.ScrollTheme, drawRow, table.RowCount);
		}

		static void DrawRow(Vector2 topLeft, float width, int index, bool isLayoutPass)
		{
			Bounds2D bounds = Bounds2D.CreateFromTopLeftAndSize(topLeft, new Vector2(width, RowHeight));
			if (!isLayoutPass && bounds.Top >= 0 && bounds.Bottom <= UI.Height)
			{
				int inputCount = table.Inputs.Length;
				for (int c = 0; c < inputCount + table.Outputs.Length; c++)
				{
					bool isInput = c < inputCount;
					string text = isInput
						? PinValueFormat.FormatValue(table.GetInputValue(index, c), table.Inputs[c].BitCount, format)
						: PinValueFormat.FormatValue(table.GetOutputState(index, c - inputCount), table.Outputs[c - inputCount].BitCount, format);
					UI.DrawText(text, ActiveUITheme.FontRegular, ActiveUITheme.FontSizeRegular, topLeft + Vector2.right * (cellWidth * (c + 1) - 1.5f), Anchor.TopRight, isInput ? Color.white : outputCol);
				}
			}

			UI.OverridePreviousBounds(bounds);
		}

		static void DrawExpression(Vector2 topLeft, float width)
		{
			string[] lines = expressionText.Split('\n');
			foreach (string l in lines)
			{
				UI.DrawText(l, ActiveUITheme.FontRegular, ActiveUITheme.FontSizeRegular, topLeft, Anchor.TopLeft, Color.white);
				topLeft = UI.PrevBounds.BottomLeft + Vector2.down * 0.6f;
			}

			UI.OverridePreviousBounds(Bounds2D.CreateFromTopLeftAndSize(topLeft, new Vector2(width, 0.1f)));
		}

		static void SaveCsv()
		{
			try
			{
				string folder = SavePaths.GetExportsPath(Project.ActiveProject.description.ProjectName);
				SavePaths.EnsureDirectoryExists(folder);
				string path = Path.Combine(folder, MakeFileSafe(chipName) + " truth table.csv");
				File.WriteAllText(path, TruthTableCsv.ToCsv(table, format));
				statusLine = "Saved to " + path;
			}
			catch (Exception e)
			{
				statusLine = "Could not save: " + e.Message;
			}
		}

		static string MakeFileSafe(string name)
		{
			foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
			return name;
		}

		// Runs once when the table is complete (the CSV and the minimiser both need every row)
		static void PrepareResults()
		{
			resultsPrepared = true;
			expressionText = null;
			expressionUnavailableReason = null;

			int inputCount = table.Inputs.Length;
			if (inputCount == 0) expressionUnavailableReason = "chip has no inputs";
			else if (inputCount > LogicMinimizer.MaxVariables) expressionUnavailableReason = $"at most {LogicMinimizer.MaxVariables} inputs";
			else
			{
				foreach (TruthPin pin in table.Inputs)
				{
					if (pin.BitCount != 1) expressionUnavailableReason = "inputs must be 1-bit";
				}
			}

			if (expressionUnavailableReason != null) return;

			List<int> outputs = new();
			for (int o = 0; o < table.Outputs.Length; o++)
			{
				if (table.Outputs[o].BitCount == 1) outputs.Add(o);
			}

			if (outputs.Count == 0)
			{
				expressionUnavailableReason = "no 1-bit outputs";
				return;
			}

			string[] pinNames = new string[inputCount];
			for (int i = 0; i < inputCount; i++) pinNames[i] = table.Inputs[i].Name;
			string[] names = SopFormatter.MakeVariableNames(pinNames);

			StringBuilder sb = new();
			foreach (int o in outputs)
			{
				List<int> minterms = new();
				List<int> dontCares = new();
				for (int row = 0; row < table.RowCount; row++)
				{
					uint state = table.GetOutputState(row, o);
					if (((state >> 16) & 1) != 0) dontCares.Add(row);
					else if ((state & 1) != 0) minterms.Add(row);
				}

				string sop = SopFormatter.Format(LogicMinimizer.Minimize(inputCount, minterms, dontCares), names);
				sb.Append(table.Outputs[o].Name).Append(" = ").Append(sop).Append('\n');
			}

			if (!SameNames(names, pinNames))
			{
				for (int i = 0; i < inputCount; i++)
				{
					if (i > 0) sb.Append(", ");
					sb.Append(names[i]).Append(" = ").Append(Ordinal(i)).Append(" input");
				}

				sb.Append('\n');
			}

			expressionText = sb.ToString().TrimEnd('\n');
		}

		static bool SameNames(string[] a, string[] b)
		{
			for (int i = 0; i < a.Length; i++)
			{
				if (a[i] != b[i]) return false;
			}

			return true;
		}

		static string Ordinal(int i)
		{
			string[] words = { "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth" };
			return i < words.Length ? words[i] : (i + 1) + "th";
		}
	}
}
