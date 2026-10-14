using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRSurgery.Stand;

namespace VRSurgery.EditorTools
{
    /// <summary>
    /// Builds the stand's notebook program: a scene with one object that assembles the
    /// projector or ranking screen at start (see <see cref="CentralEstande"/>), and a desktop
    /// build of only that scene, with the scripts that launch both screens.
    ///
    /// The game's own build is untouched: this scene is not in the build list, and the headset
    /// build takes only TransplanteCardiaco.
    ///
    /// Headless: -executeMethod VRSurgery.EditorTools.CentralEstandeBuilder.BuildLinuxFromCommandLine
    /// </summary>
    public static class CentralEstandeBuilder
    {
        public const string ScenePath = "Assets/Scenes/CentralEstande.unity";
        public const string LinuxFolder = "Builds/CentralEstande_Linux";
        public const string WindowsFolder = "Builds/CentralEstande_Windows";

        [MenuItem("VRSurgery/Central do estande — criar cena")]
        public static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("CentralEstande").AddComponent<CentralEstande>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[Estande] cena salva em {ScenePath}");
        }

        [MenuItem("Build/Central do estande — Linux 64")]
        public static void BuildLinux() => Build(BuildTarget.StandaloneLinux64, LinuxFolder, "CentralEstande.x86_64");

