using System.Collections.Generic;

namespace DLS.Game.Editing
{
	public static class PinMatcher
	{
		// Maps each pin ID of the old chip to the pin ID of the new chip it should take the connections of, or returns null if the chips aren't compatible.
		// Chips are compatible if they have the same number of input and output pins, with matching bit counts.
		// If both chips have the same set of pin IDs (e.g. one is a copy of the other), pins are matched by ID; otherwise by their order on the chip.
		public static Dictionary<int, int> TryMatch(CorePin[] oldInputs, CorePin[] oldOutputs, CorePin[] newInputs, CorePin[] newOutputs)
		{
			Dictionary<int, int> map = new();
			if (!TryMatchSide(oldInputs, newInputs, map) || !TryMatchSide(oldOutputs, newOutputs, map)) return null;
			return map;
		}

		static bool TryMatchSide(CorePin[] oldPins, CorePin[] newPins, Dictionary<int, int> map)
		{
			if (oldPins.Length != newPins.Length) return false;

			if (TryMatchByID(oldPins, newPins, map)) return true;

			for (int i = 0; i < oldPins.Length; i++)
			{
				if (oldPins[i].BitCount != newPins[i].BitCount) return false;
				map[oldPins[i].ID] = newPins[i].ID;
			}

			return true;
		}

		static bool TryMatchByID(CorePin[] oldPins, CorePin[] newPins, Dictionary<int, int> map)
		{
			Dictionary<int, int> newBitCountByID = new();
			foreach (CorePin pin in newPins) newBitCountByID[pin.ID] = pin.BitCount;
			if (newBitCountByID.Count != newPins.Length) return false;

			foreach (CorePin pin in oldPins)
			{
				if (!newBitCountByID.TryGetValue(pin.ID, out int bitCount) || bitCount != pin.BitCount) return false;
			}

			foreach (CorePin pin in oldPins) map[pin.ID] = pin.ID;
			return true;
		}
	}
}
