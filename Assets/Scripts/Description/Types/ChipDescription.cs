using System;
using Newtonsoft.Json;
using UnityEngine;

namespace DLS.Description
{
	public class ChipDescription
	{
		// ---- Name Comparison ----
		public const StringComparison NameComparison = StringComparison.OrdinalIgnoreCase;
		public static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

		// ---- Data ----
		public string DLSVersion;
		public string Name;
		public NameDisplayLocation NameLocation;
		public ChipType ChipType;
		public bool ShouldBeCached;
		public Vector2 Size;
		public Color Colour;
		public PinDescription[] InputPins;
		public PinDescription[] OutputPins;
		public SubChipDescription[] SubChips;
		public WireDescription[] Wires;
		public DisplayDescription[] Displays;
		// Optional: left out of the file when the chip has no notes, so chips saved before notes existed are unchanged
		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public NoteDescription[] Notes;

		// ---- Convenience Functions ----
		public bool HasDisplay() => Displays != null && Displays.Length > 0;
		public bool NameMatch(string otherName) => NameMatch(Name, otherName);
		public static bool NameMatch(string a, string b) => string.Equals(a, b, NameComparison);
	}

	public enum NameDisplayLocation
	{
		Centre,
		Top,
		Hidden
	}
}