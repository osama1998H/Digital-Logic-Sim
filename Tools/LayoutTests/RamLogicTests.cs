using DLS.Game.Memory;
using NUnit.Framework;

namespace LayoutTests
{
	public class RamLogicTests
	{
		[Test]
		public void ReadIsAsynchronous()
		{
			var s = new uint[RamLogic.StateLength];
			s[3] = 0x5A;
			Assert.That(RamLogic.Step(s, 3, 0, false, false, false, 8), Is.EqualTo(0x5Au));
			Assert.That(RamLogic.Step(s, 4, 0, false, false, false, 8), Is.EqualTo(0u));
			s[4] = 0x77;
			Assert.That(RamLogic.Step(s, 4, 0, false, false, false, 8), Is.EqualTo(0x77u));
			// a write with the clock low is not stored
			Assert.That(RamLogic.Step(s, 3, 0x11, true, false, false, 8), Is.EqualTo(0x5Au));
			Assert.That(s[3], Is.EqualTo(0x5Au));
		}

		[Test]
		public void WriteOnlyOnRisingEdge()
		{
			var s = new uint[RamLogic.StateLength];
			Assert.That(RamLogic.Step(s, 3, 0xAB, true, false, false, 8), Is.EqualTo(0u));
			Assert.That(s[3], Is.EqualTo(0u));
			Assert.That(RamLogic.Step(s, 3, 0xAB, true, false, true, 8), Is.EqualTo(0xABu)); // rising edge writes
			Assert.That(RamLogic.Step(s, 3, 0x11, true, false, true, 8), Is.EqualTo(0xABu)); // clock still high: no write
			Assert.That(s[3], Is.EqualTo(0xABu));
			Assert.That(RamLogic.Step(s, 3, 0x11, true, false, false, 8), Is.EqualTo(0xABu)); // clock falls: no write
			Assert.That(s[3], Is.EqualTo(0xABu));
			Assert.That(RamLogic.Step(s, 3, 0x11, true, false, true, 8), Is.EqualTo(0x11u)); // next rising edge writes
			Assert.That(s[3], Is.EqualTo(0x11u));
			Assert.That(s[RamLogic.PrevClockIndex], Is.EqualTo(1u));
		}

		[Test]
		public void ResetBeatsWrite()
		{
			var s = new uint[RamLogic.StateLength];
			RamLogic.Step(s, 200, 0x34, true, false, true, 8);
			RamLogic.Step(s, 200, 0, false, false, false, 8);
			Assert.That(RamLogic.Step(s, 200, 0, false, true, false, 8), Is.EqualTo(0x34u)); // reset only acts on a rising edge
			Assert.That(s[200], Is.EqualTo(0x34u));
			RamLogic.Step(s, 1, 0x12, true, false, true, 8);
			RamLogic.Step(s, 1, 0, false, false, false, 8);
			Assert.That(RamLogic.Step(s, 1, 0xFF, true, true, true, 8), Is.EqualTo(0u)); // reset and write on the same edge
			Assert.That(s[1], Is.EqualTo(0u));
			Assert.That(s[200], Is.EqualTo(0u));
		}

		[Test]
		public void StoredValuesAreMasked()
		{
			var s = new uint[RamLogic.StateLength];
			Assert.That(RamLogic.Step(s, 2, 0x1FF, true, false, true, 8), Is.EqualTo(0xFFu));
			Assert.That(s[2], Is.EqualTo(0xFFu));
			RamLogic.Step(s, 0, 0, false, false, false, 4); // clock low so the next high is an edge
			Assert.That(RamLogic.Step(s, 9, 0xAB, true, false, true, 4), Is.EqualTo(0xBu));
			Assert.That(s[9], Is.EqualTo(0xBu));
			// address is taken modulo 256
			Assert.That(RamLogic.Step(s, 0x102, 0, false, false, false, 8), Is.EqualTo(0xFFu));
		}

		[Test]
		public void NoWriteWhenWriteLow()
		{
			var s = new uint[RamLogic.StateLength];
			Assert.That(RamLogic.Step(s, 6, 0x3C, true, false, true, 8), Is.EqualTo(0x3Cu));
			RamLogic.Step(s, 6, 0, false, false, false, 8);
			Assert.That(RamLogic.Step(s, 6, 0x99, false, false, true, 8), Is.EqualTo(0x3Cu));
			Assert.That(s[6], Is.EqualTo(0x3Cu));
		}
	}
}
