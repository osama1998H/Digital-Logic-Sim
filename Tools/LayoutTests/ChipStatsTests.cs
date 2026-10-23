using System;
using System.Collections.Generic;
using System.Linq;
using DLS.Game.Stats;
using NUnit.Framework;

namespace LayoutTests
{
	public class ChipStatsTests
	{
		class TestLib : IStatsLibrary
		{
			readonly Dictionary<string, StatsChipDef> defs = new Dictionary<string, StatsChipDef>(StringComparer.OrdinalIgnoreCase);
			public readonly Dictionary<string, int> Calls = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

			public TestLib()
			{
				Add(Nand());
			}

			public TestLib Add(StatsChipDef d)
			{
				defs[d.Name] = d;
				return this;
			}

			public StatsChipDef Get(string name)
			{
				Calls[name] = Calls.TryGetValue(name, out int c) ? c + 1 : 1;
				return defs.TryGetValue(name, out StatsChipDef d) ? d : null;
			}
		}

		static StatsPin P(int owner, int pin) => new StatsPin(owner, pin);
		static (StatsPin, StatsPin) W(StatsPin a, StatsPin b) => (a, b);

		static StatsChipDef Def(string name, StatsKind kind, int[] ins, int[] outs, (int, string)[] subs, params (StatsPin, StatsPin)[] wires)
		{
			return new StatsChipDef
			{
				Name = name,
				Kind = kind,
				InputPinIds = ins,
				OutputPinIds = outs,
				SubChips = subs,
				Wires = wires.Select(w => (w.Item1, w.Item2)).ToArray()
			};
		}

		static StatsChipDef Nand() => Def("NAND", StatsKind.Nand, new[] { 0, 1 }, new[] { 2 }, new (int, string)[0]);

		// NAND(a, a) with a single NAND subchip id n; wires from source into both inputs
		static StatsChipDef And() => Def("AND", StatsKind.Custom, new[] { 0, 1 }, new[] { 2 },
			new[] { (10, "NAND"), (11, "NAND") },
			W(P(0, 0), P(10, 0)), W(P(1, 0), P(10, 1)),
			W(P(10, 2), P(11, 0)), W(P(10, 2), P(11, 1)),
			W(P(11, 2), P(2, 0)));

		static StatsChipDef Xor() => Def("XOR", StatsKind.Custom, new[] { 0, 1 }, new[] { 2 },
			new[] { (10, "NAND"), (11, "NAND"), (12, "NAND"), (13, "NAND") },
			W(P(0, 0), P(10, 0)), W(P(1, 0), P(10, 1)),
			W(P(0, 0), P(11, 0)), W(P(10, 2), P(11, 1)),
			W(P(1, 0), P(12, 1)), W(P(10, 2), P(12, 0)),
			W(P(11, 2), P(13, 0)), W(P(12, 2), P(13, 1)),
			W(P(13, 2), P(2, 0)));

		static StatsChipDef SrLatch(string name = "SR") => Def(name, StatsKind.Custom, new[] { 0, 1 }, new[] { 2 },
			new[] { (10, "NAND"), (11, "NAND") },
			W(P(0, 0), P(10, 0)), W(P(1, 0), P(11, 1)),
			W(P(10, 2), P(11, 0)), W(P(11, 2), P(10, 1)),
			W(P(10, 2), P(2, 0)));

		[Test]
		public void Nand_IsOneGateDepthOne()
		{
			var lib = new TestLib();
			var root = Def("Top", StatsKind.Custom, new[] { 0, 1 }, new[] { 2 }, new[] { (10, "NAND") },
				W(P(0, 0), P(10, 0)), W(P(1, 0), P(10, 1)), W(P(10, 2), P(2, 0)));
			ChipStatsResult r = ChipStatsCalculator.Calculate(root, lib);
			Assert.That(r.TotalNands, Is.EqualTo(1));
			Assert.That(r.LongestPath, Is.EqualTo(1));
			Assert.That(r.HasFeedback, Is.False);
			Assert.That(r.Breakdown, Is.Empty);
			Assert.That(r.OtherParts, Is.Empty);
		}

		[Test]
		public void And_FromNandAndNot()
		{
			ChipStatsResult r = ChipStatsCalculator.Calculate(And(), new TestLib());
			Assert.That(r.TotalNands, Is.EqualTo(2));
			Assert.That(r.LongestPath, Is.EqualTo(2));
		}

		[Test]
		public void Xor_FourNands_DepthThree()
		{
			ChipStatsResult r = ChipStatsCalculator.Calculate(Xor(), new TestLib());
			Assert.That(r.TotalNands, Is.EqualTo(4));
			Assert.That(r.LongestPath, Is.EqualTo(3));
		}

