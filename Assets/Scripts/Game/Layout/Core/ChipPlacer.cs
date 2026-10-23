using System;
using System.Collections.Generic;

namespace DLS.Game.Layout
{
	public enum PlaceKind
	{
		Chip,
		InputPin,
		OutputPin
	}

	public struct PlaceNode
	{
		public int ID;
		public PlaceKind Kind;
		public LayoutPoint Position;
		public float BoxWidth, BoxHeight;
		public LayoutPoint BoxOffset; // bbox centre - Position
		public LayoutPoint SnapOffset; // snap point - Position
	}

	public struct PlaceEdge
	{
		public int FromID, ToID;
	}

	public class PlacementInput
	{
		public List<PlaceNode> Nodes = new();
		public List<PlaceEdge> Edges = new();
		public float GridSize = 0.125f;
	}

	// Layered (Sugiyama-style) placement. Input pins never move; output pins only translate horizontally together,
	// which preserves the game's pin order (y descending, then x).
	public static class ChipPlacer
	{
		const float EdgeEps = 1e-4f;

		public static Dictionary<int, LayoutPoint> Place(PlacementInput input)
		{
			float grid = input.GridSize;
			Dictionary<int, PlaceNode> nodes = new();
			foreach (PlaceNode n in input.Nodes) nodes[n.ID] = n;

			List<int> chipIDs = new();
			List<int> outputIDs = new();
			List<float> inputRights = new();
			foreach (PlaceNode n in input.Nodes)
			{
				if (n.Kind == PlaceKind.Chip) chipIDs.Add(n.ID);
				else if (n.Kind == PlaceKind.OutputPin) outputIDs.Add(n.ID);
				else inputRights.Add(CentreX(n, n.Position.X) + n.BoxWidth / 2);
			}
			chipIDs.Sort();
			outputIDs.Sort();

			// Valid edges: (input|chip) -> chip, deduplicated. Chip -> output only marks the chip as connected.
			SortedSet<(int, int)> layerEdgeSet = new();
			HashSet<int> connected = new();
			foreach (PlaceEdge e in input.Edges)
			{
				if (e.FromID == e.ToID || !nodes.TryGetValue(e.FromID, out PlaceNode from) || !nodes.TryGetValue(e.ToID, out PlaceNode to)) continue;
				if (to.Kind == PlaceKind.Chip && from.Kind != PlaceKind.OutputPin)
				{
					layerEdgeSet.Add((e.FromID, e.ToID));
					connected.Add(e.FromID);
					connected.Add(e.ToID);
				}
				else if (to.Kind == PlaceKind.OutputPin && from.Kind == PlaceKind.Chip) connected.Add(e.FromID);
			}

			List<(int from, int to)> edges = BreakCycles(layerEdgeSet, nodes);
			Dictionary<int, int> layer = AssignLayers(edges, nodes, chipIDs, connected);

			Dictionary<int, LayoutPoint> result = new();
			int maxLayer = 0;
			foreach (int id in chipIDs)
				if (layer.TryGetValue(id, out int l) && l > maxLayer) maxLayer = l;

			// Reference left edge for column 1
			bool hasInputs = inputRights.Count > 0;
			float baseLeft = 0;
			if (hasInputs)
			{
				baseLeft = float.MinValue;
				foreach (float r in inputRights) baseLeft = Math.Max(baseLeft, r);
			}
			else
			{
				baseLeft = float.MaxValue;
				foreach (int id in chipIDs) baseLeft = Math.Min(baseLeft, CentreX(nodes[id], nodes[id].Position.X) - nodes[id].BoxWidth / 2);
				if (chipIDs.Count == 0) baseLeft = 0;
			}

			// Columns
			List<int>[] columns = new List<int>[maxLayer + 1];
			float[] colWidth = new float[maxLayer + 1];
			for (int i = 1; i <= maxLayer; i++) columns[i] = new List<int>();
			foreach (int id in chipIDs)
			{
				if (!layer.TryGetValue(id, out int l)) continue;
				columns[l].Add(id);
				colWidth[l] = Math.Max(colWidth[l], nodes[id].BoxWidth);
			}

			float[] gapAfter = new float[maxLayer + 1]; // gapAfter[k] = gap between column k and k+1 (k = 0: input pins)
			for (int k = 0; k < maxLayer; k++)
			{
				int spanning = 0;
				foreach ((int from, int to) e in edges)
					if (layer[e.from] <= k && layer[e.to] >= k + 1) spanning++;
				gapAfter[k] = Math.Min((6 + 2 * spanning) * grid, 40 * grid);
			}

			float colLeft = baseLeft + (hasInputs ? gapAfter[0] : 0);
			float firstColLeft = colLeft;
			Dictionary<int, List<int>> preds = new();
			foreach ((int from, int to) e in edges)
			{
				if (!preds.TryGetValue(e.to, out List<int> list)) preds[e.to] = list = new List<int>();
				list.Add(e.from);
			}

			float rightmost = float.MinValue, lowest = float.MaxValue;
			for (int c = 1; c <= maxLayer; c++)
			{
				List<int> col = columns[c];
				float centreX = colLeft + colWidth[c] / 2;

				List<(int id, float desired)> order = new();
				foreach (int id in col)
				{
					PlaceNode n = nodes[id];
					float sum = 0;
					int count = 0;
					if (preds.TryGetValue(id, out List<int> ps))
						foreach (int p in ps)
						{
							PlaceNode pn = nodes[p];
							LayoutPoint pos = result.TryGetValue(p, out LayoutPoint placed) ? placed : pn.Position;
							sum += pos.Y + pn.BoxOffset.Y;
							count++;
						}
					order.Add((id, count > 0 ? sum / count : n.Position.Y + n.BoxOffset.Y));
				}
				order.Sort((a, b) => a.desired != b.desired ? b.desired.CompareTo(a.desired) : a.id.CompareTo(b.id));

				float[] centreY = new float[order.Count];
				float prevBottom = float.MaxValue;
				float shift = 0;
				for (int i = 0; i < order.Count; i++)
				{
					float h = nodes[order[i].id].BoxHeight;
					float y = order[i].desired;
					if (i > 0) y = Math.Min(y, prevBottom - 3 * grid - h / 2);
					centreY[i] = y;
					prevBottom = y - h / 2;
					shift += order[i].desired - y;
				}
				if (order.Count > 0) shift /= order.Count;

				// Snap, then push down by whole cells to undo any overlap the snapping introduced
				prevBottom = float.MaxValue;
				for (int i = 0; i < order.Count; i++)
				{
					PlaceNode n = nodes[order[i].id];
					float posX = SnapAxis(centreX - n.BoxOffset.X + n.SnapOffset.X, grid) - n.SnapOffset.X;
					float posY = SnapAxis(centreY[i] + shift - n.BoxOffset.Y + n.SnapOffset.Y, grid) - n.SnapOffset.Y;
					while (i > 0 && posY + n.BoxOffset.Y + n.BoxHeight / 2 > prevBottom - 2 * grid + EdgeEps) posY -= grid;
					prevBottom = posY + n.BoxOffset.Y - n.BoxHeight / 2;
					result[n.ID] = new LayoutPoint(posX, posY);
					rightmost = Math.Max(rightmost, CentreX(n, posX) + n.BoxWidth / 2);
					lowest = Math.Min(lowest, prevBottom);
				}

				if (c < maxLayer) colLeft += colWidth[c] + gapAfter[c];
			}

			// Unconnected chips: a row below everything placed so far
			List<int> loose = new();
			foreach (int id in chipIDs)
				if (!layer.ContainsKey(id)) loose.Add(id);
			if (loose.Count > 0)
			{
				bool anyLayered = result.Count > 0;
				float rowTop = anyLayered ? lowest - 4 * grid : float.MinValue;
				if (!anyLayered)
					foreach (int id in loose) rowTop = Math.Max(rowTop, nodes[id].Position.Y + nodes[id].BoxOffset.Y + nodes[id].BoxHeight / 2);
				float x = firstColLeft;
				float all = rightmost;
				foreach (int id in loose)
				{
					PlaceNode n = nodes[id];
					float posX = SnapAxis(x + n.BoxWidth / 2 - n.BoxOffset.X + n.SnapOffset.X, grid) - n.SnapOffset.X;
					float posY = SnapAxis(rowTop - n.BoxHeight / 2 - n.BoxOffset.Y + n.SnapOffset.Y, grid) - n.SnapOffset.Y;
					while (anyLayered && posY + n.BoxOffset.Y + n.BoxHeight / 2 > rowTop + EdgeEps) posY -= grid;
					result[id] = new LayoutPoint(posX, posY);
					x += n.BoxWidth + 3 * grid;
					all = Math.Max(all, CentreX(n, posX) + n.BoxWidth / 2);
				}
				if (!anyLayered) rightmost = all;
			}

			// Output pins: one shared horizontal translation
			if (outputIDs.Count > 0)
			{
				float left = float.MaxValue;
				foreach (int id in outputIDs) left = Math.Min(left, CentreX(nodes[id], nodes[id].Position.X) - nodes[id].BoxWidth / 2);
				float dx = rightmost > float.MinValue ? rightmost + 6 * grid - left : 0;
				foreach (int id in outputIDs) result[id] = new LayoutPoint(nodes[id].Position.X + dx, nodes[id].Position.Y);
			}

			return result;
		}

