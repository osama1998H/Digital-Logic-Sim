using System;
using System.Threading;

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
	// Contract: one writer fills rows strictly in order (row == RowsFilled), so a reader on
	// another thread can read any row below RowsFilled without locking.
	public sealed class TruthTable
	{
		public const int MaxInputBits = 16;

		public TruthPin[] Inputs { get; }
		public TruthPin[] Outputs { get; }
		public int TotalInputBits { get; }
		public int RowCount { get; }

		public int RowsFilled => Volatile.Read(ref rowsFilled);

		readonly uint[] outputStates;
		int rowsFilled;

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
			if (row != rowsFilled) throw new InvalidOperationException($"Rows must be filled in order: expected {rowsFilled}, got {row}.");
			if (states.Length < Outputs.Length) throw new ArgumentException($"Expected at least {Outputs.Length} output states.", nameof(states));

			Array.Copy(states, 0, outputStates, row * Outputs.Length, Outputs.Length);
			// Publish only after the data is written, so a reader never sees an unwritten row.
			Volatile.Write(ref rowsFilled, row + 1);
		}

		public uint GetOutputState(int row, int outputIndex) => outputStates[row * Outputs.Length + outputIndex];
	}
}
