using System.Collections.Generic;
using DLS.Description;
using DLS.Simulation;

namespace DLS.Game.Logic
{
	// Runs test lines against a privately built copy of a chip, in slices on the sim thread.
	// Inputs start at 0 and keep their value from line to line, so sequential chips can be clocked by hand.
	public sealed class TestBenchJob : EvaluationJob
	{
		const int LinesPerSlice = 8;
		const int StableSteps = 3;
		const int MaxSteps = 1000;

		// null = no check, "PASS", "FAIL: ..." or "ERROR: ...". Only read it once IsFinished is true
		public string[] LineResults { get; }
		public int Passed { get; private set; }
		public int Failed { get; private set; }
		public string Warning { get; }

		readonly SimEvaluator evaluator;
		readonly string cannotRunReason;
		readonly TestVector[] vectors;
		readonly TruthPin[] outputPins;
		readonly uint[] outputStates;
		readonly uint[] inputValues;
		int nextLine;

		// Call on the main thread: builds the sim tree here so the sim thread never touches shared description state
		public TestBenchJob(ChipDescription chip, ChipLibrary library, IReadOnlyList<string> lines)
		{
			LineResults = new string[lines.Count];
			vectors = new TestVector[lines.Count];

			SimChip root;
			try
			{
				string missing = TruthTableJob.FindMissingSubChip(chip, library, 0);
				if (missing != null)
				{
					cannotRunReason = $"Subchip '{missing}' is missing from the library";
					return;
				}

				root = Simulator.BuildSimChip(chip, library);
			}
			catch (System.Exception e)
			{
				cannotRunReason = "Can't evaluate this chip: " + e.Message;
				return;
			}

			TruthPin[] inputPins = TruthTableJob.ToTruthPins(chip.InputPins);
			outputPins = TruthTableJob.ToTruthPins(chip.OutputPins);

			for (int i = 0; i < lines.Count; i++)
			{
				if (TestVectorParser.TryParse(lines[i], i, inputPins, outputPins, out TestVector vector, out string error)) vectors[i] = vector;
				else LineResults[i] = "ERROR: " + error;
			}

			string timeDependent = FindTimeDependentChip(chip, library, 0);
			if (timeDependent != null) Warning = $"contains {timeDependent}: its output depends on time; drive clocks from an input pin for repeatable tests";

			evaluator = new SimEvaluator(root);
			outputStates = new uint[outputPins.Length];
			inputValues = new uint[inputPins.Length];
		}

		public bool CanRun(out string reason)
		{
			reason = cannotRunReason;
			return cannotRunReason == null;
		}

		protected internal override bool RunSlice()
		{
			int end = System.Math.Min(nextLine + LinesPerSlice, vectors.Length);

			for (; nextLine < end; nextLine++)
			{
				if (IsCancelled) return true;
				TestVector vector = vectors[nextLine];
				if (vector == null) continue;

				foreach ((int pin, uint value) in vector.Inputs) inputValues[pin] = value;
				for (int i = 0; i < inputValues.Length; i++) evaluator.SetInput(i, inputValues[i]);

				bool settled = evaluator.StepUntilStable(StableSteps, MaxSteps) >= 0;
				if (!settled)
				{
					LineResults[nextLine] = "FAIL: outputs did not settle (oscillation?)";
					Failed++;
					continue;
				}

				if (!vector.HasCheck) continue;

				for (int o = 0; o < outputStates.Length; o++) outputStates[o] = evaluator.GetOutput(o);
				string failure = TestVectorChecker.Check(vector, outputStates, outputPins);
				if (failure == null)
				{
					LineResults[nextLine] = "PASS";
					Passed++;
				}
				else
				{
					LineResults[nextLine] = "FAIL: " + failure;
					Failed++;
				}
			}

			Progress = vectors.Length == 0 ? 1 : (float)nextLine / vectors.Length;
			return nextLine >= vectors.Length;
		}

		// Returns the name of the first chip whose output depends on time (CLOCK, KEY, RTC, PULSE), or null
		static string FindTimeDependentChip(ChipDescription chip, ChipLibrary library, int depth)
		{
			string own = TimeDependentName(chip.ChipType);
			if (own != null) return own;
			if (depth > 64) return null;

			foreach (SubChipDescription sub in chip.SubChips)
			{
				if (!library.TryGetChipDescription(sub.Name, out ChipDescription subDesc)) continue;
				string found = FindTimeDependentChip(subDesc, library, depth + 1);
				if (found != null) return found;
			}

			return null;
		}

		static string TimeDependentName(ChipType type)
		{
			switch (type)
			{
				case ChipType.Clock: return "CLOCK";
				case ChipType.Key: return "KEY";
				case ChipType.RTC: return "RTC";
				case ChipType.Pulse: return "PULSE";
				default: return null;
			}
		}
	}
}
