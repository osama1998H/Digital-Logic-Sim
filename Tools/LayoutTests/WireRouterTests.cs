using System;
using System.Collections.Generic;
using DLS.Game.Layout;
using NUnit.Framework;

namespace LayoutTests
{
	public class WireRouterTests
	{
		static LayoutPin MakePin(int key, int owner, float x, float y, int dir) =>
			new LayoutPin { Key = key, OwnerID = owner, Position = new LayoutPoint(x, y), ExitDirX = dir, BitCount = 1 };

		static LayoutConnection MakeConnection(int key, int source, int target) =>
			new LayoutConnection { Key = key, SourcePinKey = source, TargetPinKey = target, BitCount = 1 };

		static LayoutProblem TwoPinProblem(float targetX, float targetY)
		{
			LayoutProblem problem = new();
			problem.Pins[1] = MakePin(1, 1, 0, 0, 1);
			problem.Pins[2] = MakePin(2, 2, targetX, targetY, -1);
			problem.Connections.Add(MakeConnection(0, 1, 2));
			return problem;
		}

		// Walls around the region containing the escape node of a target pin at (2, 0) exiting -x
		static void AddEnclosure(LayoutProblem problem, float offsetY = 0)
		{
			problem.Obstacles.Add(new LayoutRect(10, 1.4f, -0.6f + offsetY, 1.5f, 0.6f + offsetY));
			problem.Obstacles.Add(new LayoutRect(11, 2.5f, -0.6f + offsetY, 2.6f, 0.6f + offsetY));
			problem.Obstacles.Add(new LayoutRect(12, 1.4f, 0.5f + offsetY, 2.6f, 0.6f + offsetY));
			problem.Obstacles.Add(new LayoutRect(13, 1.4f, -0.6f + offsetY, 2.6f, -0.5f + offsetY));
		}

		[Test]
		public void Route_EmptyProblem_ReturnsEmpty()
		{
			LayoutResult result = WireRouter.Route(new LayoutProblem(), new RouterSettings());

			Assert.That(result.Connections.Count, Is.EqualTo(0));
			Assert.That(result.FailedCount, Is.EqualTo(0));
		}

		[Test]
		public void Route_FacingPinsSameRow_IsStraight()
		{
			LayoutResult result = WireRouter.Route(TwoPinProblem(2, 0), new RouterSettings());

			Assert.That(result.Connections.Count, Is.EqualTo(1));
			RoutedConnection c = result.Connections[0];
			Assert.That(c.Routed, Is.True);
			Assert.That(c.Points.Count, Is.EqualTo(2));
			Assert.That(c.Points[0].Y, Is.EqualTo(0));
			Assert.That(c.Points[1].Y, Is.EqualTo(0));
			Assert.That(c.Points[0].X, Is.EqualTo(0));
			Assert.That(c.Points[1].X, Is.EqualTo(2));
			Assert.That(c.SourceAttach, Is.EqualTo(AttachKind.Pin));
			Assert.That(c.TargetAttach, Is.EqualTo(AttachKind.Pin));
		}

		[Test]
		public void Route_AvoidsObstacle()
		{
			LayoutProblem problem = TwoPinProblem(2, 0);
			LayoutRect obstacle = new(5, 0.8f, -0.5f, 1.2f, 0.5f);
			problem.Obstacles.Add(obstacle);

			LayoutResult result = WireRouter.Route(problem, new RouterSettings());

			RoutedConnection c = result.Connections[0];
			Assert.That(c.Routed, Is.True);
			Assert.That(TestGeometry.AllAxisAligned(c.Points), Is.True);
			for (int i = 0; i < c.Points.Count - 1; i++)
			{
				Assert.That(TestGeometry.SegmentIntersectsRect(c.Points[i], c.Points[i + 1], obstacle), Is.False, $"segment {i}");
			}
		}