        [MenuItem("Build/Central do estande — Windows 64")]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, WindowsFolder, "CentralEstande.exe");

        public static void BuildLinuxFromCommandLine() =>
            Exit(Build(BuildTarget.StandaloneLinux64, LinuxFolder, "CentralEstande.x86_64"));

        public static void BuildWindowsFromCommandLine() =>
            Exit(Build(BuildTarget.StandaloneWindows64, WindowsFolder, "CentralEstande.exe"));

        private static void Exit(BuildResult result) => EditorApplication.Exit(result == BuildResult.Succeeded ? 0 : 1);

        private static BuildResult Build(BuildTarget target, string folder, string executable)
        {
            if (!File.Exists(ScenePath)) { BuildScene(); }
            Directory.CreateDirectory(folder);

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.Combine(folder, executable),
                target = target,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            });

            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[Estande] CENTRAL_BUILD_FAILED {report.summary.result}, {report.summary.totalErrors} erro(s)");
                return report.summary.result;
            }

            if (target == BuildTarget.StandaloneLinux64) { WriteLinuxLaunchers(folder, executable); }
            else { WriteWindowsLaunchers(folder, executable); }
            File.WriteAllText(Path.Combine(folder, "LEIA-ME.txt"), ReadMe(target == BuildTarget.StandaloneLinux64));

            Debug.Log($"[Estande] CENTRAL_BUILD_OK {folder} ({report.summary.totalSize / (1024 * 1024)} MB)");
            return report.summary.result;
        }

        private static void WriteLinuxLaunchers(string folder, string executable)
        {
            string launcher =
                "#!/bin/sh\n" +
                "# Central do estande: abre o projetor e o ranking, cada um no seu monitor.\n" +
                "# Monitores contados a partir de 1 (1 = a tela do notebook). Para trocar:\n" +
                "#   MONITOR_PROJETOR=2 MONITOR_RANKING=3 ./iniciar_estande.sh\n" +
                "# Se o óculos não for achado sozinho, passe o IP dele:  ./iniciar_estande.sh -quest 10.42.0.23\n" +
                "cd \"$(dirname \"$0\")\"\n" +
                "mkdir -p dados_estande\n" +
                $"./{executable} -modo projetor -monitor ${{MONITOR_PROJETOR:-2}} -screen-fullscreen 0 -logFile dados_estande/log_projetor.txt \"$@\" &\n" +
                "sleep 2\n" +
                $"./{executable} -modo ranking -monitor ${{MONITOR_RANKING:-3}} -screen-fullscreen 0 -logFile dados_estande/log_ranking.txt \"$@\" &\n" +
                "wait\n";
            string path = Path.Combine(folder, "iniciar_estande.sh");
            File.WriteAllText(path, launcher.Replace("\r\n", "\n"));
            MakeExecutable(path);
            MakeExecutable(Path.Combine(folder, executable));
        }

        private static void WriteWindowsLaunchers(string folder, string executable)
        {
            string launcher =
                "@echo off\r\n" +
                "rem Central do estande: abre o projetor e o ranking, cada um no seu monitor.\r\n" +
                "cd /d \"%~dp0\"\r\n" +
                "if not exist dados_estande mkdir dados_estande\r\n" +
                "if \"%MONITOR_PROJETOR%\"==\"\" set MONITOR_PROJETOR=2\r\n" +
                "if \"%MONITOR_RANKING%\"==\"\" set MONITOR_RANKING=3\r\n" +
                $"start \"\" {executable} -modo projetor -monitor %MONITOR_PROJETOR% -screen-fullscreen 0 -logFile dados_estande\\log_projetor.txt %*\r\n" +
                "timeout /t 2 >nul\r\n" +
                $"start \"\" {executable} -modo ranking -monitor %MONITOR_RANKING% -screen-fullscreen 0 -logFile dados_estande\\log_ranking.txt %*\r\n";
            File.WriteAllText(Path.Combine(folder, "iniciar_estande.bat"), launcher);
        }

        private static void MakeExecutable(string path)
        {
#if UNITY_EDITOR_LINUX || UNITY_EDITOR_OSX
            try
            {
                System.Diagnostics.Process.Start("chmod", $"+x \"{Path.GetFullPath(path)}\"")?.WaitForExit(5000);
            }
            catch (System.Exception error)
            {
                Debug.LogWarning($"[Estande] chmod falhou em {path}: {error.Message}");
            }
#endif
        }

        private static string ReadMe(bool linux) =>
            "CENTRAL DO ESTANDE — Transplante Cardíaco VR\n" +
            "============================================\n\n" +
            "O jogo roda sozinho no Quest. Este programa roda no notebook ao lado do manequim e mostra:\n" +
            "  - PROJETOR: a vista de cima do paciente, que o projetor joga sobre o manequim;\n" +
            "  - RANKING: o ranking do dia, o que está acontecendo no óculos e o nome de quem terminou.\n\n" +
            "1. REDE\n" +
            "   O Quest e o notebook precisam estar na MESMA rede Wi-Fi. Wi-Fi de evento costuma\n" +
            "   bloquear aparelhos entre si: use o hotspot do notebook ou de um celular.\n\n" +
            "2. ABRIR\n" +
            (linux ? "   ./iniciar_estande.sh\n" : "   iniciar_estande.bat\n") +
            "   Abre as duas telas. Monitor 1 é a tela do notebook; o padrão é projetor no 2 e ranking no 3.\n" +
            "   Com só um monitor extra:  MONITOR_PROJETOR=2 MONITOR_RANKING=1 " + (linux ? "./iniciar_estande.sh" : "iniciar_estande.bat") + "\n" +
            "   Se o óculos não for achado sozinho: passe o IP dele com  -quest 10.42.0.23\n\n" +
            "3. NO DIA\n" +
            "   F9 / F10  muda a janela de monitor      F11  tela cheia      F1  ajuda\n" +
            "   PROJETOR: setas movem, + e - dão zoom, R gira, H e V espelham, G mostra o padrão de teste.\n" +
            "             O ajuste fica salvo em dados_estande/calibracao_projetor.json.\n" +
            "   RANKING:  quando alguém termina, aparece o campo de nome. Digite e Enter.\n" +
            "             O visitante também pode digitar no teclado do próprio óculos.\n" +
            "             F2 edita o ranking (R renomeia, Delete duas vezes remove).\n\n" +
            "4. DADOS\n" +
            "   O ranking zera sozinho a cada dia. Cada dia fica em dados_estande/ranking_AAAA-MM-DD.json,\n" +
            "   com uma cópia .csv para a pesquisa (todas as rodadas, inclusive as que não terminaram).\n";
    }
}
