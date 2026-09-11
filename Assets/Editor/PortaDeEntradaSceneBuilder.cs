using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using VRSurgery.Data;
using VRSurgery.Interaction;
using VRSurgery.Ports;
using VRSurgery.Session;
using VRSurgery.Surgery;
using VRSurgery.Tools;
using VRSurgery.VR;

namespace VRSurgery.EditorTools
{
    /// <summary>
    /// Builds "A Porta de Entrada": the trocar-insertion round described in the event concept.
    ///
    /// A separate scene from SurgeryMVP rather than a rewrite of it. The two are different games —
    /// one is an incision and a bleed, this is four port sites and a decision at each — and the
    /// existing scene carries a suite that measures it. Replacing it in place would have deleted
    /// working, tested work to make room for this.
    ///
    /// Everything vertical is anchored the same way the first scene was: the template floor, and
    /// the patient's own belly surface sampled from the mesh at build time.
    /// </summary>
    public static class PortaDeEntradaSceneBuilder
    {
        private const string SourceScene = "Assets/Scenes/SampleScene.unity";
        private const string TargetScene = "Assets/Scenes/PortaDeEntrada.unity";

        private const string BodyFbx = "Assets/Models/Patient/PATIENT_ExternalBody.fbx";
        private const string TableFbx = "Assets/Models/Environment/PROP_OperatingTable.fbx";
        private const string TrayFbx = "Assets/Models/Environment/PROP_InstrumentTray.fbx";
        private const string TrocarGlb = "Assets/Models/Tools/TROCAR_11mm.glb";
        private const string GauzeGlb = "Assets/Models/Tools/GAUZE_Pad.glb";

        private const float TableTopY = 0.95f;
        private const float TableLengthZ = 1.90f;

        /// <summary>Stance and tray carried over from the validated layout of the first scene.</summary>
        private static Vector3 PlayerStance => new Vector3(0.45f, 0f, 0.406f);
        private static readonly Vector3 TrayStandPosition = new Vector3(0.44f, 0f, 0.80f);
        private const float TrayStandWidthX = 0.20f;
        private const float TrayStandDepthZ = 0.50f;

        private const int ProjectionDisplayIndex = 1;

        /// <summary>
        /// The four port sites, in body coordinates (X across, Z along). Laid out the way a
        /// laparoscopic cholecystectomy is: the camera port at the umbilicus, the working port in
        /// the epigastrium, and two smaller ports out to the patient's right.
        ///
        /// The vessel offset differs per site on purpose. The inferior epigastric runs up the
        /// wall lateral to the midline, so the midline sites are comparatively forgiving and the
        /// lateral ones are where the real decision is.
        /// </summary>
        private struct PortSpec
        {
            public string Id;
            public float X, Z;
            public float SafeRadius;
            public Vector2 VesselOffset;
            public float VesselRadius;
        }

        private static readonly PortSpec[] Sites =
        {
            new PortSpec { Id = "umbilical",  X =  0.00f, Z = 0.34f, SafeRadius = 0.020f,
                           VesselOffset = new Vector2(0.020f, 0.004f), VesselRadius = 0.009f },
            new PortSpec { Id = "epigastrico", X = 0.01f, Z = 0.50f, SafeRadius = 0.019f,
                           VesselOffset = new Vector2(-0.017f, 0.006f), VesselRadius = 0.010f },
            new PortSpec { Id = "subcostal",  X =  0.07f, Z = 0.45f, SafeRadius = 0.017f,
                           VesselOffset = new Vector2(0.012f, -0.008f), VesselRadius = 0.011f },
            new PortSpec { Id = "flanco",     X =  0.10f, Z = 0.36f, SafeRadius = 0.016f,
                           VesselOffset = new Vector2(-0.010f, 0.009f), VesselRadius = 0.011f },
        };

        [MenuItem("VRSurgery/Build 'A Porta de Entrada'")]
        public static void BuildFromMenu()
        {
            Build();
            EditorUtility.DisplayDialog("A Porta de Entrada",
                "Cena construída. Veja o Console para as medidas.", "OK");
        }

        public static void Build()
        {
            EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single);

            GameObject area = GameObject.Find("Teleport Area");
            if (area != null) { Object.DestroyImmediate(area); }

            RenameRig();

            GameObject table = BuildTable();
            GameObject patient = BuildPatient(table);
            GameObject systems = BuildSystems();

            GameObject tray = BuildTray();
            GameObject trocar = BuildTrocar(tray);
            GameObject gauze = BuildGauze(tray);

