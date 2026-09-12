using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using VRSurgery.Data;
using VRSurgery.Interaction;
using VRSurgery.Session;
using VRSurgery.Surgery;
using VRSurgery.Tools;
using VRSurgery.Transplant;

namespace VRSurgery.EditorTools
{
    /// <summary>
    /// Builds the heart transplant scene: patient supine on the table, thorax assembled, sternum
    /// wired to open, heart grabbable.
    ///
    /// Nothing is placed by typed coordinates. The four anatomical models were generated
    /// separately and only the heart carries a measurement anyone trusts, so every other position
    /// is derived at build time from the meshes themselves — the table's top surface, the body's
    /// own bounds, the height of the thorax as a fraction of stature. That way re-exporting any
    /// one model moves what depends on it instead of silently leaving it floating.
    /// </summary>
    public static class TransplanteSceneBuilder
    {
        private const string SourceScene = "Assets/Scenes/SampleScene.unity";
        private const string TargetScene = "Assets/Scenes/TransplanteCardiaco.unity";

        private const string TableGlb = "Assets/Models/Environment/PROP_OperatingTable.glb";
        private const string BodyGlb = "Assets/Models/Patient/PATIENT_BodySkin.glb";
        private const string RibcageGlb = "Assets/Models/Patient/PATIENT_Ribcage.glb";
        private const string SternumGlb = "Assets/Models/Patient/PATIENT_Sternum.glb";
        private const string HeartGlb = "Assets/Models/Patient/PATIENT_Heart.glb";

        private const float TableTopY = 0.95f;

        /// <summary>
        /// Level the scene is built for. The bypass sites and the round length both follow from
        /// it, so switching this and rebuilding is the whole change — there is no second place
        /// holding a duration that has to be kept in step.
        /// </summary>
        private static SurgicalDifficulty _difficulty = SurgicalDifficulty.Medio;

        [MenuItem("VRSurgery/Transplante — nível Fácil")]
        public static void BuildEasy() { _difficulty = SurgicalDifficulty.Facil; Build(); }

        [MenuItem("VRSurgery/Transplante — nível Médio")]
        public static void BuildMedium() { _difficulty = SurgicalDifficulty.Medio; Build(); }

        [MenuItem("VRSurgery/Transplante — nível Difícil")]
        public static void BuildHard() { _difficulty = SurgicalDifficulty.Dificil; Build(); }

        /// <summary>
        /// Where the thorax sits, as a fraction of stature measured from the soles. The heart
        /// centre lands a little below the mid-thorax and to the patient's left, which is where
        /// a heart actually is.
        /// </summary>
        private const float ThoraxCentreOfHeight = 0.735f;

        /// <summary>
        /// No correction. The first ribcage came out flat — 20.0 x 10.8 x 32.0cm against a real
        /// cage's 28 x 20 x 30 — and had to be stretched per axis just to let an 8.7cm heart sit
        /// inside without touching ribs front and back. The regenerated model is barrel-shaped:
        /// its depth-to-width ratio measures 0.67 against a real 0.71, where the old one was 0.54.
        /// That scales uniformly to 23.8 x 16.0 x 30.0cm and needs no distortion at all.
        /// </summary>
        private static readonly Vector3 RibcageProportionFix = Vector3.one;

        [MenuItem("VRSurgery/Build 'Transplante Cardíaco'")]
        public static void BuildFromMenu()
        {
            Build();
            EditorUtility.DisplayDialog("Transplante Cardíaco",
                "Cena construída. Veja o Console para as medidas e o veredito de proporção.", "OK");
        }

        public static void Build()
        {
            EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single);

            GameObject area = GameObject.Find("Teleport Area");
            if (area != null) { Object.DestroyImmediate(area); }

            RenameRig();

            GameObject table = BuildTable();
            GameObject patient = BuildPatient(out Bounds bodyBounds);
            GameObject systems = BuildSystems();

            Vector3 thorax = ThoraxCentre(bodyBounds);
            GameObject ribcage = BuildRibcage(patient, thorax);
            GameObject heart = BuildHeart(patient, thorax);
            GameObject sternum = BuildSternum(patient, thorax, heart, WorldBounds(ribcage));

