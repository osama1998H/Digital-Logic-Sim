using System;
using System.Collections.Generic;

namespace DLS.Game.Layout
{
	// Orthogonal A* wire router over a RoutingLattice
	public static class WireRouter
	{
		const float Eps = 1e-4f;

		public static LayoutResult Route(LayoutProblem problem, RouterSettings settings)
		{
			LayoutResult result = new();
			if (problem.Connections.Count == 0) return result;

			RoutingLattice lattice = RoutingLattice.Build(problem, settings);
			PathSearch search = new(lattice, settings);
			List<LayoutConnection> order = SortedByKey(problem.Connections);
			Dictionary<int, HashSet<int>> netPins = BuildNetPins(order);
			List<RoutedConnection> failed = new();

			foreach (LayoutConnection connection in order)
			{
				RoutedConnection routed = new() { Key = connection.Key, SourceAttach = AttachKind.Pin, TargetAttach = AttachKind.Pin };
				if (TryRouteSingle(problem, lattice, search, connection, netPins[connection.SourcePinKey], routed, out float cost))
				{
					result.Connections.Add(routed);
					result.TotalCost += cost;
				}
				else failed.Add(routed);
			}

			result.Connections.AddRange(failed);
			result.FailedCount = failed.Count;
			return result;
		}

		static bool TryRouteSingle(LayoutProblem problem, RoutingLattice lattice, PathSearch search, LayoutConnection connection, HashSet<int> netPins, RoutedConnection routed, out float cost)
		{
			cost = 0;
			if (!problem.Pins.TryGetValue(connection.SourcePinKey, out LayoutPin source)) return false;
			if (!problem.Pins.TryGetValue(connection.TargetPinKey, out LayoutPin target)) return false;

			(int sx, int sy) = lattice.EscapeNode(source);
			(int tx, int ty) = lattice.EscapeNode(target);
			if (sx < 0 || sy < 0 || tx < 0 || ty < 0) return false;

			search.Begin(netPins);
			search.AddStart(sx, sy, PathSearch.HorizontalDir(source.ExitDirX), 0);
			search.AddGoal(tx, ty, PathSearch.HorizontalDir(-target.ExitDirX));
			if (!search.Run(true) && !search.Run(false)) return false;

			List<LayoutPoint> points = new() { source.Position };
			search.AppendPath(points);
			points.Add(target.Position);

			routed.Routed = true;
			routed.Points = Simplify(points);
			cost = search.PathCost;
			return true;
		}

		static List<LayoutConnection> SortedByKey(List<LayoutConnection> connections)
		{
			int[] indices = new int[connections.Count];
			for (int i = 0; i < indices.Length; i++) indices[i] = i;
			Array.Sort(indices, (a, b) =>
			{
				int byKey = connections[a].Key.CompareTo(connections[b].Key);
				return byKey != 0 ? byKey : a.CompareTo(b);
			});

			List<LayoutConnection> sorted = new(indices.Length);
			foreach (int i in indices) sorted.Add(connections[i]);
			return sorted;
		}

		// Pins of each net (source pin and all its targets), keyed by source pin; membership lookups only
		static Dictionary<int, HashSet<int>> BuildNetPins(List<LayoutConnection> connections)
		{
			Dictionary<int, HashSet<int>> nets = new();
			foreach (LayoutConnection connection in connections)
			{
				if (!nets.TryGetValue(connection.SourcePinKey, out HashSet<int> pins))
				{
					pins = new HashSet<int> { connection.SourcePinKey };
					nets.Add(connection.SourcePinKey, pins);
				}

				pins.Add(connection.TargetPinKey);
			}

			return nets;
		}

		// Removes duplicate points and the middle point of any collinear triple
		static List<LayoutPoint> Simplify(List<LayoutPoint> points)
		{
			List<LayoutPoint> simplified = new(points.Count);
			foreach (LayoutPoint p in points)
			{
				if (simplified.Count > 0 && SamePoint(simplified[simplified.Count - 1], p)) continue;
				if (simplified.Count >= 2 && Collinear(simplified[simplified.Count - 2], simplified[simplified.Count - 1], p))
				{
					simplified.RemoveAt(simplified.Count - 1);
					if (SamePoint(simplified[simplified.Count - 1], p)) continue;
				}

				simplified.Add(p);
			}

			return simplified;
		}

		static bool SamePoint(LayoutPoint a, LayoutPoint b) => Math.Abs(a.X - b.X) < Eps && Math.Abs(a.Y - b.Y) < Eps;

		static bool Collinear(LayoutPoint a, LayoutPoint b, LayoutPoint c)
		{
			bool sameX = Math.Abs(a.X - b.X) < Eps && Math.Abs(b.X - c.X) < Eps;
			bool sameY = Math.Abs(a.Y - b.Y) < Eps && Math.Abs(b.Y - c.Y) < Eps;
			return sameX || sameY;
		}

