using System;
using System.Collections.Generic;

namespace DLS.Game.Layout
{
	// Orthogonal A* wire router over a RoutingLattice. Each net (connections sharing a source pin) is routed as a tree:
	// later targets branch off the net's already-routed wires. Bus writers end on their trunk's wire.
	public static class WireRouter
	{
		const float Eps = 1e-4f;

		public static LayoutResult Route(LayoutProblem problem, RouterSettings settings)
		{
			if (problem.Connections.Count == 0) return new LayoutResult();

			RoutingLattice lattice = RoutingLattice.Build(problem, settings);
			return new NetRouter(problem, lattice, settings).Run();
		}

		sealed class Net
		{
			public int Index; // position in routing order, the occupancy owner id
			public int SourcePinKey;
			public int Key; // lowest connection key in the net
			public bool HasTrunk, Complete;
			public float HalfPerimeter;
			public readonly List<LayoutConnection> Connections = new(); // key order
			public readonly HashSet<int> Pins = new(); // membership lookups only
		}

		sealed class RouteRecord
		{
			public LayoutConnection Connection;
			public Net Net;
			public RoutedConnection Result;
			public WireQuery Query;
			public readonly List<int> States = new(); // searched lattice path, start to goal
			public int ArrivalDir; // goal's preferred arrival direction
			public float Cost;
			public int ResultIndex; // slot in result.Connections
			public readonly List<int> Marks = new(); // occupancy handles
		}

		// Lattice nodes between a pin and its escape node (pin side inclusive, escape node exclusive)
		readonly struct Stub
		{
			public readonly bool Active;
			readonly int row;
			readonly float pinX, escapeX;

			public Stub(int row, float pinX, float escapeX)
			{
				Active = true;
				this.row = row;
				this.pinX = pinX;
				this.escapeX = escapeX;
			}

			public bool Contains(int iy, float x)
			{
				if (!Active || iy != row) return false;
				return escapeX > pinX ? x >= pinX - Eps && x < escapeX - Eps : x <= pinX + Eps && x > escapeX + Eps;
			}
		}

		sealed class NetRouter
		{
			readonly LayoutProblem problem;
			readonly RoutingLattice lattice;
			readonly RouterSettings settings;
			readonly Occupancy occupancy;
			readonly PathSearch search;
			readonly int width;

			readonly LayoutResult result = new();
			readonly List<RoutedConnection> failed = new();
			readonly List<Net> nets = new();
			readonly Dictionary<int, Net> netBySource = new();
			readonly Dictionary<int, LayoutConnection> connectionByKey = new();
			readonly Dictionary<int, RouteRecord> routedByKey = new();
			readonly List<LayoutConnection> deferredWriters = new();
			readonly Dictionary<int, List<LayoutConnection>> writersByTrunk = new(); // trunk key -> writers, key order
			readonly Dictionary<int, RouteRecord> pendingByKey = new(); // improvement pass: rerouted, not yet kept

			// Current net's tree: lattice node -> (lowest owner key, lowest segment index of that owner's final Points)
			readonly int[] treeStamp, treeOwnerKey, treeOwnerSeg;
			readonly List<int> treeNodes = new();
			int treeGen;

			readonly List<int> rasterNodes = new(), rasterSegs = new();

			public NetRouter(LayoutProblem problem, RoutingLattice lattice, RouterSettings settings)
			{
				this.problem = problem;
				this.lattice = lattice;
				this.settings = settings;
				float maxHalfWidth = 0;
				foreach (LayoutConnection c in problem.Connections) maxHalfWidth = Math.Max(maxHalfWidth, settings.HalfWidth(c.BitCount));
				occupancy = new Occupancy(lattice, settings, maxHalfWidth);
				search = new PathSearch(lattice, occupancy, settings);
				width = lattice.Width;

				int nodeCount = lattice.Width * lattice.Height;
				treeStamp = new int[nodeCount];
				treeOwnerKey = new int[nodeCount];
				treeOwnerSeg = new int[nodeCount];
			}

			public LayoutResult Run()
			{
				BuildNets();
				MarkStubs();

				foreach (Net net in nets)
				{
					RouteNet(net);
					net.Complete = true;
					RouteReadyWriters();
				}

				foreach (LayoutConnection writer in deferredWriters) Fail(writer);

				for (int pass = 0; pass < settings.ImprovementPasses; pass++)
				{
					foreach (Net net in nets) Improve(net);
				}

				failed.Sort((a, b) => a.Key.CompareTo(b.Key));
				result.Connections.AddRange(failed);
				result.FailedCount = failed.Count;
				return result;
			}

