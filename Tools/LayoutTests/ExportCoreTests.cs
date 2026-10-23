using System;
using DLS.Game.Export;
using NUnit.Framework;

namespace LayoutTests
{
	public class ExportCoreTests
	{
		[Test] public void Fit_WideBoundsLimitedByWidth()
		{
			var f = ExportFraming.Fit(0, 0, 32, 2, 16f / 9f, 0);
			Assert.That(f.orthoSize, Is.EqualTo(9f).Within(1e-4)); // half-width 16 / aspect
			Assert.That(f.centreX, Is.EqualTo(16f)); Assert.That(f.centreY, Is.EqualTo(1f));
		}
		[Test] public void Fit_TallBoundsLimitedByHeight() => Assert.That(ExportFraming.Fit(0, 0, 2, 10, 16f / 9f, 0).orthoSize, Is.EqualTo(5f).Within(1e-4));
		[Test] public void Fit_AppliesPadding() => Assert.That(ExportFraming.Fit(0, 0, 2, 10, 16f / 9f, 0.1f).orthoSize, Is.EqualTo(5.5f).Within(1e-4));
		[Test] public void Fit_DegenerateBoundsUseMinimumHalfHeight()
		{
			var f = ExportFraming.Fit(3, 4, 3, 4, 16f / 9f, 0);
			Assert.That(f.orthoSize, Is.EqualTo(1f).Within(1e-4));
			Assert.That(f.centreX, Is.EqualTo(3f)); Assert.That(f.centreY, Is.EqualTo(4f));
		}
		[Test] public void FileName_Format() => Assert.That(ExportNaming.FileName("AND-8", new DateTime(2026, 10, 23, 14, 5, 9)), Is.EqualTo("AND-8 2026-10-23 14-05-09.png"));
		[Test] public void FileName_ReplacesInvalidChars() => Assert.That(ExportNaming.FileName("A/B:C", new DateTime(2026, 1, 2, 3, 4, 5)), Is.EqualTo("A_B_C 2026-01-02 03-04-05.png"));
		[Test] public void FileName_EmptyChipName() => Assert.That(ExportNaming.FileName("", new DateTime(2026, 1, 2, 3, 4, 5)), Does.StartWith("Untitled "));
		[Test] public void FileName_TrimsTrailingDotsAndSpaces() => Assert.That(ExportNaming.FileName("AND. .", new DateTime(2026, 1, 2, 3, 4, 5)), Is.EqualTo("AND 2026-01-02 03-04-05.png"));
		[Test] public void FileName_WhitespaceOnlyIsUntitled() => Assert.That(ExportNaming.FileName("   ", new DateTime(2026, 1, 2, 3, 4, 5)), Does.StartWith("Untitled "));
		[Test] public void FileName_ReplacesControlChars() => Assert.That(ExportNaming.FileName("A\tB\nC", new DateTime(2026, 1, 2, 3, 4, 5)), Is.EqualTo("A_B_C 2026-01-02 03-04-05.png"));
	}
}
