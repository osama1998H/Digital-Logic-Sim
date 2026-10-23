using System;
using System.Collections.Generic;
using System.Linq;
using DLS.Description;
using DLS.Game;
using DLS.Game.Editing;
using Seb.Helpers;
using Seb.Types;
using Seb.Vis;
using Seb.Vis.UI;
using UnityEngine;

namespace DLS.Graphics
{
	public static class ContextMenu
	{
		const int pad = 10;
		const string menuDividerString = "#--#";
		static string interactionContextName;
		static bool bottomBarItemIsCollection;
		static Vector2 mouseOpenMenuPos;

		static MenuEntry[] activeContextMenuEntries;
		static readonly MenuEntry dividerMenuEntry = new(menuDividerString, null, null);
		static bool wasMouseOverMenu;
		static string contextMenuHeader;
		static bool keepOpenThisFrame; // set when an entry switches to another page of entries, rather than performing an action

		static readonly MenuEntry[] pinColEntries = ((PinColour[])Enum.GetValues(typeof(PinColour))).Select(col =>
			new MenuEntry(Format(Enum.GetName(typeof(PinColour), col)), () => SetCol(col), CanSetCol)
		).ToArray();


		static readonly MenuEntry deleteEntry = new(Format("DELETE"), Delete, CanDelete);
		static readonly MenuEntry openChipEntry = new(Format("OPEN"), OpenChip, CanOpenChip);
		static readonly MenuEntry labelChipEntry = new(Format("LABEL"), OpenChipLabelPopup, CanLabelChip);
		static readonly MenuEntry replaceChipEntry = new(Format("REPLACE..."), OpenReplaceMenu, CanReplaceChip);
		static readonly MenuEntry replaceAllChipsEntry = new(Format("REPLACE ALL..."), OpenReplaceAllMenu, CanReplaceChip);
		static readonly MenuEntry selectSameEntry = new(Format("SELECT SAME"), SelectSameType, CanEditCurrentChip);

		static readonly MenuEntry statsEntry = new(Format("STATISTICS"), OpenStatsMenu, () => interactionContext is SubChipInstance);

		static readonly MenuEntry truthTableEntry = new(Format("TRUTH TABLE"), OpenTruthTableForSubChip, () => true);

		static readonly MenuEntry testBenchEntry = new(Format("TEST BENCH"), OpenTestBenchForSubChip, () => true);

		static readonly MenuEntry[] entries_customSubchip =
		{
			new(Format("VIEW"), EnterViewMode, CanEnterViewMode),
			openChipEntry,
			statsEntry,
			truthTableEntry,
			testBenchEntry,
			labelChipEntry,
			dividerMenuEntry,
			replaceChipEntry,
			replaceAllChipsEntry,
			selectSameEntry,
			dividerMenuEntry,
			deleteEntry
		};

		static readonly MenuEntry[] entries_builtinSubchip =
		{
			statsEntry,
			truthTableEntry,
			testBenchEntry,
			labelChipEntry,
			dividerMenuEntry,
			replaceChipEntry,
			replaceAllChipsEntry,
			selectSameEntry,
			dividerMenuEntry,
			deleteEntry
		};

		static readonly MenuEntry[] entries_builtinLED = entries_builtinSubchip.Concat(new[] { dividerMenuEntry }).Concat(pinColEntries).ToArray();

		static readonly MenuEntry[] entries_builtinBus =
		{
			new(Format("FLIP"), FlipBus, CanFlipBus),
			labelChipEntry,
			deleteEntry
		};

		static readonly MenuEntry[] entries_builtinKeySubchip =
		{
			new(Format("REBIND"), OpenKeyBindMenu, CanEditCurrentChip),
			labelChipEntry,
			deleteEntry
		};

		static readonly MenuEntry[] entries_builtinRomSubchip =
		{
			new(Format("EDIT"), OpenRomEditMenu, CanEditCurrentChip),
			labelChipEntry,
			deleteEntry
		};

		static readonly MenuEntry[] entries_builtinPulseChip =
		{
			new(Format("EDIT"), OpenPulseEditMenu, CanEditCurrentChip),
			labelChipEntry,
			deleteEntry
		};


