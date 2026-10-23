using DLS.Game.Editing;
using NUnit.Framework;

namespace LayoutTests
{
	public class SequenceNamingTests
	{
		[Test]
		public void StackedColumn_BottomGetsIndexZero()
		{
			PositionedItem[] pins = new PositionedItem[8];
			for (int i = 0; i < 8; i++) pins[i] = new PositionedItem(100 + i, 0, -i); // id 100 on top, 107 at bottom
			int[] order = SequenceNaming.OrderForSequence(pins, 0);
			Assert.That(order, Is.EqualTo(new[] { 107, 106, 105, 104, 103, 102, 101, 100 }));
		}

		[Test]
		public void Row_RightMostGetsIndexZero()
		{
			PositionedItem[] items =
			{
				new(1, 0, 0.01f),
				new(2, 2, 0),
				new(3, 4, -0.01f)
			};
			int[] order = SequenceNaming.OrderForSequence(items, 0.0625f);
			Assert.That(order, Is.EqualTo(new[] { 3, 2, 1 }));
		}

		[Test]
		public void Order_IsReverseOfSavedPinOrder()
		{
			PositionedItem[] pins =
			{
				new(1, 0, 1),
				new(2, 1, 1),
				new(3, 0, 0),
				new(4, 5, 2)
			};
			int[] sequence = SequenceNaming.OrderForSequence(pins, 0);
			int[] saved = ArrangeMath.PinOrder(pins);
			System.Array.Reverse(saved);
			Assert.That(sequence, Is.EqualTo(saved));
		}

		[Test]
		public void Ties_AreBrokenByID()
		{
			PositionedItem[] items = { new(5, 0, 0), new(2, 0, 0) };
			Assert.That(SequenceNaming.OrderForSequence(items, 0), Is.EqualTo(new[] { 2, 5 }));
		}

		[Test]
		public void Names_AppendIndex()
		{
			Assert.That(SequenceNaming.Names("A", 3), Is.EqualTo(new[] { "A0", "A1", "A2" }));
			Assert.That(SequenceNaming.Names("FA", 2, 4), Is.EqualTo(new[] { "FA4", "FA5" }));
		}

		[Test]
		public void Fits_AccountsForLargestIndexDigits()
		{
			Assert.That(SequenceNaming.Fits("ABCDEFGHIJKLMN", 10, 16), Is.True); // ...9 -> 15 chars
			Assert.That(SequenceNaming.Fits("ABCDEFGHIJKLMNO", 11, 16), Is.False); // ...10 -> 17 chars
		}
	}
}
