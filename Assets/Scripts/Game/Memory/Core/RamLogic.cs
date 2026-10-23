namespace DLS.Game.Memory
{
	// Engine-free RAM behaviour. state[0..255] holds the cells, state[256] holds the previous clock level.
	public static class RamLogic
	{
		public const int WordCount = 256;
		public const int PrevClockIndex = WordCount;
		public const int StateLength = WordCount + 1;

		// Reads are asynchronous. Writes and reset happen only on a rising clock edge.
		public static uint Step(uint[] state, uint address, uint data, bool write, bool reset, bool clock, int dataBits)
		{
			uint mask = dataBits >= 32 ? uint.MaxValue : (1u << dataBits) - 1u;
			int addr = (int)(address & (WordCount - 1));
			bool prevClock = state[PrevClockIndex] != 0;

			if (clock && !prevClock)
			{
				// Reset wins over write when both are high on the same edge.
				if (reset)
				{
					for (int i = 0; i < WordCount; i++) state[i] = 0;
				}
				else if (write)
				{
					state[addr] = data & mask;
				}
			}

			state[PrevClockIndex] = clock ? 1u : 0u;
			return state[addr] & mask;
		}
	}
}
