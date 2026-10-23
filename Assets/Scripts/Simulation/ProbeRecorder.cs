using System;
using DLS.Description;
using DLS.Game.Probes;

namespace DLS.Simulation
{
	// Immutable once published: the main thread replaces it instead of mutating it.
	// 'buffer' is owned by the simulation thread.
	public sealed class ProbeRecording
	{
		public readonly PinAddress[] Addresses;
		public readonly SampleRing Ring;
		internal readonly uint[] buffer;

		public ProbeRecording(PinAddress[] addresses, SampleRing ring)
		{
			Addresses = addresses;
			Ring = ring;
			buffer = new uint[addresses.Length];
		}
	}

	// Simulation thread
	public static class ProbeRecorder
	{
		public const uint Floating = 0xFFFF0000;

		public static volatile ProbeRecording Current;

		public static void Record(SimChip root, long step)
		{
			ProbeRecording rec = Current;
			if (rec == null || root == null) return;

			PinAddress[] addresses = rec.Addresses;
			uint[] buffer = rec.buffer;
			for (int i = 0; i < addresses.Length; i++)
			{
				try
				{
					// Missing pin (deleted element): floating
					buffer[i] = root.TryGetSimPinFromAddress(addresses[i], out SimPin pin) ? pin.State : Floating;
				}
				catch (Exception)
				{
					buffer[i] = Floating; // sim chip being modified from the main thread
				}
			}

			rec.Ring.Write(step, buffer);
		}
	}
}
