using System.Collections.Generic;
using Seb.Types;
using UnityEngine;

namespace DLS.Game.Layout
{
	// Converts the edited chip into the engine-free layout problems
	public static class LayoutProblemBuilder
	{
		// Connection keys are wire indices: DescriptionCreator.UpdateWireIndicesForDescriptionCreation must have been called
		public static LayoutProblem BuildRoutingProblem(DevChipInstance chip)
		{
			LayoutProblem problem = new();

			foreach (IMoveable element in chip.Elements)
			{
				Bounds2D bounds = element.BoundingBox;
				problem.Obstacles.Add(new LayoutRect(element.ID, bounds.Min.x, bounds.Min.y, bounds.Max.x, bounds.Max.y));
			}

			Dictionary<(int ownerID, int pinID), int> pinKeys = new();

			for (int i = 0; i < chip.Wires.Count; i++)
			{
				WireInstance wire = chip.Wires[i];
				int sourceKey = GetOrAddPin(wire.SourcePin);
				int targetKey = GetOrAddPin(wire.TargetPin);
				bool isBusWriter = wire.TargetConnectionInfo.IsConnectedAtWire && wire.ConnectedWire.IsBusWire;

				problem.Connections.Add(new LayoutConnection
				{
					Key = wire.descriptionCreator_wireIndex,
					SourcePinKey = sourceKey,
					TargetPinKey = targetKey,
					BitCount = (int)wire.bitCount,
					IsBusTrunk = wire.IsBusWire,
					TargetAttachToKey = isBusWriter ? wire.ConnectedWire.descriptionCreator_wireIndex : -1
				});
			}

			return problem;

			int GetOrAddPin(PinInstance pin)
			{
				(int, int) address = (pin.Address.PinOwnerID, pin.Address.PinID);
				if (pinKeys.TryGetValue(address, out int key)) return key;

				key = pinKeys.Count;
				pinKeys.Add(address, key);
				problem.Pins.Add(key, new LayoutPin
				{
					Key = key,
					OwnerID = pin.Address.PinOwnerID,
					Position = ToLayout(pin.GetWorldPos()),
					ExitDirX = (int)pin.ForwardDir.x, // ForwardDir already accounts for bus flip
					BitCount = (int)pin.bitCount
				});
				return key;
			}
		}

		public static PlacementInput BuildPlacementInput(DevChipInstance chip)
		{
			PlacementInput input = new();

			foreach (IMoveable element in chip.Elements)
			{
				PlaceKind kind;
				if (element is SubChipInstance) kind = PlaceKind.Chip;
				else if (element is DevPinInstance devPin) kind = devPin.IsInputPin ? PlaceKind.InputPin : PlaceKind.OutputPin;
				else continue;

				Bounds2D bounds = element.BoundingBox;
				input.Nodes.Add(new PlaceNode
				{
					ID = element.ID,
					Kind = kind,
					Position = ToLayout(element.Position),
					BoxWidth = bounds.Size.x,
					BoxHeight = bounds.Size.y,
					BoxOffset = ToLayout(bounds.Centre - element.Position),
					SnapOffset = ToLayout(element.SnapPoint - element.Position)
				});
			}

			foreach (WireInstance wire in chip.Wires)
			{
				input.Edges.Add(new PlaceEdge { FromID = wire.SourcePin.Address.PinOwnerID, ToID = wire.TargetPin.Address.PinOwnerID });
			}

			return input;
		}

		public static LayoutPoint ToLayout(Vector2 p) => new(p.x, p.y);

		public static Vector2 ToVector(LayoutPoint p) => new(p.X, p.Y);
	}
}
