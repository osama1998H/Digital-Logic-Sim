using System.Collections.Generic;
using System.Linq;
using DLS.Game;
using DLS.Game.Editing;
using Seb.Types;
using Seb.Vis;
using Seb.Vis.UI;
using UnityEngine;

namespace DLS.Graphics
{
	// Renames several dev pins (or labels several subchips) at once, either all with the same name or as a numbered sequence (A0, A1, ...)
	public static class BatchRenameMenu
	{
		const string MaxLengthPinName = "MY LONG PIN NAME";
		const string MaxLengthLabel = "MY LONG LABEL TEXT";
		const int SequenceModeIndex = 0;

		static readonly UIHandle ID_NameField = new("BatchRenameMenu_NameField");
		static readonly UIHandle ID_NamingMode = new("BatchRenameMenu_NamingMode");
		static readonly string[] NamingModeOptions = { "Sequence", "Same name" };
		static readonly string[] CancelConfirmButtonNames = { "CANCEL", "CONFIRM" };
		static readonly bool[] ButtonGroupInteractStates = { true, true };

		static List<IMoveable> targets = new();
		static bool isPinMode;

		static int MaxLength => isPinMode ? MaxLengthPinName.Length : MaxLengthLabel.Length;

		// Must be set before the menu is opened (opening a menu clears the current selection)
		public static void SetTargets(IEnumerable<IMoveable> elements, bool renamePins)
		{
			targets = elements.ToList();
			isPinMode = renamePins;
		}

		public static void OnMenuOpened()
		{
			InputFieldState inputFieldState = UI.GetInputFieldState(ID_NameField);
			inputFieldState.SetText(CommonPrefix(targets.Select(GetName)));
			inputFieldState.SelectAll();
			UI.GetWheelSelectorState(ID_NamingMode).index = SequenceModeIndex;
		}

		public static void DrawMenu()
		{
			UI.DrawFullscreenPanel(DrawSettings.ActiveUITheme.MenuBackgroundOverlayCol);
			const float spacing = 0.8f;

			DrawSettings.UIThemeDLS theme = DrawSettings.ActiveUITheme;
			InputFieldTheme inputTheme = theme.ChipNameInputField;
			Draw.ID panelID = UI.ReservePanel();

			using (UI.BeginBoundsScope(true))
			{
				string title = $"{(isPinMode ? "RENAME" : "LABEL")} {targets.Count} {(isPinMode ? "PINS" : "CHIPS")}";
				Vector2 pos = UI.Centre + Vector2.up * 7;
				MenuHelper.DrawText(title, pos, Anchor.TextCentre, Color.white, true);

				Vector2 unpaddedSize = Draw.CalculateTextBoundsSize(MaxLengthLabel, inputTheme.fontSize, inputTheme.font);
				const float padX = 2.25f;
				Vector2 inputFieldSize = unpaddedSize + new Vector2(padX, 2.25f);

				// Name input
				InputFieldState inputFieldState = UI.InputField(ID_NameField, inputTheme, UI.PrevBounds.CentreBottom + Vector2.down * spacing, inputFieldSize, string.Empty, Anchor.CentreTop, padX / 2, ValidateNameInput, true);
				Bounds2D inputFieldBounds = UI.PrevBounds;
				string baseName = inputFieldState.text;

				// Naming mode
				Vector2 topLeftCurr = inputFieldBounds.BottomLeft + Vector2.down * spacing;
				int modeIndex = MenuHelper.LabeledOptionsWheel("Naming", Color.white, topLeftCurr, new Vector2(inputFieldBounds.Width, DrawSettings.SelectorWheelHeight), ID_NamingMode, NamingModeOptions, 15.2f, true);
				bool sequence = modeIndex == SequenceModeIndex;

				// Preview
				bool valid = !sequence || SequenceNaming.Fits(baseName, targets.Count, MaxLength);
				string preview = valid ? CreatePreview(baseName, sequence) : "Name too long for sequence";
				MenuHelper.DrawText(preview, UI.PrevBounds.CentreBottom + Vector2.down * (spacing + 0.4f), Anchor.TextCentre, valid ? Color.white : Color.red);
				Vector2 buttonsTopLeft = new(inputFieldBounds.Left, UI.PrevBounds.Bottom - spacing);

				// Buttons
				ButtonGroupInteractStates[1] = valid;
				int buttonIndex = UI.HorizontalButtonGroup(CancelConfirmButtonNames, ButtonGroupInteractStates, theme.ButtonTheme, buttonsTopLeft, inputFieldBounds.Width, DrawSettings.DefaultButtonSpacing, 0, Anchor.TopLeft);

				MenuHelper.DrawReservedMenuPanel(panelID, UI.GetCurrentBoundsScope());

				if (KeyboardShortcuts.CancelShortcutTriggered || buttonIndex == 0) Close();
				else if ((KeyboardShortcuts.ConfirmShortcutTriggered && valid) || buttonIndex == 1) Confirm(baseName, sequence);
			}
		}

