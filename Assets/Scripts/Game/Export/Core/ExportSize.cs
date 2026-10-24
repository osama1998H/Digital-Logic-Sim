using System;

namespace DLS.Game.Export
{
	// Engine-free output size for image export: the requested scale is lowered until the image fits the texture size limit
	public static class ExportSize
	{
		public static (int width, int height, int scale) Fit(int screenWidth, int screenHeight, int requestedScale, int maxSize)
		{
			int longest = Math.Max(screenWidth, screenHeight);

			for (int scale = Math.Max(1, requestedScale); scale >= 1; scale--)
			{
				if ((long)longest * scale <= maxSize) return (screenWidth * scale, screenHeight * scale, scale);
			}

			// Even the window size is too big: shrink it, keeping the aspect ratio
			double shrink = maxSize / (double)longest;
			int width = Math.Max(1, (int)(screenWidth * shrink));
			int height = Math.Max(1, (int)(screenHeight * shrink));
			return (width, height, 1);
		}
	}
}
