namespace DLS.Simulation
{
	// Drives a privately built SimChip tree (from Simulator.BuildSimChip), one isolated step at a time.
	// Note: Step and StepUntilStable must only be called from the sim thread (i.e. from an EvaluationJob).
	public sealed class SimEvaluator
	{
		public SimChip Root { get; }

		readonly uint[] prevOutputs;
		// Frame numbers far above the live simulation's, so this tree's pins never see a frame index twice
		int frame = 1 << 30;
		bool hasStepped;

		public SimEvaluator(SimChip root)
		{
			Root = root;
			prevOutputs = new uint[root.OutputPins.Length];
		}

		public void SetInput(int inputIndex, uint bits)
		{
			SimPin pin = Root.InputPins[inputIndex];
			uint mask = (1u << (int)pin.numberOfBits) - 1;
			pin.State = bits & mask;
		}

		public uint GetOutput(int outputIndex) => Root.OutputPins[outputIndex].State;

		public void Step()
		{
			// A freshly built tree has its subchips in description order, so the first step determines the traversal order
			Simulator.StepIsolated(Root, !hasStepped, frame);
			frame++;
			hasStepped = true;
		}

		// Returns the number of steps taken, or -1 if the outputs never stayed unchanged for stableSteps consecutive steps within maxSteps
		public int StepUntilStable(int stableSteps, int maxSteps)
		{
			if (stableSteps < 1) stableSteps = 1;

			int numStableSteps = 0;
			for (int step = 1; step <= maxSteps; step++)
			{
				SaveOutputs();
				Step();

				if (OutputsUnchanged()) numStableSteps++;
				else numStableSteps = 0;

				if (numStableSteps >= stableSteps) return step;
			}

			return -1;
		}

		void SaveOutputs()
		{
			for (int i = 0; i < prevOutputs.Length; i++)
			{
				prevOutputs[i] = Root.OutputPins[i].State;
			}
		}

		bool OutputsUnchanged()
		{
			for (int i = 0; i < prevOutputs.Length; i++)
			{
				if (prevOutputs[i] != Root.OutputPins[i].State) return false;
			}

			return true;
		}
	}
}
