using System.Collections.Generic;
using System.Linq;
using Seb.Types;
using UnityEngine;
using static DLS.Graphics.DrawSettings;

namespace DLS.Game.Editing
{
	// Aligns / distributes the current selection
	public static class SelectionArranger
	{
		const float MinMoveDst = 0.0001f;

		public static bool CanArrange(ChipInteractionController controller, ArrangeOp op)
		{
			int minCount = op is ArrangeOp.DistributeH or ArrangeOp.DistributeV ? 3 : 2;
			return controller.HasControl && controller.SelectedElements.Count >= minCount && !controller.IsPlacingOrMovingElementOrCreatingWire;
		}

		// Returns false if the arrangement was refused because elements would overlap, or because it would change the order of the chip's pins
		public static bool Apply(ChipInteractionController controller, ArrangeOp op)
		{
			if (!CanArrange(controller, op)) return false;

			DevChipInstance devChip = controller.ActiveDevChip;
			List<IMoveable> selection = controller.SelectedElements;
			bool snap = controller.project.SnapPreferenceEnabled;

			ElementBox[] boxes = selection.Select(ToBox).ToArray();
			(float dx, float dy)[] deltas = ArrangeMath.ComputeDeltas(boxes, op, snap ? GridSize : 0, snap ? GridSize / 2 : 0);
			if (deltas.All(d => Mathf.Abs(d.dx) < MinMoveDst && Mathf.Abs(d.dy) < MinMoveDst)) return true;

			ElementBox[] movedBoxes = new ElementBox[boxes.Length];
			Dictionary<int, Vector2> newPositions = new();
			for (int i = 0; i < boxes.Length; i++)
			{
				movedBoxes[i] = boxes[i].Offset(deltas[i].dx, deltas[i].dy);
				newPositions[selection[i].ID] = selection[i].Position + new Vector2(deltas[i].dx, deltas[i].dy);
			}

			ElementBox[] otherBoxes = devChip.Elements.Where(e => !e.IsSelected).Select(ToBox).ToArray();
			bool valid = !ArrangeMath.AnyOverlap(movedBoxes, otherBoxes) && PinOrderUnchanged(devChip, newPositions);

			if (!valid)
			{
				// Highlight the selection as invalid (same as when trying to move it somewhere it doesn't fit)
				foreach (IMoveable element in selection) element.IsValidMovePos = false;
				return false;
			}

			// Move elements (using the same steps as a regular move, so that it can be undone the same way)
			for (int i = 0; i < selection.Count; i++)
			{
				selection[i].MoveStartPosition = selection[i].Position;
				selection[i].Position = newPositions[selection[i].ID];
				selection[i].IsValidMovePos = true;
			}

			foreach (WireInstance wire in devChip.Wires)
			{
				IMoveable a = wire.SourcePin.parent;
				IMoveable b = wire.TargetPin.parent;
				// Wires between two moved elements keep their shape; other wires just stretch to the moved pins
				if (a.IsSelected && b.IsSelected) wire.MoveOffset = (a.Position - a.MoveStartPosition + b.Position - b.MoveStartPosition) / 2;
				else wire.MoveOffset = Vector2.zero;
			}

			devChip.UndoController.RecordMoveElements(selection);

			foreach (WireInstance wire in devChip.Wires)
			{
				wire.ApplyMoveOffset();
			}

			return true;
		}

		static bool PinOrderUnchanged(DevChipInstance devChip, Dictionary<int, Vector2> newPositions)
		{
			return SameOrder(devChip.GetInputPins(), newPositions) && SameOrder(devChip.GetOutputPins(), newPositions);
		}

		static bool SameOrder(IEnumerable<DevPinInstance> pins, Dictionary<int, Vector2> newPositions)
		{
			DevPinInstance[] pinArray = pins.ToArray();
			PositionedItem[] before = pinArray.Select(p => new PositionedItem(p.ID, p.Position.x, p.Position.y)).ToArray();
			PositionedItem[] after = pinArray.Select(p =>
			{
				Vector2 pos = newPositions.TryGetValue(p.ID, out Vector2 newPos) ? newPos : p.Position;
				return new PositionedItem(p.ID, pos.x, pos.y);
			}).ToArray();

			return ArrangeMath.PinOrder(before).SequenceEqual(ArrangeMath.PinOrder(after));
		}

		static ElementBox ToBox(IMoveable element)
		{
			Bounds2D bounds = element.BoundingBox;
			Vector2 anchor = element.SnapPoint;
			return new ElementBox(element.ID, bounds.Min.x, bounds.Min.y, bounds.Max.x, bounds.Max.y, anchor.x, anchor.y);
		}
	}
}
