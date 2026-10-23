using System;
using System.Collections.Generic;
using System.Threading;
using DLS.Game.Probes;
using NUnit.Framework;

namespace LayoutTests
{
	public class ProbeCoreTests
	{
		static uint[][] MakeOut(int n, int probeCount)
		{
			uint[][] v = new uint[n][];
			for (int i = 0; i < n; i++) v[i] = new uint[probeCount];
			return v;
		}

		[Test]
		public void Ring_KeepsWrittenOrder()
		{
			SampleRing ring = new(2, 128);
			for (int i = 0; i < 10; i++) ring.Write(i + 5, new uint[] { (uint)i, (uint)(i * 2) });

			long[] steps = new long[64];
			uint[][] vals = MakeOut(64, 2);
			int n = ring.CopyLatest(64, steps, vals);

			Assert.That(n, Is.EqualTo(10));
			for (int i = 0; i < 10; i++)
			{
				Assert.That(steps[i], Is.EqualTo(i + 5));
				Assert.That(vals[i][0], Is.EqualTo((uint)i));
				Assert.That(vals[i][1], Is.EqualTo((uint)(i * 2)));
			}
			Assert.That(ring.LatestStep, Is.EqualTo(14));
		}

		[Test]
		public void Ring_WrapsAndKeepsNewest()
		{
			SampleRing ring = new(1, 128);
			for (int i = 0; i < 1000; i++) ring.Write(i, new uint[] { (uint)i });

			long[] steps = new long[64];
			uint[][] vals = MakeOut(64, 1);
			int n = ring.CopyLatest(64, steps, vals);

			Assert.That(n, Is.EqualTo(64));
			Assert.That(steps[0], Is.EqualTo(936));
			Assert.That(steps[63], Is.EqualTo(999));
			for (int i = 0; i < n; i++) Assert.That(vals[i][0], Is.EqualTo((uint)steps[i]));
		}

		[Test]
		public void Ring_CopyLatestNeverReadsMoreThanCapacityMinus64()
		{
			SampleRing ring = new(1, 128);
			for (int i = 0; i < 1000; i++) ring.Write(i, new uint[] { (uint)i });
			long[] steps = new long[500];
			uint[][] vals = MakeOut(500, 1);
			Assert.That(ring.CopyLatest(500, steps, vals), Is.EqualTo(64));
			Assert.That(steps[0], Is.EqualTo(936));
		}

		[Test]
		public void Ring_ReaderNeverSeesHalfWrittenSlot()
		{
			SampleRing ring = new(2, 128);
			bool stop = false;
			Thread writer = new(() =>
			{
				long step = 0;
				uint[] v = new uint[2];
				while (!Volatile.Read(ref stop))
				{
					v[0] = v[1] = (uint)step;
					ring.Write(step, v);
					step++;
				}
			});
			writer.Start();
			long start = Environment.TickCount64;
			long deadline = start + 200;
			long hardCap = start + 5000;

			long[] steps = new long[64];
			uint[][] vals = MakeOut(64, 2);
			long reads = 0, samples = 0;
			string failure = null;
			while (failure == null && (Environment.TickCount64 < deadline || samples < 10000) && Environment.TickCount64 < hardCap)
			{
				int n = ring.CopyLatest(64, steps, vals);
				reads++;
				if (n > 64) failure = "count " + n + " exceeds maxCount";
				for (int i = 0; i < n && failure == null; i++)
				{
					samples++;
					if (vals[i][0] != (uint)steps[i] || vals[i][1] != (uint)steps[i]) failure = $"torn sample at {i}: step {steps[i]} values {vals[i][0]},{vals[i][1]}";
					else if (i > 0 && steps[i] != steps[i - 1] + 1) failure = $"steps not consecutive at {i}: {steps[i - 1]} then {steps[i]}";
				}
			}
			Volatile.Write(ref stop, true);
			writer.Join();

			Assert.That(failure, Is.Null);
			Assert.That(reads, Is.GreaterThan(0));
			Assert.That(samples, Is.GreaterThan(0));
		}