		static readonly MenuEntry[] entries_subChipOutput = pinColEntries;

		static readonly MenuEntry[] entries_inputDevPin = new[]
		{
			new(Format("EDIT"), OpenPinEditMenu, CanEditCurrentChip),
			new(Format("DELETE"), Delete, CanDelete),
			dividerMenuEntry
		}.Concat(pinColEntries).ToArray();

		static readonly MenuEntry[] entries_outputDevPin =
		{
			entries_inputDevPin[0],
			entries_inputDevPin[1]
		};

		static readonly MenuEntry[] entries_wire =
		{
			new(Format("EDIT"), EditWire, CanEditWire),
			new(Format("DELETE"), Delete, CanDelete)
		};

		// ---- Multiple selected elements ----
		static readonly MenuEntry arrangeEntry = new("Arrange  >".PadRight(pad), OpenArrangeEntries, CanArrangeSelection);
		static readonly MenuEntry deleteSelectionEntry = new(Format("DELETE"), DeleteSelection, CanDelete);

		static readonly MenuEntry[] entries_multiPins =
		{
			new(Format("RENAME..."), OpenBatchRenameMenu, CanEditCurrentChip),
			arrangeEntry,
			deleteSelectionEntry
		};

		static readonly MenuEntry[] entries_multiSubchips =
		{
			new(Format("LABEL..."), OpenBatchRenameMenu, CanEditCurrentChip),
			new(Format("REPLACE..."), OpenReplaceSelectionMenu, CanReplaceSelection),
			arrangeEntry,
			deleteSelectionEntry
		};

		static readonly MenuEntry[] entries_multiMixed =
		{
			arrangeEntry,
			deleteSelectionEntry
		};

		static readonly MenuEntry[] entries_arrange =
		{
			ArrangeEntry("Align left", "Ctrl+Shift+Left", ArrangeOp.AlignLeft),
			ArrangeEntry("Align centre", "Ctrl+Shift+C", ArrangeOp.AlignHCentre),
			ArrangeEntry("Align right", "Ctrl+Shift+Right", ArrangeOp.AlignRight),
			dividerMenuEntry,
			ArrangeEntry("Align top", "Ctrl+Shift+Up", ArrangeOp.AlignTop),
			ArrangeEntry("Align middle", "Ctrl+Shift+M", ArrangeOp.AlignVCentre),
			ArrangeEntry("Align bottom", "Ctrl+Shift+Down", ArrangeOp.AlignBottom),
			dividerMenuEntry,
			ArrangeEntry("Space horiz.", "Ctrl+Shift+H", ArrangeOp.DistributeH),
			ArrangeEntry("Space vert.", "Ctrl+Shift+V", ArrangeOp.DistributeV)
		};

		static readonly MenuEntry[] entries_bottomBarChip =
		{
			openChipEntry,
			new(Format("UN-STAR"), UnstarBottomBarEntry, () => true)
		};

		static readonly MenuEntry[] entries_collectionPopupChip =
		{
			openChipEntry
		};

		static readonly MenuEntry[] entries_bottomBarCollection =
		{
			new(Format("UN-STAR"), UnstarBottomBarEntry, () => true)
		};

		public static bool IsOpen { get; private set; }
		public static IInteractable interactionContext { get; private set; }


		static string Format(string s)
		{
			s = char.ToUpper(s[0]) + s.Substring(1).ToLower();
			return s.PadRight(pad);
		}

		public static void Update()
		{
			bool inMenu = !(UIDrawer.ActiveMenu is UIDrawer.MenuType.None or UIDrawer.MenuType.BottomBarMenuPopup or UIDrawer.MenuType.ChipCustomization);
			if (inMenu)
			{
				CloseContextMenu();
			}
			else
			{
				HandleOpenMenuInput();

				// Draw
				if (IsOpen) DrawContextMenu(activeContextMenuEntries);

				// Close menu input
				if ((InputHelper.IsMouseDownThisFrame(MouseButton.Left) && !keepOpenThisFrame) || KeyboardShortcuts.CancelShortcutTriggered)
				{
					CloseContextMenu();
				}

				keepOpenThisFrame = false;
			}
		}