		[Test]
		public void ParallelGates_DepthIsMax()
		{
			var lib = new TestLib().Add(And());
			var wires = new List<(StatsPin, StatsPin)>();
			var ins = new List<int>();
			var outs = new List<int>();
			var subs = new List<(int, string)>();
			for (int i = 0; i < 3; i++)
			{
				int id = 20 + i;
				subs.Add((id, "AND"));
				ins.Add(100 + i * 2); ins.Add(101 + i * 2); outs.Add(200 + i);
				wires.Add(W(P(100 + i * 2, 0), P(id, 0)));
				wires.Add(W(P(101 + i * 2, 0), P(id, 1)));
				wires.Add(W(P(id, 2), P(200 + i, 0)));
			}
			var root = Def("Top", StatsKind.Custom, ins.ToArray(), outs.ToArray(), subs.ToArray(), wires.ToArray());
			ChipStatsResult r = ChipStatsCalculator.Calculate(root, lib);
			Assert.That(r.TotalNands, Is.EqualTo(6));
			Assert.That(r.LongestPath, Is.EqualTo(2));
		}

		[Test]
		public void RippleChain_DepthAdds()
		{
			var lib = new TestLib().Add(And());
			var root = Def("Top", StatsKind.Custom, new[] { 0, 1, 3 }, new[] { 2 }, new[] { (20, "AND"), (21, "AND") },
				W(P(0, 0), P(20, 0)), W(P(1, 0), P(20, 1)),
				W(P(20, 2), P(21, 0)), W(P(3, 0), P(21, 1)),
				W(P(21, 2), P(2, 0)));
			ChipStatsResult r = ChipStatsCalculator.Calculate(root, lib);
			Assert.That(r.TotalNands, Is.EqualTo(4));
			Assert.That(r.LongestPath, Is.EqualTo(4));
		}

		[Test]
		public void PassThroughSplitMerge_AddsZero()
		{
			var pt = Def("SPLIT", StatsKind.PassThrough, new[] { 0 }, new[] { 1, 2 }, new (int, string)[0]);
			var lib = new TestLib().Add(pt);
			var root = Def("Top", StatsKind.Custom, new[] { 0 }, new[] { 1 }, new[] { (20, "SPLIT"), (21, "NAND"), (22, "SPLIT") },
				W(P(0, 0), P(20, 0)),
				W(P(20, 1), P(21, 0)), W(P(20, 2), P(21, 1)),
				W(P(21, 2), P(22, 0)),
				W(P(22, 1), P(1, 0)));
			ChipStatsResult r = ChipStatsCalculator.Calculate(root, lib);
			Assert.That(r.TotalNands, Is.EqualTo(1));
			Assert.That(r.LongestPath, Is.EqualTo(1));
			Assert.That(r.Breakdown, Is.Empty);
			Assert.That(r.OtherParts, Is.Empty);
		}

		[Test]
		public void SrLatch_HasFeedback()
		{
			ChipStatsResult r = ChipStatsCalculator.Calculate(SrLatch(), new TestLib());
			Assert.That(r.HasFeedback, Is.True);
			Assert.That(r.LongestPath, Is.Null);
			Assert.That(r.TotalNands, Is.EqualTo(2));
		}

		[Test]
		public void FeedbackInsideSubChip_PropagatesToParent()
		{
			var lib = new TestLib().Add(SrLatch());
			var root = Def("Top", StatsKind.Custom, new[] { 0, 1 }, new[] { 2 }, new[] { (20, "SR") },
				W(P(0, 0), P(20, 0)), W(P(1, 0), P(20, 1)), W(P(20, 2), P(2, 0)));
			ChipStatsResult r = ChipStatsCalculator.Calculate(root, lib);
			Assert.That(r.HasFeedback, Is.True);
			Assert.That(r.LongestPath, Is.Null);
			Assert.That(r.TotalNands, Is.EqualTo(2));
		}

		[Test]
		public void OpaqueParts_ListedNotCounted()
		{
			var clock = Def("CLOCK", StatsKind.Opaque, new int[0], new[] { 0 }, new (int, string)[0]);
			var lib = new TestLib().Add(clock);
			var root = Def("Top", StatsKind.Custom, new int[0], new[] { 1 }, new[] { (20, "CLOCK") },
				W(P(20, 0), P(1, 0)));
			ChipStatsResult r = ChipStatsCalculator.Calculate(root, lib);
			Assert.That(r.OtherParts.ToArray(), Is.EqualTo(new[] { ("CLOCK", 1L) }));
			Assert.That(r.TotalNands, Is.EqualTo(0));
			Assert.That(r.LongestPath, Is.EqualTo(0));
			Assert.That(r.Breakdown, Is.Empty);
		}

		[Test]
		public void Breakdown_SortedByTotalNands()
		{
			var lib = new TestLib().Add(And()).Add(Xor());
			var root = Def("Top", StatsKind.Custom, new int[0], new int[0],
				new[] { (20, "XOR"), (21, "AND"), (22, "AND"), (23, "AND") });
			ChipStatsResult r = ChipStatsCalculator.Calculate(root, lib);
			Assert.That(r.TotalNands, Is.EqualTo(10));
			Assert.That(r.Breakdown.ToArray(), Is.EqualTo(new[] { ("AND", 3L, 2L), ("XOR", 1L, 4L) }));
		}

