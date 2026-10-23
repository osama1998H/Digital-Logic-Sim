using System;
using System.Collections.Generic;
using DLS.Description;
using DLS.Game;
using DLS.Game.Notes;
using Seb.Helpers;
using Seb.Types;
using Seb.Vis;
using Seb.Vis.UI;
using UnityEngine;

namespace DLS.Graphics
{
	// Edits the text (up to NoteText.MaxLines lines of NoteText.MaxLineLength characters) and colour of a note.
	// A newly placed note opens this menu straight away; cancelling it, or confirming empty text, removes the note again (and its undo step).
	public static class NoteEditMenu
	{
		const float RowHeight = 1.5f;
		const float RowSpacing = 0.1f;
		const float Spacing = 0.8f;

		static readonly UIHandle[] IDS_line = CreateLineHandles();
		static readonly UIHandle ID_Colour = new("NoteEdit_Colour");
		static readonly string[] ColourNames = Enum.GetNames(typeof(NoteColour));
		static readonly Func<string, bool> lineValidator = ValidateLine;

		static NoteInstance note;
		static bool isNewNote;
		static NoteInstance pendingPlacedNote; // placed by the user but the editor hasn't been opened yet

		static UIHandle[] CreateLineHandles()
		{
			UIHandle[] handles = new UIHandle[NoteText.MaxLines];
			for (int i = 0; i < handles.Length; i++) handles[i] = new UIHandle("NoteEdit_Line", i);
			return handles;
		}

		// Starts placing a new note at the cursor. The editor opens once the note has been placed.
		public static void BeginPlacingNewNote(Project project)
		{
			if (!project.CanEditViewedChip) return;
			pendingPlacedNote = project.controller.StartPlacingNote();
		}

		// Called every frame: opens the editor for a pending note once it has been placed, and forgets it if placing was cancelled
		public static void UpdatePendingPlacement(Project project)
		{
			if (pendingPlacedNote == null) return;

			if (project.ViewedChip.TryGetNoteByID(pendingPlacedNote.ID, out NoteInstance placed) && ReferenceEquals(placed, pendingPlacedNote))
			{
				SetTarget(pendingPlacedNote, true);
				pendingPlacedNote = null;
				UIDrawer.SetActiveMenu(UIDrawer.MenuType.NoteEdit);
			}
			else if (!project.controller.IsPlacingElements)
			{
				pendingPlacedNote = null; // placement was cancelled
			}
		}

		public static void SetTarget(NoteInstance target, bool isNew)
		{
			note = target;
			isNewNote = isNew;
		}

		public static void OnMenuOpened()
		{
			string[] lines = NoteText.Lines(note.Text);
			for (int i = 0; i < IDS_line.Length; i++)
			{
				UI.GetInputFieldState(IDS_line[i]).SetText(i < lines.Length ? lines[i] : string.Empty, i == 0);
			}

			UI.GetWheelSelectorState(ID_Colour).index = (int)note.Colour;
		}

		public static void DrawMenu()
		{
			MenuHelper.DrawBackgroundOverlay();
			DrawSettings.UIThemeDLS theme = DrawSettings.ActiveUITheme;
			InputFieldTheme inputTheme = theme.ChipNameInputField;
			inputTheme.fontSize = MenuHelper.Theme.FontSizeRegular;

			float fieldWidth = Mathf.Min(Draw.CalculateTextBoundsSize(new string('M', NoteText.MaxLineLength), inputTheme.fontSize, inputTheme.font).x * 0.8f + 2, UI.Width * 0.8f);
			float totalHeight = 1.5f + Spacing + NoteText.MaxLines * (RowHeight + RowSpacing) + Spacing + DrawSettings.SelectorWheelHeight + Spacing + DrawSettings.ButtonHeight;
			Vector2 topLeft = new(UI.Centre.x - fieldWidth / 2, UI.Centre.y + totalHeight / 2);

			Draw.ID panelID = UI.ReservePanel();
			int focusedRow = -1;
			CancelOrConfirm result = CancelOrConfirm.None;

			using (UI.BeginBoundsScope(true))
			{
				MenuHelper.DrawText(isNewNote ? "NEW NOTE" : "EDIT NOTE", topLeft + new Vector2(fieldWidth / 2, -0.75f), Anchor.TextCentre, Color.white, true);

				Vector2 pos = topLeft + Vector2.down * (1.5f + Spacing);
				for (int i = 0; i < IDS_line.Length; i++)
				{
					UI.InputField(IDS_line[i], inputTheme, pos, new Vector2(fieldWidth, RowHeight), string.Empty, Anchor.TopLeft, 0.5f, lineValidator);
					if (UI.GetInputFieldState(IDS_line[i]).focused) focusedRow = i;
					pos.y -= RowHeight + RowSpacing;
				}

				pos.y -= Spacing - RowSpacing;
				MenuHelper.LabeledOptionsWheel("Colour", Color.white, pos, new Vector2(fieldWidth, DrawSettings.SelectorWheelHeight), ID_Colour, ColourNames, fieldWidth * 0.5f, true);
				pos = UI.PrevBounds.BottomLeft + Vector2.down * Spacing;

				MenuHelper.CancelConfirmResult buttonResult = MenuHelper.DrawCancelConfirmButtons(pos, fieldWidth, false, false);
				if (buttonResult == MenuHelper.CancelConfirmResult.Cancel) result = CancelOrConfirm.Cancel;
				else if (buttonResult == MenuHelper.CancelConfirmResult.Confirm) result = CancelOrConfirm.Confirm;

				MenuHelper.DrawReservedMenuPanel(panelID, UI.GetCurrentBoundsScope());
			}

			// ---- Keyboard ----
			if (KeyboardShortcuts.CancelShortcutTriggered) result = CancelOrConfirm.Cancel;

			if (result == CancelOrConfirm.None)
			{
				if (KeyboardShortcuts.ConfirmShortcutTriggered)
				{
					if (InputHelper.CtrlIsHeld) result = CancelOrConfirm.Confirm;
					else if (focusedRow >= 0) FocusRow(focusedRow, focusedRow + 1);
				}
				else if (focusedRow >= 0 && InputHelper.IsKeyDownThisFrame(KeyCode.DownArrow)) FocusRow(focusedRow, focusedRow + 1);
				else if (focusedRow >= 0 && InputHelper.IsKeyDownThisFrame(KeyCode.UpArrow)) FocusRow(focusedRow, focusedRow - 1);
				else if (focusedRow >= 0 && InputHelper.CtrlIsHeld && InputHelper.IsKeyDownThisFrame(KeyCode.V)) TryPasteMultiLine(focusedRow);
			}

			if (result == CancelOrConfirm.Cancel) Cancel();
			else if (result == CancelOrConfirm.Confirm) Confirm();
		}