		[Test]
		public void Route_OffsetRows_TwoBends()
		{
			LayoutResult result = WireRouter.Route(TwoPinProblem(2, 0.5f), new RouterSettings());

			RoutedConnection c = result.Connections[0];
			Assert.That(c.Routed, Is.True);
			Assert.That(c.Points.Count, Is.EqualTo(4));
			Assert.That(TestGeometry.AllAxisAligned(c.Points), Is.True);
		}

		[Test]
		public void Route_EnclosedTarget_ReportsFailureWithoutThrowing()
		{
			LayoutProblem problem = TwoPinProblem(2, 0);
			AddEnclosure(problem);

			LayoutResult result = null;
			Assert.DoesNotThrow(() => result = WireRouter.Route(problem, new RouterSettings()));

			Assert.That(result.Connections.Count, Is.EqualTo(1));
			Assert.That(result.Connections[0].Routed, Is.False);
			Assert.That(result.Connections[0].Points, Is.Null);
			Assert.That(result.FailedCount, Is.EqualTo(1));
		}

		[Test]
		public void Route_IsDeterministic()
		{
			LayoutProblem MakeProblem()
			{
				LayoutProblem problem = new();
				problem.Obstacles.Add(new LayoutRect(5, 0.8f, -0.5f, 1.2f, 0.5f));
				problem.Obstacles.Add(new LayoutRect(6, 0.4f, 0.9f, 1.6f, 1.3f));
				problem.Pins[1] = MakePin(1, 1, 0, 0, 1);
				problem.Pins[2] = MakePin(2, 2, 2, 0, -1);
				problem.Pins[3] = MakePin(3, 1, 0, 1.06f, 1);
				problem.Pins[4] = MakePin(4, 2, 2, -0.31f, -1);
				problem.Connections.Add(MakeConnection(7, 3, 4));
				problem.Connections.Add(MakeConnection(2, 1, 2));
				return problem;
			}

			LayoutResult a = WireRouter.Route(MakeProblem(), new RouterSettings());
			LayoutResult b = WireRouter.Route(MakeProblem(), new RouterSettings());

			Assert.That(a.Connections.Count, Is.EqualTo(b.Connections.Count));
			for (int i = 0; i < a.Connections.Count; i++)
			{
				Assert.That(a.Connections[i].Key, Is.EqualTo(b.Connections[i].Key));
				Assert.That(a.Connections[i].Routed, Is.True);
				List<LayoutPoint> pa = a.Connections[i].Points, pb = b.Connections[i].Points;
				Assert.That(TestGeometry.AllAxisAligned(pa), Is.True);
				(int sourcePin, int targetPin) = a.Connections[i].Key == 7 ? (3, 4) : (1, 2);
				LayoutProblem pins = MakeProblem();
				AssertSamePoint(pa[0], pins.Pins[sourcePin].Position);
				AssertSamePoint(pa[^1], pins.Pins[targetPin].Position);
				Assert.That(pa.Count, Is.EqualTo(pb.Count));
				for (int j = 0; j < pa.Count; j++)
				{
					Assert.That(pa[j].X, Is.EqualTo(pb[j].X));
					Assert.That(pa[j].Y, Is.EqualTo(pb[j].Y));
				}
			}

			Assert.That(a.TotalCost, Is.EqualTo(b.TotalCost));
		}

		[Test]
		public void Route_DetourBeyondWindow_FallsBackToFullLattice()
		{
			LayoutProblem problem = TwoPinProblem(2, 0);
			// Wall reaching 24 cells above and below the pins, beyond the 16-cell search window
			LayoutRect wall = new(5, 0.9f, -3, 1.1f, 3);
			problem.Obstacles.Add(wall);

			LayoutResult result = WireRouter.Route(problem, new RouterSettings());

			RoutedConnection c = result.Connections[0];
			Assert.That(c.Routed, Is.True);
			Assert.That(result.FailedCount, Is.EqualTo(0));
			for (int i = 0; i < c.Points.Count - 1; i++)
			{
				Assert.That(TestGeometry.SegmentIntersectsRect(c.Points[i], c.Points[i + 1], wall), Is.False, $"segment {i}");
			}
		}

