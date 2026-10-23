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
			Build(steps, values, 0, count, segments);
			return segments;
		}

		// Same as above for samples [start, start + count), written into a reused list (cleared first)
		public static void Build(long[] steps, uint[] values, int start, int count, List<WaveSegment> output)
		{
			output.Clear();
			if (count <= 0) return;

			long segStart = steps[start];
			long end = segStart + 1;
			uint value = values[start];

			for (int i = start + 1; i < start + count; i++)
			{
				if (steps[i] == end && values[i] == value)
				{
					end++;
					continue;
				}

				output.Add(new WaveSegment(segStart, end, value));
				segStart = steps[i];
				end = steps[i] + 1;
				value = values[i];
			}

			output.Add(new WaveSegment(segStart, end, value));
		}
	}
}
