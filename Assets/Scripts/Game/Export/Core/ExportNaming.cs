using System;
using System.Globalization;
using System.Text;

namespace DLS.Game.Export
{
	// Engine-free file naming for exported images. The invalid set is fixed (not Path.GetInvalidFileNameChars)
	// so a name exported on macOS is the same one a Windows user would get.
	public static class ExportNaming
	{
		const string InvalidChars = "/\\:*?\"<>|";
		const string Untitled = "Untitled";
		const string TimeFormat = "yyyy-MM-dd HH-mm-ss";

		public static string FileName(string chipName, DateTime time)
		{
			string name = Sanitize(chipName);
			return $"{name} {time.ToString(TimeFormat, CultureInfo.InvariantCulture)}.png";
		}

		static string Sanitize(string chipName)
		{
			if (string.IsNullOrWhiteSpace(chipName)) return Untitled;

			StringBuilder sb = new(chipName.Length);
			foreach (char c in chipName)
			{
				bool invalid = char.IsControl(c) || InvalidChars.IndexOf(c) >= 0;
				sb.Append(invalid ? '_' : c);
			}
			// Windows silently drops trailing dots and spaces, so strip them here to keep the name stable
			string trimmed = sb.ToString().TrimEnd(' ', '.');
			return trimmed.Length == 0 ? Untitled : trimmed;
		}
	}
}