		[Test]
		public void Ring_DiscardsSlotsOverwrittenDuringCopy()
		{
			// Deterministic lap: the writer overwrites slots mid-copy via the test hook.
			SampleRing ring = new(1, 128);
			for (int i = 0; i < 128; i++) ring.Write(i, new uint[] { (uint)i });
			ring.AfterCopyHook = () =>
			{
				ring.AfterCopyHook = null;
				for (int i = 128; i < 128 + 100; i++) ring.Write(i, new uint[] { (uint)i + 1000000 });
			};

			long[] steps = new long[64];
			uint[][] vals = MakeOut(64, 1);
			int n = ring.CopyLatest(64, steps, vals);

			Assert.That(n, Is.GreaterThan(0));
			for (int i = 0; i < n; i++)
			{
				Assert.That(vals[i][0], Is.EqualTo((uint)steps[i]), "stale/overwritten sample leaked at " + i);
				if (i > 0) Assert.That(steps[i], Is.EqualTo(steps[i - 1] + 1));
			}
			// Newest copied sample is the last one that was published before the copy began.
			Assert.That(steps[n - 1], Is.EqualTo(127));
			Assert.That(steps[0], Is.EqualTo(101));
		}

		[Test]
		public void Ring_ClearDropsHistory()
		{
			SampleRing ring = new(1, 128);
			for (int i = 0; i < 20; i++) ring.Write(i, new uint[] { 7 });
			ring.Clear();

			long[] steps = new long[64];
			uint[][] vals = MakeOut(64, 1);
			// Pending clear reads as empty before the writer acts on it.
			Assert.That(ring.CopyLatest(64, steps, vals), Is.EqualTo(0));
			Assert.That(ring.LatestStep, Is.EqualTo(-1));

			ring.Write(100, new uint[] { 9 });
			int n = ring.CopyLatest(64, steps, vals);
			Assert.That(n, Is.EqualTo(1));
			Assert.That(steps[0], Is.EqualTo(100));
			Assert.That(vals[0][0], Is.EqualTo(9u));
			Assert.That(ring.LatestStep, Is.EqualTo(100));
		}

		[Test]
		public void Segments_MergeEqualRuns()
		{
			long[] steps = { 10, 11, 12, 13, 14, 15 };
			uint[] vals = { 0, 0, 1, 1, 1, 0 };
			List<WaveSegment> segs = WaveformSegments.Build(steps, vals, 6);

			Assert.That(segs.Count, Is.EqualTo(3));
			AssertSeg(segs[0], 10, 12, 0);
			AssertSeg(segs[1], 12, 15, 1);
			AssertSeg(segs[2], 15, 16, 0);
		}

		[Test]
		public void Segments_GapStartsNewSegment()
		{
			long[] steps = { 1, 2, 3, 10, 11 };
			uint[] vals = { 5, 5, 5, 5, 5 };
			List<WaveSegment> segs = WaveformSegments.Build(steps, vals, 5);

			Assert.That(segs.Count, Is.EqualTo(2));
			AssertSeg(segs[0], 1, 4, 5);
			AssertSeg(segs[1], 10, 12, 5);
		}

		[Test]
		public void Segments_EmptyAndSingle()
		{
			Assert.That(WaveformSegments.Build(new long[0], new uint[0], 0).Count, Is.EqualTo(0));
			List<WaveSegment> segs = WaveformSegments.Build(new long[] { 4 }, new uint[] { 2 }, 1);
			Assert.That(segs.Count, Is.EqualTo(1));
			AssertSeg(segs[0], 4, 5, 2);
		}

		[Test]
		public void Segments_BuildIntoReusesListAndHonoursStart()
		{
			long[] steps = { 1, 2, 3, 4, 5, 6 };
			uint[] vals = { 9, 9, 7, 7, 7, 1 };
			List<WaveSegment> output = new() { new WaveSegment(0, 1, 0) };
			WaveformSegments.Build(steps, vals, 1, 4, output);

			Assert.That(output.Count, Is.EqualTo(2));
			AssertSeg(output[0], 2, 3, 9);
			AssertSeg(output[1], 3, 6, 7);

			WaveformSegments.Build(steps, vals, 0, 0, output);
			Assert.That(output.Count, Is.EqualTo(0));
		}

