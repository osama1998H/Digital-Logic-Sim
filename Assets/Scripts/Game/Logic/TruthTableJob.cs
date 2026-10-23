using DLS.Description;
using DLS.Simulation;

namespace DLS.Game.Logic
{
	// Fills a truth table by stepping a privately built copy of the chip, in slices on the sim thread.
	public sealed class TruthTableJob : EvaluationJob
	{
		const int RowsPerSlice = 256;

		public TruthTable Table { get; }

		readonly SimEvaluator evaluator;
		readonly string cannotRunReason;
		readonly uint[] rowOutputs;
		int nextRow;

		// Call on the main thread: builds the sim tree here so the sim thread never touches shared description state
		public TruthTableJob(ChipDescription chip, ChipLibrary library)
		{
			int totalInputBits = 0;
			foreach (PinDescription pin in chip.InputPins) totalInputBits += (int)pin.BitCount;

			if (totalInputBits > TruthTable.MaxInputBits)
			{
				cannotRunReason = $"Too many input bits ({totalInputBits} > {TruthTable.MaxInputBits})";
				return;
			}

			SimChip root;
			try
			{
				string missing = FindMissingSubChip(chip, library, 0);
				if (missing != null)
				{
					cannotRunReason = $"Subchip '{missing}' is missing from the library";
					return;
				}

				root = Simulator.BuildSimChip(chip, library);
				if (!root.IsCombinational()) root = null;
			}
			catch (System.Exception e)
			{
				cannotRunReason = "Can't evaluate this chip: " + e.Message;
				return;
			}

			if (root == null)
			{
				cannotRunReason = "This chip has memory, a clock or feedback; use the Test Bench";
				return;
			}

			Table = new TruthTable(ToTruthPins(chip.InputPins), ToTruthPins(chip.OutputPins));
			evaluator = new SimEvaluator(root);
			rowOutputs = new uint[chip.OutputPins.Length];
		}

		public bool CanRun(out string reason)
		{
			reason = cannotRunReason;
			return cannotRunReason == null;
		}

		protected internal override bool RunSlice()
		{
			int end = System.Math.Min(nextRow + RowsPerSlice, Table.RowCount);

			for (; nextRow < end; nextRow++)
			{
				if (IsCancelled) return true;

				for (int i = 0; i < Table.Inputs.Length; i++) evaluator.SetInput(i, Table.GetInputValue(nextRow, i));
				evaluator.Step();
				for (int o = 0; o < rowOutputs.Length; o++) rowOutputs[o] = evaluator.GetOutput(o);
				Table.SetOutputStates(nextRow, rowOutputs);
			}

			Progress = (float)nextRow / Table.RowCount;
			return nextRow >= Table.RowCount;
		}

		internal static string FindMissingSubChip(ChipDescription chip, ChipLibrary library, int depth)
		{
			if (depth > 64) return null;
			foreach (SubChipDescription sub in chip.SubChips)
			{
				if (!library.TryGetChipDescription(sub.Name, out ChipDescription subDesc)) return sub.Name;
				string missing = FindMissingSubChip(subDesc, library, depth + 1);
				if (missing != null) return missing;
			}

			return null;
		}

		internal static TruthPin[] ToTruthPins(PinDescription[] pins)
		{
			TruthPin[] result = new TruthPin[pins.Length];
			for (int i = 0; i < pins.Length; i++) result[i] = new TruthPin(pins[i].Name, (int)pins[i].BitCount);
			return result;
		}
	}
}
