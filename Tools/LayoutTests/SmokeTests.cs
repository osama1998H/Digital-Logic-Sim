using DLS.Game.Layout;
using NUnit.Framework;

namespace LayoutTests
{
	public class SmokeTests
	{
		[Test]
		public void LayoutPoint_Constructs()
		{
			Assert.That(new LayoutPoint(1, 2).X, Is.EqualTo(1f));
		}
	}
}
