using System;

namespace DLS.Game.Logic
{
	public readonly struct TruthPin
	{
		public readonly string Name;
		public readonly int BitCount;

		public TruthPin(string name, int bitCount)
		{
			Name = name;
			BitCount = bitCount;
		}
	}

	// Rows enumerate every input combination; the first input is the most significant.
	// Outputs hold raw pin states (bits | floating flags << 16), as read from the simulation.
	public sealed class TruthTable
	{
		public const int MaxInputBits = 16;

		public TruthPin[] Inputs { get; }
		public TruthPin[] Outputs { get; }
		public int TotalInputBits { get; }
		public int RowCount { get; }
		public int RowsFilled { get; private set; }

		readonly uint[] outputStates;
		readonly bool[] rowFilled;

		public TruthTable(TruthPin[] inputs, TruthPin[] outputs)
		{
			Inputs = inputs;
			Outputs = outputs;

			foreach (TruthPin pin in inputs)
			{
				if (pin.BitCount < 1) throw new ArgumentException($"Input '{pin.Name}' must have at least 1 bit.", nameof(inputs));
				TotalInputBits += pin.BitCount;
			}
			if (TotalInputBits > MaxInputBits) throw new ArgumentException($"Truth table inputs exceed {MaxInputBits} bits.", nameof(inputs));

			RowCount = 1 << TotalInputBits;
			outputStates = new uint[RowCount * outputs.Length];
			rowFilled = new bool[RowCount];
		}

		public uint GetInputValue(int row, int inputIndex)
		{
			int shift = 0;
			for (int i = inputIndex + 1; i < Inputs.Length; i++) shift += Inputs[i].BitCount;
			int bits = Inputs[inputIndex].BitCount;
			return ((uint)row >> shift) & ((1u << bits) - 1);
		}

		public void SetOutputStates(int row, uint[] states)
		{
			if (!rowFilled[row])
			{
				rowFilled[row] = true;
				RowsFilled++;
			}
			Array.Copy(states, 0, outputStates, row * Outputs.Length, Outputs.Length);
		}

		public uint GetOutputState(int row, int outputIndex) => outputStates[row * Outputs.Length + outputIndex];
	}
}
