using System.Linq;
using DLS.Game.Logic;
using NUnit.Framework;

namespace LayoutTests
{
	public class LogicMinimizerTests
	{
		static string Sop(int vars, int[] on, int[] dc = null) =>
			SopFormatter.Format(LogicMinimizer.Minimize(vars, on, dc ?? new int[0]), new[] { "A", "B", "C", "D" }.Take(vars).ToArray());
		[Test] public void Xor() => Assert.That(Sop(2, new[] { 1, 2 }), Is.EqualTo("A'*B + A*B'"));
		[Test] public void Majority() => Assert.That(Sop(3, new[] { 3, 5, 6, 7 }), Is.EqualTo("A*B + A*C + B*C"));
		[Test] public void AlwaysOne() => Assert.That(Sop(2, new[] { 0, 1, 2, 3 }), Is.EqualTo("1"));
		[Test] public void AlwaysZero() => Assert.That(Sop(2, new int[0]), Is.EqualTo("0"));
		[Test] public void DontCaresAreUsed() => Assert.That(Sop(3, new[] { 1, 3 }, new[] { 5, 7 }), Is.EqualTo("C"));
		[Test] public void MakeVariableNames_DuplicatesFallBackToLetters() =>
			Assert.That(SopFormatter.MakeVariableNames(new[] { "IN", "IN" }), Is.EqualTo(new[] { "A", "B" }));
		[Test] public void MakeVariableNames_KeepsValidNames() =>
			Assert.That(SopFormatter.MakeVariableNames(new[] { "CIN", "X1" }), Is.EqualTo(new[] { "CIN", "X1" }));
	}
}
