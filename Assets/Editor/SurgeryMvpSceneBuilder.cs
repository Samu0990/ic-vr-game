using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using VRSurgery.Audio;
using VRSurgery.Data;
using VRSurgery.Diagnostics;
using VRSurgery.Interaction;
using VRSurgery.Session;
using VRSurgery.Surgery;
using VRSurgery.Tissue;
using VRSurgery.Tools;
using VRSurgery.VR;

namespace VRSurgery.EditorTools
{
    /// <summary>
    /// Builds the MVP surgery scene on top of the VR template's SampleScene, saving to a separate
    /// scene file so the template scene stays pristine.
    ///
    /// Every vertical number is anchored to two measurements rather than carried over: the
    /// template floor's top surface at Y = 0.0005 (SceneBoundsDump), and the patient's own belly
    /// surface, sampled from the mesh at build time. The old project's positions were calibrated
    /// against a fixed-player layout and a hand-built room, neither of which exists here.
    /// </summary>
    public static class SurgeryMvpSceneBuilder
    {
        private const string SourceScene = "Assets/Scenes/SampleScene.unity";
        private const string TargetScene = "Assets/Scenes/SurgeryMVP.unity";

        private const string BodyFbx = "Assets/Models/Patient/PATIENT_ExternalBody.fbx";
        private const string VertebraeFbx = "Assets/Models/Patient/PATIENT_Vertebrae.fbx";
        private const string TrayFbx = "Assets/Models/Environment/PROP_InstrumentTray.fbx";
        private const string TableFbx = "Assets/Models/Environment/PROP_OperatingTable.glb";
        private const string ScalpelFbx = "Assets/Models/Tools/SCALPEL_FromGLB.fbx";
        private const string ScalpelAlbedo = "Assets/Models/Tools/BISTURI_Image_0.png";
        private const string ScalpelNormal = "Assets/Models/Tools/BISTURI_Image_2.png";
        private const string ScalpelMetallic = "Assets/Models/Tools/BISTURI_MetallicSmoothness.png";
        private const string PosterTexture = "Assets/Textures/Que_atrevido.jpeg";
        private const string IncisionClip = "Assets/Audio/SFX_Incision.wav";
        private const string ScalpelDefinition = "Assets/Data/Tool_Scalpel.asset";
        private const string ForcepsDefinition = "Assets/Data/Tool_Forceps.asset";
        private const string SurgeryAsset = "Assets/Data/Surgery_VerticalSlice.asset";
        private const string SessionAsset = "Assets/Data/Session_EventBooth.asset";

        private const string ScalpelTag = "Scalpel";

        /// <summary>Table top height. Fixed for the MVP — not swept, not derived.</summary>
        private const float TableTopY = 0.95f;

        // Measured off PROP_OperatingTable.glb, not chosen. The model that replaced the first
        // table is a real hospital table — wheeled base, hydraulic column, segmented padded top —
        // and it is wider and longer than the block it replaced. VerifyTableTop compares against
        // these, so they describe the model rather than an intention.
        private const float TableWidthX = 0.575f;
        private const float TableLengthZ = 2.002f;

        /// <summary>Migrated scalpel metrics. They describe the model, so they survive the move.</summary>
        private const float ScalpelGripLocalZ = -0.02f;
        private const float ScalpelGripToTip = 0.05518f;
        private const float ScalpelGripToButt = 0.06562f;

        /// <summary>Patient lies along Z with the head at +Z; the feet end is the player's side.</summary>
        private static float FeetZ => -TableLengthZ * 0.5f;

        /// <summary>
        /// Z of the operative field. Taken from the vertebrae mesh bounds (Z 0.331..0.481 in
        /// model space) because the vertebra is the surgical target, not the abdomen.
        /// </summary>
        private const float FieldZ = 0.406f;

        /// <summary>Mid-point of the body along Z, so the projected view is centred on the patient.</summary>
        private const float PatientCentreZ = 0f;

        /// <summary>
        /// Where the player stands. Measured with StanceSolver against the same reach model the
        /// ergonomics tests use, not chosen by eye.
        ///
        /// The feet end cannot work: at Z -0.40 — already inside the table footprint — the field
        /// is still 0.871 m from the shoulder, i.e. OutOfReach. Standing beside the table is the
        /// only position that reaches a field at mid-torso, which is also where a surgeon stands.
        /// X 0.45 measures 0.532 m (Precision) at 20.2 deg below eye level; the table edge is at
        /// X 0.25, so the player stands 0.20 m clear of it.
        /// </summary>
        private static Vector3 PlayerStance => new Vector3(0.45f, 0f, FieldZ);

        /// <summary>
        /// Instrument stand. Placement is boxed in from three sides: it has to stay off the
        /// table (X +/-0.25), stay inside Precision reach of a shoulder, and stay in front of
        /// the player — the ergonomics rule is yaw &lt; 90 deg, and a first attempt at X 0.80 put
        /// it 128 deg off-axis, i.e. behind the shoulder line. A narrow pedestal hard against
        /// the table edge is what fits. Top matches the table so both surfaces are level.
        /// </summary>
        // --- Decorative wall poster ------------------------------------------------------
        // Pure background dressing, kept out of the working volume. Wall_Front's inner face sits
        // at X 2.67 (SceneBoundsDump); the whole surgical setup lives below X 0.54, so a poster
        // here cannot compete with anything the procedure needs. Walls span Y 0.30 to 2.44.
        //
        // Edit these three to move it — the scene is generated from code, so dragging the object
        // in the Hierarchy is undone by the next rebuild.
        private static readonly Vector3 PosterPosition = new Vector3(2.665f, 1.60f, 0.40f);

        /// <summary>A Unity quad shows its face toward -Z, so yaw 90 turns it to face -X, into the room.</summary>
        private static readonly Vector3 PosterRotationEuler = new Vector3(0f, 90f, 0f);

        private const float PosterWidth = 0.45f;

        private static readonly Vector3 TrayStandPosition = new Vector3(0.44f, 0f, 0.80f);

        private const float TrayStandWidthX = 0.20f;
        private const float TrayStandDepthZ = 0.50f;

        /// <summary>
        /// Forceps, laid on the same tray as the scalpel and 6 cm nearer the table edge.
        ///
        /// It belongs on the player's other side — the layout calls for a tray per hand — but the
        /// validated stance cannot produce that. Facing the work centre, everything reachable on
        /// the player's left is over the patient; the only facing that straddles two trays is
        /// square to the field, and it pushes BOTH instruments to 88 deg off-axis, right against
        /// the limit that forbids reaching behind the shoulder line. Beside the scalpel measures
        /// 39 deg off-axis at Precision reach — better than the scalpel's own 48 — so the
        /// instrument is comfortable here even though the two-tray rule stays unmet.
        /// </summary>
        private static readonly Vector3 ForcepsRestPosition = new Vector3(0.38f, 0f, 0.80f);

        /// <summary>
        /// Objective monitor, 1.15 m straight down the player's line of sight and slightly above
        /// eye level, past the patient's head where it clears the table and the tray. Placed from
        /// the stance rather than by eye: the ergonomics rule is a glance, not a turn.
        /// </summary>
        private static readonly Vector3 MonitorPosition = new Vector3(-0.423f, 1.501f, 1.154f);

        private const float MonitorWidth = 0.52f;
        private const float MonitorHeight = 0.32f;

        /// <summary>0-based; index 0 is "Display 1", index 1 is "Display 2".</summary>
        /// <summary>Layer for things only the operator should see: rig visuals, teleport markers.</summary>
        private const string OperatorLayer = "OperatorOnly";

        private const string SimulatorPrefab =
            "Assets/Samples/XR Interaction Toolkit/3.5.1/XR Interaction Simulator/XR Interaction Simulator.prefab";

        /// <summary>
        /// Lets the scene be played with keyboard and mouse. Without it, pressing Play on a
        /// machine with no headset gives a frozen camera and no hands — the project looks broken
        /// when it is only waiting for hardware that is not there.
        /// </summary>
        /// <summary>
        /// Keyboard and mouse simulator: only in the editor. On Android/Quest, input comes from
        /// the headset and hand tracking; a keyboard simulator would break things.
        /// </summary>
#if UNITY_ANDROID
        private const bool IncludeXrSimulator = false;
#else
        private const bool IncludeXrSimulator = true;
#endif

        private const int SpectatorDisplayIndex = 0;
        private const int ProjectionDisplayIndex = 1;