		[Test]
		public void Route_TotalCost_SumsPathCosts()
		{
			RouterSettings settings = new();
			LayoutProblem problem = TwoPinProblem(2, 0.5f);
			problem.Pins[3] = MakePin(3, 1, 0, -1, 1);
			problem.Pins[4] = MakePin(4, 2, 2, -1, -1);
			problem.Connections.Add(MakeConnection(1, 3, 4));

			LayoutResult result = WireRouter.Route(problem, settings);

			// Escape nodes 0.125 and 1.875: 14 horizontal steps; offset wire adds 4 vertical steps and two bends
			float straight = 14;
			float offset = 14 + 4 + 2 * settings.BendCost;
			Assert.That(result.FailedCount, Is.EqualTo(0));
			Assert.That(result.TotalCost, Is.EqualTo(straight + offset).Within(1e-3f));
		}

		[Test]
		public void Route_UnroutedListedAfterRouted_ByKey()
		{
			LayoutProblem problem = new();
			AddEnclosure(problem);
			problem.Pins[1] = MakePin(1, 1, 0, 0, 1);
			problem.Pins[2] = MakePin(2, 2, 2, 0, -1); // enclosed
			problem.Pins[3] = MakePin(3, 1, 0, 3, 1);
			problem.Pins[4] = MakePin(4, 2, 2, 3, -1);
			problem.Pins[5] = MakePin(5, 1, 0, -3, 1);
			problem.Pins[6] = MakePin(6, 2, 2, -3, -1);
			problem.Connections.Add(MakeConnection(5, 1, 2));
			problem.Connections.Add(MakeConnection(3, 3, 4));
			problem.Connections.Add(MakeConnection(1, 1, 2));
			problem.Connections.Add(MakeConnection(2, 5, 6));

			LayoutResult result = WireRouter.Route(problem, new RouterSettings());

			int[] keys = new int[result.Connections.Count];
			for (int i = 0; i < keys.Length; i++) keys[i] = result.Connections[i].Key;
			Assert.That(keys, Is.EqualTo(new[] { 2, 3, 1, 5 }));
			Assert.That(result.Connections[0].Routed && result.Connections[1].Routed, Is.True);
			Assert.That(result.Connections[2].Routed || result.Connections[3].Routed, Is.False);
			Assert.That(result.FailedCount, Is.EqualTo(2));
		}

		[Test]
		public void Route_MissingPin_ReportsFailureWithoutThrowing()
		{
			LayoutProblem problem = TwoPinProblem(2, 0);
			problem.Connections.Add(MakeConnection(1, 1, 99));

			LayoutResult result = null;
			Assert.DoesNotThrow(() => result = WireRouter.Route(problem, new RouterSettings()));

			Assert.That(result.Connections.Count, Is.EqualTo(2));
			Assert.That(result.Connections[0].Routed, Is.True);
			Assert.That(result.Connections[1].Key, Is.EqualTo(1));
			Assert.That(result.Connections[1].Routed, Is.False);
			Assert.That(result.FailedCount, Is.EqualTo(1));
		}

		[Test]
		public void Route_AvoidsOtherPinsEscapeNodes()
		{
			LayoutProblem problem = TwoPinProblem(2, 0);
			// Unconnected pin whose escape node (1.125, 0) lies on the straight route
			problem.Pins[3] = MakePin(3, 3, 1, 0, 1);

			LayoutResult result = WireRouter.Route(problem, new RouterSettings());

			RoutedConnection c = result.Connections[0];
			Assert.That(c.Routed, Is.True);
			LayoutPoint reserved = new(1.125f, 0);
			for (int i = 0; i < c.Points.Count - 1; i++)
			{
				Assert.That(TestGeometry.PointOnSegment(reserved, c.Points[i], c.Points[i + 1]), Is.False, $"segment {i}");
			}
		}

