using System.Collections.Generic;
using System.Linq;
using DLS.Description;
using DLS.SaveSystem;

namespace DLS.Game.Editing
{
	// Swaps subchips for a different chip type with compatible pins, keeping their wires connected
	public static class SubChipReplacer
	{
		public static bool CanReplace(SubChipInstance subChip)
		{
			Project project = Project.ActiveProject;
			if (!project.CanEditViewedChip || !IsReplaceableType(subChip.Description)) return false;

			// Subchips shown as a display on the chip are referenced by ID, so leave those alone
			ChipDescription saved = project.ViewedChip.LastSavedDescription;
			return saved?.Displays == null || saved.Displays.All(d => d.SubChipID != subChip.ID);
		}

		// Chips with instance data (key binding, ROM contents etc) and buses can't be replaced, since that data would be lost or meaningless
		static bool IsReplaceableType(ChipDescription description)
		{
			ChipType type = description.ChipType;
			(bool isInput, bool isOutput, _) = ChipTypeHelper.IsInputOrOutputPin(type);
			return !isInput && !isOutput && !ChipTypeHelper.IsBusType(type) && DescriptionCreator.CreateDefaultInstanceData(type) == null;
		}

		public static List<string> CompatibleChipNames(ChipDescription source)
		{
			Project project = Project.ActiveProject;
			List<string> names = new();

			foreach (ChipDescription candidate in project.chipLibrary.allChips)
			{
				if (candidate.NameMatch(source.Name) || !IsReplaceableType(candidate)) continue;
				if (!project.ViewedChip.CanAddSubchip(candidate.Name)) continue;
				if (MatchPins(source, candidate) == null) continue;

				names.Add(candidate.Name);
			}

			return names;
		}

		public static IEnumerable<SubChipInstance> SubChipsOfType(DevChipInstance devChip, string chipName)
		{
			return devChip.GetSubchips().Where(s => s.Description.NameMatch(chipName));
		}

		public static bool Replace(IReadOnlyList<SubChipInstance> subChips, string newChipName)
		{
			Project project = Project.ActiveProject;
			DevChipInstance devChip = project.ViewedChip;
			if (subChips.Count == 0 || !project.chipLibrary.TryGetChipDescription(newChipName, out ChipDescription newChip)) return false;
			if (!devChip.CanAddSubchip(newChipName) || !IsReplaceableType(newChip)) return false;

			// Work out pin mappings for each chip type being replaced
			Dictionary<int, Dictionary<int, int>> pinMapBySubChipID = new();
			foreach (SubChipInstance subChip in subChips)
			{
				if (!CanReplace(subChip)) return false;
				Dictionary<int, int> map = MatchPins(subChip.Description, newChip);
				if (map == null) return false;
				pinMapBySubChipID[subChip.ID] = map;
			}

			project.controller.CancelEverything();

			// Before
			DescriptionCreator.UpdateWireIndicesForDescriptionCreation(devChip);
			WireDescription[] wiresBefore = devChip.Wires.Select(DescriptionCreator.CreateWireDescription).ToArray();
			SubChipDescription[] subChipsBefore = subChips.Select(DescriptionCreator.CreateSubChipDescription).ToArray();

			// After: same ID, position and label; pin IDs remapped in wires and colour info
			SubChipDescription[] subChipsAfter = subChipsBefore.Select(before =>
			{
				Dictionary<int, int> map = pinMapBySubChipID[before.ID];
				OutputPinColourInfo[] colours = before.OutputPinColourInfo?
					.Where(c => map.ContainsKey(c.PinID))
					.Select(c => new OutputPinColourInfo(c.PinColour, map[c.PinID]))
					.ToArray();
				return new SubChipDescription(newChip.Name, before.ID, before.Label, before.Position, colours, null);
			}).ToArray();

			WireDescription[] wiresAfter = wiresBefore.Select(wire =>
			{
				wire.SourcePinAddress = RemapAddress(wire.SourcePinAddress);
				wire.TargetPinAddress = RemapAddress(wire.TargetPinAddress);
				return wire;
			}).ToArray();

			devChip.ReplaceSubChips(subChipsAfter, wiresAfter);
			devChip.UndoController.RecordReplaceSubChips(subChipsBefore, subChipsAfter, wiresBefore, wiresAfter);

			// Select the new chips
			HashSet<int> ids = subChipsAfter.Select(s => s.ID).ToHashSet();
			foreach (IMoveable element in devChip.Elements)
			{
				if (ids.Contains(element.ID)) project.controller.Select(element, true);
			}

			return true;

			PinAddress RemapAddress(PinAddress address)
			{
				if (pinMapBySubChipID.TryGetValue(address.PinOwnerID, out Dictionary<int, int> map) && map.TryGetValue(address.PinID, out int newPinID))
				{
					return new PinAddress(address.PinOwnerID, newPinID);
				}

				return address;
			}
		}

		static Dictionary<int, int> MatchPins(ChipDescription oldChip, ChipDescription newChip)
		{
			return PinMatcher.TryMatch(ToCorePins(oldChip.InputPins), ToCorePins(oldChip.OutputPins), ToCorePins(newChip.InputPins), ToCorePins(newChip.OutputPins));
		}

		static CorePin[] ToCorePins(PinDescription[] pins)
		{
			return (pins ?? new PinDescription[0]).Select(p => new CorePin(p.ID, (int)p.BitCount)).ToArray();
		}
	}
}
