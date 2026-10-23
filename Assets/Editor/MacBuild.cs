using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

// Command line build for macOS. Usage:
// Unity -batchmode -quit -projectPath <project> -executeMethod MacBuild.Build [-buildPath <path.app>] [-arch arm64|x64|universal]
public static class MacBuild
{
	const string defaultBuildPath = "Builds/macOS/Digital-Logic-Sim.app";

	public static void Build()
	{
		string buildPath = GetArg("-buildPath") ?? defaultBuildPath;

		UnityEditor.OSXStandalone.UserBuildSettings.architecture = (GetArg("-arch") ?? "arm64") switch
		{
			"x64" => OSArchitecture.x64,
			"universal" => OSArchitecture.x64ARM64,
			_ => OSArchitecture.ARM64
		};

		BuildPlayerOptions options = new()
		{
			scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
			locationPathName = buildPath,
			target = BuildTarget.StandaloneOSX,
			options = BuildOptions.None
		};

		BuildReport report = BuildPipeline.BuildPlayer(options);
		Console.WriteLine($"MacBuild: {report.summary.result} ({report.summary.totalErrors} errors) -> {buildPath}");
		if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
	}

	static string GetArg(string name)
	{
		string[] args = Environment.GetCommandLineArgs();
		int i = Array.IndexOf(args, name);
		return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
	}
}