		[Test]
		public void Route_SameNetMayPassOwnPinsEscapeNodes()
		{
			LayoutProblem problem = TwoPinProblem(2, 0);
			// Second target of the same net; its escape node (1.125, 0) lies on the first wire's straight route
			problem.Pins[3] = MakePin(3, 3, 1, 0, 1);
			problem.Connections.Add(MakeConnection(1, 1, 3));

			LayoutResult result = WireRouter.Route(problem, new RouterSettings());

			RoutedConnection c = Find(result, 0);
			Assert.That(c.Routed, Is.True);
			Assert.That(c.Points.Count, Is.EqualTo(2));
			// Connection 0 runs through pin 3's reserved escape node, which is passable only because pin 3 is in its net
			Assert.That(TestGeometry.PointOnSegment(new LayoutPoint(1.125f, 0), c.Points[0], c.Points[1]), Is.True);
			Assert.That(Find(result, 1).Routed, Is.True);
			Assert.That(result.FailedCount, Is.EqualTo(0));
		}

		[Test]
		public void Route_Fanout_SecondTargetBranchesFromFirst()
		{
			LayoutProblem problem = new();
			problem.Pins[1] = MakePin(1, 1, 0, 0, 1);
			problem.Pins[2] = MakePin(2, 2, 2, 0.5f, -1);
			problem.Pins[3] = MakePin(3, 3, 2, 1, -1);
			problem.Connections.Add(MakeConnection(0, 1, 2));
			problem.Connections.Add(MakeConnection(1, 1, 3));

			LayoutResult result = WireRouter.Route(problem, new RouterSettings());

			Assert.That(result.FailedCount, Is.EqualTo(0));
			List<RoutedConnection> branches = result.Connections.FindAll(c => c.SourceAttach == AttachKind.Wire);
			Assert.That(branches.Count, Is.EqualTo(1));
			RoutedConnection child = branches[0];
			int otherKey = child.Key == 0 ? 1 : 0;
			Assert.That(child.AttachedToKey, Is.EqualTo(otherKey));
			AssertAttachPointOnParent(Find(result, otherKey), child.AttachedSegmentIndex, child.Points[0]);
			Assert.That(TestGeometry.AllAxisAligned(child.Points), Is.True);
		}

		[Test]
		public void Route_ParentsPrecedeChildren()
		{
			LayoutProblem problem = new();
			problem.Pins[1] = MakePin(1, 1, 0, 0, 1);
			problem.Pins[2] = MakePin(2, 2, 2, 1.5f, -1);
			problem.Pins[3] = MakePin(3, 3, 2, 1, -1);
			problem.Pins[4] = MakePin(4, 4, 2, 0.5f, -1);
			// Keys run farthest-first, so routing order (nearest-first) differs from key order
			problem.Connections.Add(MakeConnection(0, 1, 2));
			problem.Connections.Add(MakeConnection(1, 1, 3));
			problem.Connections.Add(MakeConnection(2, 1, 4));

			LayoutResult result = WireRouter.Route(problem, new RouterSettings());

			Assert.That(result.FailedCount, Is.EqualTo(0));
			int attached = 0;
			for (int i = 0; i < result.Connections.Count; i++)
			{
				RoutedConnection c = result.Connections[i];
				if (c.AttachedToKey < 0) continue;
				attached++;
				int parentIndex = result.Connections.FindIndex(p => p.Key == c.AttachedToKey);
				Assert.That(parentIndex, Is.GreaterThanOrEqualTo(0).And.LessThan(i), $"connection {c.Key}");
				AssertAttachPointOnParent(result.Connections[parentIndex], c.AttachedSegmentIndex, c.Points[0]);
			}

			Assert.That(attached, Is.EqualTo(2));
		}

