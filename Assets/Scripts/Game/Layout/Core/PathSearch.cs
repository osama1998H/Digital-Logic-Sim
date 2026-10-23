using System;
using System.Collections.Generic;

namespace DLS.Game.Layout
{
	// A* from a set of start states to a set of goal nodes. State = node * 4 + dir, node = iy * Width + ix.
	// Each goal node may have a preferred arrival direction; arriving any other way costs BendCost.
	// Steps also pay the occupancy's clearance and crossing costs for the current query's wire.
	internal sealed class PathSearch
	{
		public const int DirPosX = 0, DirNegX = 1, DirPosY = 2, DirNegY = 3, AnyDir = -1;
		static readonly int[] stepX = { 1, -1, 0, 0 };
		static readonly int[] stepY = { 0, 0, 1, -1 };

		// MinTurns[(dir * 16 + requiredDirMask) * 5 + arrival] (arrival 0..3 = preferred direction, 4 = any): fewest
		// direction changes (no reversals) from dir that use every required direction, plus one if the last direction
		// differs from the preferred arrival
		static readonly int[] MinTurns = BuildMinTurns();

		readonly RoutingLattice lattice;
		readonly Occupancy occupancy;
		readonly int width, height, windowMargin;
		readonly float bendCost, invGrid;
		readonly float[] stepX1, stepY1; // length in cells of the step from index i to i + 1
		readonly bool[] blocked;
		readonly float[] distX, distY, turnPenalty; // per query: heuristic distance terms by column / row, MinTurns * BendCost
		readonly int[] requiredX, requiredY; // per query: direction bits that must still be travelled, by column / row
		readonly int[] markHeads; // occupancy list heads, to skip empty lists without a call
		readonly int nodeMarkBase;

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
		WireQuery query;
		int arrivalSlot; // MinTurns arrival index: the single preferred arrival direction shared by all goals, else 4
		float goalMinX, goalMinY, goalMaxX, goalMaxY;
		int winX0, winY0, winX1, winY1;

		int bestState;
		public float PathCost { get; private set; }

		public PathSearch(RoutingLattice lattice, Occupancy occupancy, RouterSettings settings)
		{
			this.lattice = lattice;
			this.occupancy = occupancy;
			width = lattice.Width;
			height = lattice.Height;
			windowMargin = settings.WindowMarginCells;
			bendCost = settings.BendCost;
			invGrid = 1 / settings.GridSize;

			stepX1 = new float[Math.Max(0, width - 1)];
			for (int i = 0; i < stepX1.Length; i++) stepX1[i] = (lattice.Xs[i + 1] - lattice.Xs[i]) * invGrid;
			stepY1 = new float[Math.Max(0, height - 1)];
			for (int i = 0; i < stepY1.Length; i++) stepY1[i] = (lattice.Ys[i + 1] - lattice.Ys[i]) * invGrid;

			int nodeCount = width * height;
			blocked = new bool[nodeCount];
			for (int iy = 0; iy < height; iy++)
			{
				for (int ix = 0; ix < width; ix++) blocked[iy * width + ix] = lattice.IsBlocked(ix, iy);
			}

			distX = new float[width];
			distY = new float[height];
			requiredX = new int[width];
			requiredY = new int[height];
			turnPenalty = new float[MinTurns.Length];
			for (int i = 0; i < MinTurns.Length; i++) turnPenalty[i] = MinTurns[i] * bendCost;

			markHeads = occupancy.Heads;
			nodeMarkBase = occupancy.NodeListBase;
			gScore = new float[nodeCount * 4];
			parent = new int[nodeCount * 4];
			stateStamp = new int[nodeCount * 4];
			startStamp = new int[nodeCount];
			goalStamp = new int[nodeCount];
			goalDir = new int[nodeCount];
		}

		public static int HorizontalDir(int dirX) => dirX >= 0 ? DirPosX : DirNegX;

		// Starts a new query; reserved escape nodes of pins in netPins are passable
		public void Begin(HashSet<int> netPins, WireQuery query)
		{
			queryGen++;
			this.netPins = netPins;
			this.query = query;
			startStates.Clear();
			startCosts.Clear();
			goalNodes.Clear();
			bestState = -1;
			PathCost = 0;
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

			int slot = preferredArrivalDir == AnyDir ? 4 : preferredArrivalDir;
			arrivalSlot = goalNodes.Count == 0 || slot == arrivalSlot ? slot : 4;
			goalStamp[node] = queryGen;
			goalDir[node] = preferredArrivalDir;
			goalNodes.Add(node);
		}

		// Node where the found path ends
		public int EndNode => bestState >> 2;

		// Searches the bounding box of starts and goals expanded by WindowMarginCells, then the full lattice
		// if that found nothing and the window did not already cover it. Returns false if no path exists.
		public bool Run()
		{
			bestState = -1;
			if (startStates.Count == 0 || goalNodes.Count == 0) return false;

			SetWindowAroundQuery();
			PrepareHeuristic();
			if (Search()) return true;
			if (winX0 == 0 && winY0 == 0 && winX1 == width - 1 && winY1 == height - 1) return false;

			winX0 = 0;
			winY0 = 0;
			winX1 = width - 1;
			winY1 = height - 1;
			return Search();
		}

