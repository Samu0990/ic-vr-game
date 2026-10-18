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
        private static readonly string[] Instruments =
        {
            "Bisturi", "BisturiEletricoCaneta", "PincaDenteRato", "PortaAgulha", "SerraEsternal", "PasInternas",
        };

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
                Vector3 restPosition = instrument.transform.position;
                Quaternion restRotation = instrument.transform.rotation;
                PoseFingers(hand, hold, HandleRadius(instrument, hold));
                // In Edit Mode no XRI selection exists, so ControllerHand would otherwise keep
                // its fallback 40-degree pitch. Reproduce the held tool's own rotation directly.
                hand.GrabPoint.SetPositionAndRotation(hand.HoldPosition(hold),
                    hand.transform.rotation * Quaternion.Euler(hold.pitchDegrees, 0f, 0f));
                Attach(instrument, hand.GrabPoint);

                report.AppendLine(Measure(instrumentName, hand, instrument, hold));
                report.AppendLine(Sweep(hand, instrument));
                // Sweep deliberately finishes at a fully closed fist. Restore the actual
                // per-instrument pose before photographing it, otherwise every capture lies.
                PoseFingers(hand, hold, HandleRadius(instrument, hold));
                Shoot(folder, instrumentName, hand, instrument);

                instrument.transform.SetPositionAndRotation(restPosition, restRotation);
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
            Vector3 origin = hand.HoldPosition(hold);
            Vector3 axis = hand.transform.rotation * Quaternion.Euler(hold.pitchDegrees, 0f, 0f) * Vector3.forward;

            for (int finger = 0; finger < HandPoser.FingerCount; finger++)
            {
                if (hold.precisionPinch && finger > HandPoser.Middle)
                {
                    poser.SetFingerImmediate(finger, hold.gripClosure);
                    continue;
                }

                float solved = poser.SolveClosure(finger, origin, axis, radius, out float error);
                float declared = finger == HandPoser.Thumb ? hold.thumbClosure
                    : finger == HandPoser.Index ? hold.indexClosure : hold.gripClosure;
                float declaredDistance = Mathf.Abs(
                    poser.DistanceAt(finger, declared, origin, axis) - radius);
                poser.SetFingerImmediate(finger, error <= declaredDistance ? solved : declared);
            }
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
            Vector3 axis = instrument.GripPoint.forward;

            BoxCollider box = tool.GetComponent<BoxCollider>();
            float handleRadius = HandleRadius(instrument, hold);

            StringBuilder s = new StringBuilder();
            s.AppendLine($"--- {name}");
            s.AppendLine($"    pega declarada: grip={hold.gripClosure:0.00} index={hold.indexClosure:0.00} " +
                         $"thumb={hold.thumbClosure:0.00} pitch={hold.pitchDegrees:0}");
            s.AppendLine($"    raio do cabo: {handleRadius * 1000f:0.0} mm");

            Transform palm = HandPoser.FindBone(hand.Model, "Palm");
            if (palm != null)
            {
                s.AppendLine($"    palma -> eixo do cabo: {DistanceToAxis(palm.position, axisOrigin, axis) * 1000f:0.0} mm");
            }

            foreach (string finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
            {
                Transform tip = HandPoser.FindBone(hand.Model, finger + "Tip");
                if (tip == null) { continue; }

                float d = DistanceToAxis(tip.position, axisOrigin, axis) * 1000f;
                float gap = d - handleRadius * 1000f;
                string verdict = gap < -3f ? "DENTRO do aço" : gap > 12f ? "LONGE, fecha no vazio" : "ok";
                s.AppendLine($"    {finger,-7} ponta -> eixo: {d,6:0.0} mm   folga {gap,6:0.0} mm   {verdict}");
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
            Vector3 axis = instrument.GripPoint.forward;

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
            Bounds bounds = default;
            bool hasBounds = false;
            foreach (Renderer renderer in hand.Model.GetComponentsInChildren<Renderer>(true))
            {
                if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
                else { bounds.Encapsulate(renderer.bounds); }
            }
            foreach (Renderer renderer in instrument.GetComponentsInChildren<Renderer>(true))
            {
                if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
                else { bounds.Encapsulate(renderer.bounds); }
            }

            Vector3 centre = hasBounds ? bounds.center : hand.GrabPoint.position;
            Transform model = hand.Model;
            const float fov = 38f;
            float radius = hasBounds ? Mathf.Max(0.12f, bounds.extents.magnitude) : 0.15f;
            float distance = Mathf.Max(0.38f, radius / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * 1.25f);

            // Três ângulos em volta da mão, perto o bastante para ver dedo e cabo.
            (string tag, Vector3 dir)[] shots =
            {
                ("lado", model.right),
                ("palma", -model.up),
                ("frente", model.forward),
            };

            // Render only the hand and the current instrument. In the theatre, earlier probe
            // iterations and nearby tray tools otherwise overlap the grip and make the evidence
            // impossible to read.
            const int probeLayer = 31;
            Dictionary<GameObject, int> oldLayers = new Dictionary<GameObject, int>();
            SetLayer(hand.Model, probeLayer, oldLayers);
            SetLayer(instrument.transform, probeLayer, oldLayers);

            foreach ((string tag, Vector3 dir) in shots)
            {
                Vector3 from = centre + dir.normalized * distance + Vector3.up * radius * 0.12f;
                Render(Path.Combine(folder, $"{name}_{tag}.png"), from, centre, fov, 1 << probeLayer);
            }

            foreach (KeyValuePair<GameObject, int> entry in oldLayers) { entry.Key.layer = entry.Value; }
        }

        private static void SetLayer(Transform root, int layer, Dictionary<GameObject, int> oldLayers)
        {
            if (root == null) { return; }
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (oldLayers.ContainsKey(child.gameObject)) { continue; }
                oldLayers.Add(child.gameObject, child.gameObject.layer);
                child.gameObject.layer = layer;
            }
        }

        private static void Render(string path, Vector3 from, Vector3 look, float fov, int cullingMask)
        {
            GameObject rig = new GameObject("GripProbeCam");
            Camera cam = rig.AddComponent<Camera>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 50f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.09f, 0.11f);
            cam.cullingMask = cullingMask;
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
