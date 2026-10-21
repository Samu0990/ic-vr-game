using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
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
                    hand.transform.rotation * Quaternion.Euler(hold.pitchDegrees, hold.yawDegrees, 0f));
                Attach(instrument, hand.GrabPoint);

                report.AppendLine(Measure(instrumentName, hand, instrument, hold));
                if (instrumentName == "Bisturi")
                {
                    report.AppendLine(PrecisionGeometry(hand));
                    report.AppendLine(SearchPrecisionSeat(hand, hold, HandleRadius(instrument, hold)));
                }
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
            Vector3 axis = hand.transform.rotation * Quaternion.Euler(hold.pitchDegrees, hold.yawDegrees, 0f) * Vector3.forward;

            float share = hold.precisionPinch ? hold.KnuckleShare : 1f;
            poser.SetKnuckleShare(HandPoser.Thumb, share);
            poser.SetKnuckleShare(HandPoser.Index, share);

            for (int finger = 0; finger < HandPoser.FingerCount; finger++)
            {
                // A scalpel's index finger rests along the spine; its fingertip is not supposed
                // to meet the axis. Mirror ControllerHand instead of turning the diagnostic pose
                // back into the old claw grip.
                if (hold.precisionPinch && finger == HandPoser.Index)
                {
                    poser.SetFingerImmediate(finger, hold.indexClosure);
                    continue;
                }

                if (hold.precisionPinch && finger > HandPoser.Middle)
                {
                    poser.SetFingerImmediate(finger, hold.Tuck);
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
                         $"thumb={hold.thumbClosure:0.00} pitch={hold.pitchDegrees:0} yaw={hold.yawDegrees:0}");
            s.AppendLine($"    raio do cabo: {handleRadius * 1000f:0.0} mm");

            if (box != null)
            {
                Vector3 c = tool.TransformPoint(box.center);
                float half = 0f;
                Vector3 e = box.size * 0.5f;
                half += Mathf.Abs(Vector3.Dot(tool.TransformVector(new Vector3(e.x, 0f, 0f)), axis.normalized));
                half += Mathf.Abs(Vector3.Dot(tool.TransformVector(new Vector3(0f, e.y, 0f)), axis.normalized));
                half += Mathf.Abs(Vector3.Dot(tool.TransformVector(new Vector3(0f, 0f, e.z)), axis.normalized));
                float mid = Vector3.Dot(c - axisOrigin, axis.normalized) * 1000f;
                s.AppendLine($"    corpo da ferramenta ao longo do eixo: {mid - half * 1000f:0.0} .. {mid + half * 1000f:0.0} mm");
            }

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
                float station = Vector3.Dot(tip.position - axisOrigin, axis.normalized) * 1000f;
                bool free = hold.precisionPinch && (finger == "Ring" || finger == "Little");
                string verdict = free ? "livre (pinça)"
                    : gap < -3f ? "DENTRO do aço"
                    : gap > 12f ? "LONGE, fecha no vazio" : "ok";
                s.AppendLine($"    {finger,-7} ponta -> eixo: {d,6:0.0} mm   folga {gap,6:0.0} mm   " +
                             $"ao longo {station,7:0.0} mm   {verdict}");
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

        /// <summary>
        /// Searches the hand's local cross-section for the line that a pencil-gripped scalpel
        /// should follow. This is diagnostic only: it measures the authored index pose and the
        /// best reachable thumb/middle contacts instead of guessing an offset by eye.
        /// </summary>
        private static string SearchPrecisionSeat(ControllerHand hand, in HandHold hold, float radius)
        {
            HandPoser poser = hand.Poser;
            if (poser == null || !poser.IsCalibrated) { return "    busca de assento: sem poser"; }

            float best = float.MaxValue;
            Vector3 bestOffset = Vector3.zero;
            float bestPitch = 0f, bestThumb = 0f, bestMiddle = 0f;
            float bestThumbDistance = 0f, bestIndexDistance = 0f, bestMiddleDistance = 0f;
            float bestThumbStation = 0f, bestIndexStation = 0f, bestMiddleStation = 0f;

            poser.SetKnuckleShare(HandPoser.Thumb, hold.KnuckleShare);
            poser.SetKnuckleShare(HandPoser.Index, hold.KnuckleShare);
            poser.SetFingerImmediate(HandPoser.Index, hold.indexClosure);

            for (float pitch = 45f; pitch <= 65f; pitch += 2f)
            {
                Vector3 axis = hand.transform.rotation * Quaternion.Euler(pitch, -87f, 0f) * Vector3.forward;
                for (float y = -0.04f; y <= 0.0201f; y += 0.002f)
                {
                    for (float z = -0.03f; z <= 0.0401f; z += 0.002f)
                    {
                        Vector3 offset = new Vector3(hold.seatOffset.x, y, z);
                        Vector3 origin = hand.FistPosition + hand.transform.TransformVector(offset);

                        float thumb = poser.SolveClosure(HandPoser.Thumb, origin, axis, radius, out _);
                        float middle = poser.SolveClosure(HandPoser.Middle, origin, axis, radius, out _);
                        poser.SetFingerImmediate(HandPoser.Thumb, thumb);
                        poser.SetFingerImmediate(HandPoser.Middle, middle);

                        float dt = DistanceToAxis(poser.TipPosition(HandPoser.Thumb), origin, axis);
                        float di = DistanceToAxis(poser.TipPosition(HandPoser.Index), origin, axis);
                        float dm = DistanceToAxis(poser.TipPosition(HandPoser.Middle), origin, axis);
                        float st = Vector3.Dot(poser.TipPosition(HandPoser.Thumb) - origin, axis);
                        float si = Vector3.Dot(poser.TipPosition(HandPoser.Index) - origin, axis);
                        float sm = Vector3.Dot(poser.TipPosition(HandPoser.Middle) - origin, axis);

                        // Thumb and middle must touch the handle. The straight index rides just
                        // above its spine, with glove thickness rather than an exact steel contact.
                        float indexTarget = radius + 0.004f;
                        float score = Mathf.Abs(dt - radius) + Mathf.Abs(dm - radius) +
                                      Mathf.Abs(di - indexTarget) * 1.25f +
                                      Mathf.Abs(st - sm) * 0.35f +
                                      Mathf.Abs((si - st) - 0.03f) * 0.2f;
                        if (score >= best) { continue; }

                        best = score;
                        bestOffset = offset;
                        bestPitch = pitch;
                        bestThumb = thumb;
                        bestMiddle = middle;
                        bestThumbDistance = dt;
                        bestIndexDistance = di;
                        bestMiddleDistance = dm;
                        bestThumbStation = st;
                        bestIndexStation = si;
                        bestMiddleStation = sm;
                    }
                }
            }

            return $"    busca de assento: pitch={bestPitch:0} offset={bestOffset * 1000f} mm " +
                   $"closures T={bestThumb:0.00} M={bestMiddle:0.00} " +
                   $"dist T/I/M={bestThumbDistance * 1000f:0.0}/{bestIndexDistance * 1000f:0.0}/{bestMiddleDistance * 1000f:0.0} mm " +
                   $"estacoes={bestThumbStation * 1000f:0.0}/{bestIndexStation * 1000f:0.0}/{bestMiddleStation * 1000f:0.0} mm";
        }

        private static string PrecisionGeometry(ControllerHand hand)
        {
            Transform proximal = HandPoser.FindBone(hand.Model, "IndexProximal");
            Transform tip = HandPoser.FindBone(hand.Model, "IndexTip");
            Vector3 localDirection = proximal != null && tip != null
                ? hand.transform.InverseTransformDirection((tip.position - proximal.position).normalized)
                : Vector3.zero;

            string Result(string finger)
            {
                Transform t = HandPoser.FindBone(hand.Model, finger + "Tip");
                return t != null ? hand.transform.InverseTransformPoint(t.position).ToString("F4") : "ausente";
            }

            return $"    geometria local: eixo do indicador={localDirection:F4}; " +
                   $"pontas T={Result("Thumb")} I={Result("Index")} M={Result("Middle")}";
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

            // The theatre lights are composed for the room, not for every diagnostic angle.
            // In particular, the side view used to render as an almost black silhouette and
            // could not prove whether a finger was above or below the scalpel. A camera-mounted
            // key light makes all three evidence shots readable without changing the scene or
            // any runtime material.
            GameObject keyObject = new GameObject("GripProbeKey");
            keyObject.transform.SetParent(rig.transform, false);
            Light key = keyObject.AddComponent<Light>();
            key.type = LightType.Point;
            key.range = 4f;
            key.intensity = 7f;
            key.shadows = LightShadows.None;
            key.cullingMask = cullingMask;
            keyObject.transform.position = rig.transform.position;

            RenderTexture target = new RenderTexture(1200, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = target;

            AmbientMode oldAmbientMode = RenderSettings.ambientMode;
            Color oldAmbientLight = RenderSettings.ambientLight;
            float oldAmbientIntensity = RenderSettings.ambientIntensity;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.72f, 0.75f, 0.8f);
            RenderSettings.ambientIntensity = 1f;
            cam.Render();
            RenderSettings.ambientMode = oldAmbientMode;
            RenderSettings.ambientLight = oldAmbientLight;
            RenderSettings.ambientIntensity = oldAmbientIntensity;

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
