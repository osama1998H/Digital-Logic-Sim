using System;
using System.Collections.Generic;

namespace DLS.Game.Layout
{
	// The wire a search or a committed mark belongs to. Partner: for a bus writer, the key of the trunk it ends on, else -1.
	internal readonly struct WireQuery
	{
		public readonly int Net, Key, Partner;
		public readonly float HalfWidth;

		public WireQuery(int net, int key, int partner, float halfWidth)
		{
			Net = net;
			Key = key;
			Partner = partner;
			HalfWidth = halfWidth;
		}
	}

	// Committed wires on the lattice, for clearance and crossing costs between different nets (marks of the same net are free).
	// Edge marks: every lattice edge of a wire marks the parallel edges within reach, with their perpendicular world distance;
	// stepping along a marked edge violates clearance when that distance < both half-widths + MinGap.
	// Node marks: nodes a wire passes through, flagged straight (with its axis) or not (a bend or an end). Two nets sharing a
	// node pay CrossCost when both run straight across each other, otherwise ClearanceViolationCost.
	// A bus writer and the trunk it ends on ignore each other at the writer's end node, where they must touch.
	// All costs are pairwise and symmetric, so either wire of a pair sees the same cost from the other.
	internal sealed class Occupancy
	{
		const float Eps = 1e-4f;
		const int FlagStraight = 1, FlagVertical = 2, FlagWriterEnd = 4;

		struct Entry
		{
			public int Next, Prev, List, Net, Key, Partner, Flags;
			public float Dist, Half;
		}

		readonly RoutingLattice lattice;
		readonly int width, height, nodeListBase;
		readonly float minGap, reach, crossCost, violationCost;

		// List heads: edge lists at node * 2 + axis (H edge to ix + 1, V edge to iy + 1), then node lists at nodeListBase + node
		readonly int[] head;
		Entry[] entries = new Entry[1024];
		int count, freeList = -1;

		public Occupancy(RoutingLattice lattice, RouterSettings settings, float maxHalfWidth)
		{
			this.lattice = lattice;
			width = lattice.Width;
			height = lattice.Height;
			minGap = settings.MinGap;
			reach = settings.MinGap + maxHalfWidth;
			crossCost = settings.CrossCost;
			violationCost = settings.ClearanceViolationCost;

			int nodeCount = width * height;
			nodeListBase = nodeCount * 2;
			head = new int[nodeCount * 3];
			Array.Fill(head, -1);
		}

		public int[] Heads => head;
		public int NodeListBase => nodeListBase;

		// Cost of stepping along the edge with this list index (EdgeList)
		public float EdgeCost(int list, in WireQuery q)
		{
			float cost = 0;
			for (int e = head[list]; e >= 0; e = entries[e].Next)
			{
				ref Entry en = ref entries[e];
				if (en.Net != q.Net && en.Dist < en.Half + q.HalfWidth + minGap - Eps) cost += violationCost;
			}

			return cost;
		}

		// Cost of passing through node: straight along axis (0 = H, 1 = V), or turning there
		public void NodeCosts(int node, int axis, in WireQuery q, out float straight, out float turn)
		{
			straight = turn = 0;
			for (int e = head[nodeListBase + node]; e >= 0; e = entries[e].Next)
			{
				ref Entry en = ref entries[e];
				if (en.Net == q.Net || IsWriterEndOf(en, q)) continue;
				int enAxis = (en.Flags & FlagVertical) != 0 ? 1 : 0;
				straight += (en.Flags & FlagStraight) != 0 && enAxis != axis ? crossCost : violationCost;
				turn += violationCost;
			}
		}

		// Cost of ending at node. A bus writer ignores the trunk it ends on.
		public float EndCost(int node, in WireQuery q)
		{
			float cost = 0;
			for (int e = head[nodeListBase + node]; e >= 0; e = entries[e].Next)
			{
				ref Entry en = ref entries[e];
				if (en.Net == q.Net || IsWriterEndOf(en, q) || (q.Partner >= 0 && en.Key == q.Partner)) continue;
				cost += violationCost;
			}

			return cost;
		}

