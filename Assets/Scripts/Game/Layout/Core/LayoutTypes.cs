using System.Collections.Generic;

namespace DLS.Game.Layout
{
	public readonly struct LayoutPoint
	{
		public readonly float X, Y;

		public LayoutPoint(float x, float y)
		{
			X = x;
			Y = y;
		}
	}

	public readonly struct LayoutRect
	{
		public readonly int OwnerID;
		public readonly float MinX, MinY, MaxX, MaxY;

		public LayoutRect(int ownerID, float minX, float minY, float maxX, float maxY)
		{
			OwnerID = ownerID;
			MinX = minX;
			MinY = minY;
			MaxX = maxX;
			MaxY = maxY;
		}

		public bool Contains(LayoutPoint p) => p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;
	}

	public struct LayoutPin
	{
		public int Key;
		public int OwnerID;
		public LayoutPoint Position;
		public int ExitDirX; // +1 or -1
		public int BitCount;
	}

	public class LayoutConnection
	{
		public int Key; // caller's id (the adapter uses the current wire index)
		public int SourcePinKey;
		public int TargetPinKey;
		public int BitCount;
		public bool IsBusTrunk; // bus origin -> bus terminus wire
		public int TargetAttachToKey = -1; // bus writer: target end must end on the connection with this key (the trunk)
	}

	public class LayoutProblem
	{
		public List<LayoutRect> Obstacles = new();
		public Dictionary<int, LayoutPin> Pins = new();
		public List<LayoutConnection> Connections = new();
	}

	public class RouterSettings
	{
		public float GridSize = 0.125f, WireThickness = 0.025f, MinGap = 0.05f;
		public float BendCost = 3, CrossCost = 2, ClearanceViolationCost = 30;
		public int LatticeMarginCells = 12, WindowMarginCells = 16, ImprovementPasses = 1;

		public float HalfWidth(int bitCount) => bitCount * WireThickness * 2 * 0.925f / 2;
	}

	public enum AttachKind
	{
		Pin,
		Wire
	}

	public class RoutedConnection
	{
		public int Key;
		public bool Routed;
		public List<LayoutPoint> Points; // source end -> target end, corners only, first/last = exact end coordinates
		public AttachKind SourceAttach, TargetAttach;
		public int AttachedToKey = -1; // connection whose wire this one attaches to (at whichever end is AttachKind.Wire)
		public int AttachedSegmentIndex = -1; // segment i = Points[i]..Points[i+1] of the parent's FINAL Points
	}

	public class LayoutResult
	{
		public List<RoutedConnection> Connections = new(); // parents before children
		public int FailedCount;
		public float TotalCost; // sum of A* costs of routed connections
	}
}
