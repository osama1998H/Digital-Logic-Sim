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
			if (project == null || !project.CanEditViewedChip) return default;

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
				chip.UndoController.RecordOrganize(elementIDs, positionsBefore, positionsAfter, wiresBefore, wiresAfterSnapshot);
			}

			if (Application.isEditor) Debug.Log("Organize: " + report);
			return report;
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