		[Test]
		public void View_ZoomClampsToRange()
		{
			Assert.That(WaveformView.ClampZoom(0.2f), Is.EqualTo(WaveformView.MinPxPerStep));
			Assert.That(WaveformView.ClampZoom(500), Is.EqualTo(WaveformView.MaxPxPerStep));
			Assert.That(WaveformView.Zoom(8, 1), Is.GreaterThan(8f));
			Assert.That(WaveformView.Zoom(8, -1), Is.LessThan(8f));
			Assert.That(WaveformView.Zoom(64, 10), Is.EqualTo(64f));
			Assert.That(WaveformView.Zoom(1, -10), Is.EqualTo(1f));
			Assert.That(WaveformView.Zoom(8, 0), Is.EqualTo(8f));
		}

		[Test]
		public void View_MonotonicStartSkipsBeforeBackwardJump()
		{
			Assert.That(WaveformView.MonotonicStart(new long[] { 5, 6, 7 }, 3), Is.EqualTo(0));
			Assert.That(WaveformView.MonotonicStart(new long[] { 90, 91, 0, 1, 2 }, 5), Is.EqualTo(2));
			Assert.That(WaveformView.MonotonicStart(new long[] { 1, 2, 9, 3 }, 3), Is.EqualTo(0)); // only the first 3 count
			Assert.That(WaveformView.MonotonicStart(new long[0], 0), Is.EqualTo(0));
		}

		[Test]
		public void View_LowerBoundAndIndexOfStep()
		{
			long[] steps = { 10, 11, 12, 20, 21 };
			Assert.That(WaveformView.LowerBound(steps, 0, 5, 0), Is.EqualTo(0));
			Assert.That(WaveformView.LowerBound(steps, 0, 5, 12), Is.EqualTo(2));
			Assert.That(WaveformView.LowerBound(steps, 0, 5, 13), Is.EqualTo(3));
			Assert.That(WaveformView.LowerBound(steps, 0, 5, 99), Is.EqualTo(5));
			Assert.That(WaveformView.LowerBound(steps, 2, 5, 10), Is.EqualTo(2));

			Assert.That(WaveformView.IndexOfStep(steps, 0, 5, 20), Is.EqualTo(3));
			Assert.That(WaveformView.IndexOfStep(steps, 0, 5, 15), Is.EqualTo(-1)); // gap
			Assert.That(WaveformView.IndexOfStep(steps, 0, 5, 22), Is.EqualTo(-1));
			Assert.That(WaveformView.IndexOfStep(steps, 1, 5, 10), Is.EqualTo(-1)); // before start
		}

		[Test]
		public void View_PanIsClampedToHistory()
		{
			// 100 samples (steps 0..99), 30 visible: can go back at most 70
			Assert.That(WaveformView.ClampPan(-5, 0, 99, 30), Is.EqualTo(0));
			Assert.That(WaveformView.ClampPan(40, 0, 99, 30), Is.EqualTo(40));
			Assert.That(WaveformView.ClampPan(500, 0, 99, 30), Is.EqualTo(70));
			// Less history than fits on screen: no panning
			Assert.That(WaveformView.ClampPan(10, 0, 9, 30), Is.EqualTo(0));
		}

		[Test]
		public void View_StepAtDistanceFromRightEdge()
		{
			// Step 100 occupies the 8 px left of the right edge
			Assert.That(WaveformView.StepAtDistanceFromRight(100, 0.5, 8), Is.EqualTo(100));
			Assert.That(WaveformView.StepAtDistanceFromRight(100, 7.9, 8), Is.EqualTo(100));
			Assert.That(WaveformView.StepAtDistanceFromRight(100, 8, 8), Is.EqualTo(99));
			Assert.That(WaveformView.StepAtDistanceFromRight(100, 80.1, 8), Is.EqualTo(90));
			Assert.That(WaveformView.StepAtDistanceFromRight(100, -1, 8), Is.EqualTo(100));
		}

		static List<WaveSegment> AlternatingSegments(int n)
		{
			long[] steps = new long[n];
			uint[] vals = new uint[n];
			for (int i = 0; i < n; i++)
			{
				steps[i] = i;
				vals[i] = (uint)(i & 1);
			}

			return WaveformSegments.Build(steps, vals, n);
		}

