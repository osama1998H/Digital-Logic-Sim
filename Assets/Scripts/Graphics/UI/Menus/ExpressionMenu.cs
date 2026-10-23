using System;
using System.Collections.Generic;
using DLS.Game;
using DLS.Game.Logic;
using Seb.Types;
using Seb.Vis;
using Seb.Vis.UI;
using UnityEngine;
using static DLS.Graphics.DrawSettings;

namespace DLS.Graphics
{
	// Type boolean equations and generate a laid-out circuit for them
	public static class ExpressionMenu
	{
		const int MaxLength = 200;
		const float TextPad = 0.6f;
		const float MinFontSize = 0.7f;
		const int NandOnlyIndex = 1;
		const string SyntaxHint = "NOT  ! ~ '    AND  * & .    XOR  ^    OR  + |    separate equations with ;";
		const string ExampleText = "S = A ^ B ^ CIN; COUT = A*B + CIN*(A ^ B)";

		static readonly UIHandle ID_Input = new("ExpressionMenu_Input");
		static readonly UIHandle ID_Gates = new("ExpressionMenu_Gates");
		static readonly string[] gateOptions = { "Use my gates", "NAND only" };
		static readonly char[] lineBreaks = { '\n', '\r', '\t' };
		static readonly Color errorCol = new(1, 0.4f, 0.45f);
		static readonly Color infoCol = new(0.7f, 0.7f, 0.7f);
		static readonly Color previewCol = new(0.53f, 0.8f, 0.57f);

		static GateLibrary gates;
		static string text = string.Empty; // kept between openings

		// Outcome of the last GENERATE, shown when the menu is reopened for it (the menu may already be closed, e.g. on the new-chip path)
		static string generateError; // shown in the full menu until the input changes
		static string generateNotice; // shown on its own with an OK button
		static string generateErrorText;

		// Preview, rebuilt only when its inputs change
		static string cachedText;
		static GateSet cachedGateSet;
		static bool cachedReady;
		static ParseResult parsed;
		static Netlist netlist;

		public static void Open()
		{
			Project project = Project.ActiveProject;
			if (project == null || !project.CanEditViewedChip) return;

			gates?.Cancel();
			gates = GateLibrary.VerifyAsync(project.chipLibrary);
			cachedText = null;
			generateError = null;
			generateNotice = null;
			UIDrawer.SetActiveMenu(UIDrawer.MenuType.Expression);
		}

		public static void OnMenuOpened()
		{
			InputFieldState state = UI.GetInputFieldState(ID_Input);
			state.SetText(text);
			state.SelectAll();
		}

		// Called by UIDrawer whenever the menu stops being the active one, whatever the reason
		public static void OnMenuClosed() => gates?.Cancel();