		[Test]
		public void Breakdown_TieBrokenByName_AndCountsNestedInstances()
		{
			var zed = Def("Zed", StatsKind.Custom, new int[0], new int[0], new[] { (10, "NAND"), (11, "NAND") });
			var abe = Def("abe", StatsKind.Custom, new int[0], new int[0], new[] { (10, "NAND"), (11, "NAND") });
			var wrap = Def("Wrap", StatsKind.Custom, new int[0], new int[0], new[] { (10, "Zed"), (11, "abe") });
			var lib = new TestLib().Add(zed).Add(abe).Add(wrap);
			var root = Def("Top", StatsKind.Custom, new int[0], new int[0], new[] { (20, "Wrap"), (21, "Wrap") });
			ChipStatsResult r = ChipStatsCalculator.Calculate(root, lib);
			Assert.That(r.TotalNands, Is.EqualTo(8));
			Assert.That(r.Breakdown.ToArray(), Is.EqualTo(new[] { ("Wrap", 2L, 4L), ("abe", 2L, 2L), ("Zed", 2L, 2L) }));
		}

		[Test]
		public void NameLookupIsCaseInsensitive()
		{
			var lib = new TestLib().Add(And());
			var root = Def("Top", StatsKind.Custom, new int[0], new int[0], new[] { (20, "and"), (21, "AND") });
			ChipStatsResult r = ChipStatsCalculator.Calculate(root, lib);
			Assert.That(r.TotalNands, Is.EqualTo(4));
			Assert.That(r.Breakdown.Count, Is.EqualTo(1));
			Assert.That(r.Breakdown[0].Count, Is.EqualTo(2));
			Assert.That(lib.Calls["and"], Is.EqualTo(1));
		}

		[Test]
		public void DeepNesting_IsMemoised()
		{
			var lib = new TestLib();
			string prev = "NAND";
			for (int level = 1; level <= 7; level++)
			{
				string name = "L" + level;
				lib.Add(Def(name, StatsKind.Custom, new int[0], new int[0], new[] { (1, prev), (2, prev) }));
				prev = name;
			}
			var root = Def("L8", StatsKind.Custom, new int[0], new int[0], new[] { (1, prev), (2, prev) });
			ChipStatsResult r = ChipStatsCalculator.Calculate(root, lib);
			Assert.That(r.TotalNands, Is.EqualTo(256));
			Assert.That(lib.Calls.Values.All(c => c <= 1), Is.True);
			Assert.That(r.Breakdown.Count, Is.EqualTo(7));
			Assert.That(r.Breakdown.Single(b => b.Name == "L1").Count, Is.EqualTo(128));
		}

		[Test]
		public void MissingChip_IsOpaque()
		{
			var root = Def("Top", StatsKind.Custom, new int[0], new int[0], new[] { (20, "GHOST") });
			ChipStatsResult r = ChipStatsCalculator.Calculate(root, new TestLib());
			Assert.That(r.TotalNands, Is.EqualTo(0));
			Assert.That(r.OtherParts.ToArray(), Is.EqualTo(new[] { ("missing: GHOST", 1L) }));
		}

		[Test]
		public void HasInputToOutputPath_FalseWhenOnlyOpaqueParts()
		{
			var opaque = Def("DISP", StatsKind.Opaque, new[] { 0 }, new[] { 1 }, new (int, string)[0]);
			var lib = new TestLib().Add(opaque);
			var root = Def("Top", StatsKind.Custom, new[] { 0 }, new[] { 1 }, new[] { (20, "DISP"), (21, "DISP") },
				W(P(0, 0), P(20, 0)), W(P(21, 1), P(1, 0)));
			Assert.That(ChipStatsCalculator.Calculate(root, lib).HasInputToOutputPath, Is.False);
			Assert.That(ChipStatsCalculator.Calculate(And(), new TestLib().Add(Nand())).HasInputToOutputPath, Is.True);
		}

		[Test]
		public void InputWiredToOutput_DepthZero()
		{
			var root = Def("Top", StatsKind.Custom, new[] { 0 }, new[] { 1 }, new (int, string)[0], W(P(0, 0), P(1, 0)));
			ChipStatsResult r = ChipStatsCalculator.Calculate(root, new TestLib());
			Assert.That(r.LongestPath, Is.EqualTo(0));
			Assert.That(r.HasFeedback, Is.False);
		}

		[Test]
		public void BufferParts_ListedInOtherParts_DelayOne()
		{
			var buf = Def("TRI-STATE BUFFER", StatsKind.Buffer, new[] { 0, 1 }, new[] { 2 }, new (int, string)[0]);
			var lib = new TestLib().Add(buf);
			var root = Def("Top", StatsKind.Custom, new[] { 0, 1 }, new[] { 2 }, new[] { (20, "TRI-STATE BUFFER") },
				W(P(0, 0), P(20, 0)), W(P(1, 0), P(20, 1)), W(P(20, 2), P(2, 0)));
			ChipStatsResult r = ChipStatsCalculator.Calculate(root, lib);
			Assert.That(r.LongestPath, Is.EqualTo(1));
			Assert.That(r.TotalNands, Is.EqualTo(0));
			Assert.That(r.OtherParts.ToArray(), Is.EqualTo(new[] { ("TRI-STATE BUFFER", 1L) }));
			Assert.That(r.Breakdown, Is.Empty);
		}
	}
}
