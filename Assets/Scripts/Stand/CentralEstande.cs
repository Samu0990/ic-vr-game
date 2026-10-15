using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Management;

namespace VRSurgery.Stand
{
    /// <summary>
    /// The stand's notebook program. One build, two jobs, picked on the command line:
    ///
    ///   -modo projetor   the overhead picture for the projector over the mannequin
    ///   -modo ranking    the public monitor: today's ranking and the visitor's name (default)
    ///
    /// Run one of each, one per monitor. Both find the headset on their own; -quest IP skips the
    /// search if the network blocks it. -monitor N puts the window on the Nth monitor in full
    /// screen; F9/F10 move it between monitors and F11 toggles full screen on the day.
    /// -dados PASTA chooses where the ranking files live (next to the program by default).
    /// </summary>
    public class CentralEstande : MonoBehaviour
    {
        public enum Mode
        {
            Ranking,
            Projetor,
        }

        [SerializeField] private Mode defaultMode = Mode.Ranking;

        private int _monitor = -1;

        public static string DefaultDataFolder =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "dados_estande");

        private void Awake()
        {
            StopXr();

            // A janela do projetor fica sem foco o dia inteiro e precisa continuar recebendo imagem.
            Application.runInBackground = true;
            Application.targetFrameRate = 30;
            QualitySettings.vSyncCount = 0;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            Mode mode = Argument("-modo") switch
            {
                "projetor" => Mode.Projetor,
                "ranking" => Mode.Ranking,
                _ => defaultMode,
            };

            string data = Argument("-dados") ?? DefaultDataFolder;
            Directory.CreateDirectory(data);

            // O cliente precisa saber do IP manual antes de ligar: monta desligado e liga no fim.
            GameObject linkHost = new GameObject("Link");
            linkHost.SetActive(false);
            linkHost.transform.SetParent(transform, false);
            StandLinkClient client = linkHost.AddComponent<StandLinkClient>();
            client.ManualHost = Argument("-quest");
            if (int.TryParse(Argument("-porta"), out int port)) { client.ManualPort = port; }
            client.WantsFrames = mode == Mode.Projetor;
            client.Role = mode == Mode.Projetor ? "projetor" : "ranking";
            linkHost.SetActive(true);

            if (mode == Mode.Projetor)
            {
                ProjectorView.Create(gameObject, client, data);
            }
            else
            {
                RankingView.Create(gameObject, client, new StandRanking(data));
            }

            if (int.TryParse(Argument("-monitor"), out int monitor)) { _monitor = monitor - 1; }
            Debug.Log($"[Estande] Central em modo {mode}, dados em {data}, óculos: {client.ManualHost ?? "procurando na rede"}.");
        }

        private IEnumerator Start()
        {
            if (_monitor >= 0) { yield return MoveToMonitor(_monitor, fullScreen: true); }
        }

        private void Update()
        {
            Keyboard k = Keyboard.current;
            if (k == null) { return; }

            if (k.f11Key.wasPressedThisFrame)
            {
                Screen.fullScreenMode = Screen.fullScreenMode == FullScreenMode.Windowed
                    ? FullScreenMode.FullScreenWindow
                    : FullScreenMode.Windowed;
            }

            if (k.f10Key.wasPressedThisFrame) { StartCoroutine(MoveToMonitor(CurrentMonitor() + 1, Screen.fullScreenMode != FullScreenMode.Windowed)); }
            if (k.f9Key.wasPressedThisFrame) { StartCoroutine(MoveToMonitor(CurrentMonitor() - 1, Screen.fullScreenMode != FullScreenMode.Windowed)); }
        }

        private static int CurrentMonitor()
        {
            List<DisplayInfo> displays = new List<DisplayInfo>();
            Screen.GetDisplayLayout(displays);
            int index = displays.FindIndex(d => d.Equals(Screen.mainWindowDisplayInfo));
            return Mathf.Max(0, index);
        }

        /// <summary>Puts the window on a monitor, wrapping round the list, optionally in full screen there.</summary>
        private static IEnumerator MoveToMonitor(int index, bool fullScreen)
        {
            List<DisplayInfo> displays = new List<DisplayInfo>();
            Screen.GetDisplayLayout(displays);
            if (displays.Count == 0) { yield break; }

            index = ((index % displays.Count) + displays.Count) % displays.Count;
            DisplayInfo target = displays[index];

            // Uma janela em tela cheia não se move: sai, move, e volta.
            if (Screen.fullScreenMode != FullScreenMode.Windowed)
            {
                Screen.fullScreenMode = FullScreenMode.Windowed;
                yield return null;
            }

            AsyncOperation move = null;
            try { move = Screen.MoveMainWindowTo(target, new Vector2Int(40, 40)); }
            catch (Exception error) { Debug.LogWarning($"[Estande] Não deu para mover a janela: {error.Message}"); }
            if (move != null) { yield return move; }

            if (fullScreen)
            {
                yield return null;
                Screen.SetResolution(target.width, target.height, FullScreenMode.FullScreenWindow);
            }

            Debug.Log($"[Estande] Janela no monitor {index + 1} de {displays.Count}: {target.name} {target.width}x{target.height}.");
        }

        /// <summary>
        /// The project ships with OpenXR switched on for the headset. The notebook has no headset,
        /// and an XR loader left running would fight the plain window for the screen.
        /// </summary>
        private static void StopXr()
        {
            XRManagerSettings manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (manager == null || !manager.isInitializationComplete) { return; }

            manager.StopSubsystems();
            manager.DeinitializeLoader();
            Debug.Log("[Estande] XR desligado: a central do estande não usa óculos.");
        }

        private static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) { return args[i + 1]; }
            }

            return null;
        }
    }
}
