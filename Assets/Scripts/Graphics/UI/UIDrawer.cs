using DLS.Game;
using DLS.Simulation;
using Seb.Vis.UI;

namespace DLS.Graphics
{
	public static class UIDrawer
	{
		public enum MenuType
		{
			None,
			ChipSave,
			ChipLibrary,
			BottomBarMenuPopup,
			ChipCustomization,
			Preferences,
			PinRename,
			MainMenu,
			RebindKeyChip,
			RomEdit,
			PulseEdit,
			UnsavedChanges,
			Search,
			ChipLabelPopup,
			BatchRename,
			ReplaceChip,
			ChipStats,
			TruthTable,
			TestBench,
			Expression,
			NoteEdit
		}

		static MenuType activeMenuOld;

		public static MenuType ActiveMenu { get; private set; }

		public static void Draw()
		{
			NotifyIfActiveMenuChanged();

			using (UI.CreateFixedAspectUIScope(drawLetterbox: true))
			{
				if (ActiveMenu is MenuType.MainMenu)
				{
					DrawAppMenus();
				}
				else
				{
					DrawProjectMenus(Project.ActiveProject);
				}
			}

			InteractionState.MouseIsOverUI = UI.IsMouseOverUIThisFrame;
			if (ActiveMenu is not MenuType.MainMenu) ValueTooltip.Draw(Project.ActiveProject);
		}

		static void DrawAppMenus()
		{
			MainMenu.Draw();
		}

		static void DrawProjectMenus(Project project)
		{
			MenuType menuToDraw = ActiveMenu; // cache state in case it changes while drawing/updating the menus
			// After caching, so the editor first draws on the frame after it opens (the placing click must not reach its buttons)
			NoteEditMenu.UpdatePendingPlacement(project);

			// Docked (not a MenuType, so the canvas above it stays usable); drawn before the bottom bar so its popups overlay it
			if (TimingDiagramPanel.IsOpen && (menuToDraw is MenuType.None or MenuType.BottomBarMenuPopup)) TimingDiagramPanel.Draw(project);
			if (menuToDraw != MenuType.ChipCustomization) BottomBarUI.DrawUI(project);

			bool aMenuIsOpen = true;
			if (menuToDraw == MenuType.ChipSave) ChipSaveMenu.DrawMenu();
			else if (menuToDraw == MenuType.ChipLibrary) ChipLibraryMenu.DrawMenu();
			else if (menuToDraw == MenuType.ChipCustomization) ChipCustomizationMenu.DrawMenu();
			else if (menuToDraw == MenuType.Preferences) PreferencesMenu.DrawMenu(project);
			else if (menuToDraw == MenuType.PinRename) PinEditMenu.DrawMenu();
			else if (menuToDraw == MenuType.RebindKeyChip) RebindKeyChipMenu.DrawMenu();
			else if (menuToDraw == MenuType.RomEdit) RomEditMenu.DrawMenu();
			else if (menuToDraw == MenuType.UnsavedChanges) UnsavedChangesPopup.DrawMenu();
			else if (menuToDraw == MenuType.Search) SearchPopup.DrawMenu();
			else if (menuToDraw == MenuType.ChipLabelPopup) ChipLabelMenu.DrawMenu();
			else if (menuToDraw == MenuType.PulseEdit) PulseEditMenu.DrawMenu();
			else if (menuToDraw == MenuType.BatchRename) BatchRenameMenu.DrawMenu();
			else if (menuToDraw == MenuType.ReplaceChip) ReplaceChipPopup.DrawMenu();
			else if (menuToDraw == MenuType.ChipStats) ChipStatsMenu.DrawMenu();
			else if (menuToDraw == MenuType.TruthTable) TruthTableMenu.DrawMenu();
			else if (menuToDraw == MenuType.TestBench) TestBenchMenu.DrawMenu();
			else if (menuToDraw == MenuType.Expression) ExpressionMenu.DrawMenu();
			else if (menuToDraw == MenuType.NoteEdit) NoteEditMenu.DrawMenu();
			else
			{
				bool showSimPausedBanner = project.simPaused;
				if (showSimPausedBanner) SimPausedUI.DrawPausedBanner();
				if (project.chipViewStack.Count > 1) ViewedChipsBar.DrawViewedChipsBanner(project, showSimPausedBanner);
				if (Simulator.isCreatingACache) CreateCacheUI.DrawCreatingCacheInfo();
				aMenuIsOpen = false;
			}

			Simulator.cacheCreationSuspended = aMenuIsOpen; // Cancel current caching process (and don't start new ones) while a menu is open

			ContextMenu.Update();
		}

