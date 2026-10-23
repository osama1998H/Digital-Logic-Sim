using DLS.Game.Editing;
using NUnit.Framework;

namespace LayoutTests
{
	public class BitPinNamesTests
	{
		[Test]
		public void SingleWidePin_KeepsPlainName()
		{
			Assert.That(BitPinNames.ForConversionChip(true, 0, 1, 8), Is.EqualTo("IN"));
			Assert.That(BitPinNames.ForConversionChip(false, 0, 1, 16), Is.EqualTo("OUT"));
		}

		[Test]
		public void Merge1To8_TopPinIsHighestBit()
		{
			string[] names = new string[8];
			for (int i = 0; i < 8; i++) names[i] = BitPinNames.ForConversionChip(true, i, 8, 1);
			Assert.That(names, Is.EqualTo(new[] { "IN 7", "IN 6", "IN 5", "IN 4", "IN 3", "IN 2", "IN 1", "IN 0" }));
		}

		[Test]
		public void Split16To1_TopPinIsBit15()
		{
			Assert.That(BitPinNames.ForConversionChip(false, 0, 16, 1), Is.EqualTo("OUT 15"));
			Assert.That(BitPinNames.ForConversionChip(false, 15, 16, 1), Is.EqualTo("OUT 0"));
		}

		[Test]
		public void MultiBitPins_ShowBitRange()
		{
			Assert.That(BitPinNames.ForConversionChip(false, 0, 2, 4), Is.EqualTo("OUT 7-4"));
			Assert.That(BitPinNames.ForConversionChip(false, 1, 2, 4), Is.EqualTo("OUT 3-0"));
			Assert.That(BitPinNames.ForConversionChip(true, 0, 2, 8), Is.EqualTo("IN 15-8"));
			Assert.That(BitPinNames.ForConversionChip(true, 1, 2, 8), Is.EqualTo("IN 7-0"));
		}
	}
}
