using System;
using System.Collections.Generic;
using System.Globalization;

namespace DLS.Game.Logic
{
	public enum ExpectKind
	{
		Value,
		Floating
	}

	public sealed class TestVector
	{
		public int LineIndex;
		public List<(int PinIndex, uint Value)> Inputs = new List<(int PinIndex, uint Value)>();
		public List<(int PinIndex, ExpectKind Kind, uint Value)> Expectations = new List<(int PinIndex, ExpectKind Kind, uint Value)>();
		public bool HasCheck => Expectations.Count > 0;
	}

	// Line syntax: "A=5 B=0x3 -> SUM=8". '#' starts a comment; a line without "->" only sets inputs.
	public static class TestVectorParser
	{
		public static bool TryParse(string line, int lineIndex, IReadOnlyList<TruthPin> inputs, IReadOnlyList<TruthPin> outputs, out TestVector vector, out string error)
		{
			vector = null;
			error = null;

			int hash = line.IndexOf('#');
			if (hash >= 0) line = line.Substring(0, hash);
			if (line.Trim().Length == 0) return true;

			string left = line;
			string right = null;
			int arrow = line.IndexOf("->", StringComparison.Ordinal);
			if (arrow >= 0)
			{
				left = line.Substring(0, arrow);
				right = line.Substring(arrow + 2);
			}

			var result = new TestVector { LineIndex = lineIndex };

			foreach (string token in Tokens(left))
			{
				if (!TrySplit(token, out string name, out string text, out error)) return false;
				if (!TryResolvePin(name, inputs, "in", "input", out int pin, out error)) return false;
				if (!TryParseValue(text, name, inputs[pin].BitCount, false, out ParsedValue value, out error)) return false;
				result.Inputs.Add((pin, value.Value));
			}

			if (right != null)
			{
				foreach (string token in Tokens(right))
				{
					if (!TrySplit(token, out string name, out string text, out error)) return false;
					if (!TryResolvePin(name, outputs, "out", "output", out int pin, out error)) return false;
					if (!TryParseValue(text, name, outputs[pin].BitCount, true, out ParsedValue value, out error)) return false;
					if (value.IsDontCare) continue;
					result.Expectations.Add((pin, value.IsFloating ? ExpectKind.Floating : ExpectKind.Value, value.Value));
				}
			}

			vector = result;
			return true;
		}

		static string[] Tokens(string text) => text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

		static bool TrySplit(string token, out string name, out string value, out string error)
		{
			error = null;
			int eq = token.IndexOf('=');
			name = eq > 0 ? token.Substring(0, eq) : null;
			value = eq > 0 ? token.Substring(eq + 1) : null;
			if (name == null || value.Length == 0)
			{
				error = $"expected pin=value, got '{token}'";
				return false;
			}
			return true;
		}

		public static bool TryResolvePin(string name, IReadOnlyList<TruthPin> pins, string prefix, string side, out int index, out string error)
		{
			index = -1;
			error = null;
			int matches = 0;
			for (int i = 0; i < pins.Count; i++)
			{
				if (!string.Equals(pins[i].Name, name, StringComparison.OrdinalIgnoreCase)) continue;
				if (matches == 0) index = i;
				matches++;
			}

			if (matches == 1) return true;
			if (matches > 1)
			{
				index = -1;
				error = $"'{name}' is ambiguous; use {prefix}1/{prefix}2 (position, top to bottom)";
				return false;
			}

			if (name.Length > prefix.Length && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
			    && TryParseDigits(name.Substring(prefix.Length), out ulong position) && position >= 1 && position <= (ulong)pins.Count)
			{
				index = (int)position - 1;
				return true;
			}

			error = $"unknown pin '{name}' ({side} pins are referenced by name or {prefix}1..{prefix}{pins.Count})";
			return false;
		}

		struct ParsedValue
		{
			public uint Value;
			public bool IsDontCare;
			public bool IsFloating;
		}

		static bool TryParseValue(string text, string pinName, int bits, bool isExpectation, out ParsedValue parsed, out string error)
		{
			parsed = default;
			error = null;

			if (isExpectation && text.Length == 1 && (text[0] == 'x' || text[0] == 'X'))
			{
				parsed.IsDontCare = true;
				return true;
			}
			if (isExpectation && text.Length == 1 && (text[0] == 'z' || text[0] == 'Z'))
			{
				parsed.IsFloating = true;
				return true;
			}

			bool negative = text[0] == '-';
			string body = negative ? text.Substring(1) : text;
			ulong magnitude;
			bool ok;
			if (body.StartsWith("0b", StringComparison.OrdinalIgnoreCase)) ok = TryParseRadix(body.Substring(2), 2, true, out magnitude);
			else if (body.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) ok = TryParseRadix(body.Substring(2), 16, false, out magnitude);
			else ok = TryParseDigits(body, out magnitude);

			if (!ok)
			{
				error = $"invalid value '{text}' for {pinName}";
				return false;
			}

			ulong limit = 1UL << bits;
			if (negative)
			{
				if (magnitude > limit / 2)
				{
					error = $"value {text} does not fit in {pinName} ({bits}-bit)";
					return false;
				}
				parsed.Value = (uint)((limit - magnitude) & (limit - 1));
				return true;
			}

			if (magnitude >= limit)
			{
				error = $"value {text} does not fit in {pinName} ({bits}-bit)";
				return false;
			}
			parsed.Value = (uint)magnitude;
			return true;
		}

		static bool TryParseDigits(string text, out ulong value) => TryParseRadix(text, 10, false, out value);

		static bool TryParseRadix(string text, int radix, bool allowUnderscore, out ulong value)
		{
			value = 0;
			bool any = false;
			foreach (char c in text)
			{
				if (allowUnderscore && c == '_') continue;
				int digit = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : 99;
				if (digit >= radix) return false;
				value = value * (ulong)radix + (ulong)digit;
				if (value > 0xFFFFFFFFUL) return false;
				any = true;
			}
			return any;
		}
	}
}