			static bool IsWriter(LayoutConnection c) => c.TargetAttachToKey >= 0;

			void BuildNets()
			{
				foreach (LayoutConnection c in SortedByKey(problem.Connections))
				{
					connectionByKey.TryAdd(c.Key, c);
					if (!netBySource.TryGetValue(c.SourcePinKey, out Net net))
					{
						net = new Net { SourcePinKey = c.SourcePinKey, Key = c.Key };
						net.Pins.Add(c.SourcePinKey);
						netBySource.Add(c.SourcePinKey, net);
						nets.Add(net);
					}

					net.Connections.Add(c);
					if (IsWriter(c))
					{
						if (!writersByTrunk.TryGetValue(c.TargetAttachToKey, out List<LayoutConnection> writers))
						{
							writers = new List<LayoutConnection>();
							writersByTrunk.Add(c.TargetAttachToKey, writers);
						}

						writers.Add(c);
						continue; // a writer ends on its trunk, not on its target pin
					}

					net.Pins.Add(c.TargetPinKey);
					if (c.IsBusTrunk) net.HasTrunk = true;
				}

				foreach (Net net in nets) net.HalfPerimeter = HalfPerimeter(net);

				nets.Sort((a, b) =>
				{
					if (a.HasTrunk != b.HasTrunk) return a.HasTrunk ? -1 : 1;
					if (!a.HasTrunk && a.HalfPerimeter != b.HalfPerimeter) return a.HalfPerimeter.CompareTo(b.HalfPerimeter);
					int byKey = a.Key.CompareTo(b.Key);
					return byKey != 0 ? byKey : a.SourcePinKey.CompareTo(b.SourcePinKey);
				});
				for (int i = 0; i < nets.Count; i++) nets[i].Index = i;
			}

			// Pin stubs never move, so they are marked once for the whole run (owned by the pin's net)
			void MarkStubs()
			{
				List<int> marks = new();
				foreach (Net net in nets)
				{
					LayoutConnection first = net.Connections[0];
					MarkStub(net.SourcePinKey, new WireQuery(net.Index, first.Key, -1, settings.HalfWidth(first.BitCount)), marks);
					foreach (LayoutConnection c in net.Connections)
					{
						if (!IsWriter(c)) MarkStub(c.TargetPinKey, new WireQuery(net.Index, c.Key, -1, settings.HalfWidth(c.BitCount)), marks);
					}
				}
			}

			void MarkStub(int pinKey, WireQuery query, List<int> marks)
			{
				if (!problem.Pins.TryGetValue(pinKey, out LayoutPin pin)) return;
				(int ex, int ey) = lattice.EscapeNode(pin);
				if (ex >= 0 && ey >= 0) occupancy.MarkStub(pin, ex, ey, query, marks);
			}

			float HalfPerimeter(Net net)
			{
				bool any = false;
				float minX = 0, minY = 0, maxX = 0, maxY = 0;

				void Include(int pinKey)
				{
					if (!problem.Pins.TryGetValue(pinKey, out LayoutPin pin)) return;
					LayoutPoint p = pin.Position;
					if (!any)
					{
						minX = maxX = p.X;
						minY = maxY = p.Y;
						any = true;
						return;
					}

					minX = Math.Min(minX, p.X);
					maxX = Math.Max(maxX, p.X);
					minY = Math.Min(minY, p.Y);
					maxY = Math.Max(maxY, p.Y);
				}

				Include(net.SourcePinKey);
				foreach (LayoutConnection c in net.Connections)
				{
					if (!IsWriter(c)) Include(c.TargetPinKey);
				}

				return any ? maxX - minX + (maxY - minY) : 0;
			}

			void RouteNet(Net net)
			{
				ResetTree();
				bool sourceOk = TryGetSource(net, out LayoutPin source, out int sourceNode);

				foreach (LayoutConnection c in OrderWithinNet(net, sourceOk ? sourceNode : -1))
				{
					if (!sourceOk)
					{
						Fail(c);
						continue;
					}

					if (IsWriter(c))
					{
						if (!TryGetTrunk(c, out LayoutConnection trunk))
						{
							Fail(c);
							continue;
						}

						Net trunkNet = netBySource[trunk.SourcePinKey];
						if (trunkNet != net && !trunkNet.Complete)
						{
							deferredWriters.Add(c);
							continue;
						}
					}

					RouteAndCommit(net, c, source, sourceNode);
				}
			}