		static void FocusRow(int from, int to)
		{
			if (to < 0 || to >= IDS_line.Length) return;

			InputFieldState prev = UI.GetInputFieldState(IDS_line[from]);
			InputFieldState next = UI.GetInputFieldState(IDS_line[to]);
			int cursor = prev.cursorBeforeCharIndex;
			prev.SetFocus(false);
			next.SetFocus(true);
			next.SetCursorIndex(Mathf.Min(cursor, next.text.Length));
		}

		// The input fields reject text containing line breaks, so a multi-line paste is spread over the lines starting at the focused one
		static void TryPasteMultiLine(int startRow)
		{
			string clipboard = InputHelper.GetClipboardContents();
			if (string.IsNullOrEmpty(clipboard)) return;
			clipboard = clipboard.Replace("\r\n", "\n").Replace('\r', '\n');
			if (clipboard.IndexOf('\n') < 0) return; // single line: handled by the field itself

			string[] pasted = clipboard.Split('\n');
			for (int i = 0; i < pasted.Length && startRow + i < IDS_line.Length; i++)
			{
				string line = pasted[i].Replace('\t', ' ');
				if (line.Length > NoteText.MaxLineLength) line = line.Substring(0, NoteText.MaxLineLength);
				UI.GetInputFieldState(IDS_line[startRow + i]).SetText(line, i == 0);
			}
		}

		static string CollectText()
		{
			List<string> lines = new(IDS_line.Length);
			foreach (UIHandle id in IDS_line) lines.Add(UI.GetInputFieldState(id).text);
			return NoteText.Normalize(string.Join("\n", lines));
		}

		static void Confirm()
		{
			string text = CollectText();
			NoteColour colour = (NoteColour)UI.GetWheelSelectorState(ID_Colour).index;
			DevChipInstance chip = Project.ActiveProject.ViewedChip;

			if (isNewNote)
			{
				// Replace the "add note" step (recorded when the empty note was placed) so redo restores the note with its text
				bool removedStep = chip.UndoController.TryRemoveLastAddedNote(note.ID);

				if (text.Length == 0)
				{
					chip.DeleteNote(note);
				}
				else
				{
					string textBefore = note.Text;
					NoteColour colourBefore = note.Colour;
					note.Text = text;
					note.Colour = colour;

					if (removedStep) chip.UndoController.RecordAddElements(new List<IMoveable> { note }, false);
					else chip.UndoController.RecordNoteEdit(note.ID, textBefore, colourBefore, text, colour);
				}
			}
			else
			{
				string textBefore = note.Text;
				NoteColour colourBefore = note.Colour;

				if (text != NoteText.Normalize(textBefore) || colour != colourBefore)
				{
					note.Text = text;
					note.Colour = colour;
					chip.UndoController.RecordNoteEdit(note.ID, textBefore, colourBefore, text, colour);
				}
			}

			Close();
		}

		static void Cancel()
		{
			if (isNewNote)
			{
				DevChipInstance chip = Project.ActiveProject.ViewedChip;
				chip.UndoController.TryRemoveLastAddedNote(note.ID);
				chip.DeleteNote(note);
			}

			Close();
		}

		static void Close()
		{
			note = null;
			isNewNote = false;
			UIDrawer.SetActiveMenu(UIDrawer.MenuType.None);
		}

		public static void Reset()
		{
			note = null;
			isNewNote = false;
			pendingPlacedNote = null;
		}

		static bool ValidateLine(string text)
		{
			if (text.Length > NoteText.MaxLineLength) return false;
			foreach (char c in text)
			{
				if (char.IsControl(c)) return false;
			}

			return true;
		}

		enum CancelOrConfirm
		{
			None,
			Cancel,
			Confirm
		}
	}
}
