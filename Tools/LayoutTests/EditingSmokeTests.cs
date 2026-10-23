using DLS.Game.Editing;
using NUnit.Framework;

namespace LayoutTests
{
	public class EditingSmokeTests
	{
		[Test]
		public void ElementBox_Offset_MovesBoundsAndAnchor()
		{
			ElementBox box = new ElementBox(1, 0, 0, 2, 1, 0.5f, 0.5f).Offset(1, 2);
			Assert.That(box.MinX, Is.EqualTo(1f));
			Assert.That(box.MaxY, Is.EqualTo(3f));
			Assert.That(box.AnchorX, Is.EqualTo(1.5f));
		}
	}
}
