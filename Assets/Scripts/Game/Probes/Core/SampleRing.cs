using System;
using System.Threading;

namespace DLS.Game.Probes
{
	// Single writer (simulation thread), single reader (main thread).
	// The writer fills a slot and then publishes it by bumping writeCount.
	// The reader snapshots writeCount, copies, then re-checks writeCount and drops
	// any slots the writer may have started overwriting during the copy.
	public sealed class SampleRing
	{
		const int SafetyMargin = 64;

		public readonly int ProbeCount;
		public readonly int Capacity;

		readonly long[] steps;
		readonly uint[] values;
		long writeCount; // total published samples (monotonic)
		long clearBase; // samples with index < clearBase are discarded; writer-owned
		volatile bool clearRequested;

		// Test seam: runs on the reader thread between the copy and its validation
		internal Action AfterCopyHook;

		public SampleRing(int probeCount, int capacity)
		{
			if (probeCount < 1) throw new ArgumentOutOfRangeException(nameof(probeCount));
			if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity));
			ProbeCount = probeCount;
			Capacity = capacity;
			steps = new long[capacity];
			values = new uint[capacity * probeCount];
		}

		public long LatestStep
		{
			get
			{
				if (clearRequested) return -1;
				long count = Volatile.Read(ref writeCount);
				if (count <= Volatile.Read(ref clearBase)) return -1;
				// Best-effort; may be momentarily stale
				return steps[(int)((count - 1) % Capacity)];
			}
		}

		// Writer thread only
		public void Write(long step, uint[] vals)
		{
			long count = writeCount;
			if (clearRequested)
			{
				Volatile.Write(ref clearBase, count);
				clearRequested = false;
			}

			// Store-store fence: the previous publish must be visible before this slot's stores
			Interlocked.MemoryBarrier();

			int slot = (int)(count % Capacity);
			steps[slot] = step;
			Array.Copy(vals, 0, values, slot * ProbeCount, ProbeCount);
			Volatile.Write(ref writeCount, count + 1);
		}

		// Any thread; takes effect at the writer's next Write
		public void Clear()
		{
			clearRequested = true;
		}

		// Reader thread only. Returns samples oldest first.
		public int CopyLatest(int maxCount, long[] stepsOut, uint[][] valuesOut)
		{
			if (clearRequested) return 0;

			long count = Volatile.Read(ref writeCount);
			long baseIndex = Volatile.Read(ref clearBase);
			long limit = Math.Min(maxCount, Math.Max(1, Capacity - SafetyMargin));
			limit = Math.Min(limit, Math.Min(stepsOut.Length, valuesOut.Length));
			long start = Math.Max(Math.Max(baseIndex, count - limit), 0);
			int n = (int)(count - start);
			if (n <= 0) return 0;

			for (int i = 0; i < n; i++)
			{
				int slot = (int)((start + i) % Capacity);
				stepsOut[i] = steps[slot];
				uint[] dst = valuesOut[i];
				if (dst == null || dst.Length < ProbeCount) valuesOut[i] = dst = new uint[ProbeCount];
				Array.Copy(values, slot * ProbeCount, dst, 0, ProbeCount);
			}

			AfterCopyHook?.Invoke();

			// Order the data reads above before re-reading the count
			Interlocked.MemoryBarrier();
			long countAfter = Volatile.Read(ref writeCount);

			// The writer may be filling index countAfter right now, which reuses the slot of
			// index countAfter - Capacity. Anything at or below that may be torn.
			long firstSafe = countAfter - Capacity + 1;
			long baseAfter = Volatile.Read(ref clearBase);
			long keepFrom = Math.Max(start, Math.Max(firstSafe, baseAfter));
			if (keepFrom >= start + n) return 0;
			int drop = (int)(keepFrom - start);
			if (drop <= 0) return n;

			int kept = n - drop;
			for (int i = 0; i < kept; i++)
			{
				stepsOut[i] = stepsOut[i + drop];
				(valuesOut[i], valuesOut[i + drop]) = (valuesOut[i + drop], valuesOut[i]);
			}
			return kept;
		}
	}
}
