using System;
using DLS.Game.Layout;
using NUnit.Framework;

namespace LayoutTests
{
	public class RoutingLatticeTests
	{
		static LayoutPin MakePin(int key, int owner, float x, float y, int dir) =>
			new LayoutPin { Key = key, OwnerID = owner, Position = new LayoutPoint(x, y), ExitDirX = dir, BitCount = 1 };

		[Test]
		public void Build_AddsOffGridPinRow()
		{
			LayoutProblem problem = new();
			problem.Obstacles.Add(new LayoutRect(1, 0, 0, 1, 1));
			problem.Pins[1] = MakePin(1, 1, 1f, 0.06f, 1);

			RoutingLattice lattice = RoutingLattice.Build(problem, new RouterSettings());

			Assert.That(lattice.IndexOfY(0.06f), Is.GreaterThanOrEqualTo(0));
		}

		[Test]
		public void Build_GridRowsAreGridMultiples()
		{
			LayoutProblem problem = new();
			problem.Obstacles.Add(new LayoutRect(1, 0, 0, 1, 1));
			problem.Pins[1] = MakePin(1, 1, 1f, 0.06f, 1);
			problem.Pins[2] = MakePin(2, 1, 0f, 0.31f, -1);
			RouterSettings settings = new();

			RoutingLattice lattice = RoutingLattice.Build(problem, settings);

			for (int i = 0; i < lattice.Height; i++)
			{
				float y = lattice.Ys[i];
				if (Math.Abs(y - 0.06f) < 1e-4f || Math.Abs(y - 0.31f) < 1e-4f) continue;
				float cells = y / settings.GridSize;
				Assert.That(Math.Abs(cells - MathF.Round(cells)), Is.LessThan(1e-4f), $"Ys[{i}] = {y}");
			}

			for (int i = 1; i < lattice.Height; i++) Assert.That(lattice.Ys[i], Is.GreaterThan(lattice.Ys[i - 1] + 1e-4f));
			for (int i = 1; i < lattice.Width; i++) Assert.That(lattice.Xs[i], Is.GreaterThan(lattice.Xs[i - 1] + 1e-4f));
		}

		[Test]
		public void Build_IncludesMarginAroundBounds()
		{
			LayoutProblem problem = new();
			problem.Obstacles.Add(new LayoutRect(1, 0, 0, 1, 1));
			RouterSettings settings = new();

			RoutingLattice lattice = RoutingLattice.Build(problem, settings);

			Assert.That(lattice.Xs[0], Is.LessThanOrEqualTo(-settings.LatticeMarginCells * settings.GridSize + 1e-4f));
			Assert.That(lattice.Xs[lattice.Width - 1], Is.GreaterThanOrEqualTo(1 + settings.LatticeMarginCells * settings.GridSize - 1e-4f));
		}

		[Test]
		public void IsBlocked_InsideObstacleTrue_OutsideFalse()
		{
			LayoutProblem problem = new();
			problem.Obstacles.Add(new LayoutRect(1, 0, 0, 1, 1));
			RoutingLattice lattice = RoutingLattice.Build(problem, new RouterSettings());

			int ix = lattice.IndexOfX(0.5f), iy = lattice.IndexOfY(0.5f);
			Assert.That(ix, Is.GreaterThanOrEqualTo(0));
			Assert.That(lattice.IsBlocked(ix, iy), Is.True);
			Assert.That(lattice.IsBlocked(lattice.IndexOfX(2f), lattice.IndexOfY(2f)), Is.False);
		}

		[Test]
		public void IsBlocked_ObstacleExpandedByHalfGrid()
		{
			LayoutProblem problem = new();
			problem.Obstacles.Add(new LayoutRect(1, 0, 0, 1, 1));
			RoutingLattice lattice = RoutingLattice.Build(problem, new RouterSettings());
			int iy = lattice.IndexOfY(0.5f);

			// x = 1.0625 is exactly on the expanded boundary (not strictly inside); 1.0 is inside
			Assert.That(lattice.IsBlocked(lattice.IndexOfX(1f), iy), Is.True);
			Assert.That(lattice.IsBlocked(lattice.IndexOfX(1.125f), iy), Is.False);
		}

