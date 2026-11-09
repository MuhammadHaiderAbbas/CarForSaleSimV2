using System;
using System.IO;
using System.Linq;
using CarSales.Foundation;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CarSales.EditorTools
{
    /// <summary>
    /// Phase 10 build pipeline: one-click Windows builds (release and development) into Builds/Windows[-Dev]/,
    /// with CREDITS.txt and build_info.txt next to the game. Release builds refuse assets whose licence
    /// can't ship (the Asset Studio check). Also callable from the command line:
    /// Unity -batchmode -quit -projectPath . -executeMethod CarSales.EditorTools.CarSalesBuild.BuildWindowsRelease
    /// </summary>
    public static class CarSalesBuild
    {
        public const string ExeName = "CarForSaleSimulator.exe";

        [MenuItem("Tools/Car Sales/Build/Windows (Release)", priority = 40)]
        public static void BuildWindowsRelease() => Build(false);

        [MenuItem("Tools/Car Sales/Build/Windows (Development)", priority = 41)]
        public static void BuildWindowsDevelopment() => Build(true);

        [MenuItem("Tools/Car Sales/Build/Open Builds Folder", priority = 42)]
        static void OpenFolder()
        {
            string dir = Path.GetFullPath("Builds");
            Directory.CreateDirectory(dir);
            EditorUtility.RevealInFinder(dir);
        }

        public static BuildReport Build(bool development)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled && File.Exists(s.path)).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Fail("No scenes in Build Settings. Run Tools > Car Sales > 2. Build Everything first.");
                return null;
            }
            string folder = Path.GetFullPath(Path.Combine("Builds", development ? "Windows-Dev" : "Windows"));
            Directory.CreateDirectory(folder);
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(folder, ExeName),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = development ? BuildOptions.Development | BuildOptions.AllowDebugging : BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                Fail($"Build {summary.result}: {summary.totalErrors} error(s). See the Console.");
                return report;
            }
            File.WriteAllText(Path.Combine(folder, "CREDITS.txt"), CreditsUI.Text());
            File.WriteAllText(Path.Combine(folder, "build_info.txt"), BuildInfo(development, scenes, summary));
            string msg = $"Built {(development ? "development" : "release")} game in {summary.totalTime.TotalSeconds:0}s " +
                         $"({summary.totalSize / (1024f * 1024f):0} MB): {folder}";
            Debug.Log("[Car Sales] " + msg);
            if (!Application.isBatchMode && EditorUtility.DisplayDialog("Build finished", msg, "Open folder", "OK")) EditorUtility.RevealInFinder(options.locationPathName);
            return report;
        }

        static string BuildInfo(bool development, string[] scenes, BuildSummary s) =>
            $"{CreditsUI.GameName}\n" +
            $"Version: {Application.version}  (game {SaveManager.GameVersion}, save format {SaveEnvelope.CurrentFormatVersion})\n" +
            $"Build: {(development ? "Development" : "Release")}  {s.platform}\n" +
            $"Built: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC with Unity {Application.unityVersion}\n" +
            $"Scenes: {string.Join(", ", scenes.Select(Path.GetFileNameWithoutExtension))}\n" +
            $"Size: {s.totalSize / (1024f * 1024f):0.0} MB\n";

        static void Fail(string message)
        {
            Debug.LogError("[Car Sales] " + message);
            if (!Application.isBatchMode) EditorUtility.DisplayDialog("Build", message, "OK");
            else EditorApplication.Exit(1);
        }
    }
}
