using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRSurgery.Data;
using VRSurgery.Interaction;

namespace VRSurgery.EditorTools
{
    /// <summary>
    /// Mede e fotografa como um instrumento fica na mão, sem óculos e sem entrar em Play.
    ///
    /// No Editor a mão não está segurando nada, então foto da cena parada não mostra a pega. Esta
    /// sonda reproduz a pose de segurar do jeito que a XRI faz — alinha o GripPoint da ferramenta
    /// com o ponto de pega da mão, com os dedos fechados na medida que a própria ferramenta pede
    /// — e aí mede a distância de cada ponta de dedo até o eixo do cabo. É esse número que diz se
    /// a ferramenta está na palma ou flutuando ao lado dela.
    ///
    /// Uso:
    ///   -executeMethod VRSurgery.EditorTools.GripProbe.RunFromCommandLine -out PASTA
    /// </summary>
    public static class GripProbe
    {
        private const string ScenePath = "Assets/Scenes/TransplanteCardiaco.unity";
        private static readonly string[] Instruments = { "Bisturi", "PincaDenteRato", "PortaAgulha" };

        public static void RunFromCommandLine()
        {
            string output = "Logs/GripProbe";
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-out") { output = args[i + 1]; }
            }

            try
            {
                Run(output);
                Debug.Log("[GripProbe] GRIP_PROBE_OK");
                EditorApplication.Exit(0);
            }
            catch (System.Exception exception)
            {
                Debug.LogError("[GripProbe] GRIP_PROBE_FAILED " + exception);
                EditorApplication.Exit(1);
            }
        }

        [MenuItem("VRSurgery/Sonda da pega")]
        public static void RunMenu() => Run("Logs/GripProbe");

        /// <summary>
        /// Se dá para desenhar. Em -batchmode -nographics não há dispositivo gráfico, e mandar a
        /// URP renderizar assim derruba o editor com SIGSEGV no meio da sonda — as medidas, que
        /// são o que importa, não dependem de desenhar nada.
        /// </summary>
        private static bool CanRender =>
            SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;

        public static void Run(string folder)
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Directory.CreateDirectory(folder);

            ControllerHand hand = FindHand("MaoDireita");
            if (hand == null) { Debug.LogError("[GripProbe] MaoDireita não encontrada."); return; }

            hand.AlignModel();
            StringBuilder report = new StringBuilder();

            foreach (string instrumentName in Instruments)
            {
                GameObject toolObject = Find(instrumentName);
                SurgicalInteractable instrument = toolObject != null
                    ? toolObject.GetComponent<SurgicalInteractable>()
                    : null;

                if (instrument == null || instrument.GripPoint == null)
                {
                    report.AppendLine($"{instrumentName}: ausente ou sem GripPoint");
                    continue;
                }

                HandHold hold = ControllerHand.HoldFor(instrument, HandHold.Default);
                PoseFingers(hand, hold, HandleRadius(instrument, hold));
                hand.PlaceGrabPoint();
                Attach(instrument, hand.GrabPoint);

                report.AppendLine(Measure(instrumentName, hand, instrument, hold));
                report.AppendLine(Sweep(hand, instrument));

                // Gravado a cada instrumento: sem isto um tombo no último perde as medidas de
                // todos os anteriores, que é exatamente o que acontecia ao rodar sem tela.
                File.WriteAllText(Path.Combine(folder, "pega.txt"), report.ToString());

                if (CanRender) { Shoot(folder, instrumentName, hand, instrument); }
            }

            if (!CanRender)
            {
                report.AppendLine("(sem GPU: só as medidas, sem os PNGs)");
            }