		[Test]
		public void Spans_DenseSignalIsBoundedByPixelWidth()
		{
			// 4096 alternating samples, 1 px per step, window of 800 px (the newest 800 steps)
			List<WaveSegment> segs = AlternatingSegments(4096);
			List<WaveSpan> spans = new();

			WaveformView.BuildSpans(segs, 4096 - 800, 4095, 1, 1, spans);
			Assert.That(spans.Count, Is.LessThanOrEqualTo(800));

			WaveformView.BuildSpans(segs, 4096 - 800, 4095, 1, 3, spans);
			Assert.That(spans.Count, Is.LessThanOrEqualTo(800 / 3 + 1));
			Assert.That(spans.TrueForAll(s => s.StartStep >= 4096 - 800 && s.EndStep <= 4096), Is.True);
			Assert.That(spans.Exists(s => s.Dense), Is.True);

			// Spans tile the window without gaps or overlaps
			for (int i = 1; i < spans.Count; i++) Assert.That(spans[i].StartStep, Is.EqualTo(spans[i - 1].EndStep));
			Assert.That(spans[0].StartStep, Is.EqualTo(4096 - 800));
			Assert.That(spans[^1].EndStep, Is.EqualTo(4096));
		}

		[Test]
		public void Spans_SparseSignalIsUnchanged()
		{
			long[] steps = { 0, 1, 2, 3, 4, 5, 6, 7 };
			uint[] vals = { 0, 0, 1, 1, 1, 0, 0, 1 };
			List<WaveSegment> segs = WaveformSegments.Build(steps, vals, 8);
			List<WaveSpan> spans = new();

			WaveformView.BuildSpans(segs, 0, 7, 8, 3, spans);

			Assert.That(spans.Count, Is.EqualTo(segs.Count));
			for (int i = 0; i < segs.Count; i++)
			{
				Assert.That(spans[i].StartStep, Is.EqualTo(segs[i].StartStep));
				Assert.That(spans[i].EndStep, Is.EqualTo(segs[i].EndStep));
				Assert.That(spans[i].Value, Is.EqualTo(segs[i].Value));
				Assert.That(spans[i].Dense, Is.False);
			}
		}

		[Test]
		public void Spans_ClipToWindowAndKeepGaps()
		{
			long[] steps = { 0, 1, 2, 3, 10, 11, 12 };
			uint[] vals = { 1, 1, 1, 1, 0, 0, 0 };
			List<WaveSegment> segs = WaveformSegments.Build(steps, vals, 7); // (0,4,1) (10,13,0)
			List<WaveSpan> spans = new();

			WaveformView.BuildSpans(segs, 2, 11, 8, 3, spans);

			Assert.That(spans.Count, Is.EqualTo(2));
			Assert.That((spans[0].StartStep, spans[0].EndStep), Is.EqualTo((2L, 4L)));
			Assert.That((spans[1].StartStep, spans[1].EndStep), Is.EqualTo((10L, 12L)));

			// Entirely outside the window: nothing
			WaveformView.BuildSpans(segs, 5, 9, 8, 3, spans);
			Assert.That(spans.Count, Is.EqualTo(0));
		}

		[Test]
		public void Spans_NarrowRunDoesNotMergeAcrossGap()
		{
			long[] steps = { 0, 1, 5, 6 };
			uint[] vals = { 0, 1, 0, 1 };
			List<WaveSegment> segs = WaveformSegments.Build(steps, vals, 4);
			List<WaveSpan> spans = new();

			WaveformView.BuildSpans(segs, 0, 6, 1, 3, spans);

			Assert.That(spans.TrueForAll(s => s.EndStep <= 2 || s.StartStep >= 5), Is.True);
		}

		static void AssertSeg(WaveSegment s, long start, long end, uint value)
		{
			Assert.That(s.StartStep, Is.EqualTo(start));
			Assert.That(s.EndStep, Is.EqualTo(end));
			Assert.That(s.Value, Is.EqualTo(value));
		}
	}
}
