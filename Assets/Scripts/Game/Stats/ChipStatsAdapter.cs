using System;
using System.Linq;
using DLS.Description;

namespace DLS.Game.Stats
{
	// Maps game chip descriptions onto the engine-free statistics model
	public static class ChipStatsAdapter
	{
		public static StatsChipDef ToDef(ChipDescription desc)
		{
			StatsChipDef def = new StatsChipDef
			{
				Name = desc.Name,
				Kind = KindOf(desc.ChipType),
				InputPinIds = (desc.InputPins ?? Array.Empty<PinDescription>()).Select(p => p.ID).ToArray(),
				OutputPinIds = (desc.OutputPins ?? Array.Empty<PinDescription>()).Select(p => p.ID).ToArray(),
				SubChips = (desc.SubChips ?? Array.Empty<SubChipDescription>()).Select(s => (s.ID, s.Name)).ToArray(),
				Wires = (desc.Wires ?? Array.Empty<WireDescription>()).Select(w => (new StatsPin(w.SourcePinAddress.PinOwnerID, w.SourcePinAddress.PinID), new StatsPin(w.TargetPinAddress.PinOwnerID, w.TargetPinAddress.PinID))).ToArray()
			};
			return def;
		}

		public static IStatsLibrary Library(ChipLibrary lib) => new LibraryAdapter(lib);

		public static StatsKind KindOf(ChipType type)
		{
			if (type == ChipType.Custom) return StatsKind.Custom;
			if (type == ChipType.Nand) return StatsKind.Nand;
			if (type == ChipType.TriStateBuffer) return StatsKind.Buffer;
			if (IsMergeOrSplit(type) || ChipTypeHelper.IsBusType(type)) return StatsKind.PassThrough;
			(bool isIn, bool isOut, _) = ChipTypeHelper.IsInputOrOutputPin(type);
			if (isIn || isOut) return StatsKind.PassThrough;
			return StatsKind.Opaque;
		}

		static bool IsMergeOrSplit(ChipType type)
		{
			string n = type.ToString();
			return n.StartsWith("Merge_", StringComparison.Ordinal) || n.StartsWith("Split_", StringComparison.Ordinal);
		}

		sealed class LibraryAdapter : IStatsLibrary
		{
			readonly ChipLibrary library;

			public LibraryAdapter(ChipLibrary library) => this.library = library;

			public StatsChipDef Get(string name) => library.TryGetChipDescription(name, out ChipDescription desc) ? ToDef(desc) : null;
		}
	}
}
