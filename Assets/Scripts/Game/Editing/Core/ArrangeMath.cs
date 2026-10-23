using System;
using System.Collections.Generic;

namespace DLS.Game.Editing
{
	public static class ArrangeMath
	{
		// Computes how far each box should move to align/distribute the selection (deltas are in the same order as the boxes).
		// When a snap step is non-zero, each element's anchor point is snapped to the grid along the axis that changed.
		public static (float dx, float dy)[] ComputeDeltas(IReadOnlyList<ElementBox> boxes, ArrangeOp op, float snapStepX, float snapStepY)
		{
			int n = boxes.Count;
			float[] dx = new float[n];
			float[] dy = new float[n];
			bool horizontal = op is ArrangeOp.AlignLeft or ArrangeOp.AlignHCentre or ArrangeOp.AlignRight or ArrangeOp.DistributeH;

			if (n >= 2)
			{
				switch (op)
				{
					case ArrangeOp.AlignLeft:
					{
						float target = Min(boxes, b => b.MinX);
						for (int i = 0; i < n; i++) dx[i] = target - boxes[i].MinX;
						break;
					}
					case ArrangeOp.AlignRight:
					{
						float target = Max(boxes, b => b.MaxX);
						for (int i = 0; i < n; i++) dx[i] = target - boxes[i].MaxX;
						break;
					}
					case ArrangeOp.AlignHCentre:
					{
						float target = (Min(boxes, b => b.MinX) + Max(boxes, b => b.MaxX)) / 2;
						for (int i = 0; i < n; i++) dx[i] = target - boxes[i].CentreX;
						break;
					}
					case ArrangeOp.AlignBottom:
					{
						float target = Min(boxes, b => b.MinY);
						for (int i = 0; i < n; i++) dy[i] = target - boxes[i].MinY;
						break;
					}
					case ArrangeOp.AlignTop:
					{
						float target = Max(boxes, b => b.MaxY);
						for (int i = 0; i < n; i++) dy[i] = target - boxes[i].MaxY;
						break;
					}
					case ArrangeOp.AlignVCentre:
					{
						float target = (Min(boxes, b => b.MinY) + Max(boxes, b => b.MaxY)) / 2;
						for (int i = 0; i < n; i++) dy[i] = target - boxes[i].CentreY;
						break;
					}
					case ArrangeOp.DistributeH:
						Distribute(boxes, dx, b => b.MinX, b => b.Width, b => b.CentreX);
						break;
					case ArrangeOp.DistributeV:
						Distribute(boxes, dy, b => b.MinY, b => b.Height, b => b.CentreY);
						break;
				}
			}

			(float dx, float dy)[] deltas = new (float, float)[n];
			for (int i = 0; i < n; i++)
			{
				if (horizontal && snapStepX > 0) dx[i] = Snap(boxes[i].AnchorX + dx[i], snapStepX) - boxes[i].AnchorX;
				if (!horizontal && snapStepY > 0) dy[i] = Snap(boxes[i].AnchorY + dy[i], snapStepY) - boxes[i].AnchorY;
				deltas[i] = (dx[i], dy[i]);
			}

			return deltas;
		}

		// Spreads the boxes out between the two outermost ones (which stay in place) so that the gaps between them are equal.
		// If the boxes don't fit without overlapping, their centres are spaced evenly instead.
		static void Distribute(IReadOnlyList<ElementBox> boxes, float[] delta, Func<ElementBox, float> min, Func<ElementBox, float> size, Func<ElementBox, float> centre)
		{
			int n = boxes.Count;
			if (n < 3) return;

			int[] order = new int[n];
			for (int i = 0; i < n; i++) order[i] = i;
			Array.Sort(order, (a, b) =>
			{
				int c = centre(boxes[a]).CompareTo(centre(boxes[b]));
				return c != 0 ? c : boxes[a].ID.CompareTo(boxes[b].ID);
			});

			ElementBox first = boxes[order[0]];
			ElementBox last = boxes[order[n - 1]];
			float totalSize = 0;
			foreach (int i in order) totalSize += size(boxes[i]);
			float span = min(last) + size(last) - min(first);
			float gap = (span - totalSize) / (n - 1);

			if (gap >= 0)
			{
				float pos = min(first) + size(first) + gap;
				for (int k = 1; k < n - 1; k++)
				{
					ElementBox box = boxes[order[k]];
					delta[order[k]] = pos - min(box);
					pos += size(box) + gap;
				}
			}
			else
			{
				float step = (centre(last) - centre(first)) / (n - 1);
				for (int k = 1; k < n - 1; k++)
				{
					delta[order[k]] = centre(first) + step * k - centre(boxes[order[k]]);
				}
			}
		}

		// Inclusive overlap test (touching counts as overlapping), matching the editor's placement rules
		public static bool Overlaps(ElementBox a, ElementBox b)
		{
			return !(b.MinX > a.MaxX || b.MaxX < a.MinX || b.MinY > a.MaxY || b.MaxY < a.MinY);
		}

		public static bool AnyOverlap(IReadOnlyList<ElementBox> moved, IReadOnlyList<ElementBox> others)
		{
			for (int i = 0; i < moved.Count; i++)
			{
				for (int j = i + 1; j < moved.Count; j++)
				{
					if (Overlaps(moved[i], moved[j])) return true;
				}

				foreach (ElementBox other in others)
				{
					if (Overlaps(moved[i], other)) return true;
				}
			}

			return false;
		}

		// Order of a chip's input (or output) pins: top to bottom, then left to right
		public static int[] PinOrder(IReadOnlyList<PositionedItem> pins)
		{
			List<PositionedItem> sorted = new(pins);
			sorted.Sort((a, b) => a.Y != b.Y ? b.Y.CompareTo(a.Y) : a.X != b.X ? a.X.CompareTo(b.X) : a.ID.CompareTo(b.ID));
			int[] ids = new int[sorted.Count];
			for (int i = 0; i < ids.Length; i++) ids[i] = sorted[i].ID;
			return ids;
		}

		static float Snap(float v, float step) => MathF.Round(v / step) * step;

		static float Min(IReadOnlyList<ElementBox> boxes, Func<ElementBox, float> f)
		{
			float m = float.MaxValue;
			foreach (ElementBox b in boxes) m = MathF.Min(m, f(b));
			return m;
		}

		static float Max(IReadOnlyList<ElementBox> boxes, Func<ElementBox, float> f)
		{
			float m = float.MinValue;
			foreach (ElementBox b in boxes) m = MathF.Max(m, f(b));
			return m;
		}
	}
}
