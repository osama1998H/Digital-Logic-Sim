using DLS.Game.Inspect;
using NUnit.Framework;

namespace LayoutTests
{
	public class PinValueFormatTests
	{
		[Test] public void Binary() => Assert.That(PinValueFormat.FormatValue(0b0101, 4, NumberFormat.Binary), Is.EqualTo("0101"));
		[Test] public void Hex_Padded() => Assert.That(PinValueFormat.FormatValue(0x5, 8, NumberFormat.Hex), Is.EqualTo("05"));
		[Test] public void Signed8() => Assert.That(PinValueFormat.FormatValue(0xFF, 8, NumberFormat.Signed), Is.EqualTo("-1"));
		[Test] public void Signed16() => Assert.That(PinValueFormat.FormatValue(0x8000, 16, NumberFormat.Signed), Is.EqualTo("-32768"));
		[Test] public void PartiallyFloating()
		{
			uint s = 0b0100 | (1u << 16); // bit 0 floating
			Assert.That(PinValueFormat.FormatValue(s, 4, NumberFormat.Binary), Is.EqualTo("010Z"));
			Assert.That(PinValueFormat.FormatValue(s, 4, NumberFormat.Decimal), Is.EqualTo("?"));
			Assert.That(PinValueFormat.FormatValue(s, 8, NumberFormat.Hex), Is.EqualTo("0?"));
		}
		[Test] public void FullyFloating() => Assert.That(PinValueFormat.TooltipLines("Y", 1u << 16, 1), Is.EqualTo(new[] { "Y", "Z (floating)" }));
		[Test] public void TooltipMultiBit() => Assert.That(PinValueFormat.TooltipLines("SUM", 0xA5, 8),
			Is.EqualTo(new[] { "SUM (8-bit)", "bin 1010_0101", "dec 165  (signed -91)", "hex A5" }));
		[Test] public void TooltipOneBit() => Assert.That(PinValueFormat.TooltipLines("C", 1, 1), Is.EqualTo(new[] { "C", "1" }));
	}
}
