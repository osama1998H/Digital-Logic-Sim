using System.Collections.Generic;
using System.Text;

namespace DLS.Game.Logic
{
	// Decides whether a user chip can stand in for a NOT/AND/OR/XOR gate
	public static class GateCheck
	{
		public static readonly GateKind[] UserGateKinds = { GateKind.Not, GateKind.And, GateKind.Or, GateKind.Xor };

		public static string ChipName(GateKind kind) => kind switch
		{
			GateKind.Not => "NOT",
			GateKind.And => "AND",
			GateKind.Or => "OR",
			GateKind.Xor => "XOR",
			_ => "NAND"
		};

		public static GateSet Flag(GateKind kind) => kind switch
		{
			GateKind.Not => GateSet.Not,
			GateKind.And => GateSet.And,
			GateKind.Or => GateSet.Or,
			GateKind.Xor => GateSet.Xor,
			_ => GateSet.None
		};

		public static int InputCount(GateKind kind) => kind == GateKind.Not ? 1 : 2;

		public static bool HasShape(GateKind kind, int[] inputBitCounts, int[] outputBitCounts)
		{
			if (kind == GateKind.Nand || inputBitCounts.Length != InputCount(kind) || outputBitCounts.Length != 1) return false;
			foreach (int bits in inputBitCounts)
			{
				if (bits != 1) return false;
			}
			return outputBitCounts[0] == 1;
		}

		// Rows count up with the top input as the most significant bit. AND/OR/XOR are symmetric, so pin order doesn't matter.
		public static bool Matches(GateKind kind, TruthTable table)
		{
			int[] inputBits = new int[table.Inputs.Length];
			for (int i = 0; i < inputBits.Length; i++) inputBits[i] = table.Inputs[i].BitCount;
			int[] outputBits = new int[table.Outputs.Length];
			for (int i = 0; i < outputBits.Length; i++) outputBits[i] = table.Outputs[i].BitCount;
			if (!HasShape(kind, inputBits, outputBits) || table.RowsFilled != table.RowCount) return false;

			for (int row = 0; row < table.RowCount; row++)
			{
				uint state = table.GetOutputState(row, 0);
				if (((state >> 16) & 1) != 0) return false; // floating
				if ((state & 1) != Expected(kind, row)) return false;
			}
			return true;
		}

		static uint Expected(GateKind kind, int row)
		{
			bool a = (row & 1) != 0;
			bool b = (row & 2) != 0;
			bool result = kind switch
			{
				GateKind.Not => !a,
				GateKind.And => a && b,
				GateKind.Or => a || b,
				_ => a ^ b
			};
			return result ? 1u : 0u;
		}
	}

	public static class NetlistSummary
	{
		// e.g. "Inputs: A, B, CIN | Outputs: S, COUT | Gates: 2 XOR, 2 AND, 1 OR" (gate kinds in order of first use)
		public static string Format(Netlist netlist)
		{
			List<GateKind> order = new();
			int[] counts = new int[5];
			foreach (Gate gate in netlist.Gates)
			{
				if (counts[(int)gate.Kind]++ == 0) order.Add(gate.Kind);
			}

			StringBuilder sb = new();
			sb.Append("Inputs: ").Append(string.Join(", ", netlist.InputNames));
			sb.Append(" | Outputs: ").Append(string.Join(", ", netlist.OutputNames));
			sb.Append(" | Gates: ");
			if (order.Count == 0) sb.Append("none");
			for (int i = 0; i < order.Count; i++)
			{
				if (i > 0) sb.Append(", ");
				sb.Append(counts[(int)order[i]]).Append(' ').Append(GateCheck.ChipName(order[i]));
			}
			return sb.ToString();
		}
	}
}