		public static bool InInputBlockingMenu() => !(ActiveMenu is MenuType.None or MenuType.BottomBarMenuPopup or MenuType.ChipCustomization);

		static void NotifyIfActiveMenuChanged()
		{
			// UI Changed -- notify opened
			if (ActiveMenu != activeMenuOld)
			{
				if (activeMenuOld == MenuType.ChipCustomization) CustomizationSceneDrawer.OnCustomizationMenuClosed();
				if (activeMenuOld == MenuType.TruthTable) TruthTableMenu.OnMenuClosed();
				if (activeMenuOld == MenuType.TestBench) TestBenchMenu.OnMenuClosed();
				if (activeMenuOld == MenuType.Expression) ExpressionMenu.OnMenuClosed();

				if (ActiveMenu == MenuType.ChipSave) ChipSaveMenu.OnMenuOpened();
				else if (ActiveMenu == MenuType.ChipLibrary) ChipLibraryMenu.OnMenuOpened();
				else if (ActiveMenu == MenuType.ChipCustomization) ChipCustomizationMenu.OnMenuOpened();
				else if (ActiveMenu == MenuType.PinRename) PinEditMenu.OnMenuOpened();
				else if (ActiveMenu == MenuType.Preferences) PreferencesMenu.OnMenuOpened();
				else if (ActiveMenu == MenuType.MainMenu) MainMenu.OnMenuOpened();
				else if (ActiveMenu == MenuType.RebindKeyChip) RebindKeyChipMenu.OnMenuOpened();
				else if (ActiveMenu == MenuType.RomEdit) RomEditMenu.OnMenuOpened();
				else if (ActiveMenu == MenuType.Search) SearchPopup.OnMenuOpened();
				else if (ActiveMenu == MenuType.ChipLabelPopup) ChipLabelMenu.OnMenuOpened();
				else if (ActiveMenu == MenuType.PulseEdit) PulseEditMenu.OnMenuOpened();
				else if (ActiveMenu == MenuType.BatchRename) BatchRenameMenu.OnMenuOpened();
				else if (ActiveMenu == MenuType.ReplaceChip) ReplaceChipPopup.OnMenuOpened();
				else if (ActiveMenu == MenuType.ChipStats) ChipStatsMenu.OnMenuOpened();
				else if (ActiveMenu == MenuType.TruthTable) TruthTableMenu.OnMenuOpened();
				else if (ActiveMenu == MenuType.TestBench) TestBenchMenu.OnMenuOpened();
				else if (ActiveMenu == MenuType.Expression) ExpressionMenu.OnMenuOpened();
				else if (ActiveMenu == MenuType.NoteEdit) NoteEditMenu.OnMenuOpened();

				if (InInputBlockingMenu() && Project.ActiveProject != null && Project.ActiveProject.controller != null)
				{
					Project.ActiveProject.controller.CancelEverything();
				}

				activeMenuOld = ActiveMenu;
			}
		}

		public static void ToggleBottomPopupMenu()
		{
			SetActiveMenu(ActiveMenu is MenuType.None ? MenuType.BottomBarMenuPopup : MenuType.None);
		}

		public static void SetActiveMenu(MenuType type)
		{
			ActiveMenu = type;
		}

		public static void Reset()
		{
			TruthTableMenu.OnMenuClosed();
			TestBenchMenu.OnMenuClosed();
			ExpressionMenu.OnMenuClosed();
			SetActiveMenu(MenuType.None);
			activeMenuOld = MenuType.None;
			ContextMenu.Reset();
			UI.ResetAllStates();
			BottomBarUI.Reset();
			ChipSaveMenu.Reset();
			RomEditMenu.Reset();
			ChipLibraryMenu.Reset();
			SearchPopup.Reset();
			TimingDiagramPanel.Reset();
			NoteEditMenu.Reset();
		}
	}
}