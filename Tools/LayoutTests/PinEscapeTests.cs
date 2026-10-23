using System.Collections.Generic;
using DLS.Game.Layout;
using NUnit.Framework;

namespace LayoutTests
{
	// Pin exit stubs against neighbouring chips, with the game's geometry: a subchip's bounding box reaches 0.1075 past
	// its pin x, and the lattice pads every obstacle by GridSize / 2 = 0.0625
	public class PinEscapeTests
	{
		const float PinReach = 0.1075f;
		static readonly (int, int) None = (-1, -1);

		static LayoutPin MakePin(int key, int owner, float x, float y, int dir) =>
			new LayoutPin { Key = key, OwnerID = owner, Position = new LayoutPoint(x, y), ExitDirX = dir, BitCount = 1 };

		static LayoutConnection MakeConnection(int key, int source, int target) =>
			new LayoutConnection { Key = key, SourcePinKey = source, TargetPinKey = target, BitCount = 1 };

		// Chip whose right body edge (and output pins) sit at pinX
		static LayoutRect ChipLeftOf(int owner, float pinX, float minY = -0.5f, float maxY = 0.5f) =>
			new(owner, pinX - 1 - PinReach, minY, pinX + PinReach, maxY);

		// Chip whose left body edge (and input pins) sit at pinX
		static LayoutRect ChipRightOf(int owner, float pinX, float minY = -0.5f, float maxY = 0.5f) =>
			new(owner, pinX - PinReach, minY, pinX + 1 + PinReach, maxY);

		// Output pin of chip 1 at (aX, 0) facing input pin of chip 2 at (bX, 0)
		static LayoutProblem FacingChips(float aX, float bX)
		{
			LayoutProblem problem = new();
			problem.Obstacles.Add(ChipLeftOf(1, aX));
			problem.Obstacles.Add(ChipRightOf(2, bX));
			problem.Pins[1] = MakePin(1, 1, aX, 0, 1);
			problem.Pins[2] = MakePin(2, 2, bX, 0, -1);
			return problem;
		}

		static (int, int) Escape(LayoutProblem problem, int pinKey) => RoutingLattice.Build(problem, new RouterSettings()).EscapeNode(problem.Pins[pinKey]);

		[TestCase(0f, 0.25f)]
		[TestCase(0.26f, 0.49f)]
		public void EscapeNode_NeighbourAtQuarterBodyGap_ReturnsNone(float aX, float bX)
		{
			LayoutProblem problem = FacingChips(aX, bX);
			problem.Connections.Add(MakeConnection(0, 1, 2));

			Assert.That(Escape(problem, 1), Is.EqualTo(None));
			Assert.That(Escape(problem, 2), Is.EqualTo(None));
		}

		[TestCase(0f, 0.25f)]
		[TestCase(0.26f, 0.49f)]
		public void Route_DirectWireAtQuarterBodyGap_IsUnrouted(float aX, float bX)
		{
			LayoutProblem problem = FacingChips(aX, bX);
			problem.Connections.Add(MakeConnection(0, 1, 2));

			LayoutResult result = WireRouter.Route(problem, new RouterSettings());

			Assert.That(result.Connections.Count, Is.EqualTo(1));
			Assert.That(result.Connections[0].Routed, Is.False);
			Assert.That(result.Connections[0].Points, Is.Null);
			Assert.That(result.FailedCount, Is.EqualTo(1));
		}

		[Test]
		public void Route_UnconnectedNeighbourAtQuarterBodyGap_IsNeverRunThrough()
		{
			LayoutProblem problem = new();
			problem.Obstacles.Add(ChipLeftOf(1, 0, -1, 1));
			LayoutRect neighbour = ChipRightOf(2, 0.25f);
			problem.Obstacles.Add(neighbour);
			problem.Obstacles.Add(ChipRightOf(3, 4, -1, 1));
			problem.Pins[1] = MakePin(1, 1, 0, 0, 1); // faces the neighbour
			problem.Pins[2] = MakePin(2, 1, 0, 0.75f, 1); // above the neighbour, free to leave
			problem.Pins[3] = MakePin(3, 3, 4, 0, -1);
			problem.Pins[4] = MakePin(4, 3, 4, 0.75f, -1);
			problem.Connections.Add(MakeConnection(0, 1, 3));
			problem.Connections.Add(MakeConnection(1, 2, 4));

			LayoutResult result = WireRouter.Route(problem, new RouterSettings());

			RoutedConnection blocked = result.Connections.Find(c => c.Key == 0);
			Assert.That(blocked.Routed, Is.False);
			Assert.That(blocked.Points, Is.Null);
			RoutedConnection free = result.Connections.Find(c => c.Key == 1);
			Assert.That(free.Routed, Is.True);
			AssertMisses(free.Points, neighbour);
			Assert.That(result.FailedCount, Is.EqualTo(1));
		}

		[TestCase(0f, 0.5f)]
		[TestCase(0.26f, 0.76f)]
		public void Route_DirectWireAtHalfBodyGap_IsStraight(float aX, float bX)
		{
			LayoutProblem problem = FacingChips(aX, bX);
			problem.Connections.Add(MakeConnection(0, 1, 2));

			LayoutResult result = WireRouter.Route(problem, new RouterSettings());

			Assert.That(result.FailedCount, Is.EqualTo(0));
			RoutedConnection c = result.Connections[0];
			Assert.That(c.Routed, Is.True);
			Assert.That(c.Points.Count, Is.EqualTo(2));
			Assert.That(c.Points[0].X, Is.EqualTo(aX).Within(1e-4f));
			Assert.That(c.Points[1].X, Is.EqualTo(bX).Within(1e-4f));
			Assert.That(c.Points[0].Y, Is.EqualTo(0).Within(1e-4f));
			Assert.That(c.Points[1].Y, Is.EqualTo(0).Within(1e-4f));
		}

		[Test]
		public void EscapeNode_SameNetFacingPinsAtHalfBodyGap_ShareNode()
		{
			LayoutProblem problem = FacingChips(0, 0.5f);
			problem.Connections.Add(MakeConnection(0, 1, 2));
			RoutingLattice lattice = RoutingLattice.Build(problem, new RouterSettings());

			(int ix, int iy) = lattice.EscapeNode(problem.Pins[1]);

			Assert.That(lattice.EscapeNode(problem.Pins[2]), Is.EqualTo((ix, iy)));
			Assert.That(lattice.Point(ix, iy).X, Is.EqualTo(0.25f).Within(1e-4f));
			Assert.That(lattice.ReservedPinKey(ix, iy), Is.EqualTo(1));
		}

		[Test]
		public void Route_UnconnectedNeighbourAtHalfBodyGap_RoutesAroundIt()
		{
			LayoutProblem problem = new();
			problem.Obstacles.Add(ChipLeftOf(1, 0));
			LayoutRect neighbour = ChipRightOf(2, 0.5f);
			problem.Obstacles.Add(neighbour);
			problem.Obstacles.Add(ChipRightOf(3, 4));
			problem.Pins[1] = MakePin(1, 1, 0, 0, 1);
			problem.Pins[3] = MakePin(3, 3, 4, 0, -1);
			problem.Connections.Add(MakeConnection(0, 1, 3));

			LayoutResult result = WireRouter.Route(problem, new RouterSettings());

			Assert.That(result.FailedCount, Is.EqualTo(0));
			AssertMisses(result.Connections[0].Points, neighbour);
		}

		[Test]
		public void EscapeNode_UnrelatedFacingPinsAtHalfBodyGap_HigherKeyGetsNone()
		{
			LayoutProblem problem = FacingChips(0, 0.5f);
			problem.Obstacles.Add(ChipRightOf(3, 4, 1.5f, 2.5f));
			problem.Obstacles.Add(ChipLeftOf(4, -4, 1.5f, 2.5f));
			problem.Pins[3] = MakePin(3, 3, 4, 2, -1);
			problem.Pins[4] = MakePin(4, 4, -4, 2, 1);
			problem.Connections.Add(MakeConnection(0, 1, 3)); // chip 1 -> chip 3
			problem.Connections.Add(MakeConnection(1, 4, 2)); // chip 4 -> chip 2, a different net
			RoutingLattice lattice = RoutingLattice.Build(problem, new RouterSettings());

			(int ix, int iy) = lattice.EscapeNode(problem.Pins[1]);

			Assert.That(lattice.Point(ix, iy).X, Is.EqualTo(0.25f).Within(1e-4f));
			Assert.That(lattice.ReservedPinKey(ix, iy), Is.EqualTo(1));
			Assert.That(lattice.EscapeNode(problem.Pins[2]), Is.EqualTo(None));

			LayoutResult result = WireRouter.Route(problem, new RouterSettings());

			Assert.That(result.Connections.Find(c => c.Key == 0).Routed, Is.True);
			RoutedConnection loser = result.Connections.Find(c => c.Key == 1);
			Assert.That(loser.Routed, Is.False);
			Assert.That(loser.Points, Is.Null);
		}

		[Test]
		public void EscapeNode_ForeignObstacleBeforeEscapeNode_ReturnsNone()
		{
			LayoutProblem problem = new();
			problem.Obstacles.Add(ChipLeftOf(1, 0));
			problem.Obstacles.Add(new LayoutRect(3, 0.2f, -0.05f, 0.3f, 0.05f)); // padded, covers the node at x = 0.25
			problem.Pins[1] = MakePin(1, 1, 0, 0, 1);

			Assert.That(Escape(problem, 1), Is.EqualTo(None));
		}

		[Test]
		public void EscapeNode_ForeignObstacleWithinFirstCell_ReturnsNone()
		{
			// Off-grid pin with no obstacle of its own: the node at x = 0.375 is nearer than a cell, so it is never the
			// escape node, but the stub to the node at x = 0.5 would still cross the obstacle covering it
			LayoutProblem problem = new();
			problem.Obstacles.Add(new LayoutRect(3, 0.35f, -0.05f, 0.4f, 0.05f));
			problem.Pins[1] = MakePin(1, 1, 0.26f, 0, 1);

			Assert.That(Escape(problem, 1), Is.EqualTo(None));
		}

		static void AssertMisses(List<LayoutPoint> points, LayoutRect rect)
		{
			Assert.That(points, Is.Not.Null);
			for (int i = 0; i < points.Count - 1; i++)
			{
				Assert.That(TestGeometry.SegmentIntersectsRect(points[i], points[i + 1], rect), Is.False, $"segment {i}");
			}
		}
	}
}