		[Test]
		public void Route_BusWriter_AttachesOnlyToTrunk()
		{
			LayoutProblem problem = new();
			problem.Pins[10] = MakePin(10, 1, 0, 0, 1); // bus origin
			problem.Pins[11] = MakePin(11, 2, 4, 0, -1); // bus terminus
			problem.Pins[12] = MakePin(12, 3, 3, -0.5f, -1); // reader input
			problem.Pins[20] = MakePin(20, 4, 2, -0.5f, 1); // writer output, nearer the reader branch than the trunk
			problem.Connections.Add(new LayoutConnection { Key = 0, SourcePinKey = 10, TargetPinKey = 11, BitCount = 1, IsBusTrunk = true });
			problem.Connections.Add(MakeConnection(1, 10, 12));
			problem.Connections.Add(new LayoutConnection { Key = 2, SourcePinKey = 20, TargetPinKey = 11, BitCount = 1, TargetAttachToKey = 0 });

			LayoutResult result = WireRouter.Route(problem, new RouterSettings());

			Assert.That(result.FailedCount, Is.EqualTo(0));
			RoutedConnection writer = Find(result, 2);
			Assert.That(writer.TargetAttach, Is.EqualTo(AttachKind.Wire));
			Assert.That(writer.AttachedToKey, Is.EqualTo(0));
			Assert.That(writer.SourceAttach, Is.EqualTo(AttachKind.Pin));
			AssertSamePoint(writer.Points[0], problem.Pins[20].Position);
			AssertAttachPointOnParent(Find(result, 0), writer.AttachedSegmentIndex, writer.Points[^1]);
			Assert.That(result.Connections.FindIndex(c => c.Key == 0), Is.LessThan(result.Connections.FindIndex(c => c.Key == 2)));
		}

		[Test]
		public void Route_BusTrunkUnroutable_WriterMarkedUnrouted()
		{
			LayoutProblem problem = new();
			AddEnclosure(problem);
			problem.Pins[1] = MakePin(1, 1, 0, 0, 1);
			problem.Pins[2] = MakePin(2, 2, 2, 0, -1); // enclosed bus terminus
			problem.Pins[3] = MakePin(3, 3, 0, 2, 1);
			problem.Pins[4] = MakePin(4, 4, 2, 2, -1); // reachable, but the writer must end on the trunk
			problem.Connections.Add(new LayoutConnection { Key = 0, SourcePinKey = 1, TargetPinKey = 2, BitCount = 1, IsBusTrunk = true });
			problem.Connections.Add(new LayoutConnection { Key = 1, SourcePinKey = 3, TargetPinKey = 4, BitCount = 1, TargetAttachToKey = 0 });

			LayoutResult result = WireRouter.Route(problem, new RouterSettings());

			Assert.That(Find(result, 0).Routed, Is.False);
			Assert.That(Find(result, 1).Routed, Is.False);
			Assert.That(result.FailedCount, Is.EqualTo(2));
		}

		static RoutedConnection Find(LayoutResult result, int key)
		{
			RoutedConnection c = result.Connections.Find(r => r.Key == key);
			Assert.That(c, Is.Not.Null, $"connection {key} missing from result");
			return c;
		}

		static void AssertSamePoint(LayoutPoint actual, LayoutPoint expected)
		{
			Assert.That(actual.X, Is.EqualTo(expected.X).Within(1e-4f));
			Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(1e-4f));
		}

		static void AssertAttachPointOnParent(RoutedConnection parent, int segmentIndex, LayoutPoint p)
		{
			Assert.That(parent.Routed, Is.True);
			Assert.That(segmentIndex, Is.GreaterThanOrEqualTo(0).And.LessThan(parent.Points.Count - 1));
			Assert.That(TestGeometry.PointOnSegment(p, parent.Points[segmentIndex], parent.Points[segmentIndex + 1]), Is.True,
				$"({p.X}, {p.Y}) not on parent {parent.Key} segment {segmentIndex}");
		}
	}
}
