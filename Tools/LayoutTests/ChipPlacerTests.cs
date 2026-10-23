using System;
using System.Collections.Generic;
using DLS.Game.Layout;
using NUnit.Framework;

namespace LayoutTests
{
	public class ChipPlacerTests
	{
		static PlaceNode Chip(int id, float x, float y, float w = 1f, float h = 0.75f) => new PlaceNode
		{
			ID = id, Kind = PlaceKind.Chip, Position = new LayoutPoint(x, y), BoxWidth = w, BoxHeight = h,
			BoxOffset = new LayoutPoint(0.01f, 0.02f), SnapOffset = new LayoutPoint(0.03f, -0.02f)
		};

		static PlaceNode Pin(int id, PlaceKind kind, float x, float y) => new PlaceNode
		{
			ID = id, Kind = kind, Position = new LayoutPoint(x, y), BoxWidth = 0.3f, BoxHeight = 0.3f
		};

		static void Link(PlacementInput input, int from, int to) => input.Edges.Add(new PlaceEdge { FromID = from, ToID = to });

		static (float minX, float minY, float maxX, float maxY) Box(PlaceNode n, LayoutPoint p)
		{
			float cx = p.X + n.BoxOffset.X, cy = p.Y + n.BoxOffset.Y;
			return (cx - n.BoxWidth / 2, cy - n.BoxHeight / 2, cx + n.BoxWidth / 2, cy + n.BoxHeight / 2);
		}

		static bool Overlap(PlaceNode a, LayoutPoint pa, PlaceNode b, LayoutPoint pb)
		{
			var r = Box(a, pa);
			var s = Box(b, pb);
			return r.minX < s.maxX - 1e-4f && s.minX < r.maxX - 1e-4f && r.minY < s.maxY - 1e-4f && s.minY < r.maxY - 1e-4f;
		}

		static PlaceNode Find(PlacementInput input, int id) => input.Nodes.Find(n => n.ID == id);

		[Test]
		public void Place_Chain_ColumnsIncreaseLeftToRight()
		{
			PlacementInput input = new();
			input.Nodes.Add(Pin(1, PlaceKind.InputPin, -5, 0));
			input.Nodes.Add(Chip(2, 0, 0));
			input.Nodes.Add(Chip(3, 0, 0));
			input.Nodes.Add(Chip(4, 0, 0));
			input.Nodes.Add(Pin(5, PlaceKind.OutputPin, 5, 0));
			Link(input, 1, 2);
			Link(input, 2, 3);
			Link(input, 3, 4);
			Link(input, 4, 5);

			var result = ChipPlacer.Place(input);

			Assert.That(result[2].X, Is.LessThan(result[3].X));
			Assert.That(result[3].X, Is.LessThan(result[4].X));
			Assert.That(result[4].X, Is.LessThan(result[5].X));
		}

		[Test]
		public void Place_NoChipBoundingBoxesOverlap()
		{
			PlacementInput input = new();
			input.Nodes.Add(Pin(1, PlaceKind.InputPin, -5, 0));
			for (int i = 0; i < 5; i++)
			{
				input.Nodes.Add(Chip(10 + i, 0, 0));
				Link(input, 1, 10 + i);
				Link(input, 10 + i, i < 3 ? 20 : 21);
			}
			input.Nodes.Add(Pin(20, PlaceKind.OutputPin, 5, 0.5f));
			input.Nodes.Add(Pin(21, PlaceKind.OutputPin, 5, -0.5f));

			var result = ChipPlacer.Place(input);

			for (int i = 0; i < 5; i++)
			for (int j = i + 1; j < 5; j++)
				Assert.That(Overlap(Find(input, 10 + i), result[10 + i], Find(input, 10 + j), result[10 + j]), Is.False, $"{i} vs {j}");
		}

		[Test]
		public void Place_Cycle_TerminatesAndPlacesAll()
		{
			PlacementInput input = new();
			input.Nodes.Add(Pin(1, PlaceKind.InputPin, -5, 0));
			input.Nodes.Add(Chip(2, 0, 0));
			input.Nodes.Add(Chip(3, 0, 0));
			Link(input, 1, 2);
			Link(input, 2, 3);
			Link(input, 3, 2);

			var result = ChipPlacer.Place(input);

			Assert.That(result.ContainsKey(2), Is.True);
			Assert.That(result.ContainsKey(3), Is.True);
			Assert.That(Overlap(Find(input, 2), result[2], Find(input, 3), result[3]), Is.False);
		}

		[Test]
		public void Place_SnapPointsLandOnGrid()
		{
			PlacementInput input = new();
			input.Nodes.Add(Pin(1, PlaceKind.InputPin, -5.03f, 0.07f));
			for (int i = 0; i < 4; i++)
			{
				input.Nodes.Add(Chip(10 + i, 0.017f * i, 0.031f * i, 1f + 0.1f * i, 0.7f));
				Link(input, 1, 10 + i);
			}
			input.Nodes.Add(Chip(20, 1.3f, -2.2f));

			var result = ChipPlacer.Place(input);

			foreach (PlaceNode n in input.Nodes)
			{
				if (n.Kind != PlaceKind.Chip) continue;
				LayoutPoint p = result[n.ID];
				float gx = (p.X + n.SnapOffset.X) / 0.125f, gy = (p.Y + n.SnapOffset.Y) / 0.125f;
				Assert.That(gx, Is.EqualTo(MathF.Round(gx)).Within(1e-4));
				Assert.That(gy, Is.EqualTo(MathF.Round(gy)).Within(1e-4));
			}
		}

		[Test]
		public void Place_OutputPinsKeepYAndRelativeX()
		{
			PlacementInput input = new();
			input.Nodes.Add(Pin(1, PlaceKind.InputPin, -5, 0));
			input.Nodes.Add(Chip(2, 0, 0));
			input.Nodes.Add(Pin(3, PlaceKind.OutputPin, 4, 0.5f));
			input.Nodes.Add(Pin(4, PlaceKind.OutputPin, 4.75f, 0.5f));
			Link(input, 1, 2);
			Link(input, 2, 3);
			Link(input, 2, 4);

			var result = ChipPlacer.Place(input);

			Assert.That(result[3].Y, Is.EqualTo(0.5f).Within(1e-4));
			Assert.That(result[4].Y, Is.EqualTo(0.5f).Within(1e-4));
			Assert.That(result[4].X - result[3].X, Is.EqualTo(0.75f).Within(1e-4));
		}

		[Test]
		public void Place_InputPinsNotInResult()
		{
			PlacementInput input = new();
			input.Nodes.Add(Pin(1, PlaceKind.InputPin, -5, 0));
			input.Nodes.Add(Chip(2, 0, 0));
			Link(input, 1, 2);

			var result = ChipPlacer.Place(input);

			Assert.That(result.ContainsKey(1), Is.False);
			Assert.That(result.ContainsKey(2), Is.True);
		}

		[Test]
		public void Place_UnconnectedChips_ParkedBelow()
		{
			PlacementInput input = new();
			input.Nodes.Add(Pin(1, PlaceKind.InputPin, -5, 0));
			input.Nodes.Add(Chip(2, 0, 0));
			input.Nodes.Add(Chip(3, 0, 0));
			input.Nodes.Add(Chip(9, 0, 5));
			Link(input, 1, 2);
			Link(input, 2, 3);

			var result = ChipPlacer.Place(input);

			float lowestConnected = Math.Min(Box(Find(input, 2), result[2]).minY, Box(Find(input, 3), result[3]).minY);
			Assert.That(Box(Find(input, 9), result[9]).maxY, Is.LessThan(lowestConnected));
		}
	}
}