		static void HandleOpenMenuInput()
		{
			// Open menu input
			if (InputHelper.IsMouseDownThisFrame(MouseButton.Right) && !KeyboardShortcuts.CameraActionKeyHeld && !InteractionState.MouseIsOverUI)
			{
				bool inCustomizeMenu = UIDrawer.ActiveMenu == UIDrawer.MenuType.ChipCustomization;
				IInteractable hoverElement = InteractionState.ElementUnderMouse;

				if (!inCustomizeMenu && Project.ActiveProject.controller.IsPartOfMultiSelection(hoverElement))
				{
					OpenMultiSelectionMenu();
					return;
				}

				bool openSubChipContextMenu = hoverElement is SubChipInstance && !inCustomizeMenu;
				bool openDevPinContextMenu = (hoverElement is PinInstance pin && pin.parent is DevPinInstance) || hoverElement is DevPinInstance;
				bool openWireContextMenu = hoverElement is WireInstance;
				bool openSubchipOutputPinContextMenu = hoverElement is PinInstance pin2 && pin2.parent is SubChipInstance && pin2.IsSourcePin && !pin2.IsBusPin;

				if (openSubChipContextMenu || openDevPinContextMenu || openWireContextMenu || openSubchipOutputPinContextMenu)
				{
					interactionContextName = string.Empty;
					interactionContext = hoverElement;
					string headerName = string.Empty;

					if (openSubChipContextMenu)
					{
						SubChipInstance subChip = (SubChipInstance)hoverElement;
						interactionContextName = subChip.Description.Name;

						if (subChip.ChipType == ChipType.Custom)
						{
							headerName = subChip.Description.Name;
							activeContextMenuEntries = entries_customSubchip;
						}
						else // builtin type
						{
							headerName = ChipTypeHelper.IsBusType(subChip.ChipType) ? "BUS" : subChip.Description.Name;
							if (subChip.ChipType is ChipType.Key) activeContextMenuEntries = entries_builtinKeySubchip;
							else if (ChipTypeHelper.IsRomType(subChip.ChipType)) activeContextMenuEntries = entries_builtinRomSubchip;
							else if (subChip.ChipType is ChipType.Pulse) activeContextMenuEntries = entries_builtinPulseChip;
							else if (ChipTypeHelper.IsBusType(subChip.ChipType)) activeContextMenuEntries = entries_builtinBus;
							else if (subChip.ChipType == ChipType.DisplayLED) activeContextMenuEntries = entries_builtinLED;
							else activeContextMenuEntries = entries_builtinSubchip;
						}

						Project.ActiveProject.controller.Select(interactionContext as IMoveable, false);
					}
					else if (openDevPinContextMenu)
					{
						if (interactionContext is DevPinInstance devPinInstance) interactionContext = devPinInstance.Pin;

						PinInstance activePin = (PinInstance)interactionContext;
						headerName = CreatePinHeaderName(activePin.Name);
						interactionContextName = activePin.Name;
						Project.ActiveProject.controller.Select(activePin.parent, false);
						activeContextMenuEntries = activePin.IsSourcePin ? entries_inputDevPin : entries_outputDevPin;
					}
					else if (openWireContextMenu)
					{
						WireInstance wire = (WireInstance)interactionContext;
						if (wire.IsBusWire) headerName = "BUS LINE";
						else headerName = CreateWireHeaderString(wire);

						activeContextMenuEntries = entries_wire;
					}
					else if (openSubchipOutputPinContextMenu)
					{
						PinInstance pinContext = (PinInstance)interactionContext;
						headerName = CreatePinHeaderName(pinContext.Name);
						activeContextMenuEntries = entries_subChipOutput;
					}

					SetContextMenuOpen(headerName);
				}
				else
				{
					CloseContextMenu();
				}
			}
		}

		static void OpenMultiSelectionMenu()
		{
			List<IMoveable> selection = Project.ActiveProject.controller.SelectedElements;
			interactionContext = null;
			interactionContextName = string.Empty;

			bool allInputPins = selection.All(e => e is DevPinInstance { IsInputPin: true });
			bool allOutputPins = selection.All(e => e is DevPinInstance { IsInputPin: false });
			bool allSubchips = selection.All(e => e is SubChipInstance);

			if (allInputPins || allOutputPins) activeContextMenuEntries = entries_multiPins;
			else if (allSubchips) activeContextMenuEntries = entries_multiSubchips;
			else activeContextMenuEntries = entries_multiMixed;

			SetContextMenuOpen($"{selection.Count} SELECTED");
		}

