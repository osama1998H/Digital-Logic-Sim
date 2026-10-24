using DLS.Game;
using DLS.Game.Export;
using Seb.Types;
using Seb.Vis;
using Seb.Vis.UI;
using UnityEngine;

namespace DLS.Graphics
{
	// Options for saving the viewed chip as a PNG (see ImageExporter)
	public static class ExportImageMenu
	{
		const float Width = 28;
		const float Spacing = 0.8f;
		const float RowSpacing = 0.25f;

		static readonly UIHandle ID_Scale = new("ExportImage_Scale");
		static readonly UIHandle ID_PinNames = new("ExportImage_PinNames");
		static readonly UIHandle ID_Grid = new("ExportImage_Grid");

		// Plain "x": the UI fonts may not include the multiplication sign
		static readonly string[] ScaleNames = { "1x", "2x", "4x" };
		static readonly int[] ScaleValues = { 1, 2, 4 };
		static readonly string[] PinNameOptions = { "All", "As shown" };
		static readonly string[] GridOptions = { "Hide", "Show" };

		const int DefaultScaleIndex = 1;
		const int PinNamesAllIndex = 0;
		const int GridHideIndex = 0;
		const int GridShowIndex = 1;

		public static void Open() => UIDrawer.SetActiveMenu(UIDrawer.MenuType.ExportImage);

		public static void OnMenuOpened()
		{
			UI.GetWheelSelectorState(ID_Scale).index = DefaultScaleIndex;
			UI.GetWheelSelectorState(ID_PinNames).index = PinNamesAllIndex;
			UI.GetWheelSelectorState(ID_Grid).index = GridHideIndex;
		}

		public static void DrawMenu()
		{
			MenuHelper.DrawBackgroundOverlay();
			bool hasElements = Project.ActiveProject.ViewedChip.Elements.Count > 0;
			Vector2 rowSize = new(Width, DrawSettings.SelectorWheelHeight);
			float wheelWidth = Width * 0.45f;

			Draw.ID panelID = UI.ReservePanel();
			int buttonIndex;

			using (UI.BeginBoundsScope(true))
			{
				float totalHeight = 1.5f + Spacing + (rowSize.y + RowSpacing) * 3 + Spacing + DrawSettings.ButtonHeight;
				Vector2 topLeft = new(UI.Centre.x - Width / 2, UI.Centre.y + totalHeight / 2);

				MenuHelper.DrawText("EXPORT IMAGE", topLeft + new Vector2(Width / 2, -0.75f), Anchor.TextCentre, Color.white, true);
				Vector2 pos = topLeft + Vector2.down * (1.5f + Spacing);

				MenuHelper.LabeledOptionsWheel("Scale", Color.white, pos, rowSize, ID_Scale, ScaleNames, wheelWidth, true);
				pos = UI.PrevBounds.BottomLeft + Vector2.down * RowSpacing;
				MenuHelper.LabeledOptionsWheel("Pin names", Color.white, pos, rowSize, ID_PinNames, PinNameOptions, wheelWidth, true);
				pos = UI.PrevBounds.BottomLeft + Vector2.down * RowSpacing;
				MenuHelper.LabeledOptionsWheel("Grid", Color.white, pos, rowSize, ID_Grid, GridOptions, wheelWidth, true);
				pos = UI.PrevBounds.BottomLeft + Vector2.down * Spacing;

				if (!hasElements)
				{
					MenuHelper.DrawText("Nothing to export", pos + new Vector2(Width / 2, -0.5f), Anchor.TextCentre, new Color(1, 0.4f, 0.4f));
					pos = UI.PrevBounds.BottomLeft + Vector2.down * Spacing;
					pos.x = topLeft.x;
				}

				buttonIndex = MenuHelper.DrawButtonPair("CANCEL", "EXPORT", pos, Width, false, true, hasElements);
				MenuHelper.DrawReservedMenuPanel(panelID, UI.GetCurrentBoundsScope());
			}

			// The menu stays open while the export runs (the exporter closes it), so ignore further presses
			if (ImageExporter.IsCapturing) return;

			if (KeyboardShortcuts.CancelShortcutTriggered) buttonIndex = 0;
			else if (KeyboardShortcuts.ConfirmShortcutTriggered && hasElements) buttonIndex = 1;

			if (buttonIndex == 0) UIDrawer.SetActiveMenu(UIDrawer.MenuType.None);
			else if (buttonIndex == 1) ImageExporter.Export(CollectOptions());
		}

		static ExportOptions CollectOptions()
		{
			return new ExportOptions
			{
				Scale = ScaleValues[UI.GetWheelSelectorState(ID_Scale).index],
				AllPinNames = UI.GetWheelSelectorState(ID_PinNames).index == PinNamesAllIndex,
				ShowGrid = UI.GetWheelSelectorState(ID_Grid).index == GridShowIndex
			};
		}
	}
}