		static float CentreX(PlaceNode n, float posX) => posX + n.BoxOffset.X;

		static float SnapAxis(float v, float grid) => (float)Math.Round(v / grid) * grid;

		// Iterative DFS in ascending ID order (roots first); back edges are reversed.
		static List<(int from, int to)> BreakCycles(SortedSet<(int, int)> edgeSet, Dictionary<int, PlaceNode> nodes)
		{
			Dictionary<int, List<int>> adj = new();
			HashSet<int> hasIncoming = new();
			SortedSet<int> ids = new();
			foreach ((int from, int to) in edgeSet)
			{
				if (!adj.TryGetValue(from, out List<int> list)) adj[from] = list = new List<int>();
				list.Add(to);
				hasIncoming.Add(to);
				ids.Add(from);
				ids.Add(to);
			}

			Dictionary<int, int> state = new(); // 1 = on stack, 2 = done
			HashSet<(int, int)> reversed = new();
			List<int> roots = new();
			foreach (int id in ids)
				if (!hasIncoming.Contains(id)) roots.Add(id);
			foreach (int id in ids)
				if (hasIncoming.Contains(id)) roots.Add(id);

			foreach (int root in roots)
			{
				if (state.ContainsKey(root)) continue;
				Stack<(int node, int next)> stack = new();
				stack.Push((root, 0));
				state[root] = 1;
				while (stack.Count > 0)
				{
					(int node, int next) = stack.Pop();
					List<int> outs = adj.TryGetValue(node, out List<int> o) ? o : null;
					if (outs == null || next >= outs.Count)
					{
						state[node] = 2;
						continue;
					}
					stack.Push((node, next + 1));
					int target = outs[next];
					if (!state.TryGetValue(target, out int s))
					{
						state[target] = 1;
						stack.Push((target, 0));
					}
					else if (s == 1) reversed.Add((node, target));
				}
			}

			SortedSet<(int, int)> final = new();
			foreach ((int from, int to) in edgeSet)
				final.Add(reversed.Contains((from, to)) ? (to, from) : (from, to));
			List<(int from, int to)> list2 = new();
			foreach ((int from, int to) e in final)
				if (nodes[e.to].Kind == PlaceKind.Chip) list2.Add(e);
			return list2;
		}

