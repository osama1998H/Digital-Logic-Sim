using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DLS.Game.Inspect;

namespace DLS.Game.Logic
{
	public static class TestVectorChecker
	{
		// Returns null when every expectation holds, otherwise a ", "-joined list of mismatches.
		public static string Check(TestVector vector, uint[] outputStates, IReadOnlyList<TruthPin> outputs)
		{
			StringBuilder failures = null;

			foreach ((int pin, ExpectKind kind, uint expected) in vector.Expectations)
			{
				int bits = outputs[pin].BitCount;
				uint mask = (1u << bits) - 1;
				uint state = outputStates[pin];
				uint value = state & mask;
				uint floating = (state >> 16) & mask;

				bool pass = kind == ExpectKind.Floating ? floating == mask : floating == 0 && value == expected;
				if (pass) continue;

				string actual = floating != 0
					? "0b" + PinValueFormat.FormatValue(state, bits, NumberFormat.Binary)
					: value.ToString(CultureInfo.InvariantCulture);
				string wanted = kind == ExpectKind.Floating ? "z" : expected.ToString(CultureInfo.InvariantCulture);

				if (failures == null) failures = new StringBuilder();
				else failures.Append(", ");
				failures.Append(outputs[pin].Name).Append('=').Append(actual).Append(" (expected ").Append(wanted).Append(')');
			}

			return failures?.ToString();
		}
	}

	public static class TestVectorWriter
	{
		// Pins whose names can't be parsed back (duplicates, spaces, '=', '#', '->', positional lookalikes) are written as inN/outN.
		public static string FromTruthTableRow(TruthTable table, int row)
		{
			var sb = new StringBuilder();
			for (int i = 0; i < table.Inputs.Length; i++)
			{
				if (i > 0) sb.Append(' ');
				sb.Append(Reference(table.Inputs, i, "in")).Append('=').Append(table.GetInputValue(row, i).ToString(CultureInfo.InvariantCulture));
			}

			sb.Append(" ->");
			for (int i = 0; i < table.Outputs.Length; i++)
			{
				int bits = table.Outputs[i].BitCount;
				uint mask = (1u << bits) - 1;
				uint state = table.GetOutputState(row, i);
				uint floating = (state >> 16) & mask;
				string value = floating == mask ? "z" : floating != 0 ? "x" : (state & mask).ToString(CultureInfo.InvariantCulture);
				sb.Append(' ').Append(Reference(table.Outputs, i, "out")).Append('=').Append(value);
			}
			return sb.ToString();
		}

		static string Reference(TruthPin[] pins, int index, string prefix)
		{
			string name = pins[index].Name;
			if (!CanUseName(pins, index, prefix)) return prefix + (index + 1);
			return name;
		}

		static bool CanUseName(TruthPin[] pins, int index, string prefix)
		{
			string name = pins[index].Name;
			if (string.IsNullOrEmpty(name)) return false;
			foreach (char c in name)
			{
				if (char.IsWhiteSpace(c) || c == '=' || c == '#') return false;
			}
			if (name.Contains("->") || name.StartsWith("-")) return false;

			for (int i = 0; i < pins.Length; i++)
			{
				if (i != index && string.Equals(pins[i].Name, name, System.StringComparison.OrdinalIgnoreCase)) return false;
			}
			return TestVectorParser.TryResolvePin(name, pins, prefix, "", out int resolved, out _) && resolved == index;
		}
	}
}
