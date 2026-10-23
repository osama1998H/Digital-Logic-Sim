using System;
using System.Collections.Generic;

namespace DLS.Game.Logic
{
	// Mask bit 1 = variable appears in the term. Variable 0 is the most significant bit of a minterm index.
	public readonly struct Implicant
	{
		public readonly uint Value;
		public readonly uint Mask;

		public Implicant(uint value, uint mask)
		{
			Value = value & mask;
			Mask = mask;
		}

		public bool Covers(int minterm) => ((uint)minterm & Mask) == Value;

		public int LiteralCount
		{
			get
			{
				int n = 0;
				for (uint m = Mask; m != 0; m &= m - 1) n++;
				return n;
			}
		}
	}

	public static class LogicMinimizer
	{
		public const int MaxVariables = 8;

		public static List<Implicant> Minimize(int variableCount, IReadOnlyCollection<int> minterms, IReadOnlyCollection<int> dontCares)
		{
			if (variableCount < 0 || variableCount > MaxVariables) throw new ArgumentOutOfRangeException(nameof(variableCount));
			uint full = variableCount == 0 ? 0u : (1u << variableCount) - 1;

			SortedSet<int> on = new(minterms);
			SortedSet<int> all = new(on);
			if (dontCares != null) all.UnionWith(dontCares);
			all.RemoveWhere(m => m < 0 || m > full);
			on.RemoveWhere(m => m < 0 || m > full);

			List<Implicant> result = new();
			if (on.Count == 0) return result;

			List<Implicant> primes = FindPrimes(all, full);
			List<int> targets = new(on);
			List<Implicant> chosen = new();
			HashSet<int> covered = new();

			// Essential primes
			foreach (int m in targets)
			{
				int count = 0;
				Implicant only = default;
				foreach (Implicant p in primes)
				{
					if (!p.Covers(m)) continue;
					count++;
					only = p;
				}
				if (count == 1 && !chosen.Contains(only)) chosen.Add(only);
			}
			foreach (Implicant p in chosen) MarkCovered(p, targets, covered);

			// Greedy remainder
			while (covered.Count < targets.Count)
			{
				bool found = false;
				Implicant best = default;
				int bestGain = 0;
				foreach (Implicant p in primes)
				{
					int gain = 0;
					foreach (int m in targets) if (!covered.Contains(m) && p.Covers(m)) gain++;
					if (gain == 0) continue;
					bool better = !found || gain > bestGain;
					if (!better && gain == bestGain)
					{
						int lc = p.LiteralCount, bc = best.LiteralCount;
						better = lc < bc || (lc == bc && (p.Mask < best.Mask || (p.Mask == best.Mask && p.Value < best.Value)));
					}
					if (better)
					{
						found = true;
						best = p;
						bestGain = gain;
					}
				}
				chosen.Add(best);
				MarkCovered(best, targets, covered);
			}

			return chosen;
		}

		static void MarkCovered(Implicant p, List<int> targets, HashSet<int> covered)
		{
			foreach (int m in targets) if (p.Covers(m)) covered.Add(m);
		}

		static List<Implicant> FindPrimes(SortedSet<int> cells, uint full)
		{
			List<Implicant> primes = new();
			List<Implicant> current = new();
			foreach (int m in cells) current.Add(new Implicant((uint)m, full));

			while (current.Count > 0)
			{
				bool[] merged = new bool[current.Count];
				SortedSet<(uint mask, uint value)> next = new();
				for (int i = 0; i < current.Count; i++)
				{
					for (int j = i + 1; j < current.Count; j++)
					{
						if (current[i].Mask != current[j].Mask) continue;
						uint diff = current[i].Value ^ current[j].Value;
						if (diff == 0 || (diff & (diff - 1)) != 0) continue;
						merged[i] = merged[j] = true;
						uint mask = current[i].Mask & ~diff;
						next.Add((mask, current[i].Value & mask));
					}
				}
				for (int i = 0; i < current.Count; i++) if (!merged[i]) primes.Add(current[i]);
				current = new List<Implicant>();
				foreach ((uint mask, uint value) in next) current.Add(new Implicant(value, mask));
			}
			return primes;
		}
	}
}
