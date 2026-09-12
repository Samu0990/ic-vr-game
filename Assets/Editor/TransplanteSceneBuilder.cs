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
        /// Where the thorax sits, as a fraction of stature measured from the soles. The heart
        /// centre lands a little below the mid-thorax and to the patient's left, which is where
        /// a heart actually is.
        /// </summary>
        private const float ThoraxCentreOfHeight = 0.735f;

        /// <summary>
        /// The ribcage is corrected non-uniformly, which is a stopgap and marked as one.
        ///
        /// Generated in isolation, it came out 20.0 x 10.8 x 32.0 cm. A real thoracic cage is
        /// roughly 28 wide, 20 deep and 30 tall, so this one is 71% of the width and 54% of the
        /// depth it should have for its height. A 8.7 cm heart inside a 10.8 cm cage would touch
        /// the ribs front and back. Scaling it uniformly until the depth works would make it 59 cm
        /// tall, so the proportions are corrected per axis instead — visibly better, still a
        /// distortion, and the honest fix is regenerating the model.
        /// </summary>
        private static readonly Vector3 RibcageProportionFix = new Vector3(1.40f, 0.94f, 1.85f);

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
            GameObject sternum = BuildSternum(patient, thorax, heart);

            WireProcedure(systems, sternum, heart);
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
                      $"{bounds.size.z * 100f:F1} cm (corrigido {RibcageProportionFix})");
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
            const float MidlineBand = 0.03f;   // 3 cm either side of centre

            int frontMidline = 0, backMidline = 0;

            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) { continue; }
                foreach (Vector3 v in filter.sharedMesh.vertices)
                {
                    if (Mathf.Abs(v.x) > MidlineBand) { continue; }
                    if (v.z >= 0f) { frontMidline++; } else { backMidline++; }
                }
            }

            bool spineAtPositiveZ = frontMidline > backMidline;

            Debug.Log($"[Transplante] coluna detectada em {(spineAtPositiveZ ? "+Z" : "-Z")} do modelo " +
                      $"(vértices na linha média: +Z={frontMidline}, -Z={backMidline})");

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

        private static GameObject BuildSternum(GameObject patient, Vector3 thorax, GameObject heart)
        {
            GameObject root = new GameObject("Sternum");
            root.transform.SetParent(patient.transform, true);

            GameObject model = Instantiate(SternumGlb, root.transform);
            model.name = "SternumModel";
            ConvertGltfMaterials(model);

            root.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            // On the front of the chest, which on a supine patient is up.
            root.transform.position = thorax + new Vector3(0f, 0.085f, 0.015f);

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

        private static void WireProcedure(GameObject systems, GameObject sternum, GameObject heart)
        {
            SurgeryTelemetry telemetry = systems.GetComponent<SurgeryTelemetry>();

            TransplantProcedure procedure = systems.AddComponent<TransplantProcedure>();

            EventSessionDefinition definition = EventSessionDefinition.Create(
                round: 90f, briefingTimeout: 45f, resultHold: 5f, scoreboardHold: 8f,
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