		static MenuEntry ArrangeEntry(string name, string shortcut, ArrangeOp op)
		{
			string text = name.PadRight(14) + "<color=#666666ff>" + shortcut;
			return new MenuEntry(text, () => SelectionArranger.Apply(Project.ActiveProject.controller, op), () => SelectionArranger.CanArrange(Project.ActiveProject.controller, op));
		}

		static bool CanArrangeSelection() => SelectionArranger.CanArrange(Project.ActiveProject.controller, ArrangeOp.AlignLeft);

		static void OpenArrangeEntries()
		{
			activeContextMenuEntries = entries_arrange;
			contextMenuHeader = "ARRANGE".PadRight(pad);
			keepOpenThisFrame = true;
		}

		static void OpenBatchRenameMenu()
		{
			List<IMoveable> selection = Project.ActiveProject.controller.SelectedElements;
			BatchRenameMenu.SetTargets(selection, selection[0] is DevPinInstance);
			UIDrawer.SetActiveMenu(UIDrawer.MenuType.BatchRename);
		}

		static void OpenTruthTableForSubChip() => TruthTableMenu.Open(GetSavedSubChipDescription());

		static void OpenTestBenchForSubChip() => TestBenchMenu.Open(GetSavedSubChipDescription());

		static ChipDescription GetSavedSubChipDescription()
		{
			ChipDescription description = ((SubChipInstance)interactionContext).Description;
			// Prefer the saved library version, which is what the subchip actually simulates
			if (Project.ActiveProject.chipLibrary.TryGetChipDescription(description.Name, out ChipDescription libraryDescription)) description = libraryDescription;
			return description;
		}

		static void OpenStatsMenu() => ChipStatsMenu.Open(((SubChipInstance)interactionContext).Description);

		static bool CanReplaceChip() => interactionContext is SubChipInstance subChip && SubChipReplacer.CanReplace(subChip);

		static void OpenReplaceMenu()
		{
			ReplaceChipPopup.SetTargets(new[] { (SubChipInstance)interactionContext });
			UIDrawer.SetActiveMenu(UIDrawer.MenuType.ReplaceChip);
		}

		static void OpenReplaceAllMenu()
		{
			SubChipInstance subChip = (SubChipInstance)interactionContext;
			ReplaceChipPopup.SetTargets(SubChipReplacer.SubChipsOfType(Project.ActiveProject.ViewedChip, subChip.Description.Name));
			UIDrawer.SetActiveMenu(UIDrawer.MenuType.ReplaceChip);
		}

		// Selected subchips can be replaced together if they're all of the same (replaceable) type
		static bool CanReplaceSelection()
		{
			List<IMoveable> selection = Project.ActiveProject.controller.SelectedElements;
			if (selection.Count == 0 || selection[0] is not SubChipInstance first) return false;
			return selection.All(e => e is SubChipInstance s && s.Description.NameMatch(first.Description.Name) && SubChipReplacer.CanReplace(s));
		}

		static void OpenReplaceSelectionMenu()
		{
			ReplaceChipPopup.SetTargets(Project.ActiveProject.controller.SelectedElements.OfType<SubChipInstance>());
			UIDrawer.SetActiveMenu(UIDrawer.MenuType.ReplaceChip);
		}

		// Select all subchips of the same type as the one that was clicked on
		static void SelectSameType()
		{
			SubChipInstance subChip = (SubChipInstance)interactionContext;
			ChipInteractionController controller = Project.ActiveProject.controller;
			foreach (SubChipInstance other in SubChipReplacer.SubChipsOfType(Project.ActiveProject.ViewedChip, subChip.Description.Name))
			{
				if (!other.IsSelected) controller.Select(other, true);
			}
		}

		static void DeleteSelection()
		{
			Project.ActiveProject.controller.DeleteSelectedElements();
		}

