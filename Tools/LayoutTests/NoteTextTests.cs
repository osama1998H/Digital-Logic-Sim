using System;
using System.Linq;
using DLS.Game.Notes;
using NUnit.Framework;

namespace LayoutTests
{
	public class NoteTextTests
	{
		[Test]
		public void Normalize_ClampsLinesAndLength()
		{
			string line = new string('x', 60);
			string text = string.Join("\n", Enumerable.Repeat(line, 20));

			string[] lines = NoteText.Normalize(text).Split('\n');

			Assert.That(lines.Length, Is.EqualTo(NoteText.MaxLines));
			Assert.That(lines.All(l => l.Length == NoteText.MaxLineLength), Is.True);
		}

		[Test]
		public void Normalize_TrimsTrailingEmptyLines()
		{
			Assert.That(NoteText.Normalize("abc\n\n   \n"), Is.EqualTo("abc"));
			Assert.That(NoteText.Normalize("abc\n\ndef"), Is.EqualTo("abc\n\ndef"));
		}

		[Test]
		public void Normalize_EmptyTextIsEmpty()
		{
			Assert.That(NoteText.Normalize(""), Is.EqualTo(""));
			Assert.That(NoteText.Normalize(null), Is.EqualTo(""));
			Assert.That(NoteText.Lines(""), Is.Empty);
		}

		[Test]
		public void Normalize_ConvertsLineEndings()
		{
			Assert.That(NoteText.Normalize("a\r\nb"), Is.EqualTo("a\nb"));
			Assert.That(NoteText.Normalize("a\rb"), Is.EqualTo("a\nb"));
		}

		[Test]
		public void Normalize_TabsBecomeSingleSpace()
		{
			Assert.That(NoteText.Normalize("a\tb"), Is.EqualTo("a b"));
		}

		[Test]
		public void Normalize_TrimsTrailingWhitespacePerLine()
		{
			Assert.That(NoteText.Normalize("ab  \ncd \t"), Is.EqualTo("ab\ncd"));
		}

		[Test]
		public void Lines_SplitsNormalizedText()
		{
			Assert.That(NoteText.Lines("a\r\nb\n"), Is.EqualTo(new[] { "a", "b" }));
		}

		[Test]
		public void Measure_UsesLongestLine()
		{
			(float width, float height) = NoteText.Measure("ab\nabcd", 1f, 2f, 0.5f);

			Assert.That(width, Is.EqualTo(5f));
			Assert.That(height, Is.EqualTo(5f));
		}

		[Test]
		public void Measure_EmptyTextIsOneEightCharLine()
		{
			(float width, float height) = NoteText.Measure("", 1f, 2f, 0.5f);

			Assert.That(width, Is.EqualTo(9f));
			Assert.That(height, Is.EqualTo(3f));
		}
	}
}