		// Marks a searched path (states as PathSearch.AppendStates gives them); the start node is not marked
		public void MarkPath(List<int> states, in WireQuery q, List<int> handles)
		{
			for (int i = 1; i < states.Count; i++) MarkEdge(states[i - 1] >> 2, states[i] & 3, q, handles);
			for (int i = 1; i < states.Count - 1; i++)
			{
				int dir = states[i] & 3;
				int flags = dir == (states[i + 1] & 3) ? FlagStraight | (dir >= 2 ? FlagVertical : 0) : 0;
				AddNode(states[i] >> 2, flags, q, handles);
			}

			if (states.Count >= 2) AddNode(states[states.Count - 1] >> 2, q.Partner >= 0 ? FlagWriterEnd : 0, q, handles);
		}

		// Marks the lattice nodes and edges between a pin and its escape node (escape node itself excluded: it is reserved)
		public void MarkStub(LayoutPin pin, int escapeX, int escapeY, in WireQuery q, List<int> handles)
		{
			float pinX = pin.Position.X, escX = lattice.Xs[escapeX];
			int lo = RoutingLattice.FirstIndexAbove(lattice.Xs, Math.Min(pinX, escX) - Eps);
			int hi = RoutingLattice.FirstIndexAbove(lattice.Xs, Math.Max(pinX, escX) + Eps) - 1;
			int rowStart = escapeY * width;
			for (int ix = lo; ix < hi; ix++) MarkEdge(rowStart + ix, PathSearch.DirPosX, q, handles);
			for (int ix = lo; ix <= hi; ix++)
			{
				if (ix != escapeX) AddNode(rowStart + ix, 0, q, handles);
			}
		}

		public void Remove(List<int> handles)
		{
			foreach (int e in handles)
			{
				ref Entry en = ref entries[e];
				if (en.Prev >= 0) entries[en.Prev].Next = en.Next;
				else head[en.List] = en.Next;
				if (en.Next >= 0) entries[en.Next].Prev = en.Prev;
				en.Next = freeList;
				freeList = e;
			}

			handles.Clear();
		}

		static bool IsWriterEndOf(in Entry en, in WireQuery q) => (en.Flags & FlagWriterEnd) != 0 && en.Partner == q.Key;

		// List index of the lattice edge leaving node in direction dir (PathSearch directions)
		public int EdgeList(int node, int dir)
		{
			switch (dir)
			{
				case PathSearch.DirPosX: return node * 2;
				case PathSearch.DirNegX: return (node - 1) * 2;
				case PathSearch.DirPosY: return node * 2 + 1;
				default: return (node - width) * 2 + 1;
			}
		}

		// Marks the edge itself and the parallel edges whose perpendicular distance is under its half-width + reach
		void MarkEdge(int node, int dir, in WireQuery q, List<int> handles)
		{
			int list = EdgeList(node, dir);
			int lower = list >> 1, ix = lower % width, iy = lower / width;
			float limit = q.HalfWidth + reach;

			if (dir < 2)
			{
				float[] ys = lattice.Ys;
				float y = ys[iy];
				Add(list, 0, 0, q, handles);
				for (int r = iy + 1; r < height && ys[r] - y < limit; r++) Add((r * width + ix) * 2, ys[r] - y, 0, q, handles);
				for (int r = iy - 1; r >= 0 && y - ys[r] < limit; r--) Add((r * width + ix) * 2, y - ys[r], 0, q, handles);
			}
			else
			{
				float[] xs = lattice.Xs;
				float x = xs[ix];
				Add(list, 0, 0, q, handles);
				for (int c = ix + 1; c < width && xs[c] - x < limit; c++) Add((iy * width + c) * 2 + 1, xs[c] - x, 0, q, handles);
				for (int c = ix - 1; c >= 0 && x - xs[c] < limit; c--) Add((iy * width + c) * 2 + 1, x - xs[c], 0, q, handles);
			}
		}

		void AddNode(int node, int flags, in WireQuery q, List<int> handles) => Add(nodeListBase + node, 0, flags, q, handles);

		void Add(int list, float dist, int flags, in WireQuery q, List<int> handles)
		{
			int e;
			if (freeList >= 0)
			{
				e = freeList;
				freeList = entries[e].Next;
			}
			else
			{
				if (count == entries.Length) Array.Resize(ref entries, entries.Length * 2);
				e = count++;
			}

			int first = head[list];
			entries[e] = new Entry { Next = first, Prev = -1, List = list, Net = q.Net, Key = q.Key, Partner = q.Partner, Flags = flags, Dist = dist, Half = q.HalfWidth };
			if (first >= 0) entries[first].Prev = e;
			head[list] = e;
			handles.Add(e);
		}
	}
}
