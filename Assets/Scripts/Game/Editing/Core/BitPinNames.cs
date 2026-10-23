namespace DLS.Game.Editing
{
	public static class BitPinNames
	{
		// Pin names for the built-in split/merge chips. Pins are ordered top to bottom from the most significant bits down,
		// so the names show which bit (or range of bits) of the wide pin each one carries.
		public static string ForConversionChip(bool isInput, int pinIndex, int pinCount, int bitsPerPin)
		{
			string prefix = isInput ? "IN" : "OUT";
			if (pinCount == 1) return prefix;

			int highBit = (pinCount - 1 - pinIndex) * bitsPerPin + bitsPerPin - 1;
			int lowBit = highBit - bitsPerPin + 1;
			return bitsPerPin == 1 ? $"{prefix} {highBit}" : $"{prefix} {highBit}-{lowBit}";
		}
	}
}
