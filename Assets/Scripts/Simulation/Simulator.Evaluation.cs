using System;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace DLS.Simulation
{
	// A unit of work (e.g. evaluating a chip for every input combination) that runs in small slices on the sim thread,
	// between live simulation steps, so it never races with the live simulation.
	public abstract class EvaluationJob
	{
		public volatile bool IsFinished;
		public volatile bool IsCancelled;
		public volatile float Progress;
		public volatile string Error;

		// Called repeatedly on the sim thread until it returns true. Results should be written before returning true:
		// the volatile write of IsFinished that follows publishes them to the thread that reads IsFinished.
		protected internal abstract bool RunSlice();

		public void Cancel() => IsCancelled = true;
	}

	public static partial class Simulator
	{
		// Set only while an isolated step is running, so evaluations neither read nor build the shared LUT caches (sim thread only)
		static bool bypassCaches;

		static readonly ConcurrentQueue<EvaluationJob> evaluationJobs = new();

		// When set (on the building thread only), random initial state such as RAM contents comes from this instead of the shared rng
		[ThreadStatic] internal static Random buildRng;

		// Like BuildSimChip, but random initial state (RAM contents) is the same on every call
		public static SimChip BuildSimChipDeterministic(DLS.Description.ChipDescription chipDesc, DLS.Game.ChipLibrary library)
		{
			Random old = buildRng;
			buildRng = new Random(12345);
			try
			{
				return BuildSimChip(chipDesc, library);
			}
			finally
			{
				buildRng = old;
			}
		}

		// Call from the main thread
		public static void EnqueueEvaluationJob(EvaluationJob job)
		{
			if (job == null) throw new ArgumentNullException(nameof(job));
			evaluationJobs.Enqueue(job);
		}

		// Note: this should only be called from the sim thread. Runs slices of the job at the head of the queue until the time budget is used up.
		public static void RunEvaluationJobs(double maxMilliseconds)
		{
			if (evaluationJobs.IsEmpty) return;

			long startTimestamp = Stopwatch.GetTimestamp();
			long budgetTicks = (long)(maxMilliseconds * Stopwatch.Frequency / 1000.0);

			// Only the sim thread dequeues, so the peeked job is still the head when it is dequeued below
			while (evaluationJobs.TryPeek(out EvaluationJob job))
			{
				if (!job.IsCancelled)
				{
					bool done;
					try
					{
						done = job.RunSlice();
					}
					catch (Exception e)
					{
						// A failing job must never take down the sim loop
						job.Error = string.IsNullOrEmpty(e.Message) ? e.GetType().Name : e.Message;
						UnityEngine.Debug.LogException(e);
						done = true;
					}

					if (!done && !job.IsCancelled)
					{
						if (Stopwatch.GetTimestamp() - startTimestamp >= budgetTicks) return;
						continue;
					}
				}

				evaluationJobs.TryDequeue(out _);
				job.IsFinished = true;

				if (Stopwatch.GetTimestamp() - startTimestamp >= budgetTicks) return;
			}
		}

		// Steps a chip tree that is not the live root, without disturbing the live simulation.
		// Each call needs a frame number not used before by this tree, so pins don't count inputs from an earlier step.
		internal static void StepIsolated(SimChip root, bool firstStep, int frame, Random evalRng)
		{
			int simulationFrameOld = simulationFrame;
			bool canDynamicReorderOld = canDynamicReorderThisFrame;
			uint pcgRngStateOld = pcg_rngState;
			bool bypassCachesOld = bypassCaches;
			Random stepRngOld = stepRng;

			try
			{
				simulationFrame = frame;
				canDynamicReorderThisFrame = false;
				pcg_rngState = (uint)frame * 747796405u + 1;
				bypassCaches = true;
				stepRng = evalRng;

				root.ResetReceivedFlagsOnAllPins();
				// A built-in root has no subchips, so it must be processed directly
				if (root.IsBuiltin) ProcessBuiltinChip(root);
				else if (firstStep) StepChipReorder(root);
				else StepChip(root);
			}
			finally
			{
				simulationFrame = simulationFrameOld;
				canDynamicReorderThisFrame = canDynamicReorderOld;
				pcg_rngState = pcgRngStateOld;
				bypassCaches = bypassCachesOld;
				stepRng = stepRngOld;
			}
		}
	}
}
