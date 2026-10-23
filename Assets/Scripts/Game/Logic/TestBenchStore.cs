using System.Collections.Generic;
using System.IO;
using DLS.SaveSystem;

namespace DLS.Game.Logic
{
	// Test lines are kept per chip in <project>/Tests/<chip name>.txt
	public static class TestBenchStore
	{
		public static string[] Load(string projectName, string chipName)
		{
			string path = GetPath(projectName, chipName);
			if (!File.Exists(path)) return new string[0];

			string text = File.ReadAllText(path).Replace("\r\n", "\n").Replace('\r', '\n');
			if (text.Length == 0) return new string[0];
			if (text.EndsWith("\n")) text = text.Substring(0, text.Length - 1);
			return text.Split('\n');
		}

		public static void Save(string projectName, string chipName, IReadOnlyList<string> lines)
		{
			SavePaths.EnsureDirectoryExists(SavePaths.GetTestsPath(projectName));
			File.WriteAllText(GetPath(projectName, chipName), string.Join("\n", lines));
		}

		public static string GetPath(string projectName, string chipName)
		{
			foreach (char c in Path.GetInvalidFileNameChars()) chipName = chipName.Replace(c, '_');
			return Path.Combine(SavePaths.GetTestsPath(projectName), chipName + ".txt");
		}
	}
}
