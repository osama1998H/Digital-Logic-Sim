using System;
using System.Collections.Generic;

namespace DLS.Game.Editing
{
	public static class SequenceNaming
	{
		// Order in which a sequence of names (A0, A1, ...) is assigned: from the bottom up, and right to left within a row.
		// This puts the highest index on top, matching the convention used by multi-bit pins and the split/merge chips (most significant bit on top).
		// For pins (rowTolerance = 0) this is exactly the reverse of the order in which the chip's pins are saved (y descending, then x ascending).
		public static int[] OrderForSequence(IReadOnlyList<PositionedItem> items, float rowTolerance)
		{
			List<PositionedItem> sorted = new(items);
			sorted.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.ID.CompareTo(b.ID));

			List<int> order = new();
			int rowStart = 0;
			while (rowStart < sorted.Count)
			{
				int rowEnd = rowStart + 1;
				while (rowEnd < sorted.Count && sorted[rowEnd].Y - sorted[rowStart].Y <= rowTolerance) rowEnd++;

				List<PositionedItem> row = sorted.GetRange(rowStart, rowEnd - rowStart);
				row.Sort((a, b) => a.X != b.X ? b.X.CompareTo(a.X) : a.ID.CompareTo(b.ID));
				foreach (PositionedItem item in row) order.Add(item.ID);
				rowStart = rowEnd;
			}

			return order.ToArray();
		}

		public static string[] Names(string baseName, int count, int startIndex = 0)
		{
			string[] names = new string[count];
			for (int i = 0; i < count; i++) names[i] = baseName + (startIndex + i);
			return names;
		}

		public static bool Fits(string baseName, int count, int maxLength, int startIndex = 0)
		{
			if (count <= 0) return true;
			int largestIndex = startIndex + count - 1;
			return baseName.Length + largestIndex.ToString().Length <= maxLength;
		}
	}
}