		[Test]
		public void EscapeNode_IsOutsideOwnObstacleOnExitSide()
		{
			LayoutProblem problem = new();
			problem.Obstacles.Add(new LayoutRect(1, 0, 0, 1.01f, 1));
			LayoutPin pin = MakePin(5, 1, 1.01f, 0.5f, 1);
			problem.Pins[5] = pin;
			RoutingLattice lattice = RoutingLattice.Build(problem, new RouterSettings());

			(int ix, int iy) = lattice.EscapeNode(pin);

			Assert.That(ix, Is.GreaterThanOrEqualTo(0));
			LayoutPoint p = lattice.Point(ix, iy);
			Assert.That(p.X, Is.GreaterThan(1.01f + 0.0625f));
			Assert.That(p.Y, Is.EqualTo(0.5f).Within(1e-4f));
		}

		[Test]
		public void EscapeNode_NegativeExitDirGoesLeft()
		{
			LayoutProblem problem = new();
			problem.Obstacles.Add(new LayoutRect(1, 0, 0, 1, 1));
			LayoutPin pin = MakePin(5, 1, 0f, 0.5f, -1);
			problem.Pins[5] = pin;
			RoutingLattice lattice = RoutingLattice.Build(problem, new RouterSettings());

			(int ix, int iy) = lattice.EscapeNode(pin);

			LayoutPoint p = lattice.Point(ix, iy);
			Assert.That(p.X, Is.LessThanOrEqualTo(0f - 0.125f + 1e-4f));
			Assert.That(p.Y, Is.EqualTo(0.5f).Within(1e-4f));
		}

		[Test]
		public void EscapeNode_ReservedForItsPin()
		{
			LayoutProblem problem = new();
			problem.Obstacles.Add(new LayoutRect(1, 0, 0, 1.01f, 1));
			LayoutPin pin = MakePin(5, 1, 1.01f, 0.5f, 1);
			problem.Pins[5] = pin;
			RoutingLattice lattice = RoutingLattice.Build(problem, new RouterSettings());

			(int ix, int iy) = lattice.EscapeNode(pin);

			Assert.That(lattice.ReservedPinKey(ix, iy), Is.EqualTo(5));
			Assert.That(lattice.IsBlocked(ix, iy), Is.True);
			Assert.That(lattice.ReservedPinKey(lattice.IndexOfX(2f), lattice.IndexOfY(2f)), Is.EqualTo(-1));
		}

		[Test]
		public void EscapeNode_SharedNodeSameNet_ReservedForLowerKey()
		{
			LayoutProblem problem = new();
			problem.Obstacles.Add(new LayoutRect(1, 0, 0, 1, 1));
			LayoutPin a = MakePin(9, 1, 1f, 0.5f, 1);
			LayoutPin b = MakePin(3, 1, 1f, 0.5f, 1);
			problem.Pins[9] = a;
			problem.Pins[3] = b;
			problem.Connections.Add(new LayoutConnection { Key = 0, SourcePinKey = 9, TargetPinKey = 3, BitCount = 1 });
			RoutingLattice lattice = RoutingLattice.Build(problem, new RouterSettings());

			var ea = lattice.EscapeNode(a);
			var eb = lattice.EscapeNode(b);

			Assert.That(ea.ix, Is.GreaterThanOrEqualTo(0));
			Assert.That(ea, Is.EqualTo(eb));
			Assert.That(lattice.ReservedPinKey(ea.ix, ea.iy), Is.EqualTo(3));
		}

		[Test]
		public void EscapeNode_SharedNodeOtherNet_HigherKeyGetsNone()
		{
			LayoutProblem problem = new();
			problem.Obstacles.Add(new LayoutRect(1, 0, 0, 1, 1));
			LayoutPin a = MakePin(9, 1, 1f, 0.5f, 1);
			LayoutPin b = MakePin(3, 1, 1f, 0.5f, 1);
			problem.Pins[9] = a;
			problem.Pins[3] = b;
			RoutingLattice lattice = RoutingLattice.Build(problem, new RouterSettings());

			var eb = lattice.EscapeNode(b);

			Assert.That(eb.ix, Is.GreaterThanOrEqualTo(0));
			Assert.That(lattice.ReservedPinKey(eb.ix, eb.iy), Is.EqualTo(3));
			Assert.That(lattice.EscapeNode(a), Is.EqualTo((-1, -1)));
		}
	}
}
