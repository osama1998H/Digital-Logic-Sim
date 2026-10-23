using System;

namespace DLS.Game.Probes
{
	// Maps probe columns of an old sample set onto a new probe list
	public static class ColumnRemap
	{
		// Result[i] = index in oldKeys of newKeys[i], or -1 if newKeys[i] is new
		public static int[] BuildMap<T>(T[] oldKeys, T[] newKeys, Func<T, T, bool> equals)
		{
			int[] map = new int[newKeys.Length];
			for (int i = 0; i < map.Length; i++)
			{
				map[i] = -1;
				for (int j = 0; j < oldKeys.Length; j++)
				{
					if (equals(oldKeys[j], newKeys[i]))
					{
						map[i] = j;
						break;
					}
				}
			}

			return map;
		}

		public static void RemapRow(uint[] oldRow, int[] map, uint fill, uint[] dst)
		{
			for (int i = 0; i < map.Length; i++) dst[i] = map[i] >= 0 ? oldRow[map[i]] : fill;
		}
	}
}