		static string CreateWireHeaderString(WireInstance wire)
		{
			string pinName = wire.SourcePin.Name;
			if (string.IsNullOrWhiteSpace(pinName)) return "WIRE";

			return "WIRE: " + pinName;
		}

		static string CreatePinHeaderName(string pinName)
		{
			if (string.IsNullOrWhiteSpace(pinName)) return "PIN";

			return "PIN: " + pinName;
		}

		public static void OpenBottomBarContextMenu(string name, bool isCollection, bool isFromInsideCollection)
		{
			interactionContextName = name;
			bottomBarItemIsCollection = isCollection;
			interactionContext = null;
			SetContextMenuOpen(name);

			if (isCollection)
			{
				activeContextMenuEntries = entries_bottomBarCollection;
			}
			else
			{
				activeContextMenuEntries = isFromInsideCollection ? entries_collectionPopupChip : entries_bottomBarChip;
			}
		}

		static void SetContextMenuOpen(string header)
		{
			mouseOpenMenuPos = UI.ScreenToUISpace(InputHelper.MousePos);
			contextMenuHeader = header.PadRight(pad);
			IsOpen = true;
		}


		static void DrawContextMenu(MenuEntry[] menuEntries)
		{
			Draw.StartLayer(Vector2.zero, 1, true);

			const float textOffsetX = 0.45f;
			ButtonTheme theme = DrawSettings.ActiveUITheme.MenuPopupButtonTheme;
			ButtonTheme headerTheme = DrawSettings.ActiveUITheme.MenuPopupButtonTheme;
			headerTheme.buttonCols.inactive = ColHelper.MakeCol(0.18f);
			headerTheme.textCols.inactive = Color.white;

			float menuWidth = 0;
			foreach (MenuEntry entry in menuEntries)
			{
				if (entry.Text != menuDividerString) menuWidth = Mathf.Max(menuWidth, Draw.CalculateTextBoundsSize(entry.Text, theme.fontSize, theme.font).x + 1);
			}

			float menuWidthHeader = Draw.CalculateTextBoundsSize(contextMenuHeader, theme.fontSize, theme.font).x + 1;
			menuWidth = Mathf.Max(menuWidth, menuWidthHeader);

			Draw.ID panelID = UI.ReservePanel();
			Vector2 buttonSize = new(menuWidth, 2);


			Vector2 pos = mouseOpenMenuPos;
			if (pos.x + menuWidth > UI.Width)
			{
				pos.x = UI.Width - menuWidth;
			}

			bool expandDown = pos.y >= UI.Height * 0.35f;
			float dirY = expandDown ? -1 : 1;
			Anchor anchor = expandDown ? Anchor.TopLeft : Anchor.BottomLeft;

			using (UI.BeginBoundsScope(true))
			{
				for (int i = 0; i < menuEntries.Length; i++)
				{
					int index = expandDown ? i : menuEntries.Length - i - 1;
					MenuEntry entry = menuEntries[index];

					if (index == 0 && expandDown) DrawHeader();

					if (entry.Text == menuDividerString)
					{
						pos.y += 0.5f * dirY;
						UI.DrawPanel(pos, new Vector2(menuWidth, 0.15f), ColHelper.MakeCol(0.6f), Anchor.CentreLeft);
						pos.y += 0.5f * dirY;
					}
					else
					{
						if (UI.Button(entry.Text, theme, pos, buttonSize, entry.IsEnabled(), false, false, anchor, true, textOffsetX))
						{
							entry.OnPress();
						}

						pos.y += buttonSize.y * dirY;
					}

					if (index == 0 && !expandDown) DrawHeader();
				}

				Bounds2D bounds = UI.GetCurrentBoundsScope();
				Vector2 menuSize = new(menuWidth, bounds.Height);
				UI.ModifyPanel(panelID, bounds.Centre, menuSize + Vector2.one * 0.5f, ColHelper.MakeCol(0.91f));
			}

			wasMouseOverMenu = UI.MouseInsideBounds(UI.PrevBounds);

			void DrawHeader()
			{
				UI.Button(contextMenuHeader, headerTheme, pos, buttonSize, false, false, false, anchor, true, textOffsetX);
				pos.y += buttonSize.y * dirY;
			}
		}

