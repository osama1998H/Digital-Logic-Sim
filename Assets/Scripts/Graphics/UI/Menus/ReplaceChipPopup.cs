using System;
using System.Collections.Generic;
using System.Linq;
using DLS.Description;
using DLS.Game;
using DLS.Game.Editing;
using Seb.Types;
using Seb.Vis;
using Seb.Vis.UI;
using UnityEngine;
using static DLS.Graphics.DrawSettings;

namespace DLS.Graphics
{
	// Lets the player pick a chip (with compatible pins) to replace one or more subchips with
	public static class ReplaceChipPopup
	{
		static readonly UIHandle ID_SearchInput = new("ReplaceChipPopup_SearchInput");
		static readonly UIHandle ID_Scrollbar = new("ReplaceChipPopup_Scrollbar");
		static readonly Func<string, bool> searchStringValidator = ValidateSearchInput;
		static readonly UI.ScrollViewDrawElementFunc drawEntry = DrawEntry;

		static List<SubChipInstance> targets = new();
		static string sourceChipName;
		static string[] compatibleChipNames = Array.Empty<string>();
		static string[] filteredChipNames = Array.Empty<string>();
		static int menuOpenedFrame;

		// Must be set before the menu is opened (opening a menu clears the current selection)
		public static void SetTargets(IEnumerable<SubChipInstance> subChips)
		{
			targets = subChips.ToList();
			sourceChipName = targets.Count > 0 ? targets[0].Description.Name : string.Empty;
		}

		public static void OnMenuOpened()
		{
			menuOpenedFrame = Time.frameCount;
			UI.GetInputFieldState(ID_SearchInput).ClearText();

			compatibleChipNames = targets.Count == 0 ? Array.Empty<string>() : SubChipReplacer.CompatibleChipNames(targets[0].Description).ToArray();
			Filter(string.Empty);
		}

		public static void DrawMenu()
		{
			MenuHelper.DrawBackgroundOverlay();
			Draw.ID panelID = UI.ReservePanel();

			using (UI.BeginBoundsScope(true))
			{
				InputFieldTheme inputTheme = MenuHelper.Theme.ChipNameInputField;
				(Vector2 inputFieldSize, float inputFieldTextPad) = ChipSaveMenu.GetTextInputSize();
				inputFieldSize.x += 6f;

				float width = inputFieldSize.x;
				Vector2 topLeft = new(UI.Centre.x - width / 2, UI.Height * 0.93f);

				string title = targets.Count == 1 ? $"REPLACE {sourceChipName} WITH:" : $"REPLACE {targets.Count} x {sourceChipName} WITH:";
				UI.DrawText(title, ActiveUITheme.FontBold, ActiveUITheme.FontSizeRegular, topLeft, Anchor.TopLeft, Color.white);
				topLeft = UI.PrevBounds.BottomLeft + Vector2.down * 1;

				UI.InputField(ID_SearchInput, inputTheme, topLeft, inputFieldSize, string.Empty, Anchor.TopLeft, inputFieldTextPad, searchStringValidator, true);
				topLeft = UI.PrevBounds.BottomLeft + Vector2.down * 2;

				if (compatibleChipNames.Length == 0)
				{
					UI.DrawText("No chips with matching pins", ActiveUITheme.FontRegular, ActiveUITheme.FontSizeRegular, topLeft, Anchor.TopLeft, Color.white);
					topLeft = UI.PrevBounds.BottomLeft + Vector2.down * 2;
				}
				else
				{
					UI.DrawScrollView(ID_Scrollbar, topLeft, new Vector2(width, UI.Height * 0.65f), UILayoutHelper.DefaultSpacing, Anchor.TopLeft, ActiveUITheme.ScrollTheme, drawEntry, filteredChipNames.Length);
					topLeft = UI.PrevBounds.BottomLeft + Vector2.down * 1;
				}

				bool cancel = UI.Button("CANCEL", ActiveUITheme.ButtonTheme, topLeft, new Vector2(width, 0), true, false, true, Anchor.TopLeft);

				MenuHelper.DrawReservedMenuPanel(panelID, UI.GetCurrentBoundsScope());

				if (cancel || KeyboardShortcuts.CancelShortcutTriggered) Close();
				else if (KeyboardShortcuts.ConfirmShortcutTriggered && filteredChipNames.Length > 0) ReplaceWith(filteredChipNames[0]);
			}
		}

		static void DrawEntry(Vector2 topLeft, float width, int index, bool isLayoutPass)
		{
			Bounds2D entryBounds = Bounds2D.CreateFromTopLeftAndSize(topLeft, new Vector2(width, ButtonHeight));
			bool offscreen = entryBounds.Top < 0 || entryBounds.Bottom > UI.Height;

			if (!isLayoutPass && !offscreen)
			{
				string chipName = filteredChipNames[index];
				const float nameWidth = 22f;

				UI.Button(chipName, ActiveUITheme.ChipLibraryChipToggleOn, topLeft, new Vector2(nameWidth, ButtonHeight), true, false, false, Anchor.TopLeft, true, 1, true);

				Vector2 buttonTopLeft = topLeft + Vector2.right * (nameWidth + DefaultButtonSpacing);
				float buttonWidth = width - (buttonTopLeft.x - topLeft.x);
				if (UI.Button("REPLACE", ActiveUITheme.ButtonTheme, buttonTopLeft, new Vector2(buttonWidth, ButtonHeight), true, false, false, Anchor.TopLeft))
				{
					ReplaceWith(chipName);
				}
			}

			UI.OverridePreviousBounds(entryBounds);
		}

		static void ReplaceWith(string chipName)
		{
			List<SubChipInstance> subChips = targets;
			Close();
			SubChipReplacer.Replace(subChips, chipName);
		}

		static void Filter(string searchString)
		{
			if (string.IsNullOrWhiteSpace(searchString))
			{
				filteredChipNames = compatibleChipNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
				return;
			}

			// Names starting with the search string first, then names containing it
			IEnumerable<string> startsWith = compatibleChipNames.Where(n => n.StartsWith(searchString, ChipDescription.NameComparison)).OrderBy(n => n.Length);
			IEnumerable<string> contains = compatibleChipNames.Where(n => !n.StartsWith(searchString, ChipDescription.NameComparison) && n.Contains(searchString, ChipDescription.NameComparison)).OrderBy(n => n.Length);
			filteredChipNames = startsWith.Concat(contains).ToArray();
		}

		static bool ValidateSearchInput(string text)
		{
			if (Time.frameCount == menuOpenedFrame) return false;
			if (!ChipSaveMenu.ValidateChipNameInput(text)) return false;

			Filter(text.Trim());
			return true;
		}

		static void Close()
		{
			targets = new List<SubChipInstance>();
			UIDrawer.SetActiveMenu(UIDrawer.MenuType.None);
		}
	}
}
