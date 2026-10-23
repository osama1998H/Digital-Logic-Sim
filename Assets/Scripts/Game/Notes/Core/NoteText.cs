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
			List<string> kept = new();
			foreach (string line in raw)
			{
				if (kept.Count == MaxLines) break;
				string trimmed = line.TrimEnd();
				if (trimmed.Length > MaxLineLength) trimmed = trimmed.Substring(0, MaxLineLength);
				kept.Add(trimmed);
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