		// Longest-path layering over the acyclic edge list; connected chips only.
		static Dictionary<int, int> AssignLayers(List<(int from, int to)> edges, Dictionary<int, PlaceNode> nodes, List<int> chipIDs, HashSet<int> connected)
		{
			Dictionary<int, List<int>> succ = new();
			Dictionary<int, int> inDegree = new();
			foreach ((int from, int to) in edges)
			{
				if (!succ.TryGetValue(from, out List<int> list)) succ[from] = list = new List<int>();
				list.Add(to);
				inDegree[to] = inDegree.TryGetValue(to, out int d) ? d + 1 : 1;
			}

			Dictionary<int, int> layer = new();
			SortedSet<int> ready = new();
			foreach (PlaceNode n in nodes.Values)
			{
				if (n.Kind == PlaceKind.InputPin) layer[n.ID] = 0;
				if (n.Kind != PlaceKind.OutputPin && !inDegree.ContainsKey(n.ID) && (n.Kind == PlaceKind.InputPin || connected.Contains(n.ID))) ready.Add(n.ID);
			}
			foreach (int id in ready)
				if (!layer.ContainsKey(id)) layer[id] = 1;

			while (ready.Count > 0)
			{
				int id = ready.Min;
				ready.Remove(id);
				if (!succ.TryGetValue(id, out List<int> outs)) continue;
				foreach (int t in outs)
				{
					layer[t] = Math.Max(layer.TryGetValue(t, out int lt) ? lt : 1, layer[id] + 1);
					if (--inDegree[t] == 0) ready.Add(t);
				}
			}
			return layer;
		}
	}
}