			void RouteAndCommit(Net net, LayoutConnection c, LayoutPin source, int sourceNode)
			{
				RouteRecord record = RouteConnection(net, c, source, sourceNode);
				if (record == null)
				{
					Fail(c);
					return;
				}

				Mark(record);
				record.ResultIndex = result.Connections.Count;
				routedByKey.TryAdd(c.Key, record);
				result.Connections.Add(record.Result);
				result.TotalCost += record.Cost;
				if (!IsWriter(c)) AddToTree(record);
			}

			// Writers whose trunk's net has finished; they always start at their source pin, so the tree is not needed
			void RouteReadyWriters()
			{
				for (int i = 0; i < deferredWriters.Count;)
				{
					LayoutConnection writer = deferredWriters[i];
					LayoutConnection trunk = connectionByKey[writer.TargetAttachToKey];
					if (!netBySource[trunk.SourcePinKey].Complete)
					{
						i++;
						continue;
					}

					deferredWriters.RemoveAt(i);
					Net net = netBySource[writer.SourcePinKey];
					TryGetSource(net, out LayoutPin source, out int sourceNode);
					RouteAndCommit(net, writer, source, sourceNode);
				}
			}

			bool TryGetSource(Net net, out LayoutPin source, out int sourceNode)
			{
				sourceNode = -1;
				if (!problem.Pins.TryGetValue(net.SourcePinKey, out source)) return false;
				(int sx, int sy) = lattice.EscapeNode(source);
				if (sx < 0 || sy < 0) return false;
				sourceNode = sy * width + sx;
				return true;
			}

			bool TryGetTrunk(LayoutConnection writer, out LayoutConnection trunk)
			{
				return connectionByKey.TryGetValue(writer.TargetAttachToKey, out trunk) && trunk != writer && trunk.IsBusTrunk && !IsWriter(trunk);
			}

			// Trunks by key, then other targets nearest-first from the source escape node (ties by key), then writers by key
			List<LayoutConnection> OrderWithinNet(Net net, int sourceNode)
			{
				List<LayoutConnection> trunks = new(), targets = new(), writers = new();
				List<float> distances = new();
				foreach (LayoutConnection c in net.Connections)
				{
					if (IsWriter(c)) writers.Add(c);
					else if (c.IsBusTrunk) trunks.Add(c);
					else
					{
						targets.Add(c);
						distances.Add(DistanceToTarget(sourceNode, c));
					}
				}

				int[] indices = new int[targets.Count];
				for (int i = 0; i < indices.Length; i++) indices[i] = i;
				Array.Sort(indices, (a, b) =>
				{
					int byDistance = distances[a].CompareTo(distances[b]);
					return byDistance != 0 ? byDistance : a.CompareTo(b); // targets are already in key order
				});

				List<LayoutConnection> order = new(net.Connections.Count);
				order.AddRange(trunks);
				foreach (int i in indices) order.Add(targets[i]);
				order.AddRange(writers);
				return order;
			}

			float DistanceToTarget(int sourceNode, LayoutConnection c)
			{
				if (sourceNode < 0 || !problem.Pins.TryGetValue(c.TargetPinKey, out LayoutPin target)) return float.PositiveInfinity;
				(int tx, int ty) = lattice.EscapeNode(target);
				if (tx < 0 || ty < 0) return float.PositiveInfinity;
				LayoutPoint s = lattice.Point(sourceNode % width, sourceNode / width), t = lattice.Point(tx, ty);
				return Math.Abs(s.X - t.X) + Math.Abs(s.Y - t.Y);
			}