		// A* from a set of start states to a set of goal nodes. State = node * 4 + dir, node = iy * Width + ix.
		// Each goal node may have a preferred arrival direction; arriving any other way costs BendCost.
		sealed class PathSearch
		{
			public const int DirPosX = 0, DirNegX = 1, DirPosY = 2, DirNegY = 3, AnyDir = -1;
			static readonly int[] stepX = { 1, -1, 0, 0 };
			static readonly int[] stepY = { 0, 0, 1, -1 };

			readonly RoutingLattice lattice;
			readonly int width, height, windowMargin;
			readonly float bendCost, invGrid;

			readonly float[] gScore;
			readonly int[] parent;
			readonly int[] stateStamp;
			readonly int[] startStamp, goalStamp, goalDir;
			int stateGen, queryGen;

			readonly MinHeap heap = new();
			readonly List<int> startStates = new();
			readonly List<float> startCosts = new();
			readonly List<int> goalNodes = new();
			HashSet<int> netPins;
			float goalMinX, goalMinY, goalMaxX, goalMaxY;
			int winX0, winY0, winX1, winY1;
			bool windowCoveredLattice;

			int bestState;
			public float PathCost { get; private set; }

			public PathSearch(RoutingLattice lattice, RouterSettings settings)
			{
				this.lattice = lattice;
				width = lattice.Width;
				height = lattice.Height;
				windowMargin = settings.WindowMarginCells;
				bendCost = settings.BendCost;
				invGrid = 1 / settings.GridSize;

				int nodeCount = width * height;
				gScore = new float[nodeCount * 4];
				parent = new int[nodeCount * 4];
				stateStamp = new int[nodeCount * 4];
				startStamp = new int[nodeCount];
				goalStamp = new int[nodeCount];
				goalDir = new int[nodeCount];
			}

			public static int HorizontalDir(int dirX) => dirX >= 0 ? DirPosX : DirNegX;

			// Starts a new query; reserved escape nodes of pins in netPins are passable
			public void Begin(HashSet<int> netPins)
			{
				queryGen++;
				this.netPins = netPins;
				startStates.Clear();
				startCosts.Clear();
				goalNodes.Clear();
				bestState = -1;
				PathCost = 0;
				windowCoveredLattice = false;
			}

			public void AddStart(int ix, int iy, int dir, float initialCost)
			{
				int node = iy * width + ix;
				startStamp[node] = queryGen;
				startStates.Add(node * 4 + dir);
				startCosts.Add(initialCost);
			}

			public void AddGoal(int ix, int iy, int preferredArrivalDir)
			{
				int node = iy * width + ix;
				LayoutPoint p = lattice.Point(ix, iy);
				if (goalNodes.Count == 0)
				{
					goalMinX = goalMaxX = p.X;
					goalMinY = goalMaxY = p.Y;
				}
				else
				{
					goalMinX = Math.Min(goalMinX, p.X);
					goalMaxX = Math.Max(goalMaxX, p.X);
					goalMinY = Math.Min(goalMinY, p.Y);
					goalMaxY = Math.Max(goalMaxY, p.Y);
				}

				goalStamp[node] = queryGen;
				goalDir[node] = preferredArrivalDir;
				goalNodes.Add(node);
			}

			// Windowed: restrict to the bounding box of starts and goals expanded by WindowMarginCells.
			// Returns false if no path exists; a full search is skipped if this query's window already covered the lattice.
			public bool Run(bool windowed)
			{
				bestState = -1;
				if (startStates.Count == 0 || goalNodes.Count == 0) return false;

				if (windowed)
				{
					SetWindowAroundQuery();
					windowCoveredLattice = winX0 == 0 && winY0 == 0 && winX1 == width - 1 && winY1 == height - 1;
				}
				else
				{
					if (windowCoveredLattice) return false;
					winX0 = 0;
					winY0 = 0;
					winX1 = width - 1;
					winY1 = height - 1;
				}

				stateGen++;
				heap.Clear();
				float bestCost = float.PositiveInfinity;

				for (int i = 0; i < startStates.Count; i++)
				{
					int node = startStates[i] >> 2;
					int ix = node % width, iy = node / width;
					if (InWindow(ix, iy)) Relax(startStates[i], startCosts[i], -1, ref bestCost);
				}

				while (heap.TryPop(out float f, out float g, out int state))
				{
					if (f >= bestCost) break;
					if (g > gScore[state]) continue;

					int node = state >> 2, dir = state & 3;
					int ix = node % width, iy = node / width;
					for (int nd = 0; nd < 4; nd++)
					{
						if (nd == (dir ^ 1)) continue; // no reversing
						int nx = ix + stepX[nd], ny = iy + stepY[nd];
						if (!InWindow(nx, ny) || !Passable(nx, ny)) continue;

						float step = nd < 2 ? Math.Abs(lattice.Xs[nx] - lattice.Xs[ix]) : Math.Abs(lattice.Ys[ny] - lattice.Ys[iy]);
						float cost = g + step * invGrid + (nd != dir ? bendCost : 0);
						Relax((ny * width + nx) * 4 + nd, cost, state, ref bestCost);
					}
				}

				if (bestState < 0) return false;
				PathCost = bestCost;
				return true;
			}