		public static void DrawMenu()
		{
			if (gates == null) // only Open() starts the gate check
			{
				UIDrawer.SetActiveMenu(UIDrawer.MenuType.None);
				return;
			}

			if (generateNotice != null)
			{
				DrawNotice();
				return;
			}

			MenuHelper.DrawBackgroundOverlay();
			Draw.ID panelID = UI.ReservePanel();
			UIThemeDLS theme = ActiveUITheme;

			bool nandOnly = UI.GetWheelSelectorState(ID_Gates).index == NandOnlyIndex;
			bool ready = gates.IsReady;
			DevChipInstance target = TargetChipIfEmpty();

			using (UI.BeginBoundsScope(true))
			{
				float width = UI.Width * 0.8f;
				Vector2 topLeft = new(UI.Centre.x - width / 2, UI.Height * 0.8f);

				UI.DrawText("FROM EXPRESSION", theme.FontBold, theme.FontSizeRegular, topLeft, Anchor.TopLeft, Color.white);
				topLeft = UI.PrevBounds.BottomLeft + Vector2.down * 1f;
				UI.DrawText(SyntaxHint, theme.FontRegular, theme.FontSizeRegular, topLeft, Anchor.TopLeft, infoCol);
				topLeft = UI.PrevBounds.BottomLeft + Vector2.down * 1f;

				// Input field (the font shrinks so that long input stays visible, since the field doesn't scroll)
				InputFieldTheme inputTheme = theme.ChipNameInputField;
				inputTheme.fontSize = FitFontSize(UI.GetInputFieldState(ID_Input).text, inputTheme.font, width - TextPad * 2 - 1);
				InputFieldState state = UI.InputField(ID_Input, inputTheme, topLeft, new Vector2(width, ButtonHeight), ExampleText, Anchor.TopLeft, TextPad, ValidateInput, true);
				Bounds2D fieldBounds = UI.PrevBounds;
				text = NormalizeInput(state);
				if (generateError != null && text != generateErrorText) generateError = null;

				Refresh(text, EffectiveGates(gates, nandOnly, target), nandOnly || ready);
				bool hasText = !string.IsNullOrWhiteSpace(text);
				topLeft = fieldBounds.BottomLeft + Vector2.down * 0.3f;

				// Error marker under the column where parsing failed
				if (hasText && !parsed.Success)
				{
					int index = Mathf.Clamp(parsed.ErrorIndex, 0, text.Length);
					float x = fieldBounds.Left + TextPad + Draw.CalculateTextBoundsSize(text.AsSpan(0, index), inputTheme.fontSize, inputTheme.font).x;
					x = Mathf.Min(x, fieldBounds.Right - TextPad);
					UI.DrawText("^", inputTheme.font, inputTheme.fontSize, new Vector2(x, topLeft.y), Anchor.TopLeft, errorCol);
					topLeft = new Vector2(topLeft.x, UI.PrevBounds.Bottom) + Vector2.down * 0.5f;
				}
				else
				{
					topLeft += Vector2.down * 0.7f;
				}

				(string preview, Color previewColour) = PreviewLine(hasText, nandOnly, ready);
				UI.DrawText(preview, theme.FontRegular, theme.FontSizeRegular, topLeft, Anchor.TopLeft, previewColour);
				topLeft = UI.PrevBounds.BottomLeft + Vector2.down * 1.2f;

				MenuHelper.LabeledOptionsWheel("Gates", Color.white, topLeft, new Vector2(width, SelectorWheelHeight), ID_Gates, gateOptions, 18, true);
				topLeft = UI.PrevBounds.BottomLeft + Vector2.down * 0.6f;

				UI.DrawText(GateStatus(nandOnly, ready, EffectiveGates(gates, false, target)), theme.FontRegular, theme.FontSizeRegular, topLeft, Anchor.TopLeft, infoCol);
				topLeft = UI.PrevBounds.BottomLeft + Vector2.down * 0.6f;

				if (target == null)
				{
					UI.DrawText("The open chip isn't empty, so the circuit goes into a new chip.", theme.FontRegular, theme.FontSizeRegular, topLeft, Anchor.TopLeft, infoCol);
					topLeft = UI.PrevBounds.BottomLeft + Vector2.down * 0.6f;
				}

				if (generateError != null)
				{
					UI.DrawText(generateError, theme.FontRegular, theme.FontSizeRegular, topLeft, Anchor.TopLeft, errorCol);
					topLeft = UI.PrevBounds.BottomLeft + Vector2.down * 0.6f;
				}

				// NAND-only doesn't use the user's gates, so it needn't wait for their check
				bool canGenerate = (nandOnly || ready) && hasText && parsed.Success && netlist is { Success: true };
				int button = MenuHelper.DrawButtonPair("GENERATE", "CANCEL", topLeft, width, true, canGenerate);

				MenuHelper.DrawReservedMenuPanel(panelID, UI.GetCurrentBoundsScope());

				if (button == 1 || KeyboardShortcuts.CancelShortcutTriggered)
				{
					UIDrawer.SetActiveMenu(UIDrawer.MenuType.None);
				}
				else if (canGenerate && (button == 0 || KeyboardShortcuts.ConfirmShortcutTriggered))
				{
					Generate(text, nandOnly);
				}
			}
		}

		static (string, Color) PreviewLine(bool hasText, bool nandOnly, bool ready)
		{
			if (!hasText) return ("Type one or more equations, for example: " + ExampleText, infoCol);
			if (!parsed.Success) return (parsed.Error, errorCol);
			if (!nandOnly && !ready) return ("Checking your gate chips...", infoCol);
			if (!netlist.Success) return (netlist.Error, errorCol);
			return (NetlistSummary.Format(netlist), previewCol);
		}

		static string GateStatus(bool nandOnly, bool ready, GateSet usable)
		{
			if (nandOnly) return "Every gate is built from NAND chips.";
			if (!ready) return "Checking your NOT, AND, OR and XOR chips...";

			List<string> mine = new();
			List<string> fromNand = new();
			foreach (GateKind kind in GateCheck.UserGateKinds)
			{
				if ((usable & GateCheck.Flag(kind)) != 0) mine.Add(gates.ChipNameFor(kind));
				else fromNand.Add(GateCheck.ChipName(kind));
			}

			if (mine.Count == 0) return "No usable NOT, AND, OR or XOR chips found: every gate is built from NAND.";
			if (fromNand.Count == 0) return $"Using your {string.Join(", ", mine)} chips.";
			return $"Using your {string.Join(", ", mine)} chips. {string.Join(", ", fromNand)} built from NAND.";
		}

		static void Refresh(string source, GateSet gateSet, bool ready)
		{
			if (source == cachedText && gateSet == cachedGateSet && ready == cachedReady) return;

			cachedText = source;
			cachedGateSet = gateSet;
			cachedReady = ready;
			parsed = ExprParser.Parse(source);
			netlist = parsed.Success && ready ? NetlistBuilder.Build(parsed, gateSet) : null;
		}

