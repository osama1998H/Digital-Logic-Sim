namespace DLS.Game.Editing
{
	public enum ArrangeOp
	{
		AlignLeft,
		AlignHCentre,
		AlignRight,
		AlignTop,
		AlignVCentre,
		AlignBottom,
		DistributeH,
		DistributeV
	}

	// Axis-aligned bounds of an element plus the point it snaps to the grid by
	public readonly struct ElementBox
	{
		public readonly int ID;
		public readonly float MinX, MinY, MaxX, MaxY;
		public readonly float AnchorX, AnchorY;

		public ElementBox(int id, float minX, float minY, float maxX, float maxY, float anchorX, float anchorY)
		{
			ID = id;
			MinX = minX;
			MinY = minY;
			MaxX = maxX;
			MaxY = maxY;
			AnchorX = anchorX;
			AnchorY = anchorY;
		}

		public float CentreX => (MinX + MaxX) / 2;
		public float CentreY => (MinY + MaxY) / 2;
		public float Width => MaxX - MinX;
		public float Height => MaxY - MinY;

		public ElementBox Offset(float dx, float dy) => new(ID, MinX + dx, MinY + dy, MaxX + dx, MaxY + dy, AnchorX + dx, AnchorY + dy);
	}

	public readonly struct CorePin
	{
		public readonly int ID;
		public readonly int BitCount;

		public CorePin(int id, int bitCount)
		{
			ID = id;
			BitCount = bitCount;
		}
	}

	public readonly struct PositionedItem
	{
		public readonly int ID;
		public readonly float X, Y;

		public PositionedItem(int id, float x, float y)
		{
			ID = id;
			X = x;
			Y = y;
		}
	}
}