		static string CreatePreview(string baseName, bool sequence)
		{
			if (!sequence) return $"All named \"{baseName}\"";
			if (targets.Count == 1) return $"{baseName}0";
			string lowName = baseName + 0;
			string highName = baseName + (targets.Count - 1);
			return $"{lowName} (bottom) ... {highName} (top)";
		}

		static void Confirm(string baseName, bool sequence)
		{
			IMoveable[] ordered = OrderedTargets();
			string[] newNames = sequence ? SequenceNaming.Names(baseName, ordered.Length) : Enumerable.Repeat(baseName, ordered.Length).ToArray();
			string[] oldNames = ordered.Select(GetName).ToArray();

			for (int i = 0; i < ordered.Length; i++)
			{
				SetName(ordered[i], newNames[i]);
			}

			if (!oldNames.SequenceEqual(newNames))
			{
				Project.ActiveProject.ViewedChip.UndoController.RecordRename(ordered.Select(e => e.ID).ToArray(), oldNames, newNames);
			}

			Close();

			// Restore the selection so that it can be arranged etc. straight away
			ChipInteractionController controller = Project.ActiveProject.controller;
			foreach (IMoveable element in ordered) controller.Select(element, true);
		}

		static IMoveable[] OrderedTargets()
		{
			// Pins are ordered exactly in reverse of their saved order, so that the bottom pin is the lowest bit (index 0).
			// Chips are allowed some vertical slack so that a row of chips that isn't perfectly lined up is still numbered right to left.
			float rowTolerance = isPinMode ? 0 : DrawSettings.GridSize;
			PositionedItem[] items = targets.Select(e => new PositionedItem(e.ID, e.Position.x, e.Position.y)).ToArray();
			Dictionary<int, IMoveable> lookup = targets.ToDictionary(e => e.ID, e => e);
			return SequenceNaming.OrderForSequence(items, rowTolerance).Select(id => lookup[id]).ToArray();
		}

		public static string GetName(IMoveable element)
		{
			return element switch
			{
				DevPinInstance devPin => devPin.Pin.Name,
				SubChipInstance subChip => subChip.Label,
				_ => string.Empty
			};
		}

		static void SetName(IMoveable element, string name)
		{
			if (element is DevPinInstance devPin) devPin.Pin.Name = name;
			else if (element is SubChipInstance subChip) subChip.Label = name;
		}

		// e.g. for pins named "A0", "A1", "A2" suggest "A"
		static string CommonPrefix(IEnumerable<string> names)
		{
			string prefix = null;
			foreach (string name in names)
			{
				string n = name ?? string.Empty;
				if (prefix == null)
				{
					prefix = n;
					continue;
				}

				int len = 0;
				while (len < prefix.Length && len < n.Length && prefix[len] == n[len]) len++;
				prefix = prefix.Substring(0, len);
			}

			return (prefix ?? string.Empty).TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
		}

		static bool ValidateNameInput(string name) => name.Length <= MaxLength;

		static void Close()
		{
			UIDrawer.SetActiveMenu(UIDrawer.MenuType.None);
		}
	}
}
