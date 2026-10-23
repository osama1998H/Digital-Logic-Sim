using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace DLS.Game.Logic
{
	public static class SopFormatter
	{
		static readonly Regex ValidName = new("^[A-Za-z_][A-Za-z0-9_]*$");

		public static string Format(IReadOnlyList<Implicant> terms, string[] variableNames)
		{
			if (terms.Count == 0) return "0";
			int n = variableNames.Length;
			List<(int literals, string text)> items = new();
			foreach (Implicant t in terms)
			{
				if (t.Mask == 0) return "1";
				StringBuilder sb = new();
				int literals = 0;
				for (int v = 0; v < n; v++)
				{
					uint bit = 1u << (n - 1 - v);
					if ((t.Mask & bit) == 0) continue;
					if (literals++ > 0) sb.Append('*');
					sb.Append(variableNames[v]);
					if ((t.Value & bit) == 0) sb.Append('\'');
				}
				items.Add((literals, sb.ToString()));
			}
			items.Sort((a, b) => a.literals != b.literals ? a.literals.CompareTo(b.literals) : string.CompareOrdinal(a.text, b.text));
			return string.Join(" + ", items.ConvertAll(i => i.text));
		}

		public static string[] MakeVariableNames(string[] pinNames)
		{
			bool ok = true;
			HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
			foreach (string name in pinNames)
			{
				if (name == null || !ValidName.IsMatch(name) || !seen.Add(name))
				{
					ok = false;
					break;
				}
			}
			if (ok) return (string[])pinNames.Clone();

			string[] letters = new string[pinNames.Length];
			for (int i = 0; i < letters.Length; i++) letters[i] = i < 26 ? ((char)('A' + i)).ToString() : "V" + i;
			return letters;
		}
	}
}
