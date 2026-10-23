using System;

namespace DLS.Game.Probes
{
	// Window maths for the timing diagram. Samples are passed as a step array, oldest first.
	public static class WaveformView
	{
		public const float MinPxPerStep = 1;
		public const float MaxPxPerStep = 64;
		public const float DefaultPxPerStep = 8;
		const float ZoomFactorPerNotch = 1.25f;

		public static float ClampZoom(float pxPerStep) => Math.Max(MinPxPerStep, Math.Min(MaxPxPerStep, pxPerStep));

		public static float Zoom(float pxPerStep, float wheelDelta) => ClampZoom(pxPerStep * MathF.Pow(ZoomFactorPerNotch, wheelDelta));

		// Index of the first sample in the newest run of increasing steps, so a step counter that
		// jumped backwards (simulator reset) never draws older samples on top of newer ones
		public static int MonotonicStart(long[] steps, int count)
		{
			int i = count - 1;
			while (i > 0 && steps[i - 1] < steps[i]) i--;
			return Math.Max(0, i);
		}

		// Pan is counted in steps back from the newest; it can't scroll past the oldest sample
		public static long ClampPan(long pan, long oldestStep, long newestStep, long visibleSteps)
		{
			long maxPan = Math.Max(0, newestStep - oldestStep + 1 - visibleSteps);
			return Math.Max(0, Math.Min(pan, maxPan));
		}

		// First index in [start, end) whose step is >= target (end if none). Steps must be increasing.
		public static int LowerBound(long[] steps, int start, int end, long target)
		{
			int lo = start;
			int hi = end;
			while (lo < hi)
			{
				int mid = lo + (hi - lo) / 2;
				if (steps[mid] < target) lo = mid + 1;
				else hi = mid;
			}

			return lo;
		}

		public static int IndexOfStep(long[] steps, int start, int end, long step)
		{
			int i = LowerBound(steps, start, end, step);
			return i < end && steps[i] == step ? i : -1;
		}

		// The step drawn at a given distance left of the right edge, where rightStep occupies the last stepWidth
		public static long StepAtDistanceFromRight(long rightStep, double distance, double stepWidth)
		{
			if (distance < 0) distance = 0;
			return rightStep - (long)Math.Floor(distance / stepWidth);
		}
	}
}
