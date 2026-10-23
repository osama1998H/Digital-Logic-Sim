using System;
using System.Collections.Generic;
using System.Linq;
using DLS.Description;
using DLS.Game.Layout;
using DLS.SaveSystem;
using UnityEngine;

namespace DLS.Game.Logic
{
	// Builds a netlist into the (empty) chip being edited: input pins, gate subchips, output pins and wires,
	// then lays it out like Organize All. Everything is added as a single undo step.
	public static class CircuitGenerator
	{
		const float PinSpacing = 0.5f;
		const float OutputPinX = 6;
		const float TempColumnSpacing = 1.5f;
		const float TempRowSpacing = 1.5f;

		// Returns null on success, otherwise the reason nothing was generated
		public static string Generate(Netlist netlist, GateLibrary gates, DevChipInstance chip)
		{
			Project project = Project.ActiveProject;
			if (!netlist.Success) return netlist.Error;
			if (project == null || !project.CanEditViewedChip || chip != project.ViewedChip) return "The chip can't be edited";
			if (chip.Elements.Count > 0 || chip.Wires.Count > 0) return "The chip must be empty";
			if (!gates.IsReady) return "Still checking your gate chips";

			ChipLibrary library = project.chipLibrary;
			foreach (Gate gate in netlist.Gates)
			{
				if (!gates.Has(gate.Kind)) return $"No {GateCheck.ChipName(gate.Kind)} chip available";
				string name = gates.ChipNameFor(gate.Kind);
				if (!library.HasChip(name) || !chip.CanAddSubchip(name)) return $"The {name} chip can't be used here";
			}

			project.controller.CancelEverything();

			List<IMoveable> added = new();
			PinDescription inputTemplate = library.GetChipDescription(ChipTypeHelper.GetName(ChipType.In_1Bit)).InputPins[0];
			PinDescription outputTemplate = library.GetChipDescription(ChipTypeHelper.GetName(ChipType.Out_1Bit)).OutputPins[0];

			// Input pins, A at the top
			int inputCount = netlist.InputNames.Length;
			PinInstance[] netSources = new PinInstance[inputCount + netlist.Gates.Count];
			for (int i = 0; i < inputCount; i++)
			{
				DevPinInstance pin = AddDevPin(chip, inputTemplate, netlist.InputNames[i], new Vector2(0, ColumnY(inputCount, i)), true, added);
				netSources[i] = pin.Pin;
			}

			// Gates (in topological order, so every input net already has a source pin). Temporary positions by depth; the placer moves them.
			int[] netDepth = new int[netSources.Length];
			List<int> rowsAtDepth = new();
			foreach (Gate gate in netlist.Gates)
			{
				int depth = 1;
				foreach (int net in gate.InputNets) depth = Mathf.Max(depth, netDepth[net] + 1);
				netDepth[gate.OutputNet] = depth;
				while (rowsAtDepth.Count <= depth) rowsAtDepth.Add(0);
				Vector2 tempPos = new(depth * TempColumnSpacing, -rowsAtDepth[depth]++ * TempRowSpacing);

				ChipDescription desc = library.GetChipDescription(gates.ChipNameFor(gate.Kind));
				int id = IDGenerator.GenerateNewElementID(chip);
				SubChipInstance subChip = new(desc, DescriptionCreator.CreateBuiltinSubChipDescriptionForPlacement(desc.ChipType, desc.Name, id, tempPos));
				chip.AddNewSubChip(subChip, false);
				added.Add(subChip);

				netSources[gate.OutputNet] = FindPin(subChip.OutputPins, gates.OutputPinId(gate.Kind));
				int[] inputPinIds = gates.InputPinIds(gate.Kind);
				for (int k = 0; k < gate.InputNets.Length; k++)
				{
					AddWire(chip, netSources[gate.InputNets[k]], FindPin(subChip.InputPins, inputPinIds[k]));
				}
			}

			// Output pins, in equation order from the top
			int outputCount = netlist.OutputNames.Length;
			for (int o = 0; o < outputCount; o++)
			{
				DevPinInstance pin = AddDevPin(chip, outputTemplate, netlist.OutputNames[o], new Vector2(OutputPinX, ColumnY(outputCount, o)), false, added);
				AddWire(chip, netSources[netlist.OutputNets[o]], pin.Pin);
			}

			// A failed layout still leaves a working circuit (with straight wires), so it isn't an error
			string layoutFailure = LayOut(chip);
			if (layoutFailure != null && Application.isEditor) Debug.LogError("Generated circuit layout failed: " + layoutFailure);

			// Recorded last: the undo step snapshots the final positions and wires
			chip.UndoController.RecordAddElements(added, true);
			return null;
		}

		// Stacked top-down, centred on y = 0
		public static float ColumnY(int count, int index) => (count - 1) * (PinSpacing / 2) - index * PinSpacing;

		static DevPinInstance AddDevPin(DevChipInstance chip, PinDescription template, string name, Vector2 position, bool isInput, List<IMoveable> added)
		{
			PinDescription desc = template;
			desc.ID = IDGenerator.GenerateNewElementID(chip); // unique among chip.Elements, so add before generating the next ID
			desc.Name = name;
			desc.Position = position;
			DevPinInstance pin = new(desc, isInput);
			chip.AddNewDevPin(pin, false);
			added.Add(pin);
			return pin;
		}

		static void AddWire(DevChipInstance chip, PinInstance source, PinInstance target)
		{
			WireInstance wire = new(new WireInstance.ConnectionInfo { pin = source }, new WireInstance.ConnectionInfo { pin = target }, new Vector2[2], chip.Wires.Count);
			chip.AddWire(wire, false);
		}

		static PinInstance FindPin(PinInstance[] pins, int id)
		{
			foreach (PinInstance pin in pins)
			{
				if (pin.Address.PinID == id) return pin;
			}

			throw new Exception($"Pin {id} not found");
		}

		// Same steps as ChipOrganizer.OrganizeAll, without its undo record. Returns null on success; on failure the wires are left as straight lines.
		static string LayOut(DevChipInstance chip)
		{
			DescriptionCreator.UpdateWireIndicesForDescriptionCreation(chip);
			List<WireInstance> wiresBeforeInstances = new(chip.Wires);
			WireDescription[] wiresBefore = chip.Wires.Select(DescriptionCreator.CreateWireDescription).ToArray();

			try
			{
				Dictionary<int, LayoutPoint> placed = ChipPlacer.Place(LayoutProblemBuilder.BuildPlacementInput(chip));
				foreach (IMoveable element in chip.Elements)
				{
					if (placed.TryGetValue(element.ID, out LayoutPoint p)) element.Position = LayoutProblemBuilder.ToVector(p);
				}

				LayoutResult result = WireRouter.Route(LayoutProblemBuilder.BuildRoutingProblem(chip), new RouterSettings());
				WireDescription[] wiresAfter = LayoutApplier.BuildWireDescriptions(result, wiresBeforeInstances, wiresBefore, out string failure);
				if (failure != null) return failure;

				if (chip.ReplaceAllWires(wiresAfter))
				{
					chip.ReplaceAllWires(wiresBefore);
					return "A routed wire failed to load";
				}

				return null;
			}
			catch (Exception e)
			{
				chip.ReplaceAllWires(wiresBefore);
				return $"{e.Message}\n{e.StackTrace}";
			}
		}
	}
}