			// Appends the lattice points of the found path, start to goal
			public void AppendPath(List<LayoutPoint> points)
			{
				int first = points.Count;
				for (int s = bestState; s >= 0; s = parent[s])
				{
					int node = s >> 2;
					points.Add(lattice.Point(node % width, node / width));
				}

				points.Reverse(first, points.Count - first);
			}

			void Relax(int state, float cost, int from, ref float bestCost)
			{
				if (stateStamp[state] == stateGen && cost >= gScore[state]) return;
				stateStamp[state] = stateGen;
				gScore[state] = cost;
				parent[state] = from;

				int node = state >> 2;
				int ix = node % width, iy = node / width;
				heap.Push(cost + Heuristic(ix, iy), cost, state);

				if (goalStamp[node] == queryGen)
				{
					int preferred = goalDir[node];
					float total = cost + (preferred != AnyDir && preferred != (state & 3) ? bendCost : 0);
					if (total < bestCost)
					{
						bestCost = total;
						bestState = state;
					}
				}
			}

			float Heuristic(int ix, int iy)
			{
				float x = lattice.Xs[ix], y = lattice.Ys[iy];
				float dx = x < goalMinX ? goalMinX - x : x > goalMaxX ? x - goalMaxX : 0;
				float dy = y < goalMinY ? goalMinY - y : y > goalMaxY ? y - goalMaxY : 0;
				return (dx + dy) * invGrid;
			}

			bool InWindow(int ix, int iy) => ix >= winX0 && ix <= winX1 && iy >= winY0 && iy <= winY1;

			bool Passable(int ix, int iy)
			{
				if (!lattice.IsBlocked(ix, iy)) return true;
				int node = iy * width + ix;
				if (startStamp[node] == queryGen || goalStamp[node] == queryGen) return true;
				int reservedKey = lattice.ReservedPinKey(ix, iy);
				return reservedKey != -1 && netPins != null && netPins.Contains(reservedKey);
			}

			void SetWindowAroundQuery()
			{
				int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;

				void Include(int node)
				{
					int ix = node % width, iy = node / width;
					x0 = Math.Min(x0, ix);
					y0 = Math.Min(y0, iy);
					x1 = Math.Max(x1, ix);
					y1 = Math.Max(y1, iy);
				}

				foreach (int state in startStates) Include(state >> 2);
				foreach (int node in goalNodes) Include(node);

				winX0 = Math.Max(0, x0 - windowMargin);
				winY0 = Math.Max(0, y0 - windowMargin);
				winX1 = Math.Min(width - 1, x1 + windowMargin);
				winY1 = Math.Min(height - 1, y1 + windowMargin);
			}
		}

		// Binary min-heap ordered by f, then higher g (deeper first), then insertion order
		sealed class MinHeap
		{
			struct Entry
			{
				public float F, G;
				public int Seq, State;
			}

			Entry[] entries = new Entry[256];
			int count, nextSeq;

			public void Clear()
			{
				count = 0;
				nextSeq = 0;
			}

			public void Push(float f, float g, int state)
			{
				if (count == entries.Length) Array.Resize(ref entries, entries.Length * 2);
				entries[count] = new Entry { F = f, G = g, Seq = nextSeq++, State = state };
				int i = count++;
				while (i > 0)
				{
					int p = (i - 1) / 2;
					if (!Less(entries[i], entries[p])) break;
					(entries[i], entries[p]) = (entries[p], entries[i]);
					i = p;
				}
			}

			public bool TryPop(out float f, out float g, out int state)
			{
				if (count == 0)
				{
					f = g = 0;
					state = -1;
					return false;
				}

				Entry top = entries[0];
				f = top.F;
				g = top.G;
				state = top.State;

				entries[0] = entries[--count];
				int i = 0;
				while (true)
				{
					int l = i * 2 + 1, r = l + 1, smallest = i;
					if (l < count && Less(entries[l], entries[smallest])) smallest = l;
					if (r < count && Less(entries[r], entries[smallest])) smallest = r;
					if (smallest == i) break;
					(entries[i], entries[smallest]) = (entries[smallest], entries[i]);
					i = smallest;
				}

				return true;
			}

			static bool Less(Entry a, Entry b)
			{
				if (a.F != b.F) return a.F < b.F;
				if (a.G != b.G) return a.G > b.G;
				return a.Seq < b.Seq;
			}
		}
	}
}