        [MenuItem("VRSurgery/Rebuild MVP Scene")]
        public static void BuildFromMenu()
        {
            Build();
            EditorUtility.DisplayDialog("VR Surgery",
                "SurgeryMVP scene rebuilt. See the Console for the measured numbers.", "OK");
        }

        public static void BuildFromCommandLine()
        {
            try
            {
                Build();
                Debug.Log("[SurgeryMVP] BUILD_OK");
                EditorApplication.Exit(0);
            }
            catch (System.Exception exception)
            {
                Debug.LogError("[SurgeryMVP] BUILD_FAILED " + exception);
                EditorApplication.Exit(1);
            }
        }

        public static void Build()
        {
            EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single);
            EnsureTag(ScalpelTag);

            // Discrete anchors were chosen over free roaming, so the template's free-roam area goes.
            GameObject area = GameObject.Find("Teleport Area");
            if (area != null)
            {
                Object.DestroyImmediate(area);
                Debug.Log("[SurgeryMVP] Removed 'Teleport Area' (discrete anchors only).");
            }

            RenameRig();
            GameObject table = BuildTable();
            GameObject patient = BuildPatient();

            // The guide's hierarchy puts the organ inside OperatingTable. Parenting it to the
            // table CUBE would inherit that cube's non-uniform scale (0.70, 0.95, 2.00) and
            // squash the patient, so the table root is an unscaled empty and the scaled top is
            // a sibling of the patient underneath it.
            patient.transform.SetParent(table.transform, true);

            float surfaceY = MeasureBodySurfaceY(patient, 0f, FieldZ, 0.05f);
            Debug.Log($"[SurgeryMVP] belly surface sampled at (0, {FieldZ:F3}) = Y {surfaceY:F4} " +
                      $"({(surfaceY - TableTopY) * 100f:F1} cm above the table)");

            GameObject systems = BuildSystems();
            GameObject region = BuildTissue(patient, surfaceY, systems.GetComponent<SurgeryTelemetry>());
            GameObject tray = BuildInstrumentTray();
            GameObject scalpel = BuildScalpel(tray);
            GameObject forceps = BuildForceps(tray);
            GameObject monitor = BuildObjectiveMonitor();
            WireEventSession(systems, monitor, region, scalpel, forceps);
            GameObject projectionHud = BuildProjectionHUD(systems, region);
            WireUrgencyTint(systems);
            BuildSimulator();
            HideOperatorVisualsFromProjection();
            BuildSpectatorCamera();
            BuildProjectionCamera();
            PlaceAnchor();
            BuildWallPoster();
            BuildProbe(systems, scalpel);

            Report("Table", table);
            Report("Patient", patient);
            Report("Scalpel", scalpel);
            Report("Forceps", forceps);
            Report("Monitor", monitor);
            Report("ProjectionHUD", projectionHud);

