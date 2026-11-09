using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace CarSales.Installer
{
    /// <summary>
    /// Has NO dependencies, so it always compiles - even in a fresh project.
    /// Checks for the packages Car Sales needs and offers to install them.
    /// The game scripts stay switched off (define constraints) until the packages exist.
    /// </summary>
    [InitializeOnLoad]
    static class CarSalesDependencyInstaller
    {
        const string SessionKey = "CarSales.DepsChecked";
        // Input System last: enabling its backend restarts the editor.
        static readonly string[] Required = { "com.unity.ugui", "com.unity.ai.navigation", "com.unity.test-framework", "com.unity.inputsystem" };
        const string QueueKey = "CarSales.InstallQueue";
        static ListRequest listRequest;
        static bool manual;

        static CarSalesDependencyInstaller()
        {
            // Continue an install that was interrupted by a script reload.
            if (!string.IsNullOrEmpty(SessionState.GetString(QueueKey, "")))
            {
                EditorApplication.delayCall += InstallNext;
                return;
            }
            if (SessionState.GetBool(SessionKey, false)) return;
            SessionState.SetBool(SessionKey, true);
            EditorApplication.delayCall += Begin;
        }

        [MenuItem("Tools/Car Sales/1. Check & Install Packages", priority = 0)]
        static void CheckManually()
        {
            manual = true;
            Begin();
        }

        static void Begin()
        {
            listRequest = Client.List(true);
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            if (listRequest == null || !listRequest.IsCompleted) return;
            EditorApplication.update -= Poll;

            if (listRequest.Status != StatusCode.Success)
            {
                UnityEngine.Debug.LogWarning("[CarSales] Could not list packages: " + listRequest.Error?.message);
                return;
            }

            var installed = new HashSet<string>(listRequest.Result.Select(p => p.name));
            var missing = Required.Where(r => !installed.Contains(r)).ToArray();

            if (missing.Length == 0)
            {
                if (manual)
                    EditorUtility.DisplayDialog("Car Sales Simulator",
                        "All required packages are installed.\n\nNext: Tools > Car Sales > 2. Build Everything", "OK");
                manual = false;
                return;
            }
            manual = false;

            bool ok = EditorUtility.DisplayDialog("Car Sales Simulator",
                "These packages are required:\n\n  " + string.Join("\n  ", missing) +
                "\n\nInstall them now? (If Unity asks to enable the new Input System backend, click YES - the editor restarts.)",
                "Install", "Later");
            if (ok)
            {
                SessionState.SetString(QueueKey, string.Join(",", missing));
                InstallNext();
            }
        }

        // Installs packages one at a time (works on every Unity version).
        // The queue lives in SessionState so it survives the domain reload after each install.
        static AddRequest addRequest;

        static void InstallNext()
        {
            var queue = SessionState.GetString(QueueKey, "").Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries).ToList();
            if (queue.Count == 0)
            {
                SessionState.EraseString(QueueKey);
                UnityEngine.Debug.Log("[CarSales] All packages installed. Next: Tools > Car Sales > 2. Build Everything");
                return;
            }
            string id = queue[0];
            SessionState.SetString(QueueKey, string.Join(",", queue.Skip(1)));
            UnityEngine.Debug.Log("[CarSales] Installing " + id + " ...");
            addRequest = Client.Add(id);
            EditorApplication.update += PollAdd;
        }

        static void PollAdd()
        {
            if (addRequest == null || !addRequest.IsCompleted) return;
            EditorApplication.update -= PollAdd;
            if (addRequest.Status == StatusCode.Success)
                UnityEngine.Debug.Log("[CarSales] Installed " + addRequest.Result.name + " " + addRequest.Result.version);
            else
                UnityEngine.Debug.LogError("[CarSales] Package install failed: " + addRequest.Error?.message);
            InstallNext();
        }
    }
}
