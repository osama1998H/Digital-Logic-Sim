using System;
using System.Collections.Generic;

namespace DLS.Game.Layout
{
	// Grid of candidate wire corner positions for the orthogonal router
	public class RoutingLattice
	{
		const float Eps = 1e-4f;

		public readonly float[] Xs, Ys;
		readonly LayoutRect[] expandedObstacles;
		readonly bool[] obstacleBlocked;
		readonly int[] reservedPinKey;
		readonly float gridSize;

		public int Width => Xs.Length;
		public int Height => Ys.Length;

		RoutingLattice(float[] xs, float[] ys, LayoutProblem problem, RouterSettings settings)
		{
			Xs = xs;
			Ys = ys;
			gridSize = settings.GridSize;

			float half = settings.GridSize / 2;
			expandedObstacles = new LayoutRect[problem.Obstacles.Count];
			for (int i = 0; i < expandedObstacles.Length; i++)
			{
				LayoutRect r = problem.Obstacles[i];
				expandedObstacles[i] = new LayoutRect(r.OwnerID, r.MinX - half, r.MinY - half, r.MaxX + half, r.MaxY + half);
			}

			obstacleBlocked = new bool[Width * Height];
			reservedPinKey = new int[Width * Height];
			Array.Fill(reservedPinKey, -1);

			foreach (LayoutRect r in expandedObstacles)
			{
				int x0 = FirstIndexAbove(Xs, r.MinX + Eps), x1 = FirstIndexAbove(Xs, r.MaxX - Eps) - 1;
				int y0 = FirstIndexAbove(Ys, r.MinY + Eps), y1 = FirstIndexAbove(Ys, r.MaxY - Eps) - 1;
				for (int iy = y0; iy <= y1; iy++)
				{
					for (int ix = x0; ix <= x1; ix++) obstacleBlocked[iy * Width + ix] = true;
				}
			}
		}

		public static RoutingLattice Build(LayoutProblem problem, RouterSettings settings)
		{
			float g = settings.GridSize;
			bool any = false;
			float minX = 0, minY = 0, maxX = 0, maxY = 0;

			void Include(float x0, float y0, float x1, float y1)
			{
				if (!any)
				{
					minX = x0;
					minY = y0;
					maxX = x1;
					maxY = y1;
					any = true;
					return;
				}

				minX = Math.Min(minX, x0);
				minY = Math.Min(minY, y0);
				maxX = Math.Max(maxX, x1);
				maxY = Math.Max(maxY, y1);
			}

			foreach (LayoutRect r in problem.Obstacles) Include(r.MinX, r.MinY, r.MaxX, r.MaxY);
			foreach (LayoutPin pin in problem.Pins.Values) Include(pin.Position.X, pin.Position.Y, pin.Position.X, pin.Position.Y);

			if (!any) return new RoutingLattice(new float[0], new float[0], problem, settings);

			int m = settings.LatticeMarginCells;
			int cx0 = (int)Math.Floor(minX / g + Eps) - m, cx1 = (int)Math.Ceiling(maxX / g - Eps) + m;
			int cy0 = (int)Math.Floor(minY / g + Eps) - m, cy1 = (int)Math.Ceiling(maxY / g - Eps) + m;

			float[] xs = new float[cx1 - cx0 + 1];
			for (int i = 0; i < xs.Length; i++) xs[i] = (float)((cx0 + i) * (double)g);

			// Off-grid pin rows replace any grid row within epsilon so pin Y is matched exactly
			List<float> pinYs = new();
			foreach (LayoutPin pin in problem.Pins.Values) pinYs.Add(pin.Position.Y);
			pinYs.Sort();

			List<float> ys = new();
			for (int c = cy0; c <= cy1; c++)
			{
				float y = (float)(c * (double)g);
				if (!NearAny(pinYs, y)) ys.Add(y);
			}

			ys.AddRange(pinYs);
			ys.Sort();
			List<float> uniqueYs = new();
			foreach (float y in ys)
			{
				if (uniqueYs.Count == 0 || y > uniqueYs[uniqueYs.Count - 1] + Eps) uniqueYs.Add(y);
			}

			RoutingLattice lattice = new(xs, uniqueYs.ToArray(), problem, settings);

			// Sorted so shared escape nodes always go to the lowest pin key
			List<int> keys = new(problem.Pins.Keys);
			keys.Sort();
			foreach (int key in keys)
			{
				(int ix, int iy) = lattice.EscapeNode(problem.Pins[key]);
				if (ix < 0 || iy < 0) continue;
				int i = iy * lattice.Width + ix;
				if (lattice.reservedPinKey[i] == -1) lattice.reservedPinKey[i] = key;
			}

			return lattice;
		}

		public bool IsBlocked(int ix, int iy)
		{
			int i = iy * Width + ix;
			return obstacleBlocked[i] || reservedPinKey[i] != -1;
		}

		public int ReservedPinKey(int ix, int iy) => reservedPinKey[iy * Width + ix];

		public int IndexOfX(float x) => IndexOf(Xs, x);

		public int IndexOfY(float y) => IndexOf(Ys, y);

		public (int ix, int iy) EscapeNode(LayoutPin pin)
		{
			int iy = IndexOfY(pin.Position.Y);
			if (iy < 0) return (-1, -1);

			int dir = pin.ExitDirX >= 0 ? 1 : -1;
			int start = dir > 0 ? FirstIndexAbove(Xs, pin.Position.X - Eps) : FirstIndexAbove(Xs, pin.Position.X + Eps) - 1;
			for (int ix = start; ix >= 0 && ix < Width; ix += dir)
			{
				if ((Xs[ix] - pin.Position.X) * dir < gridSize - Eps) continue;
				if (!obstacleBlocked[iy * Width + ix]) return (ix, iy);
			}

			return (-1, -1);
		}

		public LayoutPoint Point(int ix, int iy) => new(Xs[ix], Ys[iy]);

		static int IndexOf(float[] values, float v)
		{
			int i = FirstIndexAbove(values, v - Eps);
			return i < values.Length && Math.Abs(values[i] - v) <= Eps ? i : -1;
		}

		// First index whose value is >= v (values sorted ascending)
		static int FirstIndexAbove(float[] values, float v)
		{
			int lo = 0, hi = values.Length;
			while (lo < hi)
			{
				int mid = (lo + hi) / 2;
				if (values[mid] < v) lo = mid + 1;
				else hi = mid;
			}

			return lo;
		}

		static bool NearAny(List<float> sorted, float v)
		{
			int lo = 0, hi = sorted.Count;
			while (lo < hi)
			{
				int mid = (lo + hi) / 2;
				if (sorted[mid] < v - Eps) lo = mid + 1;
				else hi = mid;
			}

			return lo < sorted.Count && sorted[lo] <= v + Eps;
		}
	}
}
