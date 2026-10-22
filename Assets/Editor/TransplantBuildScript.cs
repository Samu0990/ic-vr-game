using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRSurgery.Transplant;

namespace VRSurgery.EditorTools
{
    /// <summary>
    /// Builds Meta Health for the stand: one Windows build that runs on the PC with the Quest on
    /// a Link cable, the queue's monitor on Display 1 and the projector over the mannequin on
    /// Display 2. One scene, the transplant, at the level chosen in the VRSurgery menu (Médio
    /// until another is chosen).
    ///
    /// Uso por linha de comando:
    ///   -executeMethod VRSurgery.EditorTools.TransplantBuildScript.BuildWindowsFromCommandLine [-nivel Facil|Medio|Dificil]
    ///   -executeMethod VRSurgery.EditorTools.TransplantBuildScript.BuildLinuxFromCommandLine
    ///   -executeMethod VRSurgery.EditorTools.TransplantBuildScript.BuildQuestFromCommandLine [-nivel Facil|Medio|Dificil]
    /// The Quest build is an APK that runs on the headset alone, with no PC: no projector and no
    /// queue monitor then. The Linux build is kept for the command line only.
    /// </summary>
    public static class TransplantBuildScript
    {
        private const string Scene = "Assets/Scenes/TransplanteCardiaco.unity";
        private const string WindowsPath = "Builds/Windows/MetaHealth.exe";
        private const string QuestPath = "Builds/Quest/MetaHealth.apk";

        /// <summary>Where the level picked in the VRSurgery menu is remembered for the next build.</summary>
        public const string LevelPreference = "VRSurgery.NivelDaBuild";

        /// <summary>The level the build uses: the last one built from the VRSurgery menu, Médio if none.</summary>
        public static SurgicalDifficulty ChosenLevel() =>
            (SurgicalDifficulty)EditorPrefs.GetInt(LevelPreference, (int)SurgicalDifficulty.Medio);

        [MenuItem("Build/Meta Health — Windows (PC + Quest no cabo)")]
        public static void BuildWindowsFromMenu()
        {
            SurgicalDifficulty level = ChosenLevel();
            bool ok = BuildWindows(level);
            EditorUtility.DisplayDialog("Meta Health — Windows",
                ok ? $"Build pronta no nível {level}:\n{Path.GetFullPath(WindowsPath)}"
                   : "A build falhou. O motivo está no Console ([Build]).", "OK");
        }

        public static void BuildWindowsFromCommandLine() => RunFromCommandLine(() => BuildWindows(LevelFromArgs() ?? ChosenLevel()));

        public static void BuildLinuxFromCommandLine() => RunFromCommandLine(() => Build(BuildTarget.StandaloneLinux64, "Builds/Linux/MetaHealth.x86_64"));

        [MenuItem("Build/Meta Health — Quest (APK, sem cabo)")]
        public static void BuildQuestFromMenu()
        {
            SurgicalDifficulty level = ChosenLevel();
            bool ok = BuildQuest(level);
            EditorUtility.DisplayDialog("Meta Health — Quest",
                ok ? $"APK pronto no nível {level}:\n{Path.GetFullPath(QuestPath)}\n\n" +
                     "Instale com: adb install -r Builds/Quest/MetaHealth.apk"
                   : "A build falhou. O motivo está no Console ([Build]).", "OK");
        }

        public static void BuildQuestFromCommandLine() => RunFromCommandLine(() => BuildQuest(LevelFromArgs() ?? ChosenLevel()));

        public static bool BuildWindows(SurgicalDifficulty level)
        {
            EnsureSceneLevel(level);
            return Build(BuildTarget.StandaloneWindows64, WindowsPath);
        }

        public static bool BuildQuest(SurgicalDifficulty level)
        {
            EnsureSceneLevel(level);
            return Build(BuildTarget.Android, QuestPath);
        }

        private static void RunFromCommandLine(Func<bool> build)
        {
            try
            {
                EditorApplication.Exit(build() ? 0 : 1);
            }
            catch (Exception exception)
            {
                Debug.LogError("[Build] TRANSPLANT_BUILD_FAILED " + exception);
                EditorApplication.Exit(1);
            }
        }

        private static SurgicalDifficulty? LevelFromArgs()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != "-nivel") { continue; }
                if (Enum.TryParse(args[i + 1], true, out SurgicalDifficulty level)) { return level; }
                Debug.LogError($"[Build] nível desconhecido '{args[i + 1]}': use Facil, Medio ou Dificil.");
            }

            return null;
        }

        /// <summary>The level the saved scene was generated at, read from its TransplantProcedure.</summary>
        private static SurgicalDifficulty? SceneLevel()
        {
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            TransplantProcedure procedure = UnityEngine.Object.FindFirstObjectByType<TransplantProcedure>();
            if (procedure == null) { return null; }

            SerializedProperty difficulty = new SerializedObject(procedure).FindProperty("difficulty");
            return difficulty != null ? (SurgicalDifficulty)difficulty.enumValueIndex : (SurgicalDifficulty?)null;
        }

        /// <summary>Regenerates the scene when it was last built at another level.</summary>
        private static void EnsureSceneLevel(SurgicalDifficulty level)
        {
            SurgicalDifficulty? current = SceneLevel();
            if (current == level)
            {
                Debug.Log($"[Build] cena já está no nível {level}");
                return;
            }

            Debug.Log($"[Build] cena no nível {current?.ToString() ?? "desconhecido"}; reconstruindo no nível {level}");
            switch (level)
            {
                case SurgicalDifficulty.Facil: TransplanteSceneBuilder.BuildEasy(); break;
                case SurgicalDifficulty.Dificil: TransplanteSceneBuilder.BuildHard(); break;
                default: TransplanteSceneBuilder.BuildMedium(); break;
            }
        }

        private static bool Build(BuildTarget target, string path)
        {
            if (!File.Exists(Scene))
            {
                Debug.LogError($"[Build] TRANSPLANT_BUILD_FAILED cena ausente: {Scene}");
                return false;
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
                return true;
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

            return false;
        }
    }
}