			// Searches a path for c against the current tree and occupancy; null if it cannot be routed. Has no side effects
			// beyond the search itself.
			RouteRecord RouteConnection(Net net, LayoutConnection c, LayoutPin source, int sourceNode)
			{
				bool writer = IsWriter(c);
				RouteRecord trunk = null;
				LayoutPin target = default;
				int tx = -1, ty = -1;
				if (writer)
				{
					if (!TryGetRouted(c.TargetAttachToKey, out trunk)) return null;
				}
				else
				{
					if (!problem.Pins.TryGetValue(c.TargetPinKey, out target)) return null;
					(tx, ty) = lattice.EscapeNode(target);
					if (tx < 0 || ty < 0) return null;
				}

				WireQuery query = new(net.Index, c.Key, writer ? c.TargetAttachToKey : -1, settings.HalfWidth(c.BitCount));
				search.Begin(net.Pins, query);

				int sx = sourceNode % width, sy = sourceNode / width;
				// A writer ends on its trunk, so it can only start at its source pin (a wire attaches to at most one wire)
				if (writer || treeNodes.Count == 0) search.AddStart(sx, sy, PathSearch.HorizontalDir(source.ExitDirX), 0);
				else
				{
					AddStartAllDirections(sourceNode);
					foreach (int node in treeNodes)
					{
						if (node != sourceNode) AddStartAllDirections(node);
					}
				}

				int arrivalDir = PathSearch.AnyDir;
				if (writer)
				{
					rasterNodes.Clear();
					rasterSegs.Clear();
					Rasterize(trunk, rasterNodes, rasterSegs);
					foreach (int node in rasterNodes) search.AddGoal(node % width, node / width, PathSearch.AnyDir);
				}
				else
				{
					arrivalDir = PathSearch.HorizontalDir(-target.ExitDirX);
					search.AddGoal(tx, ty, arrivalDir);
				}

				if (!search.Run()) return null;

				RouteRecord record = new() { Connection = c, Net = net, Query = query, ArrivalDir = arrivalDir, Cost = search.PathCost };
				RoutedConnection routed = new() { Key = c.Key, Routed = true, SourceAttach = AttachKind.Pin, TargetAttach = AttachKind.Pin };
				record.Result = routed;
				search.AppendStates(record.States);

				List<LayoutPoint> points = new(record.States.Count + 2);
				int startNode = record.States[0] >> 2;
				if (startNode == sourceNode) points.Add(source.Position);
				else
				{
					routed.SourceAttach = AttachKind.Wire;
					routed.AttachedToKey = treeOwnerKey[startNode];
					routed.AttachedSegmentIndex = treeOwnerSeg[startNode];
				}

				foreach (int state in record.States) points.Add(lattice.Point((state >> 2) % width, (state >> 2) / width));

				if (writer)
				{
					routed.TargetAttach = AttachKind.Wire;
					routed.AttachedToKey = trunk.Result.Key;
					routed.AttachedSegmentIndex = rasterSegs[rasterNodes.IndexOf(search.EndNode)];
				}
				else points.Add(target.Position);

				// Simplify keeps the first and last points, so attach points stay exact
				routed.Points = Simplify(points);
				return record;
			}

			bool TryGetRouted(int key, out RouteRecord record) => pendingByKey.TryGetValue(key, out record) || routedByKey.TryGetValue(key, out record);

			void Mark(RouteRecord record) => occupancy.MarkPath(record.States, record.Query, record.Marks);

			void Lift(RouteRecord record) => occupancy.Remove(record.Marks);

			// Lifts the net (with writers from other nets that end on its trunks), reroutes it with every other wire in
			// place, and keeps the new routing only if it routes everything again at no higher total cost.
			// Costs on both sides are summed in reroute order with earlier group members marked, so the difference is the
			// change in TotalCost.
			void Improve(Net net)
			{
				List<RouteRecord> group = new();
				TryGetSource(net, out LayoutPin source, out int sourceNode);
				foreach (LayoutConnection c in OrderWithinNet(net, sourceNode))
				{
					if (routedByKey.TryGetValue(c.Key, out RouteRecord r) && r.Connection == c) group.Add(r);
				}

				foreach (LayoutConnection c in net.Connections)
				{
					if (!c.IsBusTrunk || IsWriter(c) || !writersByTrunk.TryGetValue(c.Key, out List<LayoutConnection> writers)) continue;
					foreach (LayoutConnection w in writers)
					{
						if (netBySource[w.SourcePinKey] != net && routedByKey.TryGetValue(w.Key, out RouteRecord r) && r.Connection == w) group.Add(r);
					}
				}

				if (group.Count == 0) return;

				foreach (RouteRecord r in group) Lift(r);
				float oldCost = 0;
				foreach (RouteRecord r in group)
				{
					oldCost += search.Evaluate(r.States, r.ArrivalDir, r.Query);
					Mark(r);
				}

				foreach (RouteRecord r in group) Lift(r);

				ResetTree();
				pendingByKey.Clear();
				List<RouteRecord> rerouted = new(group.Count);
				float newCost = 0;
				foreach (RouteRecord r in group)
				{
					// Writers ignore the tree, so the same call serves writers from other nets
					TryGetSource(r.Net, out LayoutPin ownerSource, out int ownerSourceNode);
					RouteRecord fresh = RouteConnection(r.Net, r.Connection, ownerSource, ownerSourceNode);
					if (fresh == null) break;
					Mark(fresh);
					rerouted.Add(fresh);
					pendingByKey[fresh.Connection.Key] = fresh;
					newCost += fresh.Cost;
					if (r.Net == net && !IsWriter(fresh.Connection)) AddToTree(fresh);
				}

				pendingByKey.Clear();
				if (rerouted.Count < group.Count || newCost > oldCost)
				{
					foreach (RouteRecord r in rerouted) Lift(r);
					foreach (RouteRecord r in group) Mark(r);
					return;
				}

				for (int i = 0; i < group.Count; i++)
				{
					RouteRecord fresh = rerouted[i];
					fresh.ResultIndex = group[i].ResultIndex;
					result.Connections[fresh.ResultIndex] = fresh.Result;
					routedByKey[fresh.Connection.Key] = fresh;
				}

				result.TotalCost += newCost - oldCost;
			}

