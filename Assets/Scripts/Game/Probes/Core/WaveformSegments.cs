using System.Collections.Generic;

namespace DLS.Game.Probes
{
	// endStep is exclusive
	public readonly struct WaveSegment
	{
		public readonly long StartStep;
		public readonly long EndStep;
		public readonly uint Value;

		public WaveSegment(long startStep, long endStep, uint value)
		{
			StartStep = startStep;
			EndStep = endStep;
			Value = value;
		}
	}

	public static class WaveformSegments
	{
		// Merges consecutive equal values; a gap in step numbers starts a new segment
		public static List<WaveSegment> Build(long[] steps, uint[] values, int count)
		{
			List<WaveSegment> segments = new();
			if (count <= 0) return segments;

			long start = steps[0];
			long end = steps[0] + 1;
			uint value = values[0];

			for (int i = 1; i < count; i++)
			{
				if (steps[i] == end && values[i] == value)
				{
					end++;
					continue;
				}

				segments.Add(new WaveSegment(start, end, value));
				start = steps[i];
				end = steps[i] + 1;
				value = values[i];
			}

			segments.Add(new WaveSegment(start, end, value));
			return segments;
		}
	}
}
