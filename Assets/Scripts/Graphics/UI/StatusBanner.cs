using Seb.Types;
using Seb.Vis;
using Seb.Vis.UI;
using UnityEngine;
using static DLS.Graphics.DrawSettings;

namespace DLS.Graphics
{
	// A short message shown at the top of the screen for a few seconds.
	// Drawn with raw screen-space shapes rather than UI elements so that it never blocks clicks on whatever is underneath.
	public static class StatusBanner
	{
		const float Height = 2.2f;
		const float PadX = 1.5f;
		const float TopMargin = 0.4f;

		static string text;
		static float hideTime;

		public static void Show(string message, float seconds)
		{
			text = message;
			hideTime = Time.time + seconds;
		}

		// Must be called inside a UI scope
		public static void Draw()
		{
			if (string.IsNullOrEmpty(text)) return;
			if (Time.time >= hideTime)
			{
				text = null;
				return;
			}

			FontType font = ActiveUITheme.FontBold;
			float fontSize = ActiveUITheme.FontSizeRegular;
			Vector2 size = new(Seb.Vis.Draw.CalculateTextBoundsSize(text, fontSize, font).x + PadX * 2, Height);
			Vector2 centre = new(UI.Centre.x, UI.Height - TopMargin - Height / 2);

			// UI space -> screen space
			float scale = UI.UIToScreenSpace(Vector2.right).x - UI.UIToScreenSpace(Vector2.zero).x;
			Vector2 centreScreen = UI.UIToScreenSpace(centre);

			// New layer so the banner sits on top of any menu drawn before it
			Seb.Vis.Draw.StartLayer(Vector2.zero, 1, true);
			Seb.Vis.Draw.Quad(centreScreen, size * scale, ActiveUITheme.InfoBarCol);
			Seb.Vis.Draw.Text(font, text, fontSize * scale, centreScreen, Anchor.TextCentre, Color.white);
		}

		public static void Reset()
		{
			text = null;
		}
	}
}
