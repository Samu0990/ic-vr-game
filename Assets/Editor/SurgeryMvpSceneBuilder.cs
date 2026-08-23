using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using VRSurgery.Audio;
using VRSurgery.Data;
using VRSurgery.Diagnostics;
using VRSurgery.Interaction;
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
        private const string TableFbx = "Assets/Models/Environment/PROP_OperatingTable.fbx";
        private const string ScalpelFbx = "Assets/Models/Tools/SCALPEL_FromGLB.fbx";
        private const string ScalpelAlbedo = "Assets/Models/Tools/BISTURI_Image_0.png";
        private const string ScalpelNormal = "Assets/Models/Tools/BISTURI_Image_2.png";
        private const string ScalpelMetallic = "Assets/Models/Tools/BISTURI_MetallicSmoothness.png";
        private const string IncisionClip = "Assets/Audio/SFX_Incision.wav";
        private const string ScalpelDefinition = "Assets/Data/Tool_Scalpel.asset";

        private const string ScalpelTag = "Scalpel";

        /// <summary>Table top height. Fixed for the MVP — not swept, not derived.</summary>
        private const float TableTopY = 0.95f;

        private const float TableWidthX = 0.50f;
        private const float TableLengthZ = 1.90f;

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
        private static readonly Vector3 TrayStandPosition = new Vector3(0.44f, 0f, 0.80f);

        private const float TrayStandWidthX = 0.20f;
        private const float TrayStandDepthZ = 0.50f;

        /// <summary>0-based; index 0 is "Display 1", index 1 is "Display 2".</summary>
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
            BuildTissue(patient, surfaceY, systems.GetComponent<SurgeryTelemetry>());
            GameObject tray = BuildInstrumentTray();
            GameObject scalpel = BuildScalpel(tray);
            BuildSpectatorCamera();
            BuildProjectionCamera();
            PlaceAnchor();
            BuildProbe(systems, scalpel);

            Report("Table", table);
            Report("Patient", patient);
            Report("Scalpel", scalpel);

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
            foreach (MeshRenderer r in top.GetComponentsInChildren<MeshRenderer>())
            {
                r.sharedMaterial = MakeMaterial(new Color(0.62f, 0.66f, 0.70f), 0.1f, 0.55f);
            }

            // A box collider stands in for the slab: the tool must not fall through the table,
            // and a mesh collider on furniture is wasted cost.
            BoxCollider slab = top.AddComponent<BoxCollider>();
            slab.center = new Vector3(0f, TableTopY - 0.03f, 0f);
            slab.size = new Vector3(TableWidthX, 0.06f, TableLengthZ);

            VerifyTableTop(top);
            return root;
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
            cam.fieldOfView = 70f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 12f;

            // A second camera left on the XR rig's stereo path would fight the headset; this one
            // renders to its own display only.
            UnityEngine.Rendering.Universal.UniversalAdditionalCameraData data =
                go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.allowXRRendering = false;

            go.AddComponent<ProjectionDisplay>();

            Debug.Log($"[SurgeryMVP] projection camera at {go.transform.position} " +
                      $"-> Display {ProjectionDisplayIndex + 1}, looking straight down at the field");
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

        private static void BuildTissue(GameObject patient, float surfaceY, SurgeryTelemetry telemetry)
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
