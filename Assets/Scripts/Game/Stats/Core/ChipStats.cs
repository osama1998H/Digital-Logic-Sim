using System;
using System.Collections.Generic;

namespace DLS.Game.Stats
{
	public enum StatsKind
	{
		Custom,
		Nand,
		Buffer,
		PassThrough,
		Opaque
	}

	// A chip's own pins are (pinId, 0); subchip pins are (subChipId, pinId)
	public readonly struct StatsPin : IEquatable<StatsPin>
	{
		public readonly int OwnerId;
		public readonly int PinId;

		public StatsPin(int ownerId, int pinId)
		{
			OwnerId = ownerId;
			PinId = pinId;
		}

		public bool Equals(StatsPin other) => OwnerId == other.OwnerId && PinId == other.PinId;
		public override bool Equals(object obj) => obj is StatsPin other && Equals(other);
		public override int GetHashCode() => unchecked(OwnerId * 397 ^ PinId);
	}

	public sealed class StatsChipDef
	{
		public string Name;
		public StatsKind Kind;
		public int[] InputPinIds = Array.Empty<int>();
		public int[] OutputPinIds = Array.Empty<int>();
		public (int Id, string ChipName)[] SubChips = Array.Empty<(int, string)>();
		public (StatsPin From, StatsPin To)[] Wires = Array.Empty<(StatsPin, StatsPin)>();
	}

	public interface IStatsLibrary
	{
		// Returns null if the chip does not exist
		StatsChipDef Get(string name);
	}

	public sealed class ChipStatsResult
	{
		public long TotalNands;
		public int? LongestPath;
		public bool HasFeedback;
		public bool HasInputToOutputPath;
		public List<(string Name, long Count, long NandsEach)> Breakdown = new List<(string, long, long)>();
		public List<(string Name, long Count)> OtherParts = new List<(string, long)>();
	}

	public static class ChipStatsCalculator
	{
		sealed class Info
		{
			public long TotalNands;
			public bool HasFeedback;
			public int[,] Delay; // [input index, output index]; -1 = no path
			public readonly Dictionary<string, TypeCount> Types = new Dictionary<string, TypeCount>(StringComparer.OrdinalIgnoreCase);
			public readonly Dictionary<string, OtherCount> Others = new Dictionary<string, OtherCount>(StringComparer.OrdinalIgnoreCase);
		}

		struct TypeCount
		{
			public string Name;
			public long Count;
			public long NandsEach;
		}

		struct OtherCount
		{
			public string Name;
			public long Count;
		}

		sealed class Context
		{
			public IStatsLibrary Library;
			public readonly Dictionary<string, StatsChipDef> Defs = new Dictionary<string, StatsChipDef>(StringComparer.OrdinalIgnoreCase);
			public readonly Dictionary<string, Info> Memo = new Dictionary<string, Info>(StringComparer.OrdinalIgnoreCase);
			public readonly HashSet<string> InProgress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		}

		public static ChipStatsResult Calculate(StatsChipDef root, IStatsLibrary library)
		{
			Context ctx = new Context { Library = library };
			Info info = ComputeCustom(root, ctx);

			ChipStatsResult result = new ChipStatsResult();
			result.TotalNands = info.TotalNands;
			result.HasFeedback = info.HasFeedback;

			if (!info.HasFeedback)
			{
				int longest = 0;
				if (info.Delay != null)
				{
					for (int i = 0; i < info.Delay.GetLength(0); i++)
					{
						for (int j = 0; j < info.Delay.GetLength(1); j++)
						{
							longest = Math.Max(longest, info.Delay[i, j]);
							if (info.Delay[i, j] >= 0) result.HasInputToOutputPath = true;
						}
					}
				}
				result.LongestPath = longest;
			}

			foreach (TypeCount t in info.Types.Values) result.Breakdown.Add((t.Name, t.Count, t.NandsEach));
			result.Breakdown.Sort((a, b) =>
			{
				int c = (b.Count * b.NandsEach).CompareTo(a.Count * a.NandsEach);
				if (c != 0) return c;
				c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
				return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
			});

			foreach (OtherCount o in info.Others.Values) result.OtherParts.Add((o.Name, o.Count));
			result.OtherParts.Sort((a, b) =>
			{
				int c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
				return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
			});

			return result;
		}

		static Info GetInfo(string name, Context ctx, out StatsChipDef def)
		{
			if (!ctx.Defs.TryGetValue(name, out def))
			{
				def = ctx.Library.Get(name);
				ctx.Defs[name] = def;
			}

			if (def == null) return null;
			if (ctx.Memo.TryGetValue(name, out Info known)) return known;

			Info info;
			switch (def.Kind)
			{
				case StatsKind.Custom:
					if (!ctx.InProgress.Add(name)) return null; // recursive definition: treat as unknown
					info = ComputeCustom(def, ctx);
					ctx.InProgress.Remove(name);
					break;
				default:
					info = ComputeLeaf(def);
					break;
			}

			ctx.Memo[name] = info;
			return info;
		}

