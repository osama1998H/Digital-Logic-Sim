using DLS.Game.Editing;
using NUnit.Framework;

namespace LayoutTests
{
	public class PinMatcherTests
	{
		static CorePin P(int id, int bits = 1) => new(id, bits);

		[Test]
		public void CopiedChip_WithReorderedPins_MatchesByID()
		{
			var map = PinMatcher.TryMatch(new[] { P(10), P(11) }, new[] { P(20) }, new[] { P(11), P(10) }, new[] { P(20) });
			Assert.That(map, Is.Not.Null);
			Assert.That(map[10], Is.EqualTo(10));
			Assert.That(map[11], Is.EqualTo(11));
			Assert.That(map[20], Is.EqualTo(20));
		}

		[Test]
		public void UnrelatedChips_MatchByOrder()
		{
			var map = PinMatcher.TryMatch(new[] { P(1), P(0) }, new[] { P(2) }, new[] { P(7), P(8) }, new[] { P(9) });
			Assert.That(map[1], Is.EqualTo(7));
			Assert.That(map[0], Is.EqualTo(8));
			Assert.That(map[2], Is.EqualTo(9));
		}

		[Test]
		public void DifferentBitCounts_AreIncompatible()
		{
			Assert.That(PinMatcher.TryMatch(new[] { P(1, 8) }, new[] { P(2) }, new[] { P(5, 4) }, new[] { P(6) }), Is.Null);
		}

		[Test]
		public void DifferentPinCounts_AreIncompatible()
		{
			Assert.That(PinMatcher.TryMatch(new[] { P(1), P(2) }, new[] { P(3) }, new[] { P(1) }, new[] { P(3) }), Is.Null);
			Assert.That(PinMatcher.TryMatch(new[] { P(1) }, new[] { P(3) }, new[] { P(1) }, new[] { P(3), P(4) }), Is.Null);
		}

		[Test]
		public void SameIDsButDifferentWidths_FallsBackToOrder()
		{
			// ID 1 changed width, so can't match by ID; by order the widths line up
			var map = PinMatcher.TryMatch(new[] { P(1, 1), P(2, 8) }, new CorePin[0], new[] { P(2, 1), P(1, 8) }, new CorePin[0]);
			Assert.That(map[1], Is.EqualTo(2));
			Assert.That(map[2], Is.EqualTo(1));
		}
	}
}
