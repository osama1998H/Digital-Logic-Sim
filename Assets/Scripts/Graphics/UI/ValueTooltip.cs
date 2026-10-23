using DLS.Game;
using DLS.Game.Inspect;
using Seb.Helpers;
using Seb.Vis;
using UnityEngine;

namespace DLS.Graphics
{
	// Drawn in raw screen space (not with UI.* widgets) so it never counts as the mouse being over UI
	public static class ValueTooltip
	{
		const float FontSizeAt1080 = 14;
		const float PaddingPx = 6;
		const float CursorOffsetPx = 16;

		public static void Draw(Project project)
		{
			if (project == null || project.description.Prefs_HideValueTooltips != 0) return;
			if (UIDrawer.InInputBlockingMenu() || UIDrawer.ActiveMenu == UIDrawer.MenuType.ChipCustomization || ContextMenu.IsOpen || InteractionState.MouseIsOverUI) return;
			if (project.controller == null || project.controller.IsPlacingOrMovingElementOrCreatingWire || project.controller.IsCreatingSelectionBox) return;

			IInteractable element = InteractionState.ElementUnderMouse;
			if (element is not (PinInstance or WireInstance)) return;
			if (!PinValueSource.TryGetHoverInfo(element, project.ViewedChip, project.CanEditViewedChip, out string name, out uint state, out int bitCount, out bool connected)) return;

			string[] lines = connected ? PinValueFormat.TooltipLines(name, state, bitCount) : new[] { name, "not connected" };

			DrawSettings.UIThemeDLS theme = DrawSettings.ActiveUITheme;
			float fontSize = FontSizeAt1080 * Screen.height / 1080f;
			float pad = PaddingPx;

			float width = 0;
			float height = 0;
			float[] lineHeights = new float[lines.Length];
			for (int i = 0; i < lines.Length; i++)
			{
				Vector2 size = Seb.Vis.Draw.CalculateTextBoundsSize(lines[i], fontSize, i == 0 ? theme.FontBold : theme.FontRegular);
				width = Mathf.Max(width, size.x);
				lineHeights[i] = Mathf.Max(size.y, fontSize);
				height += lineHeights[i];
			}

			width += pad * 2;
			height += pad * 2;

			// Below-right of the cursor, flipped to the other side near the screen edges
			Vector2 mouse = InputHelper.MousePos;
			float left = mouse.x + CursorOffsetPx;
			float top = mouse.y - CursorOffsetPx;
			if (left + width > Screen.width) left = mouse.x - CursorOffsetPx - width;
			if (top - height < 0) top = mouse.y + CursorOffsetPx + height;
			left = Mathf.Clamp(left, 0, Mathf.Max(0, Screen.width - width));
			top = Mathf.Clamp(top, Mathf.Min(height, Screen.height), Screen.height);

			Seb.Vis.Draw.StartLayer(Vector2.zero, 1, true);
			Seb.Vis.Draw.Quad(new Vector2(left + width / 2, top - height / 2), new Vector2(width, height), DrawSettings.ActiveTheme.PinLabelCol);

			float y = top - pad;
			for (int i = 0; i < lines.Length; i++)
			{
				Seb.Vis.Draw.Text(i == 0 ? theme.FontBold : theme.FontRegular, lines[i], fontSize, new Vector2(left + pad, y - lineHeights[i] / 2), Anchor.TextCentreLeft, Color.white);
				y -= lineHeights[i];
			}
		}
	}
}