		static Info ComputeLeaf(StatsChipDef def)
		{
			Info info = new Info();
			int fill = def.Kind == StatsKind.Nand || def.Kind == StatsKind.Buffer ? 1 : def.Kind == StatsKind.PassThrough ? 0 : -1;
			if (def.Kind == StatsKind.Nand) info.TotalNands = 1;
			info.Delay = new int[def.InputPinIds.Length, def.OutputPinIds.Length];
			for (int i = 0; i < info.Delay.GetLength(0); i++)
			{
				for (int j = 0; j < info.Delay.GetLength(1); j++) info.Delay[i, j] = fill;
			}

			return info;
		}

		static Info ComputeCustom(StatsChipDef def, Context ctx)
		{
			Info info = new Info();
			Dictionary<StatsPin, int> nodeIndex = new Dictionary<StatsPin, int>();
			List<List<(int To, int Weight)>> adj = new List<List<(int, int)>>();

			int Node(StatsPin p)
			{
				if (!nodeIndex.TryGetValue(p, out int idx))
				{
					idx = adj.Count;
					nodeIndex[p] = idx;
					adj.Add(new List<(int, int)>());
				}

				return idx;
			}

			foreach (int id in def.InputPinIds) Node(new StatsPin(id, 0));
			foreach (int id in def.OutputPinIds) Node(new StatsPin(id, 0));

			foreach ((int subId, string chipName) in def.SubChips)
			{
				Info sub = GetInfo(chipName, ctx, out StatsChipDef subDef);
				if (subDef == null || sub == null)
				{
					AddOther(info, subDef == null ? "missing: " + chipName : chipName, 1);
					continue;
				}

				foreach (int id in subDef.InputPinIds) Node(new StatsPin(subId, id));
				foreach (int id in subDef.OutputPinIds) Node(new StatsPin(subId, id));

				info.TotalNands += sub.TotalNands;
				if (sub.HasFeedback) info.HasFeedback = true;

				if (subDef.Kind == StatsKind.Custom)
				{
					AddType(info, subDef.Name, 1, sub.TotalNands);
					foreach (TypeCount t in sub.Types.Values) AddType(info, t.Name, t.Count, t.NandsEach);
					foreach (OtherCount o in sub.Others.Values) AddOther(info, o.Name, o.Count);
				}
				else if (subDef.Kind == StatsKind.Buffer || subDef.Kind == StatsKind.Opaque)
				{
					AddOther(info, subDef.Name, 1);
				}

				if (sub.Delay == null) continue;
				for (int i = 0; i < subDef.InputPinIds.Length; i++)
				{
					for (int j = 0; j < subDef.OutputPinIds.Length; j++)
					{
						int d = sub.Delay[i, j];
						if (d < 0) continue;
						int from = Node(new StatsPin(subId, subDef.InputPinIds[i]));
						int to = Node(new StatsPin(subId, subDef.OutputPinIds[j]));
						adj[from].Add((to, d));
					}
				}
			}

			foreach ((StatsPin from, StatsPin to) in def.Wires)
			{
				int a = Node(from);
				int b = Node(to);
				adj[a].Add((b, 0));
			}

			// Topological order (Kahn); leftover nodes mean a cycle
			int n = adj.Count;
			int[] indegree = new int[n];
			for (int u = 0; u < n; u++)
			{
				foreach ((int v, int _) in adj[u]) indegree[v]++;
			}

			int[] order = new int[n];
			int count = 0;
			for (int u = 0; u < n; u++)
			{
				if (indegree[u] == 0) order[count++] = u;
			}

			for (int head = 0; head < count; head++)
			{
				foreach ((int v, int _) in adj[order[head]])
				{
					if (--indegree[v] == 0) order[count++] = v;
				}
			}

			if (count < n) info.HasFeedback = true;

			if (!info.HasFeedback)
			{
				info.Delay = new int[def.InputPinIds.Length, def.OutputPinIds.Length];
				int[] dist = new int[n];
				for (int i = 0; i < def.InputPinIds.Length; i++)
				{
					Array.Fill(dist, -1);
					dist[nodeIndex[new StatsPin(def.InputPinIds[i], 0)]] = 0;
					for (int k = 0; k < n; k++)
					{
						int u = order[k];
						if (dist[u] < 0) continue;
						foreach ((int v, int w) in adj[u]) dist[v] = Math.Max(dist[v], dist[u] + w);
					}

					for (int j = 0; j < def.OutputPinIds.Length; j++) info.Delay[i, j] = dist[nodeIndex[new StatsPin(def.OutputPinIds[j], 0)]];
				}
			}

			return info;
		}

		static void AddType(Info info, string name, long count, long nandsEach)
		{
			info.Types.TryGetValue(name, out TypeCount t);
			if (t.Name == null) t.Name = name;
			t.Count += count;
			t.NandsEach = nandsEach;
			info.Types[name] = t;
		}

		static void AddOther(Info info, string name, long count)
		{
			info.Others.TryGetValue(name, out OtherCount o);
			if (o.Name == null) o.Name = name;
			o.Count += count;
			info.Others[name] = o;
		}
	}
}