			void AddStartAllDirections(int node)
			{
				int ix = node % width, iy = node / width;
				for (int dir = 0; dir < 4; dir++) search.AddStart(ix, iy, dir, 0);
			}

			void Fail(LayoutConnection c)
			{
				failed.Add(new RoutedConnection { Key = c.Key, SourceAttach = AttachKind.Pin, TargetAttach = AttachKind.Pin });
			}

			void ResetTree()
			{
				treeGen++;
				treeNodes.Clear();
			}

			void AddToTree(RouteRecord record)
			{
				rasterNodes.Clear();
				rasterSegs.Clear();
				Rasterize(record, rasterNodes, rasterSegs);

				int key = record.Result.Key;
				for (int i = 0; i < rasterNodes.Count; i++)
				{
					int node = rasterNodes[i], seg = rasterSegs[i];
					if (treeStamp[node] != treeGen)
					{
						treeStamp[node] = treeGen;
						treeOwnerKey[node] = key;
						treeOwnerSeg[node] = seg;
						treeNodes.Add(node);
					}
					else if (key < treeOwnerKey[node] || (key == treeOwnerKey[node] && seg < treeOwnerSeg[node]))
					{
						treeOwnerKey[node] = key;
						treeOwnerSeg[node] = seg;
					}
				}
			}

			// Lattice nodes lying on each segment of the connection's final Points, in path order, with their segment index.
			// Pin-to-escape stubs are excluded; a corner shared by two segments is listed once, under the lower index.
			void Rasterize(RouteRecord record, List<int> nodes, List<int> segs)
			{
				List<LayoutPoint> points = record.Result.Points;
				Stub sourceStub = record.Result.SourceAttach == AttachKind.Pin ? PinStub(record.Connection.SourcePinKey) : default;
				Stub targetStub = record.Result.TargetAttach == AttachKind.Pin ? PinStub(record.Connection.TargetPinKey) : default;

				void Emit(int ix, int iy, int seg)
				{
					float x = lattice.Xs[ix];
					if (sourceStub.Contains(iy, x) || targetStub.Contains(iy, x)) return;
					int node = iy * width + ix;
					if (nodes.Count > 0 && nodes[nodes.Count - 1] == node) return;
					nodes.Add(node);
					segs.Add(seg);
				}

				for (int seg = 0; seg < points.Count - 1; seg++)
				{
					LayoutPoint a = points[seg], b = points[seg + 1];
					if (Math.Abs(a.Y - b.Y) < Eps)
					{
						int iy = lattice.IndexOfY(a.Y);
						if (iy < 0) continue;
						int lo = RoutingLattice.FirstIndexAbove(lattice.Xs, Math.Min(a.X, b.X) - Eps), hi = RoutingLattice.FirstIndexAbove(lattice.Xs, Math.Max(a.X, b.X) + Eps) - 1;
						if (a.X <= b.X)
						{
							for (int ix = lo; ix <= hi; ix++) Emit(ix, iy, seg);
						}
						else
						{
							for (int ix = hi; ix >= lo; ix--) Emit(ix, iy, seg);
						}
					}
					else
					{
						int ix = lattice.IndexOfX(a.X);
						if (ix < 0) continue;
						int lo = RoutingLattice.FirstIndexAbove(lattice.Ys, Math.Min(a.Y, b.Y) - Eps), hi = RoutingLattice.FirstIndexAbove(lattice.Ys, Math.Max(a.Y, b.Y) + Eps) - 1;
						if (a.Y <= b.Y)
						{
							for (int iy = lo; iy <= hi; iy++) Emit(ix, iy, seg);
						}
						else
						{
							for (int iy = hi; iy >= lo; iy--) Emit(ix, iy, seg);
						}
					}
				}
			}

			Stub PinStub(int pinKey)
			{
				if (!problem.Pins.TryGetValue(pinKey, out LayoutPin pin)) return default;
				(int ex, int ey) = lattice.EscapeNode(pin);
				if (ex < 0 || ey < 0) return default;
				return new Stub(ey, pin.Position.X, lattice.Xs[ex]);
			}
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
	}
}
