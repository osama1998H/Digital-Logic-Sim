using System;
using System.Collections.Generic;

namespace DLS.Game.Probes
{
	// A piece of waveform to draw. Dense spans stand for several value changes squeezed into
	// less than the minimum width, and are drawn as a filled band instead of one shape per change.
	public readonly struct WaveSpan
	{
		public readonly long StartStep;
		public readonly long EndStep; // exclusive
		public readonly uint Value; // value of the first segment (not meaningful for dense spans)
		public readonly bool Dense;

		public WaveSpan(long startStep, long endStep, uint value, bool dense)
		{
			StartStep = startStep;
			EndStep = endStep;
			Value = value;
			Dense = dense;
		}
	}

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

		// Clips segments to the window [leftStep, rightStep] and merges runs of adjacent segments narrower than
		// minSpanPx into dense spans, so the number of spans is bounded by the window's pixel width
		public static void BuildSpans(List<WaveSegment> segments, long leftStep, long rightStep, double pxPerStep, double minSpanPx, List<WaveSpan> output)
		{
			output.Clear();
			long windowEnd = rightStep + 1;
			int n = segments.Count;
			int i = 0;

			while (i < n)
			{
				WaveSegment seg = segments[i++];
				long start = Math.Max(seg.StartStep, leftStep);
				long end = Math.Min(seg.EndStep, windowEnd);
				if (end <= start) continue;

				bool dense = false;
				while (i < n && (end - start) * pxPerStep < minSpanPx)
				{
					WaveSegment next = segments[i];
					long nextEnd = Math.Min(next.EndStep, windowEnd);
					// A gap, the window edge, or a neighbour wide enough to be drawn on its own ends the run
					if (next.StartStep != end || nextEnd <= end || (nextEnd - next.StartStep) * pxPerStep >= minSpanPx) break;
					end = nextEnd;
					dense = true;
					i++;
				}

				output.Add(new WaveSpan(start, end, seg.Value, dense));
			}
		}

		// The step drawn at a given distance left of the right edge, where rightStep occupies the last stepWidth
		public static long StepAtDistanceFromRight(long rightStep, double distance, double stepWidth)
		{
			if (distance < 0) distance = 0;
			return rightStep - (long)Math.Floor(distance / stepWidth);
		}
	}
}
