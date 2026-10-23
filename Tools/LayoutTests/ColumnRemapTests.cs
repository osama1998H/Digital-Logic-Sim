using DLS.Game.Probes;
using NUnit.Framework;

namespace LayoutTests
{
	public class ColumnRemapTests
	{
		[Test]
		public void BuildMap_KeepsRemovesAndAdds()
		{
			int[] map = ColumnRemap.BuildMap(new[] { 10, 20, 30 }, new[] { 30, 40, 10 }, (a, b) => a == b);
			Assert.That(map, Is.EqualTo(new[] { 2, -1, 0 }));
		}

		[Test]
		public void RemapRow_DropsRemovedAndFillsAdded()
		{
			uint[] dst = new uint[3];
			ColumnRemap.RemapRow(new uint[] { 1, 2, 3 }, new[] { 2, -1, 0 }, 99, dst);
			Assert.That(dst, Is.EqualTo(new uint[] { 3, 99, 1 }));
		}
	}
}
