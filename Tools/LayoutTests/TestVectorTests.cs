using DLS.Game.Logic;
using NUnit.Framework;

namespace LayoutTests
{
	public class TestVectorTests
	{
		static readonly TruthPin[] Ins = { new("A", 4), new("B", 4) };
		static readonly TruthPin[] Outs = { new("SUM", 5) };
		[Test] public void ParsesAssignmentsAndExpectations()
		{
			Assert.That(TestVectorParser.TryParse("A=5 B=0x3 -> SUM=8", 0, Ins, Outs, out var v, out _), Is.True);
			Assert.That(v.Inputs, Is.EqualTo(new[] { (0, 5u), (1, 3u) }));
			Assert.That(v.Expectations[0], Is.EqualTo((0, ExpectKind.Value, 8u)));
		}
		[Test] public void CommentAndBlank_ReturnNull()
		{
			Assert.That(TestVectorParser.TryParse("  # setup", 0, Ins, Outs, out var v, out _), Is.True);
			Assert.That(v, Is.Null);
			Assert.That(TestVectorParser.TryParse("   ", 0, Ins, Outs, out v, out _), Is.True);
			Assert.That(v, Is.Null);
		}
		[Test] public void SetupLine_HasNoCheck()
		{
			TestVectorParser.TryParse("A=1", 0, Ins, Outs, out var v, out _);
			Assert.That(v.HasCheck, Is.False);
		}
		[Test] public void NegativeAndBinaryValues()
		{
			TestVectorParser.TryParse("A=-1 B=0b0_1_1", 0, Ins, Outs, out var v, out _);
			Assert.That(v.Inputs, Is.EqualTo(new[] { (0, 15u), (1, 3u) }));
		}
		[Test] public void ValueTooWide_IsError()
		{
			Assert.That(TestVectorParser.TryParse("A=16", 0, Ins, Outs, out _, out string e), Is.False);
			Assert.That(e, Does.Contain("A"));
			Assert.That(TestVectorParser.TryParse("A=-9", 0, Ins, Outs, out _, out _), Is.False);
		}
		[Test] public void UnknownPin_IsError() => Assert.That(TestVectorParser.TryParse("Q=1", 0, Ins, Outs, out _, out _), Is.False);
		[Test] public void AmbiguousName_IsError()
		{
			TruthPin[] dup = { new("IN", 1), new("IN", 1) };
			Assert.That(TestVectorParser.TryParse("IN=1", 0, dup, Outs, out _, out string e), Is.False);
			Assert.That(e, Does.Contain("in1"));
			Assert.That(TestVectorParser.TryParse("in2=1", 0, dup, Outs, out var v, out _), Is.True);
			Assert.That(v.Inputs[0], Is.EqualTo((1, 1u)));
		}
		[Test] public void CaseInsensitive() => Assert.That(TestVectorParser.TryParse("a=0XF -> sum=x", 0, Ins, Outs, out _, out _), Is.True);
		[Test] public void DontCare_IsOmitted()
		{
			TestVectorParser.TryParse("A=1 -> SUM=x", 0, Ins, Outs, out var v, out _);
			Assert.That(v.Expectations, Is.Empty);
		}
		[Test] public void Check_PassAndFail()
		{
			TestVectorParser.TryParse("A=5 B=3 -> SUM=8", 0, Ins, Outs, out var v, out _);
			Assert.That(TestVectorChecker.Check(v, new[] { 8u }, Outs), Is.Null);
			Assert.That(TestVectorChecker.Check(v, new[] { 7u }, Outs), Is.EqualTo("SUM=7 (expected 8)"));
		}
		[Test] public void FloatingBits_FailNumericExpectation()
		{
			TestVectorParser.TryParse("-> SUM=8", 0, Ins, Outs, out var v, out _);
			Assert.That(TestVectorChecker.Check(v, new[] { 8u | (1u << 16) }, Outs), Is.EqualTo("SUM=0b0100Z (expected 8)"));
		}
		[Test] public void ExpectFloating()
		{
			TestVectorParser.TryParse("-> SUM=z", 0, Ins, Outs, out var v, out _);
			Assert.That(TestVectorChecker.Check(v, new[] { 0x1Fu << 16 }, Outs), Is.Null);
			Assert.That(TestVectorChecker.Check(v, new[] { 1u << 16 }, Outs), Is.Not.Null);
		}
		[Test] public void MultipleMismatches_AreJoined()
		{
			TruthPin[] outs = { new("X", 2), new("Y", 2) };
			TestVectorParser.TryParse("-> X=1 Y=2", 0, Ins, outs, out var v, out _);
			Assert.That(TestVectorChecker.Check(v, new[] { 0u, 0u }, outs), Is.EqualTo("X=0 (expected 1), Y=0 (expected 2)"));
		}
		static TruthTable HalfAdder()
		{
			var t = new TruthTable(new[] { new TruthPin("A", 1), new TruthPin("B", 1) }, new[] { new TruthPin("S", 1), new TruthPin("C", 1) });
			t.SetOutputStates(0, new[] { 0u, 0u });
			t.SetOutputStates(1, new[] { 1u, 0u });
			t.SetOutputStates(2, new[] { 1u, 0u });
			t.SetOutputStates(3, new[] { 0u, 1u });
			return t;
		}
		[Test] public void Writer_UsesNamesOrPositions()
		{
			Assert.That(TestVectorWriter.FromTruthTableRow(HalfAdder(), 3), Is.EqualTo("A=1 B=1 -> S=0 C=1"));
			var dup = new TruthTable(new[] { new TruthPin("IN", 1), new TruthPin("in", 1) }, new[] { new TruthPin("OUT", 1) });
			dup.SetOutputStates(0, new[] { 0u });
			dup.SetOutputStates(1, new[] { 0u });
			dup.SetOutputStates(2, new[] { 0u });
			dup.SetOutputStates(3, new[] { 0u });
			Assert.That(TestVectorWriter.FromTruthTableRow(dup, 3), Is.EqualTo("in1=1 in2=1 -> OUT=0"));
			var dupOut = new TruthTable(new[] { new TruthPin("A", 1) }, new[] { new TruthPin("Q", 1), new TruthPin("Q", 1) });
			dupOut.SetOutputStates(0, new[] { 0u, 0u });
			Assert.That(TestVectorWriter.FromTruthTableRow(dupOut, 0), Is.EqualTo("A=0 -> out1=0 out2=0"));
		}
		[Test] public void Writer_RoundTripsThroughParser()
		{
			var t = new TruthTable(new[] { new TruthPin("my pin", 2), new TruthPin("in2", 1), new TruthPin("B", 1) }, new[] { new TruthPin("A B", 3), new TruthPin("Z", 1) });
			for (int r = 0; r < t.RowCount; r++) t.SetOutputStates(r, new[] { (uint)(r & 7), r == 5 ? 1u << 16 : 1u });
			for (int r = 0; r < t.RowCount; r++)
			{
				string line = TestVectorWriter.FromTruthTableRow(t, r);
				Assert.That(TestVectorParser.TryParse(line, 0, t.Inputs, t.Outputs, out var v, out string e), Is.True, line + ": " + e);
				for (int i = 0; i < t.Inputs.Length; i++) Assert.That(v.Inputs[i], Is.EqualTo((i, t.GetInputValue(r, i))), line);
				uint[] states = { t.GetOutputState(r, 0), t.GetOutputState(r, 1) };
				Assert.That(TestVectorChecker.Check(v, states, t.Outputs), Is.Null, line);
			}
		}
	}
}
