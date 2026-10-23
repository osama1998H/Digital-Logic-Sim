using System;
using System.Collections.Generic;

namespace DLS.Game.Notes
{
	// Engine-free text rules for canvas notes: keeps stored text bounded so a note can't grow without limit
	public static class NoteText
	{
		public const int MaxLines = 12;
		public const int MaxLineLength = 40;
		const int EmptyMeasureLength = 8;

		public static string Normalize(string text)
		{
			if (string.IsNullOrEmpty(text)) return "";

			string unified = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\t', ' ');
			string[] raw = unified.Split('\n');
			// Clamp in this order so one pass is already stable: a second Normalize must not change the result
			int lineCount = Math.Min(raw.Length, MaxLines);
			List<string> kept = new(lineCount);
			for (int i = 0; i < lineCount; i++)
			{
				string line = raw[i];
				if (line.Length > MaxLineLength)
				{
					int cut = MaxLineLength;
					// Don't leave a lone high surrogate at the end of the line
					if (char.IsHighSurrogate(line[cut - 1])) cut--;
					line = line.Substring(0, cut);
				}
				kept.Add(line.TrimEnd());
			}

			int count = kept.Count;
			while (count > 0 && kept[count - 1].Length == 0) count--;
			return count == 0 ? "" : string.Join("\n", kept.GetRange(0, count));
		}

		public static string[] Lines(string text)
		{
			string normalized = Normalize(text);
			return normalized.Length == 0 ? new string[0] : normalized.Split('\n');
		}

		public static (float width, float height) Measure(string text, float charWidth, float lineHeight, float padding)
		{
			string[] lines = Lines(text);
			int longest = EmptyMeasureLength;
			int count = 1;
			if (lines.Length > 0)
			{
				longest = 0;
				foreach (string line in lines) longest = Math.Max(longest, line.Length);
				count = lines.Length;
			}
			float width = longest * charWidth + 2f * padding;
			float height = count * lineHeight + 2f * padding;
			return (width, height);
		}
	}
}
