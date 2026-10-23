using System;
using System.Linq;
using DLS.Game.Logic;
using NUnit.Framework;

namespace LayoutTests
{
	public class ExprParserTests
	{
		static bool Eval(string expr, string assignment) // assignment like "A=1,B=0"
		{
			var r = ExprParser.Parse(expr);
			var d = assignment.Split(',').Select(p => p.Split('=')).ToDictionary(p => p[0], p => p[1] == "1", StringComparer.OrdinalIgnoreCase);
			return r.Equations[0].Expr.Evaluate(d);
		}

		[Test] public void Precedence_NotAndXorOr() { Assert.That(Eval("A + B*C", "A=0,B=1,C=0"), Is.False); Assert.That(Eval("A ^ B*C", "A=1,B=1,C=1"), Is.False); }
		[Test] public void PostfixNot() => Assert.That(Eval("A'*B", "A=0,B=1"), Is.True);
		[Test] public void AlternativeOperators() => Assert.That(Eval("!(a & b) | 0", "A=1,B=1"), Is.False);
		[Test] public void ParsesSopFormatterOutput() => Assert.That(ExprParser.Parse("Y = A'*B + A*B'").Success, Is.True);
		[Test]
		public void MultipleEquations_AndDefaultNames()
		{
			var r = ExprParser.Parse("S = A ^ B; A*B");
			Assert.That(r.Equations.Select(e => e.Output), Is.EqualTo(new[] { "S", "Y" }));
			Assert.That(r.Inputs, Is.EqualTo(new[] { "A", "B" }));
		}
		[Test] public void VariablesAreCaseInsensitive() => Assert.That(ExprParser.Parse("Y = a + A").Inputs, Is.EqualTo(new[] { "a" }));
		[Test] public void Parse_ReportsErrorPosition_TrailingOperator() { var r = ExprParser.Parse("Y = A*"); Assert.That(r.Success, Is.False); Assert.That(r.ErrorIndex, Is.EqualTo(6)); }
		[Test] public void Parse_ReportsErrorPosition_Unclosed() { var r = ExprParser.Parse("(A+B"); Assert.That(r.Success, Is.False); Assert.That(r.ErrorIndex, Is.EqualTo(4)); }
		[Test] public void Parse_ImplicitAnd_IsErrorWithHint() { var r = ExprParser.Parse("A B"); Assert.That(r.Error, Does.Contain("A*B")); }
		[Test] public void DuplicateOutput_IsError() => Assert.That(ExprParser.Parse("Y=A; Y=B").Error, Does.Contain("Y"));
		[Test] public void NoVariables_IsError() => Assert.That(ExprParser.Parse("Y = 1").Success, Is.False);

		[Test] public void DoubleNegation() { Assert.That(Eval("A''", "A=1"), Is.True); Assert.That(Eval("!!A", "A=0"), Is.False); }
		[Test] public void NestedParensWithPostfix() { Assert.That(Eval("(A+(B*C))'", "A=0,B=1,C=0"), Is.True); Assert.That(Eval("(A+B)'", "A=1,B=0"), Is.False); }
		[Test] public void UnknownCharacter_ErrorPosition() { var r = ExprParser.Parse("A + #"); Assert.That(r.Success, Is.False); Assert.That(r.ErrorIndex, Is.EqualTo(4)); }
		[Test] public void EmptyInput_IsError() { Assert.That(ExprParser.Parse("").Success, Is.False); Assert.That(ExprParser.Parse("  \n ; ").Success, Is.False); }
		[Test]
		public void NewlineAndWhitespaceSeparated()
		{
			var r = ExprParser.Parse("S = A ^ B\n\n  C = A * B \n");
			Assert.That(r.Success, Is.True);
			Assert.That(r.Equations.Select(e => e.Output), Is.EqualTo(new[] { "S", "C" }));
		}
		[Test] public void ImplicitAnd_ErrorIndexIsOffendingToken() => Assert.That(ExprParser.Parse("Y = A B").ErrorIndex, Is.EqualTo(6));
		[Test] public void InputsSortedIgnoringCase_KeepFirstSpelling() => Assert.That(ExprParser.Parse("Y = c + B + a + b").Inputs, Is.EqualTo(new[] { "a", "B", "c" }));
		[Test] public void DotAndMiddleDotAreAnd() { Assert.That(Eval("A.B", "A=1,B=1"), Is.True); Assert.That(Eval("A·B", "A=1,B=0"), Is.False); }
		[Test] public void DefaultNamesIncrement() => Assert.That(ExprParser.Parse("A; B; C").Equations.Select(e => e.Output), Is.EqualTo(new[] { "Y", "Y2", "Y3" }));
		[Test] public void ConstantOnlyEquationAlongsideVariables() => Assert.That(ExprParser.Parse("X = A; Z = 1").Success, Is.True);

		static void AssertEquivalent(string a, string b)
		{
			for (int row = 0; row < 8; row++)
			{
				string asg = $"A={row & 1},B={(row >> 1) & 1},C={(row >> 2) & 1}";
				Assert.That(Eval(a, asg), Is.EqualTo(Eval(b, asg)), $"{a} vs {b} at {asg}");
			}
		}

		[Test] public void Precedence_OrBelowAnd_Discriminating() => Assert.That(Eval("A + B*C", "A=1,B=0,C=0"), Is.True);
		[Test] public void Precedence_OrBelowXor() => AssertEquivalent("A + B ^ C", "A + (B ^ C)");
		[Test] public void Precedence_XorBelowAnd() => AssertEquivalent("A ^ B*C", "A ^ (B*C)");
		[Test] public void Xor_Chain_IsParity() { for (int row = 0; row < 8; row++) { int a = row & 1, b = (row >> 1) & 1, c = (row >> 2) & 1; Assert.That(Eval("A ^ B ^ C", $"A={a},B={b},C={c}"), Is.EqualTo((a ^ b ^ c) == 1)); } }
		[Test] public void Precedence_PostfixNotBindsTightest() => AssertEquivalent("A + B*C'", "A + (B*(!C))");
		[Test] public void MixedPrefixPostfixNot_IsDoubleNegation() => AssertEquivalent("!A'", "A");

		[Test] public void ExplicitOutputEqualToInput_IsError()
		{
			var r = ExprParser.Parse("S = A^B; C = S*A");
			Assert.That(r.Success, Is.False);
			Assert.That(r.Error, Does.Contain("'S' is used as both an output and an input"));
			Assert.That(r.ErrorIndex, Is.EqualTo(0));
			Assert.That(ExprParser.Parse("A = B").ErrorIndex, Is.EqualTo(0));
			Assert.That(ExprParser.Parse("X = A; y = Y").Success, Is.False);
		}
		[Test]
		public void DefaultNamesSkipInputs()
		{
			var r = ExprParser.Parse("A*Y");
			Assert.That(r.Success, Is.True);
			Assert.That(r.Equations[0].Output, Is.EqualTo("Y2"));
			var r2 = ExprParser.Parse("Y2 + Y; A");
			Assert.That(r2.Equations.Select(e => e.Output), Is.EqualTo(new[] { "Y3", "Y4" }));
		}
		[Test]
		public void DeepNesting_IsErrorNotCrash()
		{
			var r = ExprParser.Parse(new string('(', 1000) + "A" + new string(')', 1000));
			Assert.That(r.Success, Is.False);
			Assert.That(ExprParser.Parse(new string('!', 1000) + "A").Success, Is.False);
			Assert.That(ExprParser.Parse(new string('(', 20) + "A" + new string(')', 20)).Success, Is.True);
		}
	}
}
