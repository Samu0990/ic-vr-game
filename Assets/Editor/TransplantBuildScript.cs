using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace VRSurgery.EditorTools
{
    /// <summary>
    /// Builds the transplant scene for desktop.
    ///
    /// Separate from <see cref="AndroidBuildScript"/>, which builds the MVP scene for the Quest
    /// and is kept as it is. The scenes here are independent — nothing in the project calls
    /// LoadScene — so a build is one scene, and the one that matters is the transplant.
    ///
    /// Uso:
    ///   -executeMethod VRSurgery.EditorTools.TransplantBuildScript.BuildLinuxFromCommandLine
    ///   -executeMethod VRSurgery.EditorTools.TransplantBuildScript.BuildWindowsFromCommandLine
    /// </summary>
    public static class TransplantBuildScript
    {
        private const string Scene = "Assets/Scenes/TransplanteCardiaco.unity";

        [MenuItem("Build/Transplante — Linux 64")]
        public static void BuildLinux() => Build(BuildTarget.StandaloneLinux64, "Builds/Linux/Transplante.x86_64");

        [MenuItem("Build/Transplante — Windows 64")]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Builds/Windows/Transplante.exe");

        /// <summary>
        /// O alvo de verdade do projeto: APK do Quest rodando sozinho. O AndroidBuildScript que
        /// já existia builda a cena do MVP, não esta.
        /// </summary>
        [MenuItem("Build/Transplante — APK do Quest")]
        public static void BuildQuest() => Build(BuildTarget.Android, "Builds/Quest/Transplante.apk");

        public static void BuildQuestFromCommandLine() => RunFromCommandLine(BuildQuest);

        public static void BuildLinuxFromCommandLine() => RunFromCommandLine(BuildLinux);

        public static void BuildWindowsFromCommandLine() => RunFromCommandLine(BuildWindows);

        private static void RunFromCommandLine(Action build)
        {
            try
            {
                build();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("[Build] TRANSPLANT_BUILD_FAILED " + exception);
                EditorApplication.Exit(1);
            }
        }

        private static void Build(BuildTarget target, string path)
        {
            if (!File.Exists(Scene))
            {
                Debug.LogError($"[Build] TRANSPLANT_BUILD_FAILED cena ausente: {Scene}");
                EditorApplication.Exit(1);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path));

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = path,
                target = target,
                targetGroup = target == BuildTarget.Android
                    ? BuildTargetGroup.Android
                    : BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[Build] TRANSPLANT_BUILD_OK {target} -> {path} " +
                          $"({summary.totalSize / 1024 / 1024} MB, {summary.totalTime})");
                return;
            }

            // Sem isto o motivo real some no meio do log.
            Debug.LogError($"[Build] TRANSPLANT_BUILD_FAILED {target}: {summary.result}, " +
                           $"{summary.totalErrors} erro(s)");

            foreach (BuildStep step in report.steps)
            {
                foreach (BuildStepMessage message in step.messages)
                {
                    if (message.type == LogType.Error || message.type == LogType.Exception)
                    {
                        Debug.LogError($"[Build]   {step.name}: {message.content}");
                    }
                }
            }
        }
    }
}
