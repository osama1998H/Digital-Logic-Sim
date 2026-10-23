using DLS.Game.Layout;
using NUnit.Framework;

namespace LayoutTests
{
	public class TestGeometryTests
	{
		static LayoutPoint P(float x, float y) => new(x, y);

		[Test]
		public void SegmentsOverlapCollinear_OverlappingParallelHorizontal_True()
		{
			Assert.That(TestGeometry.SegmentsOverlapCollinear(P(0, 0), P(2, 0), P(1, 0), P(3, 0), 0.05f), Is.True);
		}

		[Test]
		public void SegmentsOverlapCollinear_DisjointInProjection_False()
		{
			Assert.That(TestGeometry.SegmentsOverlapCollinear(P(0, 0), P(1, 0), P(2, 0), P(3, 0), 0.05f), Is.False);
		}

		[Test]
		public void SegmentsOverlapCollinear_ParallelFartherThanMinGap_False()
		{
			Assert.That(TestGeometry.SegmentsOverlapCollinear(P(0, 0), P(2, 0), P(1, 0.2f), P(3, 0.2f), 0.05f), Is.False);
		}

		[Test]
		public void SegmentsOverlapCollinear_Perpendicular_False()
		{
			Assert.That(TestGeometry.SegmentsOverlapCollinear(P(0, 0), P(2, 0), P(1, -1), P(1, 1), 0.05f), Is.False);
		}

		[Test]
		public void SegmentIntersectsRect_CrossingTrue_BesideFalse()
		{
			LayoutRect r = new(1, 0, 0, 1, 1);
			Assert.That(TestGeometry.SegmentIntersectsRect(P(-1, 0.5f), P(2, 0.5f), r), Is.True);
			Assert.That(TestGeometry.SegmentIntersectsRect(P(-1, 1.5f), P(2, 1.5f), r), Is.False);
		}

		[Test]
		public void PointOnSegment_OnTrue_OffFalse()
		{
			Assert.That(TestGeometry.PointOnSegment(P(1, 0), P(0, 0), P(2, 0)), Is.True);
			Assert.That(TestGeometry.PointOnSegment(P(3, 0), P(0, 0), P(2, 0)), Is.False);
		}
	}
}
