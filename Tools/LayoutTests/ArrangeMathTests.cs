using DLS.Game.Editing;
using NUnit.Framework;

namespace LayoutTests
{
	public class ArrangeMathTests
	{
		static ElementBox Box(int id, float minX, float minY, float w, float h) => new(id, minX, minY, minX + w, minY + h, minX, minY + h / 2);

		static ElementBox[] Moved(ElementBox[] boxes, (float dx, float dy)[] d)
		{
			ElementBox[] result = new ElementBox[boxes.Length];
			for (int i = 0; i < boxes.Length; i++) result[i] = boxes[i].Offset(d[i].dx, d[i].dy);
			return result;
		}

		readonly ElementBox[] scattered =
		{
			Box(1, 0, 0, 1, 1),
			Box(2, 3, 2, 2, 1),
			Box(3, 1, 5, 1, 2)
		};

		[TestCase(ArrangeOp.AlignLeft)]
		[TestCase(ArrangeOp.AlignRight)]
		[TestCase(ArrangeOp.AlignHCentre)]
		public void HorizontalAlign_OnlyMovesX(ArrangeOp op)
		{
			var d = ArrangeMath.ComputeDeltas(scattered, op, 0, 0);
			foreach (var delta in d) Assert.That(delta.dy, Is.EqualTo(0f));
		}

		[Test]
		public void AlignLeft_MatchesLeftMostEdge()
		{
			ElementBox[] moved = Moved(scattered, ArrangeMath.ComputeDeltas(scattered, ArrangeOp.AlignLeft, 0, 0));
			foreach (ElementBox b in moved) Assert.That(b.MinX, Is.EqualTo(0f));
		}

		[Test]
		public void AlignRight_MatchesRightMostEdge()
		{
			ElementBox[] moved = Moved(scattered, ArrangeMath.ComputeDeltas(scattered, ArrangeOp.AlignRight, 0, 0));
			foreach (ElementBox b in moved) Assert.That(b.MaxX, Is.EqualTo(5f));
		}

		[Test]
		public void AlignHCentre_UsesCentreOfSelectionBounds()
		{
			ElementBox[] moved = Moved(scattered, ArrangeMath.ComputeDeltas(scattered, ArrangeOp.AlignHCentre, 0, 0));
			foreach (ElementBox b in moved) Assert.That(b.CentreX, Is.EqualTo(2.5f));
		}

		[Test]
		public void AlignTopBottomVCentre()
		{
			ElementBox[] top = Moved(scattered, ArrangeMath.ComputeDeltas(scattered, ArrangeOp.AlignTop, 0, 0));
			foreach (ElementBox b in top) Assert.That(b.MaxY, Is.EqualTo(7f));
			ElementBox[] bottom = Moved(scattered, ArrangeMath.ComputeDeltas(scattered, ArrangeOp.AlignBottom, 0, 0));
			foreach (ElementBox b in bottom) Assert.That(b.MinY, Is.EqualTo(0f));
			ElementBox[] mid = Moved(scattered, ArrangeMath.ComputeDeltas(scattered, ArrangeOp.AlignVCentre, 0, 0));
			foreach (ElementBox b in mid) Assert.That(b.CentreY, Is.EqualTo(3.5f));
		}

		[Test]
		public void DistributeV_EqualGapsAndEndsFixed()
		{
			ElementBox[] boxes =
			{
				Box(1, 0, 0, 1, 1),
				Box(2, 0, 1.5f, 1, 1),
				Box(3, 0, 2.7f, 1, 1),
				Box(4, 0, 9, 1, 1)
			};
			var d = ArrangeMath.ComputeDeltas(boxes, ArrangeOp.DistributeV, 0, 0);
			Assert.That(d[0].dy, Is.EqualTo(0f));
			Assert.That(d[3].dy, Is.EqualTo(0f));
			ElementBox[] moved = Moved(boxes, d);
			Assert.That(moved[1].MinY, Is.EqualTo(3f).Within(1e-5));
			Assert.That(moved[2].MinY, Is.EqualTo(6f).Within(1e-5));
		}

		[Test]
		public void DistributeH_OrdersByCentreNotInputOrder()
		{
			ElementBox[] boxes =
			{
				Box(1, 10, 0, 2, 1),
				Box(2, 0, 0, 2, 1),
				Box(3, 3, 0, 2, 1)
			};
			ElementBox[] moved = Moved(boxes, ArrangeMath.ComputeDeltas(boxes, ArrangeOp.DistributeH, 0, 0));
			Assert.That(moved[1].MinX, Is.EqualTo(0f));
			Assert.That(moved[0].MinX, Is.EqualTo(10f));
			Assert.That(moved[2].MinX, Is.EqualTo(5f).Within(1e-5));
		}

		[Test]
		public void Distribute_WithFewerThanThree_DoesNothing()
		{
			ElementBox[] boxes = { Box(1, 0, 0, 1, 1), Box(2, 5, 3, 1, 1) };
			foreach (var delta in ArrangeMath.ComputeDeltas(boxes, ArrangeOp.DistributeV, 0, 0))
			{
				Assert.That(delta.dx, Is.EqualTo(0f));
				Assert.That(delta.dy, Is.EqualTo(0f));
			}
		}

		[Test]
		public void Snapping_PutsAnchorOnGrid()
		{
			ElementBox[] boxes =
			{
				new(1, 0, 0, 1, 1, 0, 0),
				new(2, 0, 1.3f, 1, 2.3f, 0, 1.3f),
				new(3, 0, 6, 1, 7, 0, 6)
			};
			const float step = 0.0625f;
			ElementBox[] moved = Moved(boxes, ArrangeMath.ComputeDeltas(boxes, ArrangeOp.DistributeV, 0.125f, step));
			foreach (ElementBox b in moved)
			{
				float k = b.AnchorY / step;
				Assert.That(k, Is.EqualTo(System.MathF.Round(k)).Within(1e-4));
			}
		}

		[Test]
		public void AlignTop_OnStackedColumn_IsReportedAsOverlapping()
		{
			ElementBox[] column = { Box(1, 0, 0, 1, 1), Box(2, 0, 2, 1, 1), Box(3, 0, 4, 1, 1) };
			ElementBox[] moved = Moved(column, ArrangeMath.ComputeDeltas(column, ArrangeOp.AlignTop, 0, 0));
			Assert.That(ArrangeMath.AnyOverlap(moved, new ElementBox[0]), Is.True);
			Assert.That(ArrangeMath.AnyOverlap(column, new ElementBox[0]), Is.False);
		}

		[Test]
		public void AnyOverlap_ChecksOtherElements()
		{
			ElementBox[] moved = { Box(1, 0, 0, 1, 1) };
			Assert.That(ArrangeMath.AnyOverlap(moved, new[] { Box(9, 1, 0, 1, 1) }), Is.True); // touching counts
			Assert.That(ArrangeMath.AnyOverlap(moved, new[] { Box(9, 1.1f, 0, 1, 1) }), Is.False);
		}
	}
}
