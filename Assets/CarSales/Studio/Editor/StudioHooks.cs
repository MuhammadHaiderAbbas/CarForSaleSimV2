using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CarSales.Studio.EditorTools
{
    /// <summary>Notices new model files in Assets/CarSales/Art/_Drop and offers to set them up.</summary>
    class DropFolderWatcher : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            var dropped = imported.Concat(moved)
                .Where(p => p.StartsWith(StudioPaths.DropFolder + "/") && StudioNames.IsModelFile(p)).ToList();
            if (dropped.Count == 0 || Application.isBatchMode) return;
            EditorApplication.delayCall += () =>
            {
                Debug.Log($"[Asset Studio] New model(s) in the Drop folder: {string.Join(", ", dropped.Select(Path.GetFileName))}. " +
                          "Open Tools > Car Sales > Asset Studio to set them up.");
                if (EditorWindow.HasOpenInstances<AssetStudioWindow>()) EditorWindow.GetWindow<AssetStudioWindow>().Repaint();
            };
        }
    }

    /// <summary>
    /// Release builds refuse assets whose licence doesn't allow a commercial game (spec rule R9); development builds
    /// only warn. Every build gets a CREDITS.txt next to it.
    /// </summary>
    class LicenceBuildCheck : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var reg = AssetDatabase.LoadAssetAtPath<AssetRegistry>(StudioPaths.RegistryPath);
            if (!reg) return;
            var blockers = reg.ReleaseBlockers().ToList();
            if (blockers.Count == 0) return;
            string list = string.Join("\n", blockers.Select(r => $" - {r.displayName}: {LicencePolicy.Check(r.licence).Reason}"));
            bool development = (report.summary.options & BuildOptions.Development) != 0;
            if (development)
            {
                Debug.LogWarning("[Asset Studio] Development build with assets that can't ship in a release:\n" + list);
                return;
            }
            throw new BuildFailedException("These assets can't be used in a release build (fix the licence in Tools > Car Sales > Asset Studio > Licences, " +
                                           "replace them, or make a Development build):\n" + list);
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            var reg = AssetDatabase.LoadAssetAtPath<AssetRegistry>(StudioPaths.RegistryPath);
            if (!reg || reg.records.Count == 0) return;
            string output = report.summary.outputPath;
            string dir = Directory.Exists(output) ? output : Path.GetDirectoryName(output);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            File.WriteAllText(Path.Combine(dir, "CREDITS.txt"), reg.CreditsText());
        }
    }
}
