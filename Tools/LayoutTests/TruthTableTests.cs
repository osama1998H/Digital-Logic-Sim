using System;
using DLS.Game.Inspect;
using DLS.Game.Logic;
using NUnit.Framework;

namespace LayoutTests
{
	public class TruthTableTests
	{
		[Test] public void TwoOneBitInputs_TopPinIsMostSignificant()
		{
			var t = new TruthTable(new[] { new TruthPin("A", 1), new TruthPin("B", 1) }, new[] { new TruthPin("Y", 1) });
			Assert.That(t.RowCount, Is.EqualTo(4));
			Assert.That(t.GetInputValue(2, 0), Is.EqualTo(1u)); // row 2 = A1 B0
			Assert.That(t.GetInputValue(2, 1), Is.EqualTo(0u));
		}
		[Test] public void MixedWidths_SplitRowIntoPinValues()
		{
			var t = new TruthTable(new[] { new TruthPin("A", 4), new TruthPin("B", 1) }, new TruthPin[0]);
			Assert.That(t.GetInputValue(0b10011, 0), Is.EqualTo(9u));
			Assert.That(t.GetInputValue(0b10011, 1), Is.EqualTo(1u));
		}
		[Test] public void TruthTable_NoInputs_HasOneRow() => Assert.That(new TruthTable(new TruthPin[0], new[] { new TruthPin("Y", 1) }).RowCount, Is.EqualTo(1));
		[Test] public void HalfAdder_Csv()
		{
			var t = new TruthTable(new[] { new TruthPin("A", 1), new TruthPin("B", 1) }, new[] { new TruthPin("S", 1), new TruthPin("C", 1) });
			uint[][] outs = { new[] { 0u, 0u }, new[] { 1u, 0u }, new[] { 1u, 0u }, new[] { 0u, 1u } };
			for (int r = 0; r < 4; r++) t.SetOutputStates(r, outs[r]);
			Assert.That(TruthTableCsv.ToCsv(t, NumberFormat.Binary), Is.EqualTo("A,B,S,C\n0,0,0,0\n0,1,1,0\n1,0,1,0\n1,1,0,1"));
		}
		[Test] public void FloatingOutput_ShowsZ()
		{
			var t = new TruthTable(new TruthPin[0], new[] { new TruthPin("Y", 1) });
			t.SetOutputStates(0, new[] { 1u << 16 });
			Assert.That(TruthTableCsv.ToCsv(t, NumberFormat.Binary), Is.EqualTo("Y\nZ"));
		}
		[Test] public void RowsFilled_AdvancesInOrder()
		{
			var t = new TruthTable(new[] { new TruthPin("A", 1) }, new[] { new TruthPin("Y", 1) });
			Assert.That(t.RowsFilled, Is.EqualTo(0));
			t.SetOutputStates(0, new[] { 1u });
			t.SetOutputStates(1, new[] { 0u });
			Assert.That(t.RowsFilled, Is.EqualTo(2));
			for (int r = 0; r < t.RowsFilled; r++)
				Assert.That(t.GetOutputState(r, 0), Is.EqualTo(r == 0 ? 1u : 0u));
		}
		[Test] public void OutOfOrderFill_Throws()
		{
			var t = new TruthTable(new[] { new TruthPin("A", 1) }, new[] { new TruthPin("Y", 1) });
			Assert.Throws<InvalidOperationException>(() => t.SetOutputStates(1, new[] { 1u }));
			t.SetOutputStates(0, new[] { 1u });
			Assert.Throws<InvalidOperationException>(() => t.SetOutputStates(0, new[] { 0u }));
			Assert.That(t.RowsFilled, Is.EqualTo(1));
		}
		[Test] public void ShortOutputStates_Throws()
		{
			var t = new TruthTable(new TruthPin[0], new[] { new TruthPin("A", 1), new TruthPin("B", 1) });
			Assert.Throws<ArgumentException>(() => t.SetOutputStates(0, new[] { 1u }));
		}
		[Test] public void TooManyInputBits_Throws() =>
			Assert.Throws<ArgumentException>(() => new TruthTable(new[] { new TruthPin("A", 9), new TruthPin("B", 8) }, new TruthPin[0]));
	}
}
