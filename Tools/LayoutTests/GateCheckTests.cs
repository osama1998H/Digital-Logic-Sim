using DLS.Game.Logic;
using NUnit.Framework;

namespace LayoutTests
{
	public class GateCheckTests
	{
		static readonly GateKind[] Kinds = { GateKind.Not, GateKind.And, GateKind.Or, GateKind.Xor };

		static uint[] Expected(GateKind kind) => kind switch
		{
			GateKind.Not => new uint[] { 1, 0 },
			GateKind.And => new uint[] { 0, 0, 0, 1 },
			GateKind.Or => new uint[] { 0, 1, 1, 1 },
			_ => new uint[] { 0, 1, 1, 0 }
		};

		static TruthTable Table(uint[] outputs, int inputs, int inputBits = 1, int outputBits = 1, int outputCount = 1, int rowsToFill = -1)
		{
			TruthPin[] ins = new TruthPin[inputs];
			for (int i = 0; i < inputs; i++) ins[i] = new TruthPin("IN" + i, inputBits);
			TruthPin[] outs = new TruthPin[outputCount];
			for (int i = 0; i < outputCount; i++) outs[i] = new TruthPin("OUT" + i, outputBits);
			var t = new TruthTable(ins, outs);
			int fill = rowsToFill < 0 ? t.RowCount : rowsToFill;
			for (int r = 0; r < fill; r++)
			{
				uint[] row = new uint[outputCount];
				for (int o = 0; o < outputCount; o++) row[o] = r < outputs.Length ? outputs[r] : 0;
				t.SetOutputStates(r, row);
			}
			return t;
		}

		static TruthTable TableFor(GateKind kind) => Table(Expected(kind), kind == GateKind.Not ? 1 : 2);

		[Test] public void EachKind_MatchesOnlyItsOwnTable()
		{
			foreach (GateKind tableKind in Kinds)
			{
				foreach (GateKind checkKind in Kinds)
				{
					Assert.That(GateCheck.Matches(checkKind, TableFor(tableKind)), Is.EqualTo(tableKind == checkKind), $"{checkKind} vs {tableKind} table");
				}
			}
		}

		[Test] public void Nand_IsNotAUserGate() => Assert.That(GateCheck.Matches(GateKind.Nand, Table(new uint[] { 1, 1, 1, 0 }, 2)), Is.False);

		// Row 1 is IN0=0, IN1=1 (top pin most significant): an "A AND NOT B" chip differs from AND only there and in row 2
		[Test] public void AsymmetricTable_IsRejected() => Assert.That(GateCheck.Matches(GateKind.Or, Table(new uint[] { 0, 0, 1, 1 }, 2)), Is.False);

		[Test] public void WrongInputCount_IsRejected()
		{
			Assert.That(GateCheck.Matches(GateKind.And, Table(new uint[] { 0, 0, 0, 0, 0, 0, 0, 1 }, 3)), Is.False);
			Assert.That(GateCheck.Matches(GateKind.Not, Table(new uint[] { 1, 0, 0, 0 }, 2)), Is.False);
			Assert.That(GateCheck.Matches(GateKind.Xor, Table(new uint[] { 0, 1 }, 1)), Is.False);
		}

		[Test] public void MultiBitPins_AreRejected()
		{
			Assert.That(GateCheck.Matches(GateKind.Not, Table(new uint[16], 1, inputBits: 4)), Is.False);
			Assert.That(GateCheck.Matches(GateKind.Not, Table(new uint[] { 1, 0 }, 1, outputBits: 4)), Is.False);
		}

		[Test] public void ExtraOutput_IsRejected() => Assert.That(GateCheck.Matches(GateKind.Not, Table(new uint[] { 1, 0 }, 1, outputCount: 2)), Is.False);

		[Test] public void FloatingOutput_IsRejected()
		{
			const uint floatingHigh = 1 | (1u << 16);
			Assert.That(GateCheck.Matches(GateKind.Not, Table(new[] { floatingHigh, 0u }, 1)), Is.False);
		}

		[Test] public void IncompleteTable_IsRejected() => Assert.That(GateCheck.Matches(GateKind.Not, Table(new uint[] { 1, 0 }, 1, rowsToFill: 1)), Is.False);

		[Test] public void Shape_ChecksCountsAndWidths()
		{
			Assert.That(GateCheck.HasShape(GateKind.Not, new[] { 1 }, new[] { 1 }), Is.True);
			Assert.That(GateCheck.HasShape(GateKind.And, new[] { 1, 1 }, new[] { 1 }), Is.True);
			Assert.That(GateCheck.HasShape(GateKind.And, new[] { 1 }, new[] { 1 }), Is.False);
			Assert.That(GateCheck.HasShape(GateKind.Or, new[] { 1, 8 }, new[] { 1 }), Is.False);
			Assert.That(GateCheck.HasShape(GateKind.Xor, new[] { 1, 1 }, new[] { 1, 1 }), Is.False);
			Assert.That(GateCheck.HasShape(GateKind.Nand, new[] { 1, 1 }, new[] { 1 }), Is.False);
		}

		[Test] public void ChipNames() => Assert.That(new[] { GateCheck.ChipName(GateKind.Not), GateCheck.ChipName(GateKind.And), GateCheck.ChipName(GateKind.Or), GateCheck.ChipName(GateKind.Xor), GateCheck.ChipName(GateKind.Nand) },
			Is.EqualTo(new[] { "NOT", "AND", "OR", "XOR", "NAND" }));

		[Test] public void Summary_FullAdderWithAllGates_ListsKindsInOrderOfFirstUse()
		{
			Netlist n = NetlistBuilder.Build(ExprParser.Parse("S = A ^ B ^ CIN; COUT = A*B + CIN*(A ^ B)"), GateSet.Not | GateSet.And | GateSet.Or | GateSet.Xor);
			Assert.That(NetlistSummary.Format(n), Is.EqualTo("Inputs: A, B, CIN | Outputs: S, COUT | Gates: 2 XOR, 2 AND, 1 OR"));
		}

		[Test] public void Summary_NandOnly()
		{
			Netlist n = NetlistBuilder.Build(ExprParser.Parse("Y = A*B"), GateSet.None);
			Assert.That(NetlistSummary.Format(n), Is.EqualTo("Inputs: A, B | Outputs: Y | Gates: 2 NAND"));
		}

		[Test] public void Summary_NoGates() => Assert.That(NetlistSummary.Format(NetlistBuilder.Build(ExprParser.Parse("Y = A"), GateSet.None)), Is.EqualTo("Inputs: A | Outputs: Y | Gates: none"));
	}
}
