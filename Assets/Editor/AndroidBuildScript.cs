using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VRSurgery.EditorTools
{
    public static class AndroidBuildScript
    {
        [MenuItem("Build/Build APK for Quest")]
        public static void BuildAPK()
        {
            string[] scenes = { "Assets/Scenes/SurgeryMVP.unity" };
            string buildPath = "Builds/SurgeryMVP.apk";

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = buildPath,
                target = BuildTarget.Android,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);

            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[AndroidBuild] APK built: {buildPath} ({(report.summary.totalSize / 1024 / 1024)}MB)");
            }
            else
            {
                Debug.LogError($"[AndroidBuild] Build failed: {report.summary.result}");
            }
        }

        [MenuItem("Build/Build APK (Batchmode)")]
        public static void BuildAPKBatchmode()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SurgeryMVP.unity");
            BuildAPK();
            EditorApplication.Exit(0);
        }
    }
}
