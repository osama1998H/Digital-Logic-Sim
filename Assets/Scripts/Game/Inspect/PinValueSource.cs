namespace DLS.Game.Inspect
{
	// Works out which value to show when the mouse is over a pin or wire
	public static class PinValueSource
	{
		public static bool TryGetHoverInfo(IInteractable element, DevChipInstance chip, bool chipEditable, out string name, out uint state, out int bitCount)
		{
			return TryGetHoverInfo(element, chip, chipEditable, out name, out state, out bitCount, out _);
		}

		// connected is false for an input pin that no wire feeds (state is meaningless then)
		public static bool TryGetHoverInfo(IInteractable element, DevChipInstance chip, bool chipEditable, out string name, out uint state, out int bitCount, out bool connected)
		{
			name = string.Empty;
			state = 0;
			bitCount = 1;
			connected = true;

			if (element is WireInstance wire)
			{
				PinInstance source = wire.SourcePin;
				if (source == null) return false;
				Fill(source, "wire: " + source.Name, chipEditable, out name, out state, out bitCount);
				return true;
			}

			if (element is not PinInstance pin) return false;

			if (pin.IsSourcePin || pin.parent is DevPinInstance)
			{
				Fill(pin, pin.Name, chipEditable, out name, out state, out bitCount);
				return true;
			}

			// Subchip input pins never receive sim state, so read the pin driving them instead
			WireInstance feeder = FindFeedingWire(chip, pin);
			if (feeder == null || feeder.SourcePin == null)
			{
				name = pin.Name;
				bitCount = (int)pin.bitCount;
				connected = false;
				return true;
			}

			Fill(feeder.SourcePin, pin.Name, chipEditable, out name, out state, out bitCount);
			return true;
		}

		static WireInstance FindFeedingWire(DevChipInstance chip, PinInstance pin)
		{
			if (chip == null) return null;

			foreach (WireInstance wire in chip.Wires)
			{
				if (wire.TargetPin == pin || wire.TargetPin_BusCorrected == pin) return wire;
			}

			return null;
		}

		static void Fill(PinInstance pin, string displayName, bool chipEditable, out string name, out uint state, out int bitCount)
		{
			name = displayName;
			bitCount = (int)pin.bitCount;
			// Same choice as PinInstance.GetStateCol: dev input pins show the player's value (updates while paused)
			bool usePlayerState = pin.IsSourcePin && pin.parent is DevPinInstance && chipEditable;
			state = usePlayerState ? pin.PlayerInputState : pin.State;
		}
	}
}