            PortProcedure procedure = BuildPorts(patient, systems);
            WireSession(systems, procedure, trocar, gauze);
            BuildProjection(systems, procedure);
            BuildUrgencyVignette(systems);

            PlaceAnchor();
            EnsureMainCamera();

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), TargetScene, true);
            RegisterSceneInBuildSettings();
            Debug.Log("[Porta] saved " + TargetScene);
        }

        // ---------------------------------------------------------------- ambiente

        private static GameObject BuildTable()
        {
            GameObject root = new GameObject("OperatingTable");
            GameObject top = Instantiate(TableFbx, root.transform);
            top.name = "TableTop";
            foreach (MeshRenderer r in top.GetComponentsInChildren<MeshRenderer>())
            {
                r.sharedMaterial = MakeMaterial(new Color(0.62f, 0.66f, 0.70f), 0.1f, 0.55f);
            }

            BoxCollider slab = top.AddComponent<BoxCollider>();
            slab.center = new Vector3(0f, TableTopY - 0.03f, 0f);
            slab.size = new Vector3(0.50f, 0.06f, TableLengthZ);
            return root;
        }

        private static GameObject BuildPatient(GameObject table)
        {
            GameObject root = new GameObject("Patient");
            root.transform.position = new Vector3(0f, TableTopY, 0f);
            Instantiate(BodyFbx, root.transform);
            root.transform.SetParent(table.transform, true);
            return root;
        }

        /// <summary>Highest body vertex near (x, z). The wall has to sit on the skin, not near it.</summary>
        private static float SampleSurfaceY(GameObject patient, float x, float z, float radius)
        {
            float best = float.MinValue;
            foreach (MeshFilter filter in patient.GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || !mesh.isReadable) { continue; }

                foreach (Vector3 v in mesh.vertices)
                {
                    Vector3 w = filter.transform.TransformPoint(v);
                    if ((w.x - x) * (w.x - x) + (w.z - z) * (w.z - z) <= radius * radius && w.y > best)
                    {
                        best = w.y;
                    }
                }
            }

            return best == float.MinValue ? TableTopY : best;
        }

        // ---------------------------------------------------------------- portas

        private static PortProcedure BuildPorts(GameObject patient, GameObject systems)
        {
            GameObject wall = new GameObject("AbdominalWall");
            wall.transform.SetParent(patient.transform, false);

            InsertionPort[] built = new InsertionPort[Sites.Length];

            for (int i = 0; i < Sites.Length; i++)
            {
                PortSpec spec = Sites[i];
                float y = SampleSurfaceY(patient, spec.X, spec.Z, 0.04f);

                GameObject site = new GameObject("Port_" + spec.Id);
                site.transform.SetParent(wall.transform, true);
                site.transform.position = new Vector3(spec.X, y, spec.Z);

                InsertionPort port = site.AddComponent<InsertionPort>();
                port.Configure(spec.Id, spec.SafeRadius, spec.VesselOffset, spec.VesselRadius);

                BuildSiteVisual(site.transform, spec);
                built[i] = port;

                Debug.Log($"[Porta] site '{spec.Id}' em ({spec.X:F3}, {y:F4}, {spec.Z:F3}) " +
                          $"seguro r={spec.SafeRadius * 1000f:F0}mm vaso r={spec.VesselRadius * 1000f:F0}mm " +
                          $"deslocado {spec.VesselOffset.magnitude * 1000f:F0}mm");
            }

            PortProcedure procedure = systems.AddComponent<PortProcedure>();
            procedure.Bind(built);
            return procedure;
        }

        /// <summary>
        /// What transillumination looks like: the safe area as a pale ring on the skin, and the
        /// vessel as a darker shadow crossing it. They overlap, which is the whole decision.
        /// </summary>
        private static void BuildSiteVisual(Transform site, PortSpec spec)
        {
            GameObject safe = GameObject.CreatePrimitive(PrimitiveType.Quad);
            safe.name = "SafeZone";
            safe.transform.SetParent(site, false);
            safe.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            safe.transform.localPosition = new Vector3(0f, 0.0012f, 0f);
            safe.transform.localScale = Vector3.one * (spec.SafeRadius * 2f);
            Object.DestroyImmediate(safe.GetComponent<Collider>());
            safe.GetComponent<MeshRenderer>().sharedMaterial =
                MakeUnlit(new Color(0.45f, 0.85f, 0.70f, 0.30f));

            GameObject vessel = GameObject.CreatePrimitive(PrimitiveType.Quad);
            vessel.name = "VesselShadow";
            vessel.transform.SetParent(site, false);
            vessel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            vessel.transform.localPosition =
                new Vector3(spec.VesselOffset.x, 0.0016f, spec.VesselOffset.y);
            vessel.transform.localScale = Vector3.one * (spec.VesselRadius * 2f);
            Object.DestroyImmediate(vessel.GetComponent<Collider>());
            vessel.GetComponent<MeshRenderer>().sharedMaterial =
                MakeUnlit(new Color(0.35f, 0.05f, 0.12f, 0.55f));
        }

        // ---------------------------------------------------------------- instrumentos

        private static GameObject BuildTray()
        {
            GameObject root = new GameObject("InstrumentStand");
            root.transform.position = TrayStandPosition;

            GameObject stand = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stand.name = "TrayStand";
            stand.transform.SetParent(root.transform, false);
            stand.transform.localScale = new Vector3(TrayStandWidthX, TableTopY, TrayStandDepthZ);
            stand.transform.localPosition = new Vector3(0f, TableTopY * 0.5f, 0f);
            stand.GetComponent<MeshRenderer>().sharedMaterial =
                MakeMaterial(new Color(0.55f, 0.58f, 0.62f), 0f, 0.3f);

            GameObject tray = Instantiate(TrayFbx, root.transform);
            tray.name = "Tray";
            tray.transform.localPosition = new Vector3(0f, TableTopY, 0f);
            tray.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            foreach (MeshRenderer r in tray.GetComponentsInChildren<MeshRenderer>())
            {
                r.sharedMaterial = MakeMaterial(new Color(0.70f, 0.72f, 0.75f), 0.85f, 0.68f);
            }

            return root;
        }

        private static GameObject BuildTrocar(GameObject tray)
        {
            GameObject trocar = new GameObject("Trocar");
            trocar.transform.position = tray.transform.position + new Vector3(-0.05f, TableTopY + 0.034f, 0f);

            GameObject mesh = Instantiate(TrocarGlb, trocar.transform);
            mesh.name = "TrocarMesh";
            // Modelled tip-at-origin pointing +Y; the tool works along -Y into the wall, and the
            // instrument lies on the tray with its tip toward the patient.
            mesh.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            mesh.transform.localPosition = new Vector3(0f, 0f, -0.02f);
            foreach (MeshRenderer r in mesh.GetComponentsInChildren<MeshRenderer>())
            {
                r.sharedMaterial = MakeMaterial(new Color(0.76f, 0.78f, 0.82f), 0.80f, 0.70f);
            }

            GameObject tip = new GameObject("TrocarTip");
            tip.transform.SetParent(trocar.transform, false);
            tip.transform.localPosition = new Vector3(0f, 0f, -0.02f);

            GameObject grip = new GameObject("GripPoint");
            grip.transform.SetParent(trocar.transform, false);
            grip.transform.localPosition = new Vector3(0f, 0f, 0.075f);

            BoxCollider box = trocar.AddComponent<BoxCollider>();
            box.size = new Vector3(0.034f, 0.034f, 0.10f);
            box.center = new Vector3(0f, 0f, 0.085f);

            Rigidbody body = trocar.AddComponent<Rigidbody>();
            body.mass = 0.08f;
            body.useGravity = false;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            ToolDefinition definition = ToolDefinition.Create(
                "trocar", "Trocáter 11 mm", ToolType.Retractor, ToolCapability.Retract);

            SurgicalInteractable interactable = trocar.AddComponent<SurgicalInteractable>();
            SetPrivateField(interactable, "toolDefinition", definition);
            SetPrivateField(interactable, "gripPoint", grip.transform);

            TrocarTool tool = trocar.AddComponent<TrocarTool>();
            tool.SetToolDefinition(definition);
            SetPrivateField(tool, "tip", tip.transform);

            AddGrab(trocar, grip.transform);
            return trocar;
        }

        private static GameObject BuildGauze(GameObject tray)
        {
            GameObject gauze = new GameObject("Gauze");
            gauze.transform.position = tray.transform.position + new Vector3(0.05f, TableTopY + 0.028f, 0f);

            GameObject mesh = Instantiate(GauzeGlb, gauze.transform);
            mesh.name = "GauzeMesh";
            foreach (MeshRenderer r in mesh.GetComponentsInChildren<MeshRenderer>())
            {
                r.sharedMaterial = MakeMaterial(new Color(0.94f, 0.94f, 0.90f), 0f, 0.12f);
            }

            GameObject pad = new GameObject("PadCentre");
            pad.transform.SetParent(gauze.transform, false);

            GameObject grip = new GameObject("GripPoint");
            grip.transform.SetParent(gauze.transform, false);

            BoxCollider box = gauze.AddComponent<BoxCollider>();
            box.size = new Vector3(0.055f, 0.020f, 0.055f);

            Rigidbody body = gauze.AddComponent<Rigidbody>();
            body.mass = 0.01f;
            body.useGravity = false;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            ToolDefinition definition = ToolDefinition.Create(
                "gauze", "Gaze", ToolType.Retractor, ToolCapability.None);

            SurgicalInteractable interactable = gauze.AddComponent<SurgicalInteractable>();
            SetPrivateField(interactable, "toolDefinition", definition);
            SetPrivateField(interactable, "gripPoint", grip.transform);

            GauzeTool tool = gauze.AddComponent<GauzeTool>();
            tool.SetToolDefinition(definition);
            SetPrivateField(tool, "padCentre", pad.transform);

            AddGrab(gauze, grip.transform);
            return gauze;
        }

        private static void AddGrab(GameObject tool, Transform attach)
        {
            XRGrabInteractable grab = tool.AddComponent<XRGrabInteractable>();
            grab.attachTransform = attach;
            grab.useDynamicAttach = false;
            grab.throwOnDetach = false;
            grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
            tool.AddComponent<ToolReleasePhysics>();
        }

        // ---------------------------------------------------------------- sessao

        private static GameObject BuildSystems()
        {
            GameObject systems = new GameObject("Systems");
            systems.AddComponent<SurgeryTelemetry>();
            return systems;
        }

        private static void WireSession(GameObject systems, PortProcedure procedure,
            GameObject trocar, GameObject gauze)
        {
            SurgeryTelemetry telemetry = systems.GetComponent<SurgeryTelemetry>();

            EventSessionDefinition definition = EventSessionDefinition.Create(
                round: 90f, briefingTimeout: 45f, resultHold: 5f, scoreboardHold: 8f,
                startOnGrab: true, pointsPerSecond: 100f);

            EventSessionController session = systems.AddComponent<EventSessionController>();
            session.Bind(definition, null, telemetry);

            Leaderboard leaderboard = systems.AddComponent<Leaderboard>();
            leaderboard.Bind(session);

            PortRoundBridge bridge = systems.AddComponent<PortRoundBridge>();
            bridge.Bind(session, procedure);

            trocar.GetComponent<TrocarTool>().Bind(procedure, trocar.transform.Find("TrocarTip"));
            gauze.GetComponent<GauzeTool>().Bind(procedure, gauze.transform.Find("PadCentre"));

            Debug.Log($"[Porta] sessão ligada: {procedure.Total} portas, rodada {definition.RoundSeconds:F0}s");
        }

        private static void BuildUrgencyVignette(GameObject systems)
        {
            Camera head = Camera.main;
            if (head == null)
            {
                Debug.LogWarning("[Porta] sem MainCamera; a vinheta de urgência não foi montada.");
                return;
            }

            // A ring on the head, just past the near plane, facing the eyes. Peripheral by
            // construction: the middle is open, so it never sits between the player and the site.
            GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ring.name = "UrgencyVignette";
            ring.transform.SetParent(head.transform, false);
            ring.transform.localPosition = new Vector3(0f, 0f, 0.12f);
            ring.transform.localScale = new Vector3(0.60f, 0.42f, 1f);
            Object.DestroyImmediate(ring.GetComponent<Collider>());

            Material mat = MakeUnlit(new Color(0.85f, 0.05f, 0.05f, 0f));
            MeshRenderer renderer = ring.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;

            UrgencySignals signals = systems.AddComponent<UrgencySignals>();
            signals.Bind(systems.GetComponent<EventSessionController>(), renderer);

            Debug.Log("[Porta] vinheta de urgência montada na cabeça (sem áudio, conforme o conceito)");
        }

        // ---------------------------------------------------------------- projecao

        private static void BuildProjection(GameObject systems, PortProcedure procedure)
        {
            GameObject camObj = new GameObject("ProjectionCamera");
            camObj.transform.position = new Vector3(0f, 2.62f, 0f);
            camObj.transform.rotation = Quaternion.Euler(90f, 90f, 0f);

            Camera cam = camObj.AddComponent<Camera>();
            cam.targetDisplay = ProjectionDisplayIndex;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.orthographic = true;
            cam.orthographicSize = 1.12f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 12f;

            UnityEngine.Rendering.Universal.UniversalAdditionalCameraData data =
                camObj.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.allowXRRendering = false;
            camObj.AddComponent<ProjectionDisplay>();

            // Vital-signs frame: clock and resolved count, big enough to read across a stand.
            GameObject canvasObj = new GameObject("ProjectionHUD");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.targetDisplay = ProjectionDisplayIndex;

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            Image tint = NewImage(canvasObj.transform, "BodyTint", Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, new Color(0.8f, 0.05f, 0.05f, 0f));

            Text clock = NewText(canvasObj.transform, "Clock", font, 200, TextAnchor.UpperCenter,
                new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(900f, 240f));

            Text resolved = NewText(canvasObj.transform, "Resolved", font, 84, TextAnchor.UpperCenter,
                new Vector2(0.5f, 1f), new Vector2(0f, -280f), new Vector2(900f, 120f));

            Text headline = NewText(canvasObj.transform, "Headline", font, 96, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(1600f, 180f));

            Text subline = NewText(canvasObj.transform, "Subline", font, 48, TextAnchor.UpperCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -90f), new Vector2(1500f, 240f));

            PortProjectionHUD hud = canvasObj.AddComponent<PortProjectionHUD>();
            hud.Bind(systems.GetComponent<EventSessionController>(), procedure,
                systems.GetComponent<Leaderboard>());
            hud.BindWidgets(clock, resolved, headline, subline, tint);

            Debug.Log("[Porta] projeção -> Display 2 (moldura de sinais vitais + camada sobre o corpo)");
        }

        private static Image NewImage(Transform parent, string name, Vector2 aMin, Vector2 aMax,
            Vector2 pos, Vector2 size, Color colour)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = aMin; rect.anchorMax = aMax;
            if (aMin == Vector2.zero && aMax == Vector2.one)
            {
                rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
            }
            else { rect.anchoredPosition = pos; rect.sizeDelta = size; }

            Image image = go.AddComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;
            return image;
        }

        private static Text NewText(Transform parent, string name, Font font, int size,
            TextAnchor anchor, Vector2 anchorPoint, Vector2 pos, Vector2 sizeDelta)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorPoint; rect.anchorMax = anchorPoint;
            rect.anchoredPosition = pos; rect.sizeDelta = sizeDelta;

            Text text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        // ---------------------------------------------------------------- helpers

        private static GameObject Instantiate(string path, Transform parent)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) { throw new System.IO.FileNotFoundException("Modelo ausente: " + path); }
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            return instance;
        }

        private static Material MakeMaterial(Color colour, float metallic, float smoothness)
        {
            Material m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = colour };
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            return m;
        }

        private static Material MakeUnlit(Color colour)
        {
            Material m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.color = colour;
            m.SetFloat("_Surface", 1f);           // transparente
            m.SetFloat("_Blend", 0f);             // alpha
            m.renderQueue = 3000;
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            return m;
        }

        private static void RenameRig()
        {
            if (GameObject.Find("XR Origin") != null) { return; }
            foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            {
                if (go.transform.parent == null && go.name.StartsWith("XR Origin"))
                {
                    go.name = "XR Origin";
                    return;
                }
            }
        }

        private static void PlaceAnchor()
        {
            Vector3 stance = PlayerStance;
            Vector3 workCentre = Vector3.Lerp(new Vector3(0f, 0f, 0.42f),
                new Vector3(TrayStandPosition.x, 0f, TrayStandPosition.z), 0.5f);
            Vector3 toWork = workCentre - stance;
            toWork.y = 0f;
            Quaternion facing = toWork.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(toWork.normalized, Vector3.up)
                : Quaternion.identity;

            GameObject anchor = GameObject.Find("Teleport Anchor");
            if (anchor != null) { anchor.transform.SetPositionAndRotation(stance, facing); }

            GameObject rig = GameObject.Find("XR Origin");
            if (rig != null) { rig.transform.SetPositionAndRotation(stance, facing); }

            Debug.Log($"[Porta] jogador em {stance} virado {facing.eulerAngles.y:F0}deg");
        }

        private static void EnsureMainCamera()
        {
            foreach (Camera c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (c.CompareTag("MainCamera")) { return; }
            }

            foreach (Camera c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (c.name.Contains("Main")) { c.tag = "MainCamera"; return; }
            }
        }

        private static void RegisterSceneInBuildSettings()
        {
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
            {
                if (s.path == TargetScene) { return; }
            }

            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            list.Add(new EditorBuildSettingsScene(TargetScene, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        private static void SetPrivateField(object target, string field, object value)
        {
            System.Reflection.FieldInfo info = target.GetType().GetField(field,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (info == null)
            {
                Debug.LogError($"[Porta] {target.GetType().Name} não tem o campo '{field}'.");
                return;
            }

            info.SetValue(target, value);
        }
    }
}
