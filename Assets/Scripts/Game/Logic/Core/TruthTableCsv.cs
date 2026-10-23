using System.Collections.Generic;
using System.Text;
using DLS.Game.Inspect;

namespace DLS.Game.Logic
{
	public static class TruthTableCsv
	{
		public static string ToCsv(TruthTable table, NumberFormat format)
		{
			var sb = new StringBuilder();
			var header = new List<string>();
			foreach (TruthPin pin in table.Inputs) header.Add(pin.Name);
			foreach (TruthPin pin in table.Outputs) header.Add(pin.Name);
			sb.Append(string.Join(",", header));

			for (int row = 0; row < table.RowCount; row++)
			{
				var cells = new List<string>();
				for (int i = 0; i < table.Inputs.Length; i++)
				{
					TruthPin pin = table.Inputs[i];
					cells.Add(PinValueFormat.FormatValue(table.GetInputValue(row, i), pin.BitCount, format));
				}
				for (int o = 0; o < table.Outputs.Length; o++)
				{
					TruthPin pin = table.Outputs[o];
					cells.Add(PinValueFormat.FormatValue(table.GetOutputState(row, o), pin.BitCount, format));
				}
				sb.Append('\n').Append(string.Join(",", cells));
			}
			return sb.ToString();
		}
	}
}
