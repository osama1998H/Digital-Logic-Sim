using System;
using System.Collections.Generic;
using System.Linq;
using DLS.Description;
using DLS.Graphics;
using DLS.SaveSystem;
using UnityEngine;

namespace DLS.Game.Layout
{
	public struct OrganizeReport
	{
		public int WiresRouted, WiresFailed, ChipsMoved;
		public bool RecordedUndo; // false when nothing changed (or organize failed and restored the chip)

		public override string ToString() => $"Wires routed: {WiresRouted}, wires failed: {WiresFailed}, chips moved: {ChipsMoved}";
	}

	// Rewrites wire shapes (and optionally element positions) of the edited chip as a single undo step.
	// Only geometry changes: every wire keeps its pins, so the simulation is never touched.
	public static class ChipOrganizer
	{
		public static OrganizeReport TidyWires(DevChipInstance chip) => Organize(chip, false);

		public static OrganizeReport OrganizeAll(DevChipInstance chip) => Organize(chip, true);

		static OrganizeReport Organize(DevChipInstance chip, bool placeElements)
		{
			Project project = Project.ActiveProject;
			if (project == null || !project.CanEditViewedChip || chip != project.ViewedChip) return default;

			project.controller.CancelEverything();

			DescriptionCreator.UpdateWireIndicesForDescriptionCreation(chip);
			List<WireInstance> wiresBeforeInstances = new(chip.Wires);
			WireDescription[] wiresBefore = chip.Wires.Select(DescriptionCreator.CreateWireDescription).ToArray();
			int[] elementIDs = chip.Elements.Select(e => e.ID).ToArray();
			Vector2[] positionsBefore = chip.Elements.Select(e => e.Position).ToArray();

			OrganizeReport report = default;
			string failure = null;

			try
			{
				if (placeElements)
				{
					Dictionary<int, LayoutPoint> placed = ChipPlacer.Place(LayoutProblemBuilder.BuildPlacementInput(chip));
					foreach (IMoveable element in chip.Elements)
					{
						if (!placed.TryGetValue(element.ID, out LayoutPoint p)) continue;
						Vector2 newPos = LayoutProblemBuilder.ToVector(p);
						if (newPos == element.Position) continue;

						element.Position = newPos;
						report.ChipsMoved++;
					}
				}

				// RouterSettings defaults (GridSize 0.125, WireThickness 0.025) match DrawSettings; the core can't read DrawSettings itself
				RouterSettings settings = new();
				Debug.Assert(settings.GridSize == DrawSettings.GridSize && settings.WireThickness == DrawSettings.WireThickness, "RouterSettings defaults no longer match DrawSettings");

				LayoutResult result = WireRouter.Route(LayoutProblemBuilder.BuildRoutingProblem(chip), settings);
				WireDescription[] wiresAfter = LayoutApplier.BuildWireDescriptions(result, wiresBeforeInstances, wiresBefore, out failure);

				if (failure == null)
				{
					if (chip.ReplaceAllWires(wiresAfter)) failure = "A wire failed to load";
					foreach (RoutedConnection connection in result.Connections)
					{
						if (connection.Routed) report.WiresRouted++;
						else report.WiresFailed++;
					}
				}
			}
			catch (Exception e)
			{
				failure = $"{e.Message}\n{e.StackTrace}";
			}

			if (failure != null)
			{
				Restore(chip, elementIDs, positionsBefore, wiresBefore);
				if (Application.isEditor) Debug.LogError("Organize failed, chip restored. Reason: " + failure);
				return new OrganizeReport { WiresFailed = wiresBefore.Length };
			}

			if (wiresBefore.Length > 0 || report.ChipsMoved > 0)
			{
				DescriptionCreator.UpdateWireIndicesForDescriptionCreation(chip);
				WireDescription[] wiresAfterSnapshot = chip.Wires.Select(DescriptionCreator.CreateWireDescription).ToArray();
				Vector2[] positionsAfter = chip.Elements.Select(e => e.Position).ToArray();
				if (!SameLayout(positionsBefore, positionsAfter, wiresBefore, wiresAfterSnapshot))
				{
					chip.UndoController.RecordOrganize(elementIDs, positionsBefore, positionsAfter, wiresBefore, wiresAfterSnapshot);
					report.RecordedUndo = true;
				}
			}

			if (Application.isEditor) Debug.Log("Organize: " + report);
			return report;
		}

		static bool SameLayout(Vector2[] positionsA, Vector2[] positionsB, WireDescription[] wiresA, WireDescription[] wiresB)
		{
			const float eps = 1e-4f;
			if (positionsA.Length != positionsB.Length || wiresA.Length != wiresB.Length) return false;

			for (int i = 0; i < positionsA.Length; i++)
			{
				if (!Near(positionsA[i], positionsB[i])) return false;
			}

			for (int i = 0; i < wiresA.Length; i++)
			{
				WireDescription a = wiresA[i], b = wiresB[i];
				if (!PinAddress.Equals(a.SourcePinAddress, b.SourcePinAddress) || !PinAddress.Equals(a.TargetPinAddress, b.TargetPinAddress)) return false;
				if (a.ConnectionType != b.ConnectionType || a.ConnectedWireIndex != b.ConnectedWireIndex || a.ConnectedWireSegmentIndex != b.ConnectedWireSegmentIndex) return false;
				if (a.Points.Length != b.Points.Length) return false;
				for (int j = 0; j < a.Points.Length; j++)
				{
					if (!Near(a.Points[j], b.Points[j])) return false;
				}
			}

			return true;

			static bool Near(Vector2 p, Vector2 q) => Mathf.Abs(p.x - q.x) <= eps && Mathf.Abs(p.y - q.y) <= eps;
		}

		static void Restore(DevChipInstance chip, int[] elementIDs, Vector2[] positions, WireDescription[] wires)
		{
			Dictionary<int, IMoveable> elementLookupByID = chip.Elements.ToDictionary(element => element.ID, element => element);
			for (int i = 0; i < elementIDs.Length; i++)
			{
				elementLookupByID[elementIDs[i]].Position = positions[i];
			}

			if (chip.ReplaceAllWires(wires) && Application.isEditor) Debug.LogError("Organize: failed to restore the original wires");
		}
	}
}