		static bool IsCustomChip() => !Project.ActiveProject.chipLibrary.IsBuiltinChip(interactionContextName);
		static bool CanEnterViewMode() => IsCustomChip();
		static bool CanLabelChip() => Project.ActiveProject.CanEditViewedChip;
		static void EnterViewMode() => Project.ActiveProject.EnterViewMode(interactionContext as SubChipInstance);

		static bool CanDelete() => Project.ActiveProject.CanEditViewedChip;
		static bool CanFlipBus() => Project.ActiveProject.CanEditViewedChip;

		static bool CanSetCol()
		{
			if (!Project.ActiveProject.CanEditViewedChip || UIDrawer.ActiveMenu == UIDrawer.MenuType.ChipCustomization) return false;
			if (interactionContext is PinInstance pin) return pin.IsSourcePin;
			if (interactionContext is SubChipInstance subchip) return subchip.ChipType == ChipType.DisplayLED;

			return false;
		}

		static void FlipBus()
		{
			((SubChipInstance)interactionContext).FlipBus();
		}

		static void SetCol(PinColour col)
		{
			if (interactionContext is PinInstance pin)
			{
				pin.Colour = col;
			}
			else if (interactionContext is SubChipInstance subchip)
			{
				Project.ActiveProject.NotifyLEDColourChanged(subchip, (uint)col);
			}
			
		}

		static void OpenChipLabelPopup()
		{
			UIDrawer.SetActiveMenu(UIDrawer.MenuType.ChipLabelPopup);
		}

		public static void EditWire()
		{
			Project.ActiveProject.controller.EnterWireEditMode((WireInstance)interactionContext);
		}

		static void Delete()
		{
			if (interactionContext is IMoveable moveable)
			{
				Project.ActiveProject.controller.Delete(moveable);
			}
			else if (interactionContext is WireInstance wire)
			{
				Project.ActiveProject.controller.DeleteWire(wire);
			}
			else if (interactionContext is PinInstance pin)
			{
				Project.ActiveProject.controller.Delete(pin.parent);
			}
		}

		static void OpenKeyBindMenu()
		{
			UIDrawer.SetActiveMenu(UIDrawer.MenuType.RebindKeyChip);
		}

		static void OpenRomEditMenu() => UIDrawer.SetActiveMenu(UIDrawer.MenuType.RomEdit);

		static void OpenPulseEditMenu() => UIDrawer.SetActiveMenu(UIDrawer.MenuType.PulseEdit);

		static bool CanEditCurrentChip() => Project.ActiveProject.CanEditViewedChip;

		static bool CanEditWire() => CanEditCurrentChip();

		static void OpenPinEditMenu()
		{
			PinEditMenu.SetTargetPin((DevPinInstance)((PinInstance)interactionContext).parent);
			UIDrawer.SetActiveMenu(UIDrawer.MenuType.PinRename);
		}

		static void OpenChip()
		{
			Project project = Project.ActiveProject;
			string chipToOpenName = interactionContextName;

			if (project.ActiveChipHasUnsavedChanges())
			{
				UnsavedChangesPopup.OpenPopup(OpenChipIfConfirmed);
			}
			else
			{
				OpenChipIfConfirmed(true);
			}

			void OpenChipIfConfirmed(bool confirm)
			{
				if (confirm)
				{
					project.LoadDevChipOrCreateNewIfDoesntExist(chipToOpenName);
				}
			}
		}

		static bool CanOpenChip() => IsCustomChip() && CanEditCurrentChip();

		public static void Reset()
		{
			CloseContextMenu();
		}

		public static void CloseContextMenu()
		{
			IsOpen = false;
		}

		public static bool HasFocus() => IsOpen && wasMouseOverMenu;

		public static void UnstarBottomBarEntry()
		{
			Project.ActiveProject.SetStarred(interactionContextName, false, bottomBarItemIsCollection, true);
		}

		public readonly struct MenuEntry
		{
			public readonly string Text;
			public readonly Action OnPress;
			public readonly Func<bool> IsEnabled;

			public MenuEntry(string text, Action onPress, Func<bool> isEnabled)
			{
				Text = text;
				OnPress = onPress;
				IsEnabled = isEnabled;
			}
		}
	}
}