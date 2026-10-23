using System;
using System.Globalization;
using System.Text;

namespace DLS.Game.Inspect
{
	public enum NumberFormat
	{
		Binary,
		Decimal,
		Hex,
		Signed
	}

	// state: bits in the low 16 bits, floating flags in the high 16 bits.
	public static class PinValueFormat
	{
		const int MaxWidth = 16;
		const uint ValueMask = 0xFFFF;

		public static string FormatValue(uint state, int bitCount, NumberFormat format)
		{
			int width = ClampWidth(bitCount);
			uint mask = (1u << width) - 1;
			uint bits = state & ValueMask & mask;
			uint floating = (state >> MaxWidth) & mask;

			switch (format)
			{
				case NumberFormat.Binary:
					return Binary(bits, floating, width);
				case NumberFormat.Hex:
					return Hex(bits, floating, width);
				case NumberFormat.Signed:
					return WholeNumber(bits, floating, width, Signed(bits, width));
				default:
					return WholeNumber(bits, floating, width, bits.ToString(CultureInfo.InvariantCulture));
			}
		}

		public static string[] TooltipLines(string name, uint state, int bitCount)
		{
			int width = ClampWidth(bitCount);
			uint mask = (1u << width) - 1;
			uint bits = state & ValueMask & mask;
			uint floating = (state >> MaxWidth) & mask;

			if (width == 1)
			{
				string value = floating != 0 ? "Z (floating)" : bits != 0 ? "1" : "0";
				return new[] { name, value };
			}

			string title = $"{name} ({bitCount}-bit)";
			if (floating == mask) return new[] { title, "Z (floating)" };

			string dec = FormatValue(state, bitCount, NumberFormat.Decimal);
			string signed = FormatValue(state, bitCount, NumberFormat.Signed);
			return new[]
			{
				title,
				"bin " + Binary(bits, floating, width, grouped: true),
				"dec " + dec + "  (signed " + signed + ")",
				"hex " + FormatValue(state, bitCount, NumberFormat.Hex)
			};
		}

		static int ClampWidth(int bitCount) => Math.Max(1, Math.Min(bitCount, MaxWidth));

		static string Binary(uint bits, uint floating, int width, bool grouped = false)
		{
			var chars = new StringBuilder(width + width / 4);
			for (int i = width - 1; i >= 0; i--)
			{
				// Groups are nibble-aligned from the LSB, so 6 bits read 11_0101.
				if (grouped && i < width - 1 && i % 4 == 3) chars.Append('_');
				bool isFloating = ((floating >> i) & 1) != 0;
				chars.Append(isFloating ? 'Z' : ((bits >> i) & 1) != 0 ? '1' : '0');
			}
			return chars.ToString();
		}

		static string Hex(uint bits, uint floating, int width)
		{
			if (floating == (1u << width) - 1) return "Z";

			int digits = (width + 3) / 4;
			var chars = new char[digits];
			for (int d = 0; d < digits; d++)
			{
				int shift = 4 * (digits - 1 - d);
				if (((floating >> shift) & 0xF) != 0) chars[d] = '?';
				else chars[d] = "0123456789ABCDEF"[(int)((bits >> shift) & 0xF)];
			}
			return new string(chars);
		}

		static string WholeNumber(uint bits, uint floating, int width, string value)
		{
			if (floating == (1u << width) - 1) return "Z";
			return floating != 0 ? "?" : value;
		}

		static string Signed(uint bits, int width)
		{
			long value = bits;
			if (((bits >> (width - 1)) & 1) != 0) value -= 1L << width;
			return value.ToString(CultureInfo.InvariantCulture);
		}
	}
}
