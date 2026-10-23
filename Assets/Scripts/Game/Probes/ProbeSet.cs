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

		// Probes are addressed relative to the edited (root) chip, so pins of a viewed subchip can't be probed
		public static bool CanProbe => Project.ActiveProject != null && Project.ActiveProject.CanEditViewedChip;

		public static bool IsProbed(PinInstance pin) => CanProbe && IndexOf(pin.Address) >= 0;

		// Returns whether the pin is probed afterwards (false if it was removed, or the limit was reached)
		public static bool Toggle(PinInstance pin)
		{
			if (!CanProbe) return false;
			int index = IndexOf(pin.Address);
			if (index >= 0)
			{
				probes.RemoveAt(index);
				Publish(true);
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
			Publish(true);
			return true;
		}

		public static void RemoveProbesOwnedBy(int ownerId)
		{
			if (probes.RemoveAll(p => p.Address.PinOwnerID == ownerId) > 0) Publish(true);
		}

		// Removes probes whose pin is no longer in the chip (e.g. element deleted by a path that doesn't
		// go through DeleteElements, such as undo of an add). Call from the UI before reading Probes.
		public static void PruneMissing(DevChipInstance chip)
		{
			if (chip == null) return;
			if (probes.RemoveAll(p => !chip.TryFindPin(p.Address, out _)) > 0) Publish(true);
		}

		// Called by Project for every switch of the edited chip. Unsaved chips (empty name) are not remembered.
		public static void OnViewedChipChanged(string chipName, DevChipInstance chip)
		{
			chipName ??= string.Empty;
			if (currentChipName.Length > 0) probesByChip[currentChipName] = probes;
			currentChipName = chipName;
			probes = chipName.Length > 0 && probesByChip.TryGetValue(chipName, out List<Probe> saved) ? saved : new List<Probe>();
			probes.RemoveAll(p => chip == null || !chip.TryFindPin(p.Address, out _));
			Publish();
		}

		public static void ForgetChip(string chipName)
		{
			if (!string.IsNullOrEmpty(chipName)) probesByChip.Remove(chipName);
		}

		// The edited chip was renamed: keep its probes under the new name
		public static void OnChipRenamed(string oldName, string newName)
		{
			probesByChip.Remove(oldName);
			if (string.Equals(currentChipName, oldName, StringComparison.OrdinalIgnoreCase)) currentChipName = newName ?? string.Empty;
		}

		// First save or Save As of the edited chip: probes follow it under its new name.
		// For Save As the original chip keeps its own remembered set.
		public static void OnChipSaved(string oldName, string newName, bool keepOld)
		{
			if (keepOld && !string.IsNullOrEmpty(oldName)) probesByChip[oldName] = new List<Probe>(probes);
			OnChipRenamed(oldName, newName);
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

		// keepHistory: carry the existing samples over to the new recording (same chip, probe list edited)
		static void Publish(bool keepHistory = false)
		{
			ProbeRecording previous = ProbeRecorder.Current;
			if (probes.Count == 0)
			{
				ProbeRecorder.Current = null;
				return;
			}

			PinAddress[] addresses = new PinAddress[probes.Count];
			for (int i = 0; i < addresses.Length; i++) addresses[i] = probes[i].Address;
			SampleRing ring = new SampleRing(addresses.Length, HistoryLength);

			if (keepHistory && previous != null)
			{
				// The new ring has no writer until it is published, so this thread can fill it
				int[] map = ColumnRemap.BuildMap(previous.Addresses, addresses, PinAddress.Equals);
				int max = previous.Ring.Capacity;
				long[] steps = new long[max];
				uint[][] values = new uint[max][];
				int count = previous.Ring.CopyLatest(max, steps, values);
				uint[] row = new uint[addresses.Length];
				for (int i = 0; i < count; i++)
				{
					ColumnRemap.RemapRow(values[i], map, ProbeRecorder.Floating, row);
					ring.Write(steps[i], row);
				}
			}

			ProbeRecorder.Current = new ProbeRecording(addresses, ring);
		}
	}
}
