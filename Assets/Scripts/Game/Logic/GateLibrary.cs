using DLS.Description;
using DLS.Simulation;

namespace DLS.Game.Logic
{
	// The user's NOT/AND/OR/XOR chips that are safe to use as gates: right shape, and a truth table (checked on the sim thread) that matches.
	// Any kind that is missing or fails is built from the builtin NAND instead.
	public sealed class GateLibrary
	{
		public const string NandName = "NAND";
		static readonly int[] nandInputPinIds = { 0, 1 }; // IN B (top), IN A
		const int NandOutputPinId = 2;

		// Indexed by GateKind
		readonly ChipDescription[] candidates = new ChipDescription[5];
		readonly TruthTableJob[] jobs = new TruthTableJob[5];
		GateSet available;
		bool isReady;

		GateLibrary()
		{
		}

		// Call on the main thread (the jobs build their simulation copies here)
		public static GateLibrary VerifyAsync(ChipLibrary lib)
		{
			GateLibrary gates = new();

			foreach (GateKind kind in GateCheck.UserGateKinds)
			{
				// The library lookup ignores case
				if (!lib.TryGetChipDescription(GateCheck.ChipName(kind), out ChipDescription chip) || !HasShape(kind, chip)) continue;

				TruthTableJob job;
				try
				{
					job = new TruthTableJob(chip, lib);
				}
				catch (System.Exception)
				{
					continue;
				}

				if (!job.CanRun(out _)) continue;

				gates.candidates[(int)kind] = chip;
				gates.jobs[(int)kind] = job;
				Simulator.EnqueueEvaluationJob(job);
			}

			return gates;
		}

		public bool IsReady
		{
			get
			{
				Poll();
				return isReady;
			}
		}

		// Only meaningful once IsReady
		public GateSet Available
		{
			get
			{
				Poll();
				return available;
			}
		}

		public bool Has(GateKind kind) => kind == GateKind.Nand || (Available & GateCheck.Flag(kind)) != 0;

		public string ChipNameFor(GateKind kind) => kind == GateKind.Nand ? NandName : VerifiedChip(kind).Name;

		public int[] InputPinIds(GateKind kind)
		{
			if (kind == GateKind.Nand) return (int[])nandInputPinIds.Clone();
			PinDescription[] pins = VerifiedChip(kind).InputPins;
			int[] ids = new int[pins.Length];
			for (int i = 0; i < ids.Length; i++) ids[i] = pins[i].ID;
			return ids;
		}

		public int OutputPinId(GateKind kind) => kind == GateKind.Nand ? NandOutputPinId : VerifiedChip(kind).OutputPins[0].ID;

		public void Cancel()
		{
			foreach (TruthTableJob job in jobs)
			{
				job?.Cancel();
			}
		}

		ChipDescription VerifiedChip(GateKind kind)
		{
			if (!Has(kind)) throw new System.ArgumentException($"No verified {GateCheck.ChipName(kind)} chip", nameof(kind));
			return candidates[(int)kind];
		}

		void Poll()
		{
			if (isReady) return;

			GateSet verified = GateSet.None;
			foreach (GateKind kind in GateCheck.UserGateKinds)
			{
				TruthTableJob job = jobs[(int)kind];
				if (job == null) continue;
				if (!job.IsFinished) return;
				if (!job.IsCancelled && job.Error == null && GateCheck.Matches(kind, job.Table)) verified |= GateCheck.Flag(kind);
			}

			available = verified;
			isReady = true;
		}

		static bool HasShape(GateKind kind, ChipDescription chip)
		{
			int[] inputBits = new int[chip.InputPins.Length];
			for (int i = 0; i < inputBits.Length; i++) inputBits[i] = (int)chip.InputPins[i].BitCount;
			int[] outputBits = new int[chip.OutputPins.Length];
			for (int i = 0; i < outputBits.Length; i++) outputBits[i] = (int)chip.OutputPins[i].BitCount;
			return GateCheck.HasShape(kind, inputBits, outputBits);
		}
	}
}