		bool Search()
		{
			bestState = -1;
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
				float straightCost = 0, turnCost = 0;
				if (parent[state] >= 0 && markHeads[nodeMarkBase + node] >= 0) occupancy.NodeCosts(node, dir >> 1, query, out straightCost, out turnCost);
				for (int nd = 0; nd < 4; nd++)
				{
					if (nd == (dir ^ 1)) continue; // no reversing
					int nx = ix + stepX[nd], ny = iy + stepY[nd];
					if (nx < winX0 || nx > winX1 || ny < winY0 || ny > winY1) continue;
					int next = ny * width + nx;
					if (blocked[next] && !PassableBlocked(next)) continue;

					// Same arithmetic as StepCost
					float length, edge = 0;
					int list;
					switch (nd)
					{
						case DirPosX:
							length = stepX1[ix];
							list = node * 2;
							break;
						case DirNegX:
							length = stepX1[nx];
							list = next * 2;
							break;
						case DirPosY:
							length = stepY1[iy];
							list = node * 2 + 1;
							break;
						default:
							length = stepY1[ny];
							list = next * 2 + 1;
							break;
					}

					if (markHeads[list] >= 0) edge = occupancy.EdgeCost(list, query);
					float cost = g + length + (nd != dir ? bendCost : 0) + ((nd == dir ? straightCost : turnCost) + edge);

					// Relax, inlined
					int nextState = next * 4 + nd;
					if (stateStamp[nextState] == stateGen && cost >= gScore[nextState]) continue;
					stateStamp[nextState] = stateGen;
					gScore[nextState] = cost;
					parent[nextState] = state;
					float h = distX[nx] + distY[ny] + turnPenalty[(nd * 16 + (requiredX[nx] | requiredY[ny])) * 5 + arrivalSlot];
					if (cost + h >= bestCost) continue; // could never be expanded, nor beat bestCost as a goal
					heap.Push(cost + h, cost, nextState);
					if (goalStamp[next] == queryGen) ConsiderGoal(nextState, cost, true, ref bestCost);
				}
			}

			if (bestState < 0) return false;
			PathCost = bestCost;
			return true;
		}

		// Cost of a path given as states (AppendStates order) for the query's wire under the current occupancy, computed
		// exactly as Search would; preferredArrivalDir is the goal's
		public float Evaluate(List<int> states, int preferredArrivalDir, WireQuery query)
		{
			this.query = query;
			float g = 0;
			for (int i = 1; i < states.Count; i++)
			{
				int state = states[i - 1], node = state >> 2, dir = state & 3, nd = states[i] & 3;
				float nodeCost = 0;
				if (i > 1)
				{
					occupancy.NodeCosts(node, dir >> 1, query, out float straightCost, out float turnCost);
					nodeCost = nd == dir ? straightCost : turnCost;
				}

				g = StepCost(g, node, dir, nd, nodeCost);
			}

			int last = states[states.Count - 1];
			return GoalCost(g, last, preferredArrivalDir, states.Count > 1);
		}

		float StepCost(float g, int node, int dir, int nd, float nodeCost)
		{
			int ix = node % width, iy = node / width;
			float length = nd == DirPosX ? stepX1[ix] : nd == DirNegX ? stepX1[ix - 1] : nd == DirPosY ? stepY1[iy] : stepY1[iy - 1];
			float edge = occupancy.EdgeCost(occupancy.EdgeList(node, nd), query);
			return g + length + (nd != dir ? bendCost : 0) + (nodeCost + edge);
		}

		// Total on arriving at a goal: wrong arrival direction costs a bend; ending on another net's node is a violation
		float GoalCost(float cost, int state, int preferred, bool moved)
		{
			float arrival = preferred != AnyDir && preferred != (state & 3) ? bendCost : 0;
			return cost + arrival + (moved ? occupancy.EndCost(state >> 2, query) : 0);
		}

		// States of the found path, start to goal
		public void AppendStates(List<int> states)
		{
			int first = states.Count;
			for (int s = bestState; s >= 0; s = parent[s]) states.Add(s);
			states.Reverse(first, states.Count - first);
		}

		void Relax(int state, float cost, int from, ref float bestCost)
		{
			if (stateStamp[state] == stateGen && cost >= gScore[state]) return;
			stateStamp[state] = stateGen;
			gScore[state] = cost;
			parent[state] = from;

			int node = state >> 2;
			int ix = node % width, iy = node / width;
			heap.Push(cost + Heuristic(ix, iy, state & 3), cost, state);
			if (goalStamp[node] == queryGen) ConsiderGoal(state, cost, from >= 0, ref bestCost);
		}

		void ConsiderGoal(int state, float cost, bool moved, ref float bestCost)
		{
			float total = GoalCost(cost, state, goalDir[state >> 2], moved);
			if (total < bestCost)
			{
				bestCost = total;
				bestState = state;
			}
		}

