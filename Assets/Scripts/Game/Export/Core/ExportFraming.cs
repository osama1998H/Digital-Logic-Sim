using System;

namespace DLS.Game.Export
{
	// Engine-free camera framing for image export: picks an orthographic centre and half-height that fit the bounds
	public static class ExportFraming
	{
		// Stops a zero-size selection (a single chip, or an empty canvas) from framing to a zero-height view
		const float MinHalfHeight = 1f;

		public static (float centreX, float centreY, float orthoSize) Fit(float minX, float minY, float maxX, float maxY, float aspect, float paddingFraction)
		{
			float halfWidth = (maxX - minX) * 0.5f;
			float halfHeight = (maxY - minY) * 0.5f;
			float centreX = minX + halfWidth;
			float centreY = minY + halfHeight;

			// The view must be tall enough for the bounds, and wide enough: width = 2 * orthoSize * aspect
			float fitHalfHeight = Math.Max(MinHalfHeight, Math.Max(halfHeight, halfWidth / aspect));
			return (centreX, centreY, fitHalfHeight * (1f + paddingFraction));
		}
	}
}
