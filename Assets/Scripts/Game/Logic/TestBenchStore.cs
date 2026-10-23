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
			string[] lines = text.Split('\n');
			for (int i = 0; i < lines.Length; i++) lines[i] = lines[i].Replace('\t', ' ');
			return lines;
		}

		public static void Save(string projectName, string chipName, IReadOnlyList<string> lines)
		{
			SavePaths.EnsureDirectoryExists(SavePaths.GetTestsPath(projectName));
			// Write to a temporary file first so a failure part-way never leaves a truncated test file
			string path = GetPath(projectName, chipName);
			string temp = path + ".tmp";
			File.WriteAllText(temp, string.Join("\n", lines));
			if (File.Exists(path)) File.Replace(temp, path, null);
			else File.Move(temp, path);
		}

		public static string GetPath(string projectName, string chipName)
		{
			foreach (char c in Path.GetInvalidFileNameChars()) chipName = chipName.Replace(c, '_');
			return Path.Combine(SavePaths.GetTestsPath(projectName), chipName + ".txt");
		}
	}
}