		// Manhattan distance to the goal box plus the bends any path from this direction must make: it has to travel in
		// each direction toward the box, and (with a single preferred arrival direction) arrive in it or pay a bend
		float Heuristic(int ix, int iy, int dir) => distX[ix] + distY[iy] + turnPenalty[(dir * 16 + (requiredX[ix] | requiredY[iy])) * 5 + arrivalSlot];

		void PrepareHeuristic()
		{
			for (int ix = 0; ix < width; ix++)
			{
				float x = lattice.Xs[ix];
				distX[ix] = x < goalMinX ? (goalMinX - x) * invGrid : x > goalMaxX ? (x - goalMaxX) * invGrid : 0;
				requiredX[ix] = x < goalMinX ? 1 << DirPosX : x > goalMaxX ? 1 << DirNegX : 0;
			}

			for (int iy = 0; iy < height; iy++)
			{
				float y = lattice.Ys[iy];
				distY[iy] = y < goalMinY ? (goalMinY - y) * invGrid : y > goalMaxY ? (y - goalMaxY) * invGrid : 0;
				requiredY[iy] = y < goalMinY ? 1 << DirPosY : y > goalMaxY ? 1 << DirNegY : 0;
			}
		}

		static int[] BuildMinTurns()
		{
			int[] table = new int[4 * 16 * 5];
			int[] dist = new int[4 * 16];
			Queue<int> queue = new();
			for (int dir = 0; dir < 4; dir++)
			{
				for (int required = 0; required < 16; required++)
				{
					// Breadth-first over (direction, required directions used so far)
					Array.Fill(dist, -1);
					int start = dir * 16 + (required & (1 << dir));
					dist[start] = 0;
					queue.Enqueue(start);
					while (queue.Count > 0)
					{
						int s = queue.Dequeue(), d = s >> 4, used = s & 15;
						for (int nd = 0; nd < 4; nd++)
						{
							if (nd == d || nd == (d ^ 1)) continue;
							int next = nd * 16 + (used | (required & (1 << nd)));
							if (dist[next] >= 0) continue;
							dist[next] = dist[s] + 1;
							queue.Enqueue(next);
						}
					}

					for (int arrival = 0; arrival < 5; arrival++)
					{
						int best = int.MaxValue;
						for (int d = 0; d < 4; d++)
						{
							int turns = dist[d * 16 + required];
							if (turns < 0) continue;
							best = Math.Min(best, turns + (arrival < 4 && d != arrival ? 1 : 0));
						}

						table[(dir * 16 + required) * 5 + arrival] = best;
					}
				}
			}

			return table;
		}

		bool InWindow(int ix, int iy) => ix >= winX0 && ix <= winX1 && iy >= winY0 && iy <= winY1;

		// A blocked node is passable as a start or goal of this query, or as the reserved escape node of a pin in its net
		bool PassableBlocked(int node)
		{
			if (startStamp[node] == queryGen || goalStamp[node] == queryGen) return true;
			int reservedKey = lattice.ReservedPinKey(node % width, node / width);
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

	// Binary min-heap ordered by f, then higher g (deeper first); remaining ties fall to the (deterministic) heap order.
	// f and g are non-negative, so their bit patterns order like the values and pack into one integer key.
	internal sealed class MinHeap
	{
		ulong[] keys = new ulong[256];
		int[] states = new int[256];
		int count;

		public void Clear() => count = 0;

		public void Push(float f, float g, int state)
		{
			if (count == keys.Length)
			{
				Array.Resize(ref keys, keys.Length * 2);
				Array.Resize(ref states, states.Length * 2);
			}

			ulong key = ((ulong)(uint)BitConverter.SingleToInt32Bits(f) << 32) | (uint.MaxValue - (uint)BitConverter.SingleToInt32Bits(g));
			int i = count++;
			while (i > 0)
			{
				int p = (i - 1) >> 1;
				if (keys[p] <= key) break;
				keys[i] = keys[p];
				states[i] = states[p];
				i = p;
			}

			keys[i] = key;
			states[i] = state;
		}

		public bool TryPop(out float f, out float g, out int state)
		{
			if (count == 0)
			{
				f = g = 0;
				state = -1;
				return false;
			}

			ulong top = keys[0];
			f = BitConverter.Int32BitsToSingle((int)(top >> 32));
			g = BitConverter.Int32BitsToSingle((int)(uint.MaxValue - (uint)top));
			state = states[0];

			count--;
			ulong lastKey = keys[count];
			int lastState = states[count], i = 0;
			while (true)
			{
				int child = i * 2 + 1;
				if (child >= count) break;
				if (child + 1 < count && keys[child + 1] < keys[child]) child++;
				if (keys[child] >= lastKey) break;
				keys[i] = keys[child];
				states[i] = states[child];
				i = child;
			}

			keys[i] = lastKey;
			states[i] = lastState;
			return true;
		}
	}
}