            GameObject donor = BuildDonorHeart(thorax);
            VesselAnastomosis[] vessels = BuildVessels(systems, thorax);
            BypassPlan plan = BypassPlan.For(_difficulty);
            List<BypassSite> bypass = BuildBypassSites(systems, thorax, plan);
            WireProcedure(systems, sternum, heart, donor, vessels, bypass, plan);
            PlaceAnchor(thorax);
            EnsureMainCamera();

            ReportFit(ribcage, heart);

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), TargetScene, true);
            RegisterSceneInBuildSettings();
            Debug.Log("[Transplante] salvo em " + TargetScene);
        }

        // ---------------------------------------------------------------- sala

        private static GameObject BuildTable()
        {
            GameObject root = new GameObject("OperatingTable");
            GameObject model = Instantiate(TableGlb, root.transform);
            model.name = "TableModel";
            ConvertGltfMaterials(model);

            BoxCollider slab = model.AddComponent<BoxCollider>();
            slab.center = new Vector3(0f, TableTopY - 0.03f, 0f);
            slab.size = new Vector3(0.575f, 0.06f, 2.002f);

            Debug.Log($"[Transplante] mesa na origem, tampo em Y={TableTopY:F3}");
            return root;
        }

        /// <summary>
        /// The patient, supine on the table with the head toward +Z.
        ///
        /// The body model stands, so it is pitched a quarter turn to lie down; that also turns its
        /// front to face up, which is what supine means. It is then dropped so its back rests on
        /// the tabletop rather than being pushed through it.
        /// </summary>
        private static GameObject BuildPatient(out Bounds bodyBounds)
        {
            GameObject root = new GameObject("Patient");

            GameObject skin = Instantiate(BodyGlb, root.transform);
            skin.name = "Body_Skin";
            ConvertGltfMaterials(skin);

            // A quarter turn about X lays the standing figure down with the head toward +Z and
            // the chest facing up.
            root.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            bodyBounds = WorldBounds(skin);
            float drop = TableTopY - bodyBounds.min.y;
            root.transform.position += new Vector3(0f, drop, 0f);

            bodyBounds = WorldBounds(skin);

            Debug.Log($"[Transplante] paciente supino: {bodyBounds.size.x * 100f:F1} x " +
                      $"{bodyBounds.size.y * 100f:F1} x {bodyBounds.size.z * 100f:F1} cm, " +
                      $"costas em Y={bodyBounds.min.y:F3}, cabeça em Z={bodyBounds.max.z:F2}");
            return root;
        }

        /// <summary>
        /// Middle of the thorax, derived from the body rather than typed. With the patient supine
        /// the long axis is Z, so stature runs from the soles at min.z to the crown at max.z.
        /// </summary>
        private static Vector3 ThoraxCentre(Bounds body)
        {
            float stature = body.size.z;
            float z = body.min.z + stature * ThoraxCentreOfHeight;

            // Mid-depth of the chest, not the surface: the organs sit inside.
            float y = Mathf.Lerp(body.min.y, body.max.y, 0.45f);

            return new Vector3(body.center.x, y, z);
        }

        // ---------------------------------------------------------------- tórax

        private static GameObject BuildRibcage(GameObject patient, Vector3 thorax)
        {
            GameObject root = new GameObject("Ribcage");
            root.transform.SetParent(patient.transform, true);

            GameObject model = Instantiate(RibcageGlb, root.transform);
            model.name = "RibcageModel";
            ConvertGltfMaterials(model);

            // Laid down with the spine against the table, which is the whole point of supine and
            // is not something to guess at: the first attempt reused the body's rotation and put
            // the cage on its side, spine out sideways. Which face carries the spine is measured
            // from the mesh, because a generated model gives no guarantee about its axes.
            root.transform.rotation = SupineRotationFor(model);
            root.transform.localScale = RibcageProportionFix;
            root.transform.position = thorax;

            Bounds bounds = WorldBounds(model);
            Debug.Log($"[Transplante] gradil: {bounds.size.x * 100f:F1} x {bounds.size.y * 100f:F1} x " +
                      $"{bounds.size.z * 100f:F1} cm" +
                      (RibcageProportionFix == Vector3.one ? " (escala uniforme, sem distorção)"
                                                           : $" (CORRIGIDO {RibcageProportionFix})"));
            return root;
        }

        /// <summary>
        /// Works out how to lay a thoracic model down so the spine ends up underneath.
        ///
        /// The spine is a dense column of vertices hugging the midline; the ribs sweep away from
        /// it and are sparse there. So the discriminator is how much geometry sits near x = 0 on
        /// each side of the model's depth axis, not how wide each side is — the ribs reach the
        /// same span front and back, and a first attempt comparing widths read 19.9cm against
        /// 20.0cm and decided nothing.
        /// </summary>
        private static Quaternion SupineRotationFor(GameObject model)
        {
            // The band has to be a fraction of the model's width, not a fixed distance. At 3cm
            // absolute it covered 1.9% of the source model and 12.6% of the same model once
            // scaled to anatomical size — tight enough to isolate the spine in one case, wide
            // enough to sweep in ribs in the other, which turned a 9957-to-0 signal into 1.4:1.
            Bounds extent = LocalBounds(model, model.transform);
            float midlineBand = Mathf.Max(0.004f, extent.size.x * 0.06f);

            int frontMidline = 0, backMidline = 0;

            // Vertices are brought into the model root's space first. Reading them raw from the
            // mesh measures whatever space the importer happened to nest them in, while the
            // rotation is applied to the root — two different frames, which is how this returned
            // a limp 1.4:1 where the same count in the authoring tool was 9957 against zero.
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) { continue; }
                foreach (Vector3 raw in filter.sharedMesh.vertices)
                {
                    Vector3 v = model.transform.InverseTransformPoint(filter.transform.TransformPoint(raw));
                    if (Mathf.Abs(v.x - extent.center.x) > midlineBand) { continue; }
                    if (v.z >= 0f) { frontMidline++; } else { backMidline++; }
                }
            }

            bool spineAtPositiveZ = frontMidline > backMidline;

            Debug.Log($"[Transplante] coluna detectada em {(spineAtPositiveZ ? "+Z" : "-Z")} do modelo " +
                      $"(faixa ±{midlineBand * 100f:F1}cm: +Z={frontMidline}, -Z={backMidline}, " +
                      $"razão {Mathf.Max(frontMidline, backMidline) / (float)Mathf.Max(1, Mathf.Min(frontMidline, backMidline)):F1}:1)");

            // Head toward +Z either way; the roll is what puts the spine down against the table.
            return spineAtPositiveZ
                ? Quaternion.Euler(90f, 0f, 0f)
                : Quaternion.Euler(-90f, 180f, 0f);
        }

        private static GameObject BuildHeart(GameObject patient, Vector3 thorax)
        {
            GameObject root = new GameObject("Heart");
            root.transform.SetParent(patient.transform, true);

            GameObject model = Instantiate(HeartGlb, root.transform);
            model.name = "HeartModel";
            ConvertGltfMaterials(model);

            // Left of the midline and slightly toward the feet, with the apex pointing down-left
            // and forward — where a heart sits, not centred in the chest like a textbook diagram.
            root.transform.rotation = Quaternion.Euler(90f, 0f, 22f);
            root.transform.position = thorax + new Vector3(-0.025f, 0.01f, -0.02f);

            DressAsGrabbable(root, model);

            Bounds bounds = WorldBounds(model);
            Debug.Log($"[Transplante] coração: {bounds.size.x * 100f:F1} x {bounds.size.y * 100f:F1} x " +
                      $"{bounds.size.z * 100f:F1} cm em {root.transform.position}");
            return root;
        }

        private static GameObject BuildSternum(GameObject patient, Vector3 thorax, GameObject heart,
            Bounds ribcage)
        {
            GameObject root = new GameObject("Sternum");
            root.transform.SetParent(patient.transform, true);

            GameObject model = Instantiate(SternumGlb, root.transform);
            model.name = "SternumModel";
            ConvertGltfMaterials(model);

            root.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            // Seated on the front of the cage rather than at a fixed offset from the thorax
            // centre, which left it hovering above the ribs with daylight underneath. On a supine
            // patient the front of the chest is the top, so it rides the ribcage's upper surface,
            // sunk slightly so the bone meets cartilage instead of resting on it.
            Bounds plate = WorldBounds(root);
            float seatY = ribcage.max.y - plate.size.y * 0.35f;
            root.transform.position = new Vector3(thorax.x, seatY, thorax.z + 0.015f);

            SternotomyController sternotomy = root.AddComponent<SternotomyController>();
            sternotomy.Bind(root.transform, new[] { heart });

            Debug.Log($"[Transplante] esterno em {root.transform.position}, " +
                      "SternotomyController revelando o coração");
            return root;
        }

        /// <summary>
        /// Gives the heart the project's grab contract. Same components the instruments use, and
        /// deliberately no SurgicalTool — that base class starts the booth's round on first grab.
        /// </summary>
        private static void DressAsGrabbable(GameObject organ, GameObject model)
        {
            Bounds local = LocalBounds(model, organ.transform);

            BoxCollider box = organ.AddComponent<BoxCollider>();
            box.center = local.center;
            box.size = local.size;

            Rigidbody body = organ.AddComponent<Rigidbody>();
            body.mass = 0.3f;
            body.useGravity = false;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            GameObject grip = new GameObject("GripPoint");
            grip.transform.SetParent(organ.transform, false);
            grip.transform.localPosition = local.center;

            ToolDefinition definition = ToolDefinition.Create(
                "heart", "Coração", ToolType.Retractor, ToolCapability.None);

            SurgicalInteractable interactable = organ.AddComponent<SurgicalInteractable>();
            SetPrivateField(interactable, "toolDefinition", definition);
            SetPrivateField(interactable, "gripPoint", grip.transform);

            XRGrabInteractable grab = organ.AddComponent<XRGrabInteractable>();
            grab.attachTransform = grip.transform;
            grab.useDynamicAttach = false;
            grab.throwOnDetach = false;
            grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;

            organ.AddComponent<ToolReleasePhysics>();
            organ.AddComponent<GrabbableOrgan>();
        }

        // ---------------------------------------------------------------- sistemas

        private static GameObject BuildSystems()
        {
            GameObject systems = new GameObject("Systems");
            systems.AddComponent<SurgeryTelemetry>();
            return systems;
        }

        /// <summary>
        /// The replacement organ, waiting on its own stand beside the table.
        ///
        /// It starts outside the patient because that is where a donor heart is: brought in cold,
        /// in a basin, and lifted into a chest that is already empty. Putting it in the thorax
        /// from the start would make the middle of the operation meaningless.
        /// </summary>
        private static GameObject BuildDonorHeart(Vector3 thorax)
        {
            GameObject stand = new GameObject("DonorStand");
            stand.transform.position = new Vector3(thorax.x + 0.50f, 0f, thorax.z - 0.35f);

            GameObject pedestal = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pedestal.name = "StandColumn";
            pedestal.transform.SetParent(stand.transform, false);
            pedestal.transform.localScale = new Vector3(0.26f, 0.90f, 0.26f);
            pedestal.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            pedestal.GetComponent<MeshRenderer>().sharedMaterial =
                MakeMaterial(new Color(0.55f, 0.58f, 0.62f), 0f, 0.3f);

            GameObject basin = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            basin.name = "Basin";
            basin.transform.SetParent(stand.transform, false);
            basin.transform.localScale = new Vector3(0.24f, 0.03f, 0.24f);
            basin.transform.localPosition = new Vector3(0f, 0.92f, 0f);
            Object.DestroyImmediate(basin.GetComponent<Collider>());
            basin.GetComponent<MeshRenderer>().sharedMaterial =
                MakeMaterial(new Color(0.72f, 0.75f, 0.78f), 0.8f, 0.6f);

            GameObject organ = new GameObject("DonorHeart");
            organ.transform.SetParent(stand.transform, true);
            organ.transform.position = stand.transform.position + new Vector3(0f, 1.0f, 0f);
            organ.transform.rotation = Quaternion.Euler(0f, 0f, 12f);

            GameObject model = Instantiate(HeartGlb, organ.transform);
            model.name = "DonorHeartModel";
            ConvertGltfMaterials(model);

            DressAsGrabbable(organ, model);
            organ.AddComponent<Heartbeat>().Bind(model.transform);

            Debug.Log($"[Transplante] coração doador na bacia em {organ.transform.position}");
            return organ;
        }

        /// <summary>
        /// The five joins, placed around the recipient's seat in roughly the order they are sewn:
        /// left atrium first and deepest, the great arteries last and most exposed.
        /// </summary>
        private static VesselAnastomosis[] BuildVessels(GameObject systems, Vector3 thorax)
        {
            (VesselSite site, Vector3 offset)[] layout =
            {
                (VesselSite.LeftAtrium,       new Vector3(-0.030f, -0.020f,  0.005f)),
                (VesselSite.InferiorVenaCava, new Vector3( 0.028f, -0.015f, -0.045f)),
                (VesselSite.SuperiorVenaCava, new Vector3( 0.032f,  0.015f,  0.050f)),
                (VesselSite.Aorta,            new Vector3(-0.008f,  0.030f,  0.055f)),
                (VesselSite.PulmonaryArtery,  new Vector3(-0.032f,  0.028f,  0.040f)),
            };

            GameObject root = new GameObject("Anastomoses");
            root.transform.SetParent(systems.transform, true);

            VesselAnastomosis[] built = new VesselAnastomosis[layout.Length];
            for (int i = 0; i < layout.Length; i++)
            {
                GameObject site = new GameObject("Vessel_" + layout[i].site);
                site.transform.SetParent(root.transform, true);
                site.transform.position = thorax + layout[i].offset;

                built[i] = site.AddComponent<VesselAnastomosis>();
                built[i].Bind(layout[i].site, null, 0.022f);

                BuildVesselMarker(site.transform, layout[i].site, 0.022f);
            }

            Debug.Log($"[Transplante] {built.Length} anastomoses posicionadas ao redor do assento");
            return built;
        }

        /// <summary>
        /// Where the pump is worked: the cannulation sites on the great vessels, the cross-clamp
        /// on the ascending aorta, and the cardioplegia cannula in the aortic root.
        ///
        /// Entry and exit share their sites, because that is true of the real thing — the clamp
        /// comes off the same aorta it went onto. The six holds come to roughly forty seconds,
        /// which is the share of a four-minute operation bypass is allowed before it stops being
        /// a procedure and becomes a minigame.
        /// </summary>
        private static List<BypassSite> BuildBypassSites(GameObject systems, Vector3 thorax,
            BypassPlan plan)
        {
            GameObject root = new GameObject("BypassSites");
            root.transform.SetParent(systems.transform, true);

            // Anatomy, not layout: where each step is performed on a real patient. A gesture that
            // carries several steps is placed at the last of them, which is the one the surgeon's
            // hands finish on.
            Dictionary<BypassStep, Vector3> where = new Dictionary<BypassStep, Vector3>
            {
                { BypassStep.Cannulate,    new Vector3( 0.035f,  0.020f,  0.030f) },
                { BypassStep.ClampAorta,   new Vector3(-0.010f,  0.035f,  0.060f) },
                { BypassStep.Cardioplegia, new Vector3(-0.005f,  0.030f,  0.048f) },
                { BypassStep.Unclamp,      new Vector3(-0.010f,  0.035f,  0.060f) },
                { BypassStep.DeAir,        new Vector3(-0.028f,  0.032f,  0.035f) },
                { BypassStep.Wean,         new Vector3( 0.060f,  0.010f, -0.060f) },
            };

            List<BypassSite> sites = new List<BypassSite>();

            foreach (BypassGesture gesture in plan.Gestures)
            {
                GameObject point = new GameObject("Bypass_" + gesture.Anchor);
                point.transform.SetParent(root.transform, true);
                point.transform.position = thorax +
                    (where.TryGetValue(gesture.Anchor, out Vector3 offset) ? offset : Vector3.zero);

                sites.Add(new BypassSite
                {
                    Step = gesture.Anchor,
                    Point = point.transform,
                    Radius = 0.028f,
                    Seconds = gesture.Seconds,
                    Chain = gesture.Steps,
                });
            }

            Debug.Log($"[Transplante] nível {plan.Difficulty}: {sites.Count} ponto(s) de CEC, " +
                      $"{plan.GestureSeconds:F0}s de gestos, {plan.DoneByTeam.Count} etapa(s) " +
                      $"já feitas pela equipe, rodada de {plan.RoundSeconds:F0}s");
            return sites;
        }

        /// <summary>
        /// A visible cuff at each join.
        ///
        /// The sites were invisible transforms, so the visitor was asked to sew five vessels with
        /// nothing to aim at. This is a ring rather than a vessel model: the generated vessel set
        /// could not be segmented into its five pieces by any means tried — loose parts gave 350
        /// shells, spatial clustering bridged the aortic arch into the pulmonary trunk, and the
        /// texture distinguishes arterial from venous rather than one vessel from another — and
        /// the heart's own vessels are the same shell soup, 776 boundary loops with no five tube
        /// ends among them. Importing 1.9M triangles that overlap vessels the heart already
        /// carries would have bought nothing the mechanic can use.
        ///
        /// Arterial red and venous blue, the one convention the generated texture did carry.
        /// </summary>
        private static void BuildVesselMarker(Transform site, VesselSite vessel, float radius)
        {
            bool arterial = vessel == VesselSite.Aorta || vessel == VesselSite.PulmonaryArtery;

            GameObject cuff = new GameObject("Cuff", typeof(MeshFilter), typeof(MeshRenderer));
            cuff.transform.SetParent(site, false);
            cuff.GetComponent<MeshFilter>().sharedMesh = MakeRing(radius * 0.55f, radius, 28);

            MeshRenderer renderer = cuff.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = MakeUnlit(arterial
                ? new Color(0.85f, 0.22f, 0.20f, 0.65f)
                : new Color(0.30f, 0.42f, 0.72f, 0.65f));
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>A flat annulus on the XZ plane — the open mouth of a vessel, seen end on.</summary>
        private static Mesh MakeRing(float inner, float outer, int segments)
        {
            Vector3[] vertices = new Vector3[segments * 2];
            int[] triangles = new int[segments * 12];

            for (int i = 0; i < segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
                vertices[i * 2] = new Vector3(cos * inner, 0f, sin * inner);
                vertices[i * 2 + 1] = new Vector3(cos * outer, 0f, sin * outer);
            }

            int t = 0;
            for (int i = 0; i < segments; i++)
            {
                int n = (i + 1) % segments;
                int a = i * 2, b = i * 2 + 1, c = n * 2, d = n * 2 + 1;

                // Both windings: a cuff is seen from wherever the surgeon's head happens to be.
                triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                triangles[t++] = c; triangles[t++] = d; triangles[t++] = b;
                triangles[t++] = a; triangles[t++] = b; triangles[t++] = c;
                triangles[t++] = c; triangles[t++] = b; triangles[t++] = d;
            }

            Mesh mesh = new Mesh { name = "VesselCuff" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Material MakeUnlit(Color colour)
        {
            Material m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = colour };
            m.SetFloat("_Surface", 1f);
            m.renderQueue = 3000;
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            return m;
        }

        private static void WireProcedure(GameObject systems, GameObject sternum, GameObject heart,
            GameObject donor, VesselAnastomosis[] vessels, List<BypassSite> bypass, BypassPlan plan)
        {
            SurgeryTelemetry telemetry = systems.GetComponent<SurgeryTelemetry>();

            TransplantProcedure procedure = systems.AddComponent<TransplantProcedure>();
            procedure.SetDifficulty(plan.Difficulty);

            EventSessionDefinition definition = EventSessionDefinition.Create(
                // From the level, not from a constant. Fácil is ninety seconds because there is
                // a third less to do, not because ninety was typed somewhere — so the booth can
                // run a fast queue or a faithful operation without the two numbers drifting apart.
                round: plan.RoundSeconds, briefingTimeout: 45f, resultHold: 6f, scoreboardHold: 8f,
                startOnGrab: true, pointsPerSecond: 100f);

            EventSessionController session = systems.AddComponent<EventSessionController>();
            session.Bind(definition, null, telemetry);

            Leaderboard leaderboard = systems.AddComponent<Leaderboard>();
            leaderboard.Bind(session);

            // The seat is where the heart starts: the donor organ has to come back to it, and the
            // native one has to be carried away from it.
            GameObject seat = new GameObject("PericardialSeat");
            seat.transform.SetParent(systems.transform, true);
            seat.transform.position = heart.transform.position;

            heart.GetComponent<GrabbableOrgan>().Bind(OrganRole.Native, seat.transform, procedure);
            donor.GetComponent<GrabbableOrgan>().Bind(OrganRole.Donor, seat.transform, procedure);

            foreach (VesselAnastomosis vessel in vessels)
            {
                vessel.Bind(vessel.Site, procedure, 0.022f);
            }

            // The payoff: the donor heart starts once the operation is finished. Wired here rather
            // than inside Heartbeat so the organ knows nothing about the procedure that installed
            // it, and can be reused wherever a beating heart is wanted.
            // The beat waits for the pump, not for the last stage. Coming off bypass is what
            // hands the circulation back; declaring the operation finished is bookkeeping that
            // happens afterwards.
            Heartbeat beat = donor.GetComponent<Heartbeat>();
            procedure.Bypass.WeanedOff += () => beat.StartBeating();

            // The cannulae and the clamp are worked with the hand itself rather than a dedicated
            // instrument, so the worker lives on the systems object and asks for no grab.
            BypassWorker worker = systems.AddComponent<BypassWorker>();
            worker.Bind(null, bypass, procedure);

            Debug.Log($"[Transplante] procedimento ligado: {procedure.VesselCount} vasos, " +
                      $"rodada {definition.RoundSeconds:F0}s, assento pericárdico em {seat.transform.position}");
        }

        /// <summary>
        /// Says plainly whether the heart fits inside the cage, because the four models were
        /// generated apart and nothing guarantees they belong to the same body.
        /// </summary>
        private static void ReportFit(GameObject ribcage, GameObject heart)
        {
            Bounds cage = WorldBounds(ribcage);
            Bounds organ = WorldBounds(heart);

            bool inside = cage.Contains(organ.min) && cage.Contains(organ.max);
            float folgaX = (cage.size.x - organ.size.x) * 100f;
            float folgaY = (cage.size.y - organ.size.y) * 100f;
            float folgaZ = (cage.size.z - organ.size.z) * 100f;

            string verdict = inside
                ? $"[Transplante] AJUSTE OK: coração dentro do gradil. Folga X={folgaX:F1}cm " +
                  $"Y={folgaY:F1}cm Z={folgaZ:F1}cm"
                : $"[Transplante] AJUSTE RUIM: coração ultrapassa o gradil. Folga X={folgaX:F1}cm " +
                  $"Y={folgaY:F1}cm Z={folgaZ:F1}cm — regerar o gradil";

            if (inside) { Debug.Log(verdict); } else { Debug.LogWarning(verdict); }
        }

        // ---------------------------------------------------------------- helpers

        private static Bounds WorldBounds(GameObject go)
        {
            bool any = false;
            Bounds bounds = new Bounds();
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            {
                if (!any) { bounds = r.bounds; any = true; } else { bounds.Encapsulate(r.bounds); }
            }

            return bounds;
        }

        private static Bounds LocalBounds(GameObject go, Transform space)
        {
            bool any = false;
            Bounds bounds = new Bounds();
            foreach (MeshFilter f in go.GetComponentsInChildren<MeshFilter>())
            {
                if (f.sharedMesh == null) { continue; }
                foreach (Vector3 v in f.sharedMesh.vertices)
                {
                    Vector3 p = space.InverseTransformPoint(f.transform.TransformPoint(v));
                    if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                    else { bounds.Encapsulate(p); }
                }
            }

            return bounds;
        }

        /// <summary>
        /// Moves glTF materials onto URP/Lit. gltFast imports on its own Shader Graph, which costs
        /// a Quest more and renders black when its variant is not compiled. The metallic-roughness
        /// map is left behind on purpose: glTF packs roughness in green and metallic in blue while
        /// URP reads metallic from red and smoothness from alpha, so copying it across would look
        /// right and be wrong in every channel.
        /// </summary>
        private static void ConvertGltfMaterials(GameObject model)
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) { return; }

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

                    target.SetFloat("_Metallic", 0.05f);
                    target.SetFloat("_Smoothness", 0.35f);
                    materials[i] = target;
                }

                renderer.sharedMaterials = materials;
            }
        }

        private static Material MakeMaterial(Color colour, float metallic, float smoothness)
        {
            Material m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = colour };
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            return m;
        }

        private static GameObject Instantiate(string path, Transform parent)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) { throw new System.IO.FileNotFoundException("Modelo ausente: " + path); }
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            return instance;
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

        /// <summary>Puts the surgeon beside the chest, on the patient's left, facing the thorax.</summary>
        private static void PlaceAnchor(Vector3 thorax)
        {
            Vector3 stance = new Vector3(thorax.x + 0.45f, 0f, thorax.z);
            Vector3 toWork = new Vector3(thorax.x - stance.x, 0f, thorax.z - stance.z);
            Quaternion facing = toWork.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(toWork.normalized, Vector3.up)
                : Quaternion.identity;

            GameObject anchor = GameObject.Find("Teleport Anchor");
            if (anchor != null) { anchor.transform.SetPositionAndRotation(stance, facing); }

            GameObject rig = GameObject.Find("XR Origin");
            if (rig != null) { rig.transform.SetPositionAndRotation(stance, facing); }

            Debug.Log($"[Transplante] cirurgião em {stance}, virado para o tórax");
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
            if (info == null) { Debug.LogError($"[Transplante] sem campo '{field}'"); return; }
            info.SetValue(target, value);
        }
    }
}