            string path = Path.Combine(folder, "pega.txt");
            File.WriteAllText(path, report.ToString());
            Debug.Log("[GripProbe] relatório:\n" + report);
        }

        /// <summary>Fecha os dedos na medida que a ferramenta pede, como em Update ao segurar.</summary>
        private static void PoseFingers(ControllerHand hand, in HandHold hold, float radius)
        {
            HandPoser poser = hand.Poser;
            if (poser == null) { return; }

            poser.Calibrate();

            // Mesmo caminho do jogo: resolve quanto cada dedo tem de fechar para alcançar o cabo,
            // e quem não alcança fica na pose que a ferramenta pediu.
            Vector3 origin = hand.FistPosition;
            Vector3 axis = hand.transform.rotation * Quaternion.Euler(hold.pitchDegrees, 0f, 0f) * Vector3.forward;

            HandPoser.Contact contact = hold.IsPencil ? HandPoser.Contact.Pad : HandPoser.Contact.Tip;

            for (int finger = 0; finger < HandPoser.FingerCount; finger++)
            {
                float declared = DeclaredClosure(hold, finger);

                // Dedo recolhido não resolve contra nada: só dobra. Igual ao jogo.
                if (!Touches(hold, finger))
                {
                    poser.SetFingerImmediate(finger, declared);
                    continue;
                }

                float solved = poser.SolveClosure(finger, origin, axis, radius, contact, out float error);
                poser.SetFingerImmediate(finger, error <= 0.022f ? solved : declared);
            }
        }

        /// <summary>Quais dedos encostam no cabo: pinça são três, punho são cinco.</summary>
        private static bool Touches(in HandHold hold, int finger)
        {
            if (!hold.IsPencil) { return true; }
            return finger == HandPoser.Thumb || finger == HandPoser.Index || finger == HandPoser.Middle;
        }

        private static float DeclaredClosure(in HandHold hold, int finger)
        {
            if (finger == HandPoser.Thumb) { return hold.thumbClosure; }
            if (finger == HandPoser.Index) { return hold.indexClosure; }
            if (!Touches(hold, finger)) { return hold.TuckClosure; }
            return hold.gripClosure;
        }

        /// <summary>Raio do cabo: o que a ferramenta declara, ou medido no collider dela.</summary>
        private static float HandleRadius(SurgicalInteractable instrument, in HandHold hold)
        {
            if (hold.handleRadius > 0f) { return hold.handleRadius; }

            BoxCollider box = instrument.GetComponent<BoxCollider>();
            return box != null ? Mathf.Min(box.size.x, box.size.y) * 0.5f : 0.009f;
        }

        /// <summary>Põe o GripPoint da ferramenta exatamente sobre o ponto de pega da mão.</summary>
        private static void Attach(SurgicalInteractable instrument, Transform handGrab)
        {
            Transform tool = instrument.transform;
            Transform grip = instrument.GripPoint;

            Quaternion gripLocal = Quaternion.Inverse(tool.rotation) * grip.rotation;
            tool.rotation = handGrab.rotation * Quaternion.Inverse(gripLocal);
            tool.position += handGrab.position - grip.position;
        }

        /// <summary>
        /// A conta que importa: a que distância cada ponta de dedo passa do eixo do cabo. Perto de
        /// zero é dedo dentro do aço; muito maior que o raio do cabo é mão fechada no vazio.
        /// </summary>
        private static string Measure(string name, ControllerHand hand, SurgicalInteractable instrument, in HandHold hold)
        {
            Transform tool = instrument.transform;
            Vector3 axisOrigin = instrument.GripPoint.position;
            Vector3 axis = tool.forward;

            BoxCollider box = tool.GetComponent<BoxCollider>();
            float handleRadius = HandleRadius(instrument, hold);

            StringBuilder s = new StringBuilder();
            s.AppendLine($"--- {name}");
            s.AppendLine($"    pega: {(hold.IsPencil ? "PINÇA (lápis)" : "PUNHO")}  " +
                         $"grip={hold.gripClosure:0.00} index={hold.indexClosure:0.00} " +
                         $"thumb={hold.thumbClosure:0.00} recolhe={hold.TuckClosure:0.00} pitch={hold.pitchDegrees:0}");
            s.AppendLine($"    raio do cabo: {handleRadius * 1000f:0.0} mm");

            Transform palm = HandPoser.FindBone(hand.Model, "Palm");
            if (palm != null)
            {
                s.AppendLine($"    palma -> eixo do cabo: {DistanceToAxis(palm.position, axisOrigin, axis) * 1000f:0.0} mm");
            }

            string[] names = { "Thumb", "Index", "Middle", "Ring", "Little" };
            for (int finger = 0; finger < names.Length; finger++)
            {
                Transform tip = HandPoser.FindBone(hand.Model, names[finger] + "Tip");
                if (tip == null) { continue; }

                // Numa pinça o cabo cruza a POLPA, não a ponta: medir a ponta mede o dedo que já
                // passou do cabo e diz que está longe quando está certo.
                bool pad = hold.IsPencil && Touches(hold, finger);
                Transform distal = HandPoser.FindBone(hand.Model, names[finger] + "Distal");
                Vector3 point = pad && distal != null ? (distal.position + tip.position) * 0.5f : tip.position;
                string where = pad && distal != null ? "polpa" : "ponta";

                float d = DistanceToAxis(point, axisOrigin, axis) * 1000f;
                float gap = d - handleRadius * 1000f;

                string verdict = !Touches(hold, finger)
                    ? "recolhido, não toca (certo)"
                    : gap < -3f ? "DENTRO do aço" : gap > 12f ? "LONGE, fecha no vazio" : "ok";

                s.AppendLine($"    {names[finger],-7} {where} -> eixo: {d,6:0.0} mm   folga {gap,6:0.0} mm   {verdict}");
            }

            return s.ToString();
        }

        /// <summary>
        /// Varre o fechamento de 0 a 1 e diz a que distância do eixo do cabo cada ponta de dedo
        /// chega. É isso que responde se fechar mais resolve, ou se nem o punho cheio alcança.
        /// </summary>
        private static string Sweep(ControllerHand hand, SurgicalInteractable instrument)
        {
            HandPoser poser = hand.Poser;
            if (poser == null) { return "    (sem HandPoser)"; }

            Vector3 origin = instrument.GripPoint.position;
            Vector3 axis = instrument.transform.forward;

            StringBuilder s = new StringBuilder();
            s.AppendLine("    varredura de fechamento (mm da ponta ao eixo do cabo):");
            s.AppendLine("      fecha   Thumb  Index Middle   Ring Little");

            for (int step = 0; step <= 10; step += 2)
            {
                float c = step / 10f;
                poser.TickTo(1f, c, c, c);

                s.Append($"      {c,5:0.0}  ");
                foreach (string finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
                {
                    Transform tip = HandPoser.FindBone(hand.Model, finger + "Tip");
                    float d = tip != null ? DistanceToAxis(tip.position, origin, axis) * 1000f : -1f;
                    s.Append($"{d,6:0.0} ");
                }

                s.AppendLine();
            }

            return s.ToString();
        }

        private static float DistanceToAxis(Vector3 point, Vector3 origin, Vector3 direction)
        {
            Vector3 v = point - origin;
            return (v - Vector3.Project(v, direction.normalized)).magnitude;
        }

        private static void Shoot(string folder, string name, ControllerHand hand, SurgicalInteractable instrument)
        {
            Vector3 centre = hand.GrabPoint.position;
            Transform model = hand.Model;

            // Três ângulos em volta da mão, perto o bastante para ver dedo e cabo.
            (string tag, Vector3 dir)[] shots =
            {
                ("lado", model.right),
                ("palma", -model.up),
                ("frente", model.forward),
            };

            foreach ((string tag, Vector3 dir) in shots)
            {
                Vector3 from = centre + dir.normalized * 0.22f + Vector3.up * 0.04f;
                Render(Path.Combine(folder, $"{name}_{tag}.png"), from, centre, 34f);
            }
        }

        private static void Render(string path, Vector3 from, Vector3 look, float fov)
        {
            GameObject rig = new GameObject("GripProbeCam");
            Camera cam = rig.AddComponent<Camera>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 50f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.09f, 0.11f);
            rig.transform.position = from;
            rig.transform.LookAt(look);

            RenderTexture target = new RenderTexture(1200, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = target;
            cam.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            Texture2D shot = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
            shot.Apply();
            RenderTexture.active = previous;

            File.WriteAllBytes(path, shot.EncodeToPNG());

            cam.targetTexture = null;
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(shot);
            Object.DestroyImmediate(rig);
        }

        private static ControllerHand FindHand(string name)
        {
            foreach (ControllerHand candidate in Object.FindObjectsByType<ControllerHand>(FindObjectsSortMode.None))
            {
                if (candidate.name == name) { return candidate; }
            }

            return null;
        }

        private static GameObject Find(string name)
        {
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (t.name == name) { return t.gameObject; }
            }

            return null;
        }
    }
}
