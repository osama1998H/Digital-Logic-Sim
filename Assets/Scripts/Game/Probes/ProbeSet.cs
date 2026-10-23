using System;
using System.Collections.Generic;
using DLS.Description;
using DLS.Simulation;

namespace DLS.Game.Probes
{
	public sealed class Probe
	{
		public string Name;
		public PinAddress Address;
		public int BitCount;
		public PinColour Colour;
	}

	// Main thread only. Probes belong to the chip being edited and are remembered per chip name for the session.
	// Every change publishes a new immutable ProbeRecording (which also starts a fresh history).
	public static class ProbeSet
	{
		public const int MaxProbes = 12;
		public const int HistoryLength = 4096;

		static List<Probe> probes = new();
		static readonly Dictionary<string, List<Probe>> probesByChip = new(StringComparer.OrdinalIgnoreCase);
		static string currentChipName = string.Empty;
		static int nextColour;

		public static IReadOnlyList<Probe> Probes => probes;
		public static SampleRing Ring => ProbeRecorder.Current?.Ring;

		public static bool IsProbed(PinInstance pin) => IndexOf(pin.Address) >= 0;

		// Returns whether the pin is probed afterwards (false if it was removed, or the limit was reached)
		public static bool Toggle(PinInstance pin)
		{
			int index = IndexOf(pin.Address);
			if (index >= 0)
			{
				probes.RemoveAt(index);
				Publish();
				return false;
			}

			if (probes.Count >= MaxProbes) return false;

			probes.Add(new Probe
			{
				Name = MakeName(pin),
				Address = pin.Address,
				BitCount = (int)pin.bitCount,
				Colour = (PinColour)(nextColour++ % Enum.GetValues(typeof(PinColour)).Length)
			});
			Publish();
			return true;
		}

		public static void RemoveProbesOwnedBy(int ownerId)
		{
			if (probes.RemoveAll(p => p.Address.PinOwnerID == ownerId) > 0) Publish();
		}

		// Removes probes whose pin is no longer in the chip (e.g. element deleted by a path that doesn't
		// go through DeleteElements, such as undo of an add). Call from the UI before reading Probes.
		public static void PruneMissing(DevChipInstance chip)
		{
			if (chip == null) return;
			if (probes.RemoveAll(p => !chip.TryFindPin(p.Address, out _)) > 0) Publish();
		}

		public static void OnViewedChipChanged(string chipName)
		{
			chipName ??= string.Empty;
			probesByChip[currentChipName] = probes;
			currentChipName = chipName;
			probes = probesByChip.TryGetValue(chipName, out List<Probe> saved) ? saved : new List<Probe>();
			Publish();
		}

		// New project: forget everything remembered for the previous one
		public static void ResetAll()
		{
			probesByChip.Clear();
			probes = new List<Probe>();
			currentChipName = string.Empty;
			nextColour = 0;
			Publish();
		}

		public static void ClearHistory() => ProbeRecorder.Current?.Ring.Clear();

		static int IndexOf(PinAddress address)
		{
			for (int i = 0; i < probes.Count; i++)
			{
				if (PinAddress.Equals(probes[i].Address, address)) return i;
			}

			return -1;
		}

		static string MakeName(PinInstance pin)
		{
			if (pin.parent is SubChipInstance sub)
			{
				string owner = string.IsNullOrEmpty(sub.Label) ? sub.Description.Name : sub.Label;
				return owner + "." + pin.Name;
			}

			return pin.Name;
		}

		static void Publish()
		{
			if (probes.Count == 0)
			{
				ProbeRecorder.Current = null;
				return;
			}

			PinAddress[] addresses = new PinAddress[probes.Count];
			for (int i = 0; i < addresses.Length; i++) addresses[i] = probes[i].Address;
			ProbeRecorder.Current = new ProbeRecording(addresses, new SampleRing(addresses.Length, HistoryLength));
		}
	}
}
