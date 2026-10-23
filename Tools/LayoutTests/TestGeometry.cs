using System;
using System.Collections.Generic;
using DLS.Game.Layout;

namespace LayoutTests
{
	public static class TestGeometry
	{
		const float Eps = 1e-4f;

		public static bool AllAxisAligned(List<LayoutPoint> pts)
		{
			for (int i = 0; i < pts.Count - 1; i++)
			{
				bool sameX = Math.Abs(pts[i].X - pts[i + 1].X) < Eps;
				bool sameY = Math.Abs(pts[i].Y - pts[i + 1].Y) < Eps;
				if (!sameX && !sameY) return false;
			}

			return true;
		}

		// Axis-aligned segments only
		public static bool SegmentIntersectsRect(LayoutPoint a, LayoutPoint b, LayoutRect r)
		{
			float minX = Math.Min(a.X, b.X), maxX = Math.Max(a.X, b.X);
			float minY = Math.Min(a.Y, b.Y), maxY = Math.Max(a.Y, b.Y);
			return maxX >= r.MinX && minX <= r.MaxX && maxY >= r.MinY && minY <= r.MaxY;
		}

		public static bool SegmentsOverlapCollinear(LayoutPoint a1, LayoutPoint a2, LayoutPoint b1, LayoutPoint b2, float minGap)
		{
			bool aVert = Math.Abs(a1.X - a2.X) < Eps, bVert = Math.Abs(b1.X - b2.X) < Eps;
			bool aHorz = Math.Abs(a1.Y - a2.Y) < Eps, bHorz = Math.Abs(b1.Y - b2.Y) < Eps;

			if (aHorz && bHorz)
			{
				if (Math.Abs(a1.Y - b1.Y) >= minGap) return false;
				return Math.Min(a1.X, a2.X) < Math.Max(b1.X, b2.X) && Math.Min(b1.X, b2.X) < Math.Max(a1.X, a2.X);
			}

			if (aVert && bVert)
			{
				if (Math.Abs(a1.X - b1.X) >= minGap) return false;
				return Math.Min(a1.Y, a2.Y) < Math.Max(b1.Y, b2.Y) && Math.Min(b1.Y, b2.Y) < Math.Max(a1.Y, a2.Y);
			}

			return false;
		}

		public static bool PointOnSegment(LayoutPoint p, LayoutPoint a, LayoutPoint b)
		{
			return p.X >= Math.Min(a.X, b.X) - Eps && p.X <= Math.Max(a.X, b.X) + Eps &&
			       p.Y >= Math.Min(a.Y, b.Y) - Eps && p.Y <= Math.Max(a.Y, b.Y) + Eps;
		}
	}
}
