using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DLS.Description;
using DLS.Game;
using DLS.Game.Stats;
using Seb.Helpers;
using Seb.Types;
using Seb.Vis;
using Seb.Vis.UI;
using UnityEngine;
using static DLS.Graphics.DrawSettings;

namespace DLS.Graphics
{
	public static class ChipStatsMenu
	{
		static readonly UIHandle ID_Scrollbar = new("ChipStatsMenu_Scrollbar");
		static readonly UI.ScrollViewDrawElementFunc drawRow = DrawRow;
		const float RowHeight = 1.8f;

		static string title;
		static string nandLine;
		static string pathLine;
		static string otherLine;
		static string copyText;
		static readonly List<(string Name, string Count, string Each, string Total)> rows = new();

		public static void Open(ChipDescription chip)
		{
			string name = string.IsNullOrWhiteSpace(chip.Name) ? "Untitled" : chip.Name;
			title = "STATISTICS: " + name;
			rows.Clear();
			otherLine = string.Empty;

			if (chip.ChipType != ChipType.Custom)
			{
				bool isNand = chip.ChipType == ChipType.Nand;
				nandLine = isNand ? "NAND gates: 1" : "Built-in part: no NAND gates";
				pathLine = isNand ? "Longest path: 1 gate delay" : string.Empty;
			}
			else
			{
				ChipStatsResult result = ChipStatsCalculator.Calculate(ChipStatsAdapter.ToDef(chip), ChipStatsAdapter.Library(Project.ActiveProject.chipLibrary));
				nandLine = "NAND gates: " + Format(result.TotalNands);
				if (result.HasFeedback || result.LongestPath == null) pathLine = "Longest path: not defined: contains a feedback loop (memory)";
				else pathLine = "Longest path: " + result.LongestPath.Value + (result.LongestPath.Value == 1 ? " gate delay" : " gate delays");

				foreach ((string n, long count, long each) in result.Breakdown) rows.Add((n, Format(count), Format(each), Format(count * each)));

				if (result.OtherParts.Count > 0)
				{
					StringBuilder other = new("Other parts: ");
					for (int i = 0; i < result.OtherParts.Count; i++)
					{
						if (i > 0) other.Append(", ");
						other.Append(Format(result.OtherParts[i].Count)).Append(" x ").Append(result.OtherParts[i].Name);
					}

					otherLine = other.ToString();
				}
			}

			copyText = BuildCopyText();
			UIDrawer.SetActiveMenu(UIDrawer.MenuType.ChipStats);
		}

		public static void OnMenuOpened()
		{
			UI.GetScrollbarState(ID_Scrollbar).scrollY = 0;
		}

		static string Format(long n) => n.ToString("N0", CultureInfo.InvariantCulture);

		static string BuildCopyText()
		{
			StringBuilder sb = new();
			sb.AppendLine(title);
			sb.AppendLine(nandLine);
			if (pathLine.Length > 0) sb.AppendLine(pathLine);
			if (rows.Count > 0)
			{
				sb.AppendLine();
				sb.AppendLine("Chip\tCount\tNANDs each\tNANDs total");
				foreach (var r in rows) sb.AppendLine($"{r.Name}\t{r.Count}\t{r.Each}\t{r.Total}");
			}

			if (otherLine.Length > 0)
			{
				sb.AppendLine();
				sb.AppendLine(otherLine);
			}

			return sb.ToString();
		}

		public static void DrawMenu()
		{
			MenuHelper.DrawBackgroundOverlay();
			Draw.ID panelID = UI.ReservePanel();

			using (UI.BeginBoundsScope(true))
			{
				float width = UI.Width * 0.5f;
				Vector2 topLeft = new(UI.Centre.x - width / 2, UI.Height * 0.9f);

				UI.DrawText(title, ActiveUITheme.FontBold, ActiveUITheme.FontSizeRegular, topLeft, Anchor.TopLeft, Color.white);
				topLeft = UI.PrevBounds.BottomLeft + Vector2.down * 1.5f;
				UI.DrawText(nandLine, ActiveUITheme.FontRegular, ActiveUITheme.FontSizeRegular, topLeft, Anchor.TopLeft, Color.white);
				topLeft = UI.PrevBounds.BottomLeft + Vector2.down * 0.8f;

				if (pathLine.Length > 0)
				{
					UI.DrawText(pathLine, ActiveUITheme.FontRegular, ActiveUITheme.FontSizeRegular, topLeft, Anchor.TopLeft, Color.white);
					topLeft = UI.PrevBounds.BottomLeft + Vector2.down * 1.5f;
				}

				if (rows.Count > 0)
				{
					DrawColumns("Chip", "Count", "NANDs each", "NANDs total", topLeft, width, new Color(0.7f, 0.7f, 0.7f));
					topLeft = UI.PrevBounds.BottomLeft + Vector2.down * 0.5f;
					UI.DrawScrollView(ID_Scrollbar, topLeft, new Vector2(width, UI.Height * 0.45f), 0, Anchor.TopLeft, ActiveUITheme.ScrollTheme, drawRow, rows.Count);
					topLeft = UI.PrevBounds.BottomLeft + Vector2.down * 1f;
				}

				if (otherLine.Length > 0)
				{
					UI.DrawText(otherLine, ActiveUITheme.FontRegular, ActiveUITheme.FontSizeRegular, topLeft, Anchor.TopLeft, Color.white);
					topLeft = UI.PrevBounds.BottomLeft + Vector2.down * 1.5f;
				}

				topLeft += Vector2.down * 0.5f;
				int pressed = MenuHelper.DrawButtonPair("COPY", "CLOSE", topLeft, width, false);

				MenuHelper.DrawReservedMenuPanel(panelID, UI.GetCurrentBoundsScope());

				if (pressed == 0) InputHelper.CopyToClipboard(copyText);
				else if (pressed == 1 || KeyboardShortcuts.CancelShortcutTriggered || KeyboardShortcuts.ConfirmShortcutTriggered) UIDrawer.SetActiveMenu(UIDrawer.MenuType.None);
			}
		}

		static void DrawColumns(string a, string b, string c, string d, Vector2 topLeft, float width, Color col)
		{
			float fontSize = ActiveUITheme.FontSizeRegular;
			UI.DrawText(a, ActiveUITheme.FontRegular, fontSize, topLeft, Anchor.TopLeft, col);
			UI.DrawText(b, ActiveUITheme.FontRegular, fontSize, topLeft + Vector2.right * (width * 0.6f), Anchor.TopRight, col);
			UI.DrawText(c, ActiveUITheme.FontRegular, fontSize, topLeft + Vector2.right * (width * 0.8f), Anchor.TopRight, col);
			UI.DrawText(d, ActiveUITheme.FontRegular, fontSize, topLeft + Vector2.right * width, Anchor.TopRight, col);
			UI.OverridePreviousBounds(Bounds2D.CreateFromTopLeftAndSize(topLeft, new Vector2(width, RowHeight)));
		}

		static void DrawRow(Vector2 topLeft, float width, int index, bool isLayoutPass)
		{
			Bounds2D bounds = Bounds2D.CreateFromTopLeftAndSize(topLeft, new Vector2(width, RowHeight));
			if (!isLayoutPass && bounds.Top >= 0 && bounds.Bottom <= UI.Height)
			{
				var r = rows[index];
				DrawColumns(r.Name, r.Count, r.Each, r.Total, topLeft, width - 1.5f, Color.white);
			}

			UI.OverridePreviousBounds(bounds);
		}
	}
}