            EnsureMainCamera();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), TargetScene, true);
            RegisterSceneInBuildSettings();
            Debug.Log("[SurgeryMVP] saved " + TargetScene);
        }

        // ------------------------------------------------------------------
        // Environment
        // ------------------------------------------------------------------

        private static GameObject BuildTable()
        {
            GameObject root = new GameObject("OperatingTable");
            root.transform.position = Vector3.zero;

            // Modelled with its base at Y = 0 and its top face at exactly TableTopY, so it
            // seats at the origin with no offset — the same trick the patient meshes use.
            GameObject top = Instantiate(TableFbx, root.transform);
            top.name = "TableTop";

            // The model arrives textured, so its surfaces are carried over rather than painted
            // flat grey the way the untextured block it replaced had to be — but they are moved
            // onto URP/Lit first. See ConvertGltfMaterials.
            ConvertGltfMaterials(top);

            // A box collider stands in for the slab: the tool must not fall through the table,
            // and a mesh collider on furniture is wasted cost.
            BoxCollider slab = top.AddComponent<BoxCollider>();
            slab.center = new Vector3(0f, TableTopY - 0.03f, 0f);
            slab.size = new Vector3(TableWidthX, 0.06f, TableLengthZ);

            VerifyTableTop(top);
            return root;
        }

        /// <summary>
        /// Moves glTF-imported materials onto URP/Lit, carrying their textures across.
        ///
        /// gltFast brings models in on its own Shader Graph. That works, but it costs a Quest more
        /// than Lit does and it drags extra shader variants into the build, and a Shader Graph
        /// variant that is not compiled renders black — which is exactly how this table first
        /// appeared in a headless capture.
        ///
        /// The metallic-roughness map is deliberately NOT carried over. glTF packs roughness in
        /// green and metallic in blue; URP/Lit reads metallic from red and smoothness from alpha.
        /// Assigning it straight across would look like it worked and be wrong in every channel,
        /// so the two values are set as scalars instead.
        /// </summary>
        private static void ConvertGltfMaterials(GameObject model)
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) { return; }

            int converted = 0;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material source = materials[i];
                    if (source == null || !source.shader.name.Contains("glTF")) { continue; }

                    Material target = new Material(lit) { name = source.name + "_URP" };

                    if (source.HasProperty("baseColorTexture"))
                    {
                        Texture albedo = source.GetTexture("baseColorTexture");
                        if (albedo != null) { target.SetTexture("_BaseMap", albedo); }
                    }

                    if (source.HasProperty("normalTexture"))
                    {
                        Texture normal = source.GetTexture("normalTexture");
                        if (normal != null)
                        {
                            target.SetTexture("_BumpMap", normal);
                            target.EnableKeyword("_NORMALMAP");
                        }
                    }

                    if (source.HasProperty("baseColorFactor"))
                    {
                        target.SetColor("_BaseColor", source.GetColor("baseColorFactor"));
                    }

                    target.SetFloat("_Metallic", 0.15f);
                    target.SetFloat("_Smoothness", 0.45f);

                    materials[i] = target;
                    converted++;
                }

                renderer.sharedMaterials = materials;
            }

            if (converted > 0)
            {
                Debug.Log($"[SurgeryMVP] {converted} material(is) glTF convertido(s) para URP/Lit.");
            }
        }

        /// <summary>
        /// The whole layout hangs off the table top being at exactly TableTopY. If a re-export
        /// ever shifts it, everything seated on it moves silently — so it is checked, not assumed.
        /// </summary>
        private static void VerifyTableTop(GameObject table)
        {
            bool any = false;
            Bounds b = default;
            foreach (Renderer r in table.GetComponentsInChildren<Renderer>())
            {
                if (!any) { b = r.bounds; any = true; } else { b.Encapsulate(r.bounds); }
            }

            if (!any) { Debug.LogError("[SurgeryMVP] Table model has no renderers."); return; }

            // Checking only the top height is not enough: a model that came in rotated can
            // still land a face at the right Y by coincidence (half of a 1.90 m length is
            // 0.95 m). All three dimensions have to match, or the table is lying on its side.
            float topError = Mathf.Abs(b.max.y - TableTopY);
            float baseError = Mathf.Abs(b.min.y);
            float widthError = Mathf.Abs(b.size.x - TableWidthX);
            float heightError = Mathf.Abs(b.size.y - TableTopY);
            float lengthError = Mathf.Abs(b.size.z - TableLengthZ);

            string message = $"[SurgeryMVP] table measured: size=({b.size.x:F3},{b.size.y:F3},{b.size.z:F3}) " +
                             $"expected=({TableWidthX:F3},{TableTopY:F3},{TableLengthZ:F3}) | " +
                             $"top Y={b.max.y:F4} base Y={b.min.y:F4}";

            float worst = Mathf.Max(topError, baseError,
                Mathf.Max(widthError, Mathf.Max(heightError, lengthError)));

            if (worst > 0.002f)
            {
                Debug.LogError(message + $" — OFF BY {worst * 1000f:F1} mm; the model is not in the expected orientation.");
            }
            else
            {
                Debug.Log(message + $" | worst error {worst * 1000f:F2} mm");
            }
        }

        /// <summary>
        /// Smallest orthographic size that still contains everything the audience needs to see:
        /// the patient, the table and the instrument on its tray. Derived from the scene's own
        /// bounds rather than typed in, so moving the tray or the table cannot silently crop the
        /// projected view.
        /// </summary>
        /// <summary>
        /// Moves the rig's own visuals onto a dedicated layer so the projection can cull them.
        ///
        /// Only GameObjects that carry a Renderer and no Collider are moved. XRI resolves
        /// interaction through physics layer masks, so relocating a collider — the teleport
        /// anchor's in particular — would quietly break teleporting to reach a cosmetic goal.
        /// </summary>
        private static void BuildSimulator()
        {
            if (!IncludeXrSimulator) { return; }

            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(SimulatorPrefab);
            if (asset == null)
            {
                Debug.LogWarning("[SurgeryMVP] XR Interaction Simulator sample not found; " +
                                 "the scene will need a headset to be playable.");
                return;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            instance.name = "XR Interaction Simulator";
            Debug.Log("[SurgeryMVP] XR Interaction Simulator added — playable without a headset");
        }

        private static void HideOperatorVisualsFromProjection()
        {
            int layer = EnsureLayer(OperatorLayer);
            if (layer < 0) { return; }

            int moved = 0;
            int skipped = 0;
            // The simulator draws its own on-screen UI; that belongs to whoever is driving the
            // editor, never to the audience.
            foreach (string rootName in new[] { "XR Origin", "Teleport Area Setup", "XR Interaction Simulator" })
            {
                GameObject root = GameObject.Find(rootName);
                if (root == null) { continue; }

                // Renderers alone are not enough: world-space UI draws through CanvasRenderer,
                // and the teleport anchor's disc plus every controller tooltip were still
                // reaching the projector after the meshes were culled.
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    bool draws = t.GetComponent<Renderer>() != null
                              || t.GetComponent<Canvas>() != null
                              || t.GetComponent<CanvasRenderer>() != null;
                    if (!draws) { continue; }

                    if (t.GetComponent<Collider>() != null) { skipped++; continue; }

                    t.gameObject.layer = layer;
                    moved++;
                }
            }

            Debug.Log($"[SurgeryMVP] {moved} operator visuals moved to '{OperatorLayer}' " +
                      $"({skipped} left alone because they carry a collider)");
        }

        /// <summary>Finds a layer by name, claiming the first free user slot if it does not exist.</summary>
        private static int EnsureLayer(string layerName)
        {
            int existing = LayerMask.NameToLayer(layerName);
            if (existing >= 0) { return existing; }

            SerializedObject tagManager = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");

            // 0-7 are Unity's built-ins; user layers start at 8.
            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty slot = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(slot.stringValue)) { continue; }

                slot.stringValue = layerName;
                tagManager.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                Debug.Log($"[SurgeryMVP] created layer '{layerName}' at index {i}");
                return i;
            }

            Debug.LogError($"[SurgeryMVP] No free user layer for '{layerName}'.");
            return -1;
        }

        private static float ProjectionSizeFor(Camera cam)
        {
            Bounds subject = default;
            bool any = false;

            foreach (string name in new[] { "Patient", "OperatingTable", "InstrumentStand", "Scalpel" })
            {
                GameObject go = GameObject.Find(name);
                if (go == null) { continue; }

                foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
                {
                    if (!any) { subject = r.bounds; any = true; } else { subject.Encapsulate(r.bounds); }
                }
            }

            if (!any)
            {
                Debug.LogWarning("[SurgeryMVP] Nothing to frame; falling back to a 1 m projection size.");
                return 1f;
            }

            // Measure the subject in the camera's own space: the camera is rolled, so world X and
            // Z do not map onto the image axes.
            float halfWidth = 0f;
            float halfHeight = 0f;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(
                    (i & 1) == 0 ? subject.min.x : subject.max.x,
                    (i & 2) == 0 ? subject.min.y : subject.max.y,
                    (i & 4) == 0 ? subject.min.z : subject.max.z);

                Vector3 local = cam.transform.InverseTransformPoint(corner);
                halfWidth = Mathf.Max(halfWidth, Mathf.Abs(local.x));
                halfHeight = Mathf.Max(halfHeight, Mathf.Abs(local.y));
            }

            // orthographicSize is the half-HEIGHT, and the half-width it buys is that times the
            // aspect ratio. Camera.aspect is NOT serialized — it is a runtime override that
            // resets to whatever the output surface is — so sizing against an assumed 16:9 here
            // silently crops on anything narrower. Sizing for the square worst case costs some
            // empty margin on a wide projector and can never crop.
            const float margin = 1.06f;
            float size = Mathf.Max(halfHeight, halfWidth) * margin;

            Debug.Log($"[SurgeryMVP] framing subject {subject.size} -> half-width {halfWidth:F3}, " +
                      $"half-height {halfHeight:F3}, orthographic size {size:F3}");
            return size;
        }

        /// <summary>
        /// Flat camera that mirrors the headset, for the spectator screen. It carries no XR
        /// rendering of its own, so it never competes with the stereo path.
        /// </summary>
        private static void BuildSpectatorCamera()
        {
            GameObject go = new GameObject("SpectatorCamera");

            Camera cam = go.AddComponent<Camera>();
            cam.targetDisplay = SpectatorDisplayIndex;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 70f;
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 60f;

            UnityEngine.Rendering.Universal.UniversalAdditionalCameraData data =
                go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.allowXRRendering = false;

            HeadsetFollowCamera follow = go.AddComponent<HeadsetFollowCamera>();
            Camera head = Camera.main;
            if (head != null)
            {
                follow.Headset = head.transform;
                go.transform.SetPositionAndRotation(head.transform.position, head.transform.rotation);
            }
            else
            {
                Debug.LogWarning("[SurgeryMVP] No MainCamera to mirror; the spectator view will be static.");
            }

            Debug.Log($"[SurgeryMVP] spectator camera -> Display {SpectatorDisplayIndex + 1}, " +
                      $"following {(head != null ? head.name : "nothing")}");
        }

        /// <summary>
        /// Overhead camera feeding the projector on a second display, so spectators see the
        /// operative field without a headset.
        /// </summary>
        private static void BuildProjectionCamera()
        {
            GameObject go = new GameObject("ProjectionCamera");
            // Framing is set by the near surface, not the table: the chest sits at Y 1.288, so
            // the camera only has (height - 1.288) of depth to fit 1.77 m of patient. At 2.40 m
            // with 62 deg that was 1.33 m of coverage and every corner fell outside. 2.62 m with
            // 70 deg covers 1.87 m, leaving ~5 cm of margin at each end, and still clears the
            // 2.83 m ceiling.
            go.transform.position = new Vector3(0f, 2.62f, PatientCentreZ);
            // Rolled 90 deg so the patient's long axis runs across the image width. On a 16:9
            // projector that is the generous axis; unrolled, the body fights the short one.
            go.transform.rotation = Quaternion.Euler(90f, 90f, 0f);

            Camera cam = go.AddComponent<Camera>();
            cam.targetDisplay = ProjectionDisplayIndex;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;

            // The audience must not see the operator's own furniture. Everything the rig draws
            // for the person wearing the headset lives on its own layer, culled here only.
            int operatorLayer = LayerMask.NameToLayer(OperatorLayer);
            if (operatorLayer >= 0) { cam.cullingMask &= ~(1 << operatorLayer); }

            // Orthographic, per the projection spec: an overhead perspective view makes the
            // instrument look like it is leaning as it moves away from the centre, which reads
            // as distortion to an audience watching a flat screen.
            cam.orthographic = true;

            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 12f;
            cam.orthographicSize = ProjectionSizeFor(cam);

            // A second camera left on the XR rig's stereo path would fight the headset; this one
            // renders to its own display only.
            UnityEngine.Rendering.Universal.UniversalAdditionalCameraData data =
                go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.allowXRRendering = false;

            go.AddComponent<ProjectionDisplay>();

            Debug.Log($"[SurgeryMVP] projection camera at {go.transform.position} " +
                      $"-> Display {ProjectionDisplayIndex + 1}, orthographic size {cam.orthographicSize:F3}, " +
                      "looking straight down");
        }

        private static GameObject BuildPatient()
        {
            // Both FBXs come out of the same .blend with a shared origin and min Y = 0, so one
            // root at table height seats the body on the top surface and drops the vertebrae
            // inside it without any per-part alignment.
            GameObject root = new GameObject("Patient");
            root.transform.position = new Vector3(0f, TableTopY, 0f);
            Instantiate(BodyFbx, root.transform);
            Instantiate(VertebraeFbx, root.transform);
            return root;
        }

        /// <summary>
        /// Highest body vertex within <paramref name="radius"/> of (x, z), in world space.
        /// The operative field has to sit on the skin: guessing an offset from the table put
        /// the old project's region 5.7 cm inside the body, i.e. every incision under the skin.
        /// </summary>
        private static float MeasureBodySurfaceY(GameObject patient, float x, float z, float radius)
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

            if (best == float.MinValue)
            {
                Debug.LogWarning("[SurgeryMVP] No readable body vertices near the field; falling back to the table.");
                return TableTopY;
            }

            return best;
        }

        // ------------------------------------------------------------------
        // Tissue
        // ------------------------------------------------------------------

        private static GameObject BuildTissue(GameObject patient, float surfaceY, SurgeryTelemetry telemetry)
        {
            // Tissue local +Y is the outward normal, which is what IncisionGeometry assumes.
            GameObject region = new GameObject("SurgicalRegion");
            region.transform.SetParent(patient.transform);
            region.transform.position = new Vector3(0f, surfaceY, FieldZ);
            region.transform.rotation = Quaternion.identity;

            TissueSurface tissue = region.AddComponent<TissueSurface>();
            tissue.Configure(new Vector2(0.07f, 0.05f), 0.02f);

            IncisionSystem incisionSystem = region.AddComponent<IncisionSystem>();
            region.AddComponent<BleedingSystem>();

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Quad);
            visual.name = "TissueVisual";
            visual.transform.SetParent(region.transform, false);
            visual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // Smaller than the first pass and lifted clear of the skin: a flat 18x12 cm sheet
            // intersected the curved chest and the shoulders poked through it.
            visual.transform.localPosition = new Vector3(0f, 0.002f, 0f);
            visual.transform.localScale = new Vector3(0.14f, 0.10f, 1f);
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            Material tissueMat = MakeMaterial(new Color(0.79f, 0.55f, 0.50f), 0f, 0.25f);
            visual.GetComponent<MeshRenderer>().sharedMaterial = tissueMat;

            BoxCollider collider = region.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, -0.01f, 0f);
            collider.size = new Vector3(0.14f, 0.02f, 0.10f);
            collider.isTrigger = true;

            GameObject wound = new GameObject("Wound", typeof(MeshFilter), typeof(MeshRenderer));
            wound.transform.SetParent(region.transform, false);
            wound.GetComponent<MeshRenderer>().sharedMaterial =
                MakeMaterial(new Color(0.45f, 0.05f, 0.06f), 0f, 0.55f);
            WoundRenderer woundRenderer = wound.AddComponent<WoundRenderer>();
            SetPrivateField(woundRenderer, "tissue", tissue);

            GameObject guideObject = new GameObject("IncisionGuide");
            guideObject.transform.SetParent(region.transform, false);
            IncisionGuide guide = guideObject.AddComponent<IncisionGuide>();
            guide.Configure(new Vector3(-0.045f, 0f, 0f), new Vector3(0.045f, 0f, 0f));

            // Surgical bands, set here rather than left to the component's defaults so the numbers
            // that decide whether a cut is good live next to the path they are measured against.
            // 1.5 mm is a clean line on a 9 cm incision; 10 mm misses.
            guide.SetTolerances(0.0015f, 0.004f, 0.010f);

            GameObject guideVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            guideVisual.name = "GuideMarker";
            guideVisual.transform.SetParent(guideObject.transform, false);
            guideVisual.transform.localPosition = new Vector3(0f, 0.0006f, 0f);
            guideVisual.transform.localScale = new Vector3(0.09f, 0.0004f, 0.002f);
            Object.DestroyImmediate(guideVisual.GetComponent<Collider>());
            guideVisual.GetComponent<MeshRenderer>().sharedMaterial =
                MakeMaterial(new Color(0.15f, 0.55f, 0.95f), 0f, 0.2f);

            SetPrivateField(incisionSystem, "guide", guide);

            AudioSource regionAudio = region.AddComponent<AudioSource>();
            regionAudio.playOnAwake = false;
            regionAudio.spatialBlend = 1f;

            // Binary-state incision on top of the swept system: this is what answers "has this
            // been cut", swaps the material, plays the sound and writes the log file.
            IncisableSkin incisable = region.AddComponent<IncisableSkin>();
            incisable.Configure("VertebralField", visual.GetComponent<MeshRenderer>(),
                MakeMaterial(new Color(0.62f, 0.16f, 0.16f), 0f, 0.4f), regionAudio);

            // Configure() does not cover the clip or the feedback object, and both are guarded by
            // null checks inside IncisableSkin — leaving them unset makes the component look
            // wired while the cut stays silent and shows no highlight.
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(IncisionClip);
            if (clip == null) { Debug.LogError("[SurgeryMVP] Incision clip missing: " + IncisionClip); }
            SetPrivateField(incisable, "incisionClip", clip);

            SetPrivateField(incisable, "incisionFeedback", BuildIncisionFeedback(region.transform));

            Debug.Log($"[SurgeryMVP] surgical region at {region.transform.position} " +
                      $"| log file -> {incisable.LogFilePath}");
            return region;
        }

        // ------------------------------------------------------------------
        // Tools
        // ------------------------------------------------------------------

        /// <summary>
        /// Highlight the guide asks for: a glowing outline around the field that switches on at
        /// the moment of the cut. Built here rather than left to the Inspector so a rebuild
        /// cannot lose it.
        /// </summary>
        private static GameObject BuildIncisionFeedback(Transform parent)
        {
            GameObject feedback = GameObject.CreatePrimitive(PrimitiveType.Quad);
            feedback.name = "IncisionFeedback";
            feedback.transform.SetParent(parent, false);
            feedback.transform.localPosition = new Vector3(0f, 0.0015f, 0f);
            feedback.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            feedback.transform.localScale = new Vector3(0.17f, 0.13f, 1f);
            Object.DestroyImmediate(feedback.GetComponent<Collider>());

            Material glow = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            glow.color = new Color(1f, 0.85f, 0.25f, 1f);
            feedback.GetComponent<MeshRenderer>().sharedMaterial = glow;

            // IncisableSkin turns it on at the cut and off on reset.
            feedback.SetActive(false);
            return feedback;
        }

        /// <summary>
        /// Cube stand with the converted tray resting on top. The stand is a primitive on
        /// purpose: it is a work surface at a known height, not something to look at.
        /// </summary>
        private static GameObject BuildInstrumentTray()
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

            // Tray is modelled with its base at Y = 0, so it seats on the stand top with no offset.
            GameObject tray = Instantiate(TrayFbx, root.transform);
            tray.name = "Tray";
            tray.transform.localPosition = new Vector3(0f, TableTopY, 0f);
            // Turned so the tray's long axis runs along Z. Left as modelled it would overhang the
            // operating table, and both surfaces sit at the same height.
            tray.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            foreach (MeshRenderer r in tray.GetComponentsInChildren<MeshRenderer>())
            {
                r.sharedMaterial = MakeMaterial(new Color(0.70f, 0.72f, 0.75f), 0.85f, 0.68f);
            }

            Debug.Log($"[SurgeryMVP] instrument stand at {TrayStandPosition}, tray surface at Y {TableTopY:F3}");
            return root;
        }

        private static GameObject BuildScalpel(GameObject tray)
        {
            GameObject scalpel = new GameObject("Scalpel");
            // On the tray, not on the operating table. The patient is 0.685 m wide on a 0.50 m
            // table, so the body covers the whole slab between the shoulders and the knees —
            // there is no free surface beside the torso to put an instrument on.
            scalpel.transform.position = tray.transform.position
                                       + new Vector3(0f, TableTopY + 0.0236f + 0.01f, 0f);

            // AlignToolForward reads vertices, which needs Read/Write on the model importer.
            EnsureReadable(ScalpelFbx);
            GameObject mesh = Instantiate(ScalpelFbx, scalpel.transform);
            mesh.name = "ScalpelMesh";
            mesh.transform.localPosition = new Vector3(0f, 0f, ScalpelGripLocalZ);

            float bladeTipLocalZ = AlignToolForward(mesh, "filo");

            Material scalpelMat = MakeScalpelMaterial();
            foreach (MeshRenderer r in mesh.GetComponentsInChildren<MeshRenderer>())
            {
                r.sharedMaterial = scalpelMat;
            }

            foreach (Transform tr in mesh.GetComponentsInChildren<Transform>())
            {
                Vector3 sc = tr.localScale;
                if (Mathf.Abs(sc.x - sc.y) > 0.001f || Mathf.Abs(sc.y - sc.z) > 0.001f)
                {
                    Debug.LogError($"[SurgeryMVP] '{tr.name}' has non-uniform scale {sc}; rotating the tool will skew it.");
                }
            }

            // Interaction volume is deliberately larger than the 12 mm handle: a grab box sized
            // to the real silhouette makes a small instrument miserable to pick up.
            BoxCollider grabBox = scalpel.AddComponent<BoxCollider>();
            grabBox.size = new Vector3(0.022f, 0.022f, ScalpelGripToTip + ScalpelGripToButt + 0.005f);
            grabBox.center = new Vector3(0f, 0f, ScalpelGripLocalZ + (ScalpelGripToTip - ScalpelGripToButt) * 0.5f);

            Rigidbody body = scalpel.AddComponent<Rigidbody>();
            body.mass = 0.05f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            // ContinuousDynamic (inherited from the old project) was free while the tool was
            // permanently kinematic. Now that release makes it dynamic and falling, it runs
            // continuous sweeps against the whole scene every physics step, which this laptop
            // feels. Speculative gets the anti-tunnelling that a 50 g tool dropping onto a 6 cm
            // slab needs, at a fraction of the cost.
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.useGravity = false;
            body.isKinematic = true;

            // Placed from the measured blade geometry. Cut detection tracks this transform, so a
            // stale value would have the blade cutting from a point that is not on the blade.
            GameObject tipObject = new GameObject("BladeTip");
            tipObject.transform.SetParent(scalpel.transform, false);
            tipObject.transform.localPosition = new Vector3(0f, 0f, bladeTipLocalZ);
            BladeTip tip = tipObject.AddComponent<BladeTip>();

            tipObject.tag = ScalpelTag;
            SphereCollider tipTrigger = tipObject.AddComponent<SphereCollider>();
            tipTrigger.radius = 0.004f;
            tipTrigger.isTrigger = true;

            GameObject gripPoint = new GameObject("GripPoint");
            gripPoint.transform.SetParent(scalpel.transform, false);
            gripPoint.transform.localPosition = new Vector3(0f, 0f, ScalpelGripLocalZ);

            ToolDefinition definition = AssetDatabase.LoadAssetAtPath<ToolDefinition>(ScalpelDefinition);
            if (definition == null)
            {
                Debug.LogWarning("[SurgeryMVP] Tool_Scalpel.asset not found; the tool will run undefined.");
            }

            SurgicalInteractable interactable = scalpel.AddComponent<SurgicalInteractable>();
            SetPrivateField(interactable, "toolDefinition", definition);
            SetPrivateField(interactable, "gripPoint", gripPoint.transform);

            // ScalpelTool must exist before CuttingInteractor: the interactor requires a
            // SurgicalTool, and since SurgicalTool is abstract Unity cannot auto-add one to
            // satisfy the requirement — AddComponent would simply return null.
            ScalpelTool tool = scalpel.AddComponent<ScalpelTool>();
            tool.SetToolDefinition(definition);

            CuttingInteractor cutter = scalpel.AddComponent<CuttingInteractor>();
            SetPrivateField(cutter, "bladeTip", tip);
            SetPrivateField(tool, "bladeTip", tip);
            SetPrivateField(tool, "cuttingInteractor", cutter);

            XRGrabInteractable grab = scalpel.AddComponent<XRGrabInteractable>();
            grab.attachTransform = gripPoint.transform;
            grab.useDynamicAttach = false;
            grab.throwOnDetach = false;
            // The guide specifies Velocity Tracking. XRI clears isKinematic while the tool is
            // held so the body can be driven by velocity, and restores it on release.
            grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;

            // Falls when dropped instead of hanging in the air. Throw On Detach stays false, so
            // it drops straight down rather than being flung.
            scalpel.AddComponent<ToolReleasePhysics>();

            ToolHoverHighlight highlight = scalpel.AddComponent<ToolHoverHighlight>();
            Material highlightMat = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                color = new Color(0.30f, 0.62f, 1f)
            };
            // No emission: enabling _EMISSION on a fresh material forces a new shader variant to
            // compile the first time it renders, and on this machine's integrated GPU that shows
            // up as a stutter exactly when the player first hovers a tool. A bright unlit-ish
            // base colour reads as a highlight without costing a variant.
            highlightMat.SetFloat("_Smoothness", 0.9f);
            SetPrivateField(highlight, "highlightMaterial", highlightMat);
            SetPrivateField(highlight, "renderers", mesh.GetComponentsInChildren<MeshRenderer>());

            Debug.Log($"[SurgeryMVP] scalpel grip->tip measured={bladeTipLocalZ - ScalpelGripLocalZ:F5} m " +
                      $"(migrated constant {ScalpelGripToTip:F5}, butt {ScalpelGripToButt:F5}, " +
                      $"delta={((bladeTipLocalZ - ScalpelGripLocalZ) - ScalpelGripToTip) * 1000f:F2} mm)");
            return scalpel;
        }

        /// <summary>
        /// The instrument that answers the bleeding. Built from primitives: there is no forceps
        /// in Assets/Models, and the procedure cannot be finished without one, so a blockout that
        /// hinges and pinches correctly is worth more than waiting for the art.
        ///
        /// Proportions are a haemostat's — a 9 cm shank the hand closes on, a hinge, and 4.8 cm
        /// jaws — because the jaw tip is a gameplay position, not decoration: it is the point
        /// BleedingSystem tests for pressure.
        /// </summary>
        private static GameObject BuildForceps(GameObject tray)
        {
            GameObject forceps = new GameObject("Forceps");
            forceps.transform.position = new Vector3(
                ForcepsRestPosition.x,
                tray.transform.position.y + TableTopY + 0.0236f + 0.01f,
                ForcepsRestPosition.z);

            Material steel = MakeMaterial(new Color(0.74f, 0.76f, 0.80f), 0.85f, 0.72f);

            // Shank. The grip sits at the same local Z as the scalpel's so both instruments end
            // up in the hand the same way round.
            GameObject shank = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shank.name = "Shank";
            shank.transform.SetParent(forceps.transform, false);
            shank.transform.localPosition = new Vector3(0f, 0f, -0.030f);
            shank.transform.localScale = new Vector3(0.009f, 0.012f, 0.090f);
            Object.DestroyImmediate(shank.GetComponent<Collider>());
            shank.GetComponent<MeshRenderer>().sharedMaterial = steel;

            GameObject hinge = new GameObject("Hinge");
            hinge.transform.SetParent(forceps.transform, false);
            hinge.transform.localPosition = new Vector3(0f, 0f, 0.015f);

            // Each jaw pivots at the hinge, so the rotating object is an empty there and the bar
            // is its child, offset forward. Rotating a primitive directly would swing it about
            // its own centre and the jaws would slide instead of opening.
            Transform upper = BuildJaw(hinge.transform, "UpperJaw", 1f, steel);
            Transform lower = BuildJaw(hinge.transform, "LowerJaw", -1f, steel);

            GameObject jawTip = new GameObject("JawTip");
            jawTip.transform.SetParent(forceps.transform, false);
            jawTip.transform.localPosition = new Vector3(0f, 0f, 0.063f);

            GameObject gripPoint = new GameObject("GripPoint");
            gripPoint.transform.SetParent(forceps.transform, false);
            gripPoint.transform.localPosition = new Vector3(0f, 0f, ScalpelGripLocalZ);

            // Same reasoning as the scalpel: the grab volume is sized for the hand, not for the
            // silhouette, or a 9 mm shank becomes miserable to pick up.
            BoxCollider grabBox = forceps.AddComponent<BoxCollider>();
            grabBox.size = new Vector3(0.024f, 0.024f, 0.110f);
            grabBox.center = new Vector3(0f, 0f, -0.025f);

            Rigidbody body = forceps.AddComponent<Rigidbody>();
            body.mass = 0.06f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.useGravity = false;
            body.isKinematic = true;

            ToolDefinition definition = AssetDatabase.LoadAssetAtPath<ToolDefinition>(ForcepsDefinition);
            if (definition == null)
            {
                Debug.LogWarning("[SurgeryMVP] Tool_Forceps.asset not found; the tool will run undefined.");
            }

            SurgicalInteractable interactable = forceps.AddComponent<SurgicalInteractable>();
            SetPrivateField(interactable, "toolDefinition", definition);
            SetPrivateField(interactable, "gripPoint", gripPoint.transform);

            ForcepsTool tool = forceps.AddComponent<ForcepsTool>();
            tool.SetToolDefinition(definition);
            SetPrivateField(tool, "upperJaw", upper);
            SetPrivateField(tool, "lowerJaw", lower);
            SetPrivateField(tool, "jawTip", jawTip.transform);

            XRGrabInteractable grab = forceps.AddComponent<XRGrabInteractable>();
            grab.attachTransform = gripPoint.transform;
            grab.useDynamicAttach = false;
            grab.throwOnDetach = false;
            grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;

            // Without this the jaws never move: ForcepsTool takes a closure value and deliberately
            // knows nothing about input, so something has to hand it the trigger.
            forceps.AddComponent<ForcepsGripInput>();

            forceps.AddComponent<ToolReleasePhysics>();

            ToolHoverHighlight highlight = forceps.AddComponent<ToolHoverHighlight>();
            Material highlightMat = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                color = new Color(0.30f, 0.62f, 1f)
            };
            highlightMat.SetFloat("_Smoothness", 0.9f);
            SetPrivateField(highlight, "highlightMaterial", highlightMat);
            SetPrivateField(highlight, "renderers", forceps.GetComponentsInChildren<MeshRenderer>());

            Debug.Log($"[SurgeryMVP] forceps at {forceps.transform.position}, jaw tip {0.063f - ScalpelGripLocalZ:F3} m from the grip");
            return forceps;
        }

        /// <summary>One hinged jaw. <paramref name="side"/> is +1 for the upper bar, -1 for the lower.</summary>
        private static Transform BuildJaw(Transform hinge, string name, float side, Material material)
        {
            GameObject pivot = new GameObject(name);
            pivot.transform.SetParent(hinge, false);

            GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = name + "Bar";
            bar.transform.SetParent(pivot.transform, false);
            bar.transform.localPosition = new Vector3(0f, side * 0.0022f, 0.024f);
            bar.transform.localScale = new Vector3(0.007f, 0.0035f, 0.048f);
            Object.DestroyImmediate(bar.GetComponent<Collider>());
            bar.GetComponent<MeshRenderer>().sharedMaterial = material;

            return pivot.transform;
        }

        // ------------------------------------------------------------------
        // Systems
        // ------------------------------------------------------------------

        private static GameObject BuildSystems()
        {
            GameObject systems = new GameObject("SurgerySystems");
            systems.AddComponent<SurgeryTelemetry>();

            AudioSource source = systems.AddComponent<AudioSource>();
            source.playOnAwake = false;
            SurgeryAudio audio = systems.AddComponent<SurgeryAudio>();
            SetPrivateField(audio, "source", source);
            return systems;
        }

        /// <summary>
        /// The objective monitor, and with it the two readouts the procedure had no way to show:
        /// which step the visitor is on, and how far the blade is from the guided line.
        ///
        /// It is a screen in the room, never a panel welded to the player's face — the same rule
        /// the rest of the UI follows. Position comes from MonitorPosition, which is derived from
        /// the stance rather than eyeballed.
        /// </summary>
        private static GameObject BuildObjectiveMonitor()
        {
            GameObject monitor = new GameObject("ObjectiveMonitor");
            monitor.transform.position = MonitorPosition;

            // Square on to the player. Yaw only: tilting the panel to chase the eye height makes
            // the text keystone on the projected view for no readability gain.
            Vector3 toPlayer = new Vector3(
                PlayerStance.x - MonitorPosition.x, 0f, PlayerStance.z - MonitorPosition.z);
            monitor.transform.rotation = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);

            GameObject screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screen.name = "Screen";
            screen.transform.SetParent(monitor.transform, false);
            screen.transform.localScale = new Vector3(MonitorWidth, MonitorHeight, 1f);
            // A Unity quad faces -Z, so it has to be turned to look back along the parent's +Z.
            screen.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            Object.DestroyImmediate(screen.GetComponent<Collider>());
            screen.GetComponent<MeshRenderer>().sharedMaterial =
                MakeMaterial(new Color(0.06f, 0.08f, 0.11f), 0f, 0.1f);

            // +Z is the player's side of the panel, so the text has to sit in front of the screen
            // quad rather than behind it, or the panel occludes every word.
            TextMesh objective = BuildScreenText(
                monitor.transform, "ObjectiveText", new Vector3(0f, 0.055f, 0.004f), 0.030f);

            TextMesh status = BuildScreenText(
                monitor.transform, "StatusText", new Vector3(0f, -0.085f, 0.004f), 0.022f);

            // Incision confirmation lamp, beside the status line.
            GameObject lamp = GameObject.CreatePrimitive(PrimitiveType.Quad);
            lamp.name = "ConfirmationLamp";
            lamp.transform.SetParent(monitor.transform, false);
            lamp.transform.localPosition = new Vector3(-0.21f, -0.085f, -0.004f);
            lamp.transform.localScale = new Vector3(0.03f, 0.03f, 1f);
            lamp.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            Object.DestroyImmediate(lamp.GetComponent<Collider>());

            // The components go on now; WireEventSession binds them once the tissue and the
            // instruments they read actually exist.
            monitor.AddComponent<SurgeryHUD>();
            monitor.AddComponent<SurgicalFeedbackHUD>();

            Debug.Log($"[SurgeryMVP] objective monitor at {MonitorPosition}, facing the stance " +
                      $"({objective.name} + {status.name} + {lamp.name})");
            return monitor;
        }

        /// <summary>
        /// A line of text on the monitor. TextMesh rather than TextMeshPro on purpose: the two
        /// HUD components already take TextMesh, and a legacy mesh costs the Quest less than a
        /// world-space canvas for what is a handful of unchanging words.
        /// </summary>
        private static TextMesh BuildScreenText(Transform parent, string name, Vector3 localPosition, float height)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            TextMesh text = go.AddComponent<TextMesh>();
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = new Color(0.82f, 0.88f, 0.95f);

            // A TextMesh with no font renders nothing and logs no error, which is the single
            // easiest way to ship a monitor that is silently blank.
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
            {
                Debug.LogError("[SurgeryMVP] Built-in font not found; the monitor will render empty.");
            }
            else
            {
                text.font = font;
                go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }

            // fontSize is the raster resolution and characterSize is the world scale; a glyph ends
            // up (fontSize * characterSize / 10) units tall. Rastering large and scaling down is
            // what keeps world-space text from looking chewed at reading distance.
            text.fontSize = 96;
            text.characterSize = height * 10f / text.fontSize;

            return text;
        }

        /// <summary>
        /// Connects the procedure to the booth loop. Every piece already existed and was already
        /// covered by tests; none of it was ever placed in the scene, so the built scene could be
        /// cut and bled in but never started, timed, won, lost or reset.
        /// </summary>
        private static void WireEventSession(
            GameObject systems, GameObject monitor, GameObject region, params GameObject[] tools)
        {
            SurgeryTelemetry telemetry = systems.GetComponent<SurgeryTelemetry>();

            SurgeryDefinition surgery = AssetDatabase.LoadAssetAtPath<SurgeryDefinition>(SurgeryAsset);
            if (surgery == null)
            {
                Debug.LogError("[SurgeryMVP] " + SurgeryAsset + " not found; the procedure has no objectives.");
            }

            EventSessionDefinition session = AssetDatabase.LoadAssetAtPath<EventSessionDefinition>(SessionAsset);
            if (session == null)
            {
                Debug.LogError("[SurgeryMVP] " + SessionAsset + " not found; the round falls back to its defaults.");
            }

            SurgeryObjectiveSystem objectives = systems.AddComponent<SurgeryObjectiveSystem>();
            objectives.SetSurgeryDefinition(surgery);

            // The booth, not the objective system, decides when a run starts: autoStart would arm
            // the procedure at scene load, so the first visitor would inherit a surgery that had
            // been running since the stand opened.
            SetPrivateField(objectives, "autoStart", false);

            IncisionSystem incision = region.GetComponent<IncisionSystem>();
            BleedingSystem bleeding = region.GetComponent<BleedingSystem>();

            SurgeryEvaluation evaluation = systems.AddComponent<SurgeryEvaluation>();
            evaluation.Bind(objectives, incision, bleeding);

            SurgicalInteractable[] interactables = new SurgicalInteractable[tools.Length];
            for (int i = 0; i < tools.Length; i++)
            {
                interactables[i] = tools[i].GetComponent<SurgicalInteractable>();
            }

            SurgeryResetController reset = systems.AddComponent<SurgeryResetController>();
            reset.Bind(incision, objectives, evaluation, telemetry, interactables);

            EventSessionController controller = systems.AddComponent<EventSessionController>();
            controller.Bind(session, reset, telemetry);

            monitor.GetComponent<SurgeryHUD>().Bind(
                objectives, monitor.transform.Find("ObjectiveText").GetComponent<TextMesh>());

            BladeTip bladeTip = null;
            foreach (GameObject tool in tools)
            {
                BladeTip candidate = tool.GetComponentInChildren<BladeTip>();
                if (candidate != null) { bladeTip = candidate; break; }
            }

            Transform guideMarker = region.transform.Find("IncisionGuide/GuideMarker");
            SurgicalFeedbackHUD feedback = monitor.GetComponent<SurgicalFeedbackHUD>();
            feedback.Bind(
                region.GetComponent<TissueSurface>(),
                region.GetComponentInChildren<IncisionGuide>(),
                region.GetComponent<IncisableSkin>(),
                bladeTip,
                guideMarker != null ? guideMarker.GetComponent<MeshRenderer>() : null,
                monitor.transform.Find("ConfirmationLamp").GetComponent<MeshRenderer>(),
                monitor.transform.Find("StatusText").GetComponent<TextMesh>());

            feedback.BindMaterials(
                MakeMaterial(new Color(0.15f, 0.55f, 0.95f), 0f, 0.2f),   // idle: the guide's own blue
                MakeMaterial(new Color(0.25f, 0.90f, 0.40f), 0f, 0.2f),   // on target
                MakeMaterial(new Color(0.98f, 0.78f, 0.20f), 0f, 0.2f),   // drifting
                MakeMaterial(new Color(0.95f, 0.25f, 0.20f), 0f, 0.2f),   // off target
                MakeMaterial(new Color(0.18f, 0.20f, 0.24f), 0f, 0.1f),   // lamp off
                MakeMaterial(new Color(0.30f, 0.95f, 0.45f), 0f, 0.1f));  // lamp on

            Debug.Log($"[SurgeryMVP] event session wired: {(surgery != null ? surgery.Objectives.Count : 0)} objectives, " +
                      $"{interactables.Length} resettable instrument(s), round " +
                      $"{(session != null ? session.RoundSeconds : 0f):F0} s");
        }

        /// <summary>
        /// The audience's screen: clock, bleeding bar, risk colour, best time and the table
        /// between visitors.
        ///
        /// Screen Space Overlay on the projector's display, not geometry in the room. An overlay
        /// canvas is never rendered by the headset's camera, so there is no way for any of this
        /// to end up floating in front of the player — and it costs the Quest nothing, because on
        /// a Quest there is no second display for it to draw to at all.
        /// </summary>
        private static GameObject BuildProjectionHUD(GameObject systems, GameObject region)
        {
            GameObject root = new GameObject("ProjectionHUD");

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.targetDisplay = ProjectionDisplayIndex;

            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            // Match height: a projector's width varies with the throw and the lens, the height is
            // what the crowd's eye level is set by.
            scaler.matchWidthOrHeight = 1f;

            // No GraphicRaycaster: nobody clicks a projection, and a raycaster on an overlay
            // canvas quietly eats pointer events the editor's simulator wants.

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // Red edge that deepens with the clock. Behind everything else, so text stays legible.
            Image vignette = BuildHudImage(root.transform, "UrgencyVignette",
                new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero,
                new Color(0.75f, 0.05f, 0.05f, 0f));

            GameObject clockGroup = new GameObject("ClockGroup", typeof(RectTransform));
            clockGroup.transform.SetParent(root.transform, false);
            StretchFull(clockGroup.GetComponent<RectTransform>());

            Text clock = BuildHudText(clockGroup.transform, "Clock", font, 220, TextAnchor.UpperCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(900f, 260f));

            // Bleeding bar. Filled horizontally so it grows the way the concept describes.
            BuildHudImage(clockGroup.transform, "BleedTrack",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -330f), new Vector2(1100f, 46f),
                new Color(0.12f, 0.12f, 0.14f, 0.85f));

            Image bleedFill = BuildHudImage(clockGroup.transform, "BleedFill",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -330f), new Vector2(1100f, 46f),
                new Color(0.85f, 0.25f, 0.25f, 1f));
            bleedFill.type = Image.Type.Filled;
            bleedFill.fillMethod = Image.FillMethod.Horizontal;
            bleedFill.fillAmount = 0f;

            Text headline = BuildHudText(root.transform, "Headline", font, 110, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(1600f, 180f));

            Text subline = BuildHudText(root.transform, "Subline", font, 52, TextAnchor.UpperCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -80f), new Vector2(1500f, 220f));

            GameObject scoreGroup = new GameObject("ScoreboardGroup", typeof(RectTransform));
            scoreGroup.transform.SetParent(root.transform, false);
            StretchFull(scoreGroup.GetComponent<RectTransform>());

            Text scoreTable = BuildHudText(scoreGroup.transform, "ScoreTable", font, 64, TextAnchor.UpperCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -140f), new Vector2(1100f, 460f));
            scoreTable.lineSpacing = 1.35f;

            Leaderboard leaderboard = systems.AddComponent<Leaderboard>();
            leaderboard.Bind(systems.GetComponent<EventSessionController>());

            ProjectionHUD hud = root.AddComponent<ProjectionHUD>();
            hud.Bind(
                systems.GetComponent<EventSessionController>(),
                region.GetComponent<BleedingSystem>(),
                leaderboard);
            hud.BindWidgets(clock, bleedFill, vignette, headline, subline,
                clockGroup, scoreGroup, scoreTable);

            Debug.Log($"[SurgeryMVP] projection HUD -> Display {ProjectionDisplayIndex + 1} " +
                      "(overlay canvas; invisible to the headset)");
            return root;
        }

        /// <summary>
        /// Hooks the room's light to the clock. Finds the template's directional light rather than
        /// adding one, so the scene keeps a single key light and the tint moves what is already
        /// lighting the patient.
        /// </summary>
        private static void WireUrgencyTint(GameObject systems)
        {
            Light key = null;
            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type == LightType.Directional)
                {
                    key = light;
                    break;
                }
            }

            if (key == null)
            {
                Debug.LogWarning("[SurgeryMVP] No directional light found; the room will not redden with the clock.");
            }

            SceneUrgencyTint tint = systems.AddComponent<SceneUrgencyTint>();
            tint.Bind(systems.GetComponent<EventSessionController>(), key);

            Debug.Log($"[SurgeryMVP] urgency tint bound to '{(key != null ? key.name : "nothing")}'");
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Image BuildHudImage(Transform parent, string name, Vector2 anchorMin,
            Vector2 anchorMax, Vector2 position, Vector2 size, Color colour)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;

            if (anchorMin == Vector2.zero && anchorMax == Vector2.one)
            {
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }
            else
            {
                rect.anchoredPosition = position;
                rect.sizeDelta = size;
            }

            Image image = go.AddComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;
            return image;
        }

        private static Text BuildHudText(Transform parent, string name, Font font, int size,
            TextAnchor anchor, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 sizeDelta)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.anchoredPosition = position;
            rect.sizeDelta = sizeDelta;

            Text text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            if (font == null)
            {
                Debug.LogError("[SurgeryMVP] Built-in font missing; the projection will render blank.");
            }

            return text;
        }

        /// <summary>
        /// Headless driver for the grab-cut-release cycle. Disabled in the scene so it never
        /// fights a real player for the scalpel; tests enable it explicitly.
        /// </summary>
        private static void BuildProbe(GameObject systems, GameObject scalpel)
        {
            SurgeryProbe probe = systems.AddComponent<SurgeryProbe>();
            probe.RunOnStart = false;

            ScalpelTool tool = scalpel.GetComponent<ScalpelTool>();
            IncisionSystem system = Object.FindFirstObjectByType<IncisionSystem>();
            probe.Bind(tool, system);
            Debug.Log("[SurgeryMVP] surgery probe wired (runOnStart=false)");
        }

        /// <summary>
        /// Decorative poster on the far wall. Unlit so the room's lighting does not wash it out,
        /// and with no collider so it can never catch a grab or a raycast.
        /// </summary>
        private static void BuildWallPoster()
        {
            // Unity rescales non-power-of-two textures on import, so the imported Texture2D can
            // report 512x512 for a 734x724 file. Turning that off keeps the real dimensions,
            // which is what the board's proportions are derived from — otherwise any picture
            // that is not already square comes out stretched.
            TextureImporter importer = AssetImporter.GetAtPath(PosterTexture) as TextureImporter;
            if (importer != null && importer.npotScale != TextureImporterNPOTScale.None)
            {
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.SaveAndReimport();
                Debug.Log("[SurgeryMVP] disabled NPOT rescaling on " + PosterTexture);
            }

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(PosterTexture);
            if (texture == null)
            {
                Debug.LogWarning($"[SurgeryMVP] No image at {PosterTexture}; skipping the meme board.");
                return;
            }

            GameObject board = GameObject.CreatePrimitive(PrimitiveType.Quad);
            board.name = "WallPoster";
            board.transform.position = PosterPosition;
            board.transform.rotation = Quaternion.Euler(PosterRotationEuler);

            // Keep the picture's own proportions instead of stretching it into a square.
            float aspect = texture.height / (float)Mathf.Max(1, texture.width);
            board.transform.localScale = new Vector3(PosterWidth, PosterWidth * aspect, 1f);

            Object.DestroyImmediate(board.GetComponent<Collider>());

            Material m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetTexture("_BaseMap", texture);
            board.GetComponent<MeshRenderer>().sharedMaterial = m;

            Debug.Log($"[SurgeryMVP] wall poster at {PosterPosition} euler {PosterRotationEuler}, " +
                      $"{PosterWidth:F2} x {PosterWidth * aspect:F2} m " +
                      $"(source {texture.width}x{texture.height})");
        }

        private static void PlaceAnchor()
        {
            GameObject anchor = GameObject.Find("Teleport Anchor");
            if (anchor == null)
            {
                Debug.LogWarning("[SurgeryMVP] No 'Teleport Anchor' in the template scene.");
                return;
            }

            // One anchor, at the feet. The standing distance is a placeholder: the reach work
            // that would validate it has not been redone for this environment.
            Vector3 stance = PlayerStance;

            // Face the midpoint of the work area rather than the patient square-on. Facing the
            // field alone puts the instrument stand out on the shoulder line, and a surgeon does
            // not stand perpendicular to the table either. The reach model takes yaw from the
            // head, so this orientation is what decides both shoulder positions.
            Vector3 workCentre = Vector3.Lerp(
                new Vector3(0f, 0f, FieldZ),
                new Vector3(TrayStandPosition.x, 0f, TrayStandPosition.z),
                0.5f);

            Vector3 toWork = workCentre - stance;
            toWork.y = 0f;
            Quaternion facing = toWork.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(toWork.normalized, Vector3.up)
                : Quaternion.identity;

            anchor.transform.position = stance;
            anchor.transform.rotation = facing;

            // With a single anchor the player has to START on it. Leaving the rig at the
            // template's default pose is why every reach measurement was taken from a spot
            // the design never puts the player in.
            GameObject rig = GameObject.Find("XR Origin");
            if (rig != null)
            {
                rig.transform.position = stance;
                rig.transform.rotation = facing;
                Debug.Log($"[SurgeryMVP] XR Origin moved onto the anchor at {stance}");
            }
            else
            {
                Debug.LogWarning("[SurgeryMVP] No XR Origin to seat on the anchor.");
            }

            Debug.Log($"[SurgeryMVP] teleport anchor at {stance} facing {facing.eulerAngles.y:F0} deg (toward the patient)");
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static GameObject Instantiate(string assetPath, Transform parent)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (asset == null)
            {
                throw new System.IO.FileNotFoundException("Model not found: " + assetPath);
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            return instance;
        }

        private static void EnsureReadable(string assetPath)
        {
            ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (importer == null || importer.isReadable) { return; }

            importer.isReadable = true;
            importer.SaveAndReimport();
            Debug.Log("[SurgeryMVP] enabled Read/Write on " + assetPath);
        }

        private static Material MakeMaterial(Color color, float metallic, float smoothness)
        {
            Material m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            return m;
        }

        private static Material MakeScalpelMaterial()
        {
            Material m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(ScalpelAlbedo);
            Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(ScalpelNormal);
            Texture2D metallic = AssetDatabase.LoadAssetAtPath<Texture2D>(ScalpelMetallic);

            if (albedo != null) { m.SetTexture("_BaseMap", albedo); }
            if (normal != null) { m.SetTexture("_BumpMap", normal); m.EnableKeyword("_NORMALMAP"); }
            if (metallic != null)
            {
                m.SetTexture("_MetallicGlossMap", metallic);
                m.EnableKeyword("_METALLICSPECGLOSSMAP");
            }

            m.SetFloat("_Smoothness", 0.8f);
            return m;
        }

        /// <summary>
        /// Rotates the tool mesh so its working end points along the tool's local +Z, and returns
        /// the blade tip's Z in the tool's (unscaled, metric) space.
        ///
        /// Ported from the old project, where hand-compensating this rotation cost two wrong
        /// fixes: an FBX may put the Z-up-to-Y-up conversion on the mesh data or on the root
        /// node, so the only reliable source is where the blade geometry actually ended up.
        /// </summary>
        private static float AlignToolForward(GameObject meshRoot, string bladePartName)
        {
            Transform blade = null;
            foreach (Transform t in meshRoot.GetComponentsInChildren<Transform>())
            {
                if (t.name == bladePartName) { blade = t; break; }
            }

            if (blade == null)
            {
                Debug.LogError($"[SurgeryMVP] Tool mesh has no '{bladePartName}' part to align by.");
                return 0f;
            }

            MeshFilter filter = blade.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable)
            {
                Debug.LogError($"[SurgeryMVP] '{bladePartName}' has no readable mesh.");
                return 0f;
            }

            // Direction is taken in the mesh root's own space so it does not depend on the
            // rotation being solved for; uniform scale preserves direction.
            meshRoot.transform.localRotation = Quaternion.identity;

            Vector3 tip = Vector3.zero;
            float best = -1f;
            foreach (Vector3 v in filter.sharedMesh.vertices)
            {
                Vector3 local = meshRoot.transform.InverseTransformPoint(blade.TransformPoint(v));
                if (local.magnitude > best) { best = local.magnitude; tip = local; }
            }

            meshRoot.transform.localRotation = Quaternion.FromToRotation(tip.normalized, Vector3.forward);

            // Length must be re-measured in the TOOL's space: the mesh root carries the FBX unit
            // scale, so a distance taken there is off by that factor.
            Transform tool = meshRoot.transform.parent;
            float tipZ = float.MinValue;
            foreach (Vector3 v in filter.sharedMesh.vertices)
            {
                float z = tool.InverseTransformPoint(blade.TransformPoint(v)).z;
                if (z > tipZ) { tipZ = z; }
            }

            return tipZ;
        }

        /// <summary>
        /// The template's rig camera ships untagged, and every ergonomics test identifies the
        /// player's head through Camera.main — without the tag they all fail in SetUp.
        /// </summary>
        /// <summary>
        /// The stance/ergonomics tests locate the player with GameObject.Find("XR Origin"), but
        /// the VR template names its rig "XR Origin Hands (XR Rig)". Renaming the instance is
        /// cheaper and less surprising than teaching every test a second name to look for.
        /// </summary>
        private static void RenameRig()
        {
            if (GameObject.Find("XR Origin") != null) { return; }

            foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            {
                if (go.transform.parent == null && go.name.StartsWith("XR Origin"))
                {
                    go.name = "XR Origin";
                    Debug.Log("[SurgeryMVP] renamed the template rig to 'XR Origin' for the stance tests.");
                    return;
                }
            }

            Debug.LogWarning("[SurgeryMVP] No XR Origin rig found in the template scene.");
        }

        private static void EnsureMainCamera()
        {
            Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (Camera c in cameras)
            {
                if (c.CompareTag("MainCamera")) { return; }
            }

            foreach (Camera c in cameras)
            {
                if (c.name.Contains("Main"))
                {
                    c.tag = "MainCamera";
                    Debug.Log("[SurgeryMVP] tagged '" + c.name + "' as MainCamera.");
                    return;
                }
            }

            Debug.LogWarning("[SurgeryMVP] No camera to tag as MainCamera.");
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
            Debug.Log("[SurgeryMVP] registered " + TargetScene + " in build settings.");
        }

        private static void EnsureTag(string tag)
        {
            SerializedObject tagManager = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty tags = tagManager.FindProperty("tags");

            for (int i = 0; i < tags.arraySize; i++)
            {
                if (tags.GetArrayElementAtIndex(i).stringValue == tag) { return; }
            }

            tags.InsertArrayElementAtIndex(tags.arraySize);
            tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
            tagManager.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log($"[SurgeryMVP] Added missing tag '{tag}'.");
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            System.Reflection.FieldInfo field = target.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field == null)
            {
                Debug.LogError($"[SurgeryMVP] {target.GetType().Name} has no field '{fieldName}'.");
                return;
            }

            field.SetValue(target, value);
        }

        private static void Report(string label, GameObject go)
        {
            bool any = false;
            Bounds b = default;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            {
                if (!any) { b = r.bounds; any = true; } else { b.Encapsulate(r.bounds); }
            }

            if (!any) { Debug.Log($"@@B | {label} | no renderers"); return; }
            Debug.Log($"@@B | {label} | min=({b.min.x:F3},{b.min.y:F3},{b.min.z:F3}) " +
                      $"max=({b.max.x:F3},{b.max.y:F3},{b.max.z:F3}) size=({b.size.x:F3},{b.size.y:F3},{b.size.z:F3})");
        }
    }
}
