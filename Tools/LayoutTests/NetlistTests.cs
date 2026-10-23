using System;
using System.Linq;
using DLS.Game.Logic;
using NUnit.Framework;

namespace LayoutTests
{
	public class NetlistTests
	{
		[TestCase("Y = A ^ B")] [TestCase("Y = A'*B + A*C'")] [TestCase("Y = !(A+B+C) ^ D")] [TestCase("S = A ^ B ^ C; C = A*B + C*(A ^ B)")] [TestCase("Y = 1; Z = A*0")]
		public void NandOnly_PreservesTruthTable(string text) => AssertEquivalent(text, GateSet.None);
		[TestCase("S = A ^ B ^ C; C = A*B + C*(A ^ B)")]
		public void AllGates_PreservesTruthTable(string text) => AssertEquivalent(text, GateSet.Not | GateSet.Or | GateSet.And | GateSet.Xor);
		[TestCase("Y = A'*B + A*C'")] [TestCase("Y = A ^ B + C")]
		public void SomeGates_PreservesTruthTable(string text)
		{
			AssertEquivalent(text, GateSet.Not);
			AssertEquivalent(text, GateSet.And | GateSet.Or);
			AssertEquivalent(text, GateSet.Not | GateSet.Xor);
		}
		static void AssertEquivalent(string text, GateSet gates)
		{
			var p = ExprParser.Parse(text); var n = NetlistBuilder.Build(p, gates);
			Assert.That(n.Error, Is.Null);
			int k = p.Inputs.Count;
			for (int row = 0; row < 1 << k; row++)
			{
				bool[] ins = Enumerable.Range(0, k).Select(i => ((row >> (k - 1 - i)) & 1) == 1).ToArray();
				var d = p.Inputs.Select((name, i) => (name, ins[i])).ToDictionary(t => t.name, t => t.Item2, StringComparer.OrdinalIgnoreCase);
				bool[] outs = n.Evaluate(ins);
				for (int o = 0; o < p.Equations.Count; o++) Assert.That(outs[o], Is.EqualTo(p.Equations[o].Expr.Evaluate(d)), $"{text} row {row}");
			}
		}
		[Test] public void NandOnly_UsesOnlyNand() => Assert.That(NetlistBuilder.Build(ExprParser.Parse("Y=A*B+C"), GateSet.None).Gates.All(g => g.Kind == GateKind.Nand), Is.True);
		[Test] public void SharedSubexpressions_AreBuiltOnce() => Assert.That(NetlistBuilder.Build(ExprParser.Parse("X = A*B; Y = A*B + C"), GateSet.And | GateSet.Or).Gates.Count(g => g.Kind == GateKind.And), Is.EqualTo(1));
		[Test] public void SharedNots_AreBuiltOnce() => Assert.That(NetlistBuilder.Build(ExprParser.Parse("X = A'*B; Y = A' + B"), GateSet.Not | GateSet.And | GateSet.Or).Gates.Count(g => g.Kind == GateKind.Not), Is.EqualTo(1));
		[Test] public void DoubleNegation_AddsNoGates() { var n = NetlistBuilder.Build(ExprParser.Parse("Y = A''"), GateSet.Not); Assert.That(n.Gates, Is.Empty); Assert.That(n.OutputNets[0], Is.EqualTo(0)); }
		[Test] public void IdentityOutput_HasNoGates() { var n = NetlistBuilder.Build(ExprParser.Parse("Y = A"), GateSet.None); Assert.That(n.Gates, Is.Empty); Assert.That(n.OutputNets[0], Is.EqualTo(0)); }
		[Test] public void Gates_AreInTopologicalOrder()
		{
			var n = NetlistBuilder.Build(ExprParser.Parse("Y = A ^ B ^ C + D"), GateSet.None);
			foreach (var g in n.Gates) Assert.That(g.InputNets.All(i => i < g.OutputNet), Is.True);
		}
		[Test] public void NoInputs_IsError() { var n = NetlistBuilder.Build(ExprParser.Parse("Y = 1"), GateSet.None); Assert.That(n.Error, Does.Contain("at least one input")); Assert.That(n.Gates, Is.Empty); }
		[Test] public void ParseError_IsPropagated() => Assert.That(NetlistBuilder.Build(ExprParser.Parse("Y = A +"), GateSet.None).Success, Is.False);
		[Test] public void TooManyInputs_IsError()
		{
			string text = "Y = " + string.Join("+", Enumerable.Range(0, 17).Select(i => "V" + i));
			Assert.That(NetlistBuilder.Build(ExprParser.Parse(text), GateSet.And | GateSet.Or).Error, Does.Contain("too many inputs"));
		}
		[Test] public void SixteenInputs_AreAllowed()
		{
			string text = "Y = " + string.Join("+", Enumerable.Range(0, 16).Select(i => "V" + i));
			Assert.That(NetlistBuilder.Build(ExprParser.Parse(text), GateSet.Or).Success, Is.True);
		}
		[Test] public void TooManyOutputs_IsError()
		{
			string text = string.Join(";", Enumerable.Range(0, 9).Select(i => "O" + i + " = A"));
			Assert.That(NetlistBuilder.Build(ExprParser.Parse(text), GateSet.None).Error, Does.Contain("too many outputs"));
		}
		[Test] public void TooManyGates_IsError()
		{
			string text = string.Join(";", Enumerable.Range(0, 8).Select(o => "O" + o + " = " + string.Join("^", Enumerable.Range(0, 16).Select(i => "V" + (i + o) % 16))));
			var n = NetlistBuilder.Build(ExprParser.Parse(text), GateSet.None);
			Assert.That(n.Error, Does.Contain("too many gates")); Assert.That(n.Gates, Is.Empty);
		}
	}
}