		// The user's gates that pass the check and can be placed in the target chip (null target = a new blank chip, which can hold any of them)
		static GateSet EffectiveGates(GateLibrary gateLibrary, bool nandOnly, DevChipInstance target)
		{
			if (nandOnly || !gateLibrary.IsReady) return GateSet.None;

			GateSet result = gateLibrary.Available;
			if (target == null) return result;

			foreach (GateKind kind in GateCheck.UserGateKinds)
			{
				if (gateLibrary.Has(kind) && !target.CanAddSubchip(gateLibrary.ChipNameFor(kind))) result &= ~GateCheck.Flag(kind);
			}

			return result;
		}

		// The circuit goes into the open chip only if it is empty
		static DevChipInstance TargetChipIfEmpty()
		{
			DevChipInstance viewed = Project.ActiveProject.ViewedChip;
			return viewed.Elements.Count == 0 ? viewed : null;
		}

		static void Generate(string source, bool nandOnly)
		{
			GateLibrary gateLibrary = gates;
			Project project = Project.ActiveProject;

			if (TargetChipIfEmpty() != null)
			{
				UIDrawer.SetActiveMenu(UIDrawer.MenuType.None);
				GenerateInto(project.ViewedChip);
				return;
			}

			// Same steps as NEW CHIP in the bottom bar menu
			if (project.ActiveChipHasUnsavedChanges()) UnsavedChangesPopup.OpenPopup(CreateChipAndGenerate);
			else
			{
				UIDrawer.SetActiveMenu(UIDrawer.MenuType.None);
				CreateChipAndGenerate(true);
			}

			void CreateChipAndGenerate(bool confirmed)
			{
				if (!confirmed) return;
				Project.ActiveProject.CreateBlankDevChip();
				GenerateInto(Project.ActiveProject.ViewedChip);
			}

			void GenerateInto(DevChipInstance chip)
			{
				string error;
				string notice = null;
				try
				{
					Netlist result = NetlistBuilder.Build(ExprParser.Parse(source), EffectiveGates(gateLibrary, nandOnly, chip));
					error = CircuitGenerator.Generate(result, gateLibrary, chip, out notice);
				}
				catch (Exception e)
				{
					error = "Generation failed: " + e.Message;
					Debug.LogException(e);
				}

				if (error == null && notice == null) return;

				// Reopen the menu to tell the user (it is closed by now)
				if (error != null) Debug.LogWarning("Generate from expression failed: " + error);
				generateError = error;
				generateErrorText = source;
				generateNotice = error == null ? notice : null;
				UIDrawer.SetActiveMenu(UIDrawer.MenuType.Expression);
			}
		}

		static void DrawNotice()
		{
			MenuHelper.DrawBackgroundOverlay();
			Draw.ID panelID = UI.ReservePanel();

			using (UI.BeginBoundsScope(true))
			{
				UI.DrawText(generateNotice, ActiveUITheme.FontRegular, ActiveUITheme.FontSizeRegular, UI.Centre + Vector2.up * 3, Anchor.TextCentre, Color.white);
				Bounds2D textBounds = UI.PrevBounds;
				bool ok = UI.Button("OK", ActiveUITheme.ButtonTheme, textBounds.CentreBottom + Vector2.down * 1f, new Vector2(textBounds.Width, ButtonHeight), true, false, false, Anchor.CentreTop);
				MenuHelper.DrawReservedMenuPanel(panelID, UI.GetCurrentBoundsScope());

				if (ok || KeyboardShortcuts.ConfirmShortcutTriggered || KeyboardShortcuts.CancelShortcutTriggered)
				{
					generateNotice = null;
					UIDrawer.SetActiveMenu(UIDrawer.MenuType.None);
				}
			}
		}

		static float FitFontSize(string s, FontType font, float maxWidth)
		{
			float max = ActiveUITheme.FontSizeRegular;
			if (string.IsNullOrEmpty(s)) return max;
			float widthAtSizeOne = Draw.CalculateTextBoundsSize(s.AsSpan(), 1, font).x;
			return widthAtSizeOne <= 0 ? max : Mathf.Clamp(maxWidth / widthAtSizeOne, MinFontSize, max);
		}

		// Pasted line breaks become equation separators, so the field stays on one line
		static string NormalizeInput(InputFieldState state)
		{
			string s = state.text ?? string.Empty;
			if (s.IndexOfAny(lineBreaks) < 0) return s;

			s = s.Replace("\r\n", ";").Replace('\n', ';').Replace('\r', ';').Replace('\t', ' ');
			state.SetText(s);
			return s;
		}

		static bool ValidateInput(string s) => s.Length <= MaxLength;
	}
}
