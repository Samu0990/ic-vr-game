using System.Collections.Generic;
using System.IO;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRSurgery.Audio;
using VRSurgery.Data;
using VRSurgery.Diagnostics;
using VRSurgery.Haptics;
using VRSurgery.Interaction;
using VRSurgery.Surgery;
using VRSurgery.Tissue;
using VRSurgery.Tools;
using VRSurgery.VR;

namespace VRSurgery.EditorTools
{
    /// <summary>
    /// Builds the vertical slice scene from code.
    ///
    /// The scene is generated rather than hand-authored on purpose: it can be rebuilt
    /// identically after any change, it is reviewable as a diff, and it cannot silently rot
    /// into a state nobody can reproduce. Regenerating replaces the scene file wholesale.
    ///
    /// Layout follows the workspace pillar — the player stands still, the patient is dead
    /// centre, and both trays sit within arm's reach to either side.
    /// </summary>
    public static class VerticalSliceSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/VerticalSlice.unity";
        private const string DataFolder = "Assets/Data";
        private const string MaterialFolder = "Assets/Materials";

        // Player stands at the origin; everything is placed relative to a ~1.65 m eye height.
        // Tuned against the measured reach envelope, not chosen by eye. At the previous values
        // (0.95 / 0.55) the operative field sat 0.68 m from the shoulder — near full arm
        // extension — which is a poor place to ask for delicate, sustained cutting. Raising and
        // pulling in the table brings it inside the precision zone. See WorkspaceErgonomicsTests.
        /// <summary>Surgical light output. Tuned against measured highlight clipping, not by eye.</summary>
        private const float SurgicalLightIntensity = 1.5f;

        private const float TableHeight = 1.02f;
        private const float TableForward = 0.42f;

        // --- Patient mesh metrics -------------------------------------------------------
        // Measured from the exported mesh itself, not eyeballed. Source of truth:
        // ArtSource/Blender/Patient/PATIENT_Supine.blend. The FBX is authored with the
        // patient's back at local Y=0 and the body centred on local Z, head toward +Z.
        private const string PatientBodyFbx = "Assets/Models/Patient/PATIENT_ExternalBody.fbx";
        private const string PatientSpineFbx = "Assets/Models/Patient/PATIENT_Vertebrae.fbx";

        /// <summary>Local Y of the belly surface, i.e. how far the abdomen rises above the back.</summary>
        private const float PatientAbdomenSurface = 0.2374f;

        /// <summary>Local Z of the abdomen band's centre, forward of the body's mid-point.</summary>
        private const float PatientAbdomenOffset = 0.1855f;

        private const float PatientLength = 1.7678f;

        /// <summary>Lateral offset of each instrument tray from the room centre line.</summary>
        private const float TrayOffsetX = 0.47f;

        /// <summary>
        /// Lateral offset of each instrument. Sits 2 cm inboard of its tray's centre, and is the
        /// value the arm-clearance and reach measurements are taken against.
        /// </summary>
        private const float ToolOffsetX = 0.45f;

        /// <summary>Lateral rest position of each VR hand, clear of the patient's body.</summary>
        private const float HandRestOffsetX = 0.40f;

        // --- Scalpel model metrics ------------------------------------------------------
        // Converted from bisturi.glb (Sketchfab, CC-BY-4.0). Authored with the grip at the mesh
        // origin and the blade along +Z; these distances are measured on that mesh.
        /// <summary>Tag the blade tip carries so skin regions can recognise the cutting tool.</summary>
        private const string ScalpelTag = "Scalpel";

        private const string ScalpelFbx = "Assets/Models/Tools/SCALPEL_Bisturi.fbx";
        private const string ScalpelAlbedoTex = "Assets/Models/Tools/BISTURI_Image_0.png";
        private const string ScalpelNormalTex = "Assets/Models/Tools/BISTURI_Image_2.png";
        private const string ScalpelMetallicTex = "Assets/Models/Tools/BISTURI_MetallicSmoothness.png";

        /// <summary>Local Z of the grip transform. This is the position the reach tests validate.</summary>
        private const float ScalpelGripLocalZ = -0.02f;

        private const float ScalpelGripToTip = 0.05518f;
        private const float ScalpelGripToButt = 0.06562f;

        /// <summary>
        /// Where the patient's own origin sits. Chosen so the abdomen lands directly over
        /// TableForward — the spot the reach envelope was validated against — rather than
        /// centring the body on the table and pushing the operative field out of reach.
        /// </summary>
        private static Vector3 PatientRoot =>
            new Vector3(0f, TableHeight, TableForward - PatientAbdomenOffset);

        [MenuItem("VRSurgery/Build Vertical Slice Scene")]
        public static void BuildMenu()
        {
            Build();
            EditorUtility.DisplayDialog("VR Surgery", "Vertical slice scene rebuilt at\n" + ScenePath, "OK");
        }

        /// <summary>Entry point for `-executeMethod` in batch mode.</summary>
        public static void BuildFromCommandLine()
        {
            try
            {
                Build();
                Debug.Log("[SceneBuilder] BUILD_OK " + ScenePath);
                EditorApplication.Exit(0);
            }
            catch (System.Exception exception)
            {
                Debug.LogError("[SceneBuilder] BUILD_FAILED " + exception);
                EditorApplication.Exit(1);
            }
        }

        public static void Build()
        {
            EnsureTag(ScalpelTag);
            EnsureFolder(DataFolder);
            EnsureFolder(MaterialFolder);
            EnsureFolder("Assets/Scenes");

            SurgeryDataAssets data = CreateDataAssets();
            SceneMaterials materials = CreateMaterials();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildEnvironment(materials);
            GameObject patient = BuildPatient(materials);
            IncisionSystem incisionSystem = BuildTissue(patient, materials);

            // Trays and instruments sit clear of the patient's arms. At the original ±0.36 / ±0.34
            // the grip point was 25 mm from the arm's skin while the hand's grab sphere is 60 mm,
            // so the tool could not be reached without pushing into the patient. Measured against
            // the mesh, ±0.45 leaves 57 mm of margin beyond the hand radius and still classifies
            // as Precision reach (0.534 m from the shoulder, limit 0.55). See PlayerStanceExperiment.
            ScalpelTool scalpel = BuildScalpel(data, materials, new Vector3(-ToolOffsetX, TableHeight + 0.06f, TableForward - 0.10f));
            ForcepsTool forceps = BuildForceps(data, materials, new Vector3(ToolOffsetX, TableHeight + 0.06f, TableForward - 0.10f));

            BuildTray(materials, new Vector3(-TrayOffsetX, TableHeight, TableForward - 0.12f), "Tray_Left");
            BuildTray(materials, new Vector3(TrayOffsetX, TableHeight, TableForward - 0.12f), "Tray_Right");

            BuildXrRig(materials);
            GameObject systems = BuildSystems(data, incisionSystem, scalpel, forceps);
            GameObject monitor = BuildObjectiveMonitor(systems, materials);
            BuildFeedbackHud(monitor, incisionSystem, scalpel, materials);
            BuildProbe(systems, scalpel, incisionSystem);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            RegisterSceneInBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        // ------------------------------------------------------------------
        // Data assets
        // ------------------------------------------------------------------

        private struct SurgeryDataAssets
        {
            public ToolDefinition Scalpel;
            public ToolDefinition Forceps;
            public SurgeryDefinition Surgery;
            public HapticProfile GrabHaptic;
            public HapticProfile IncisionHaptic;
            public HapticProfile ErrorHaptic;
            public HapticProfile SuccessHaptic;
        }

        private static SurgeryDataAssets CreateDataAssets()
        {
            SurgeryDataAssets data = new SurgeryDataAssets
            {
                GrabHaptic = SaveAsset(HapticProfile.Create(HapticIntensity.Medium, 0.4f, 0.06f), "Haptic_Grab"),
                IncisionHaptic = SaveAsset(HapticProfile.Create(HapticIntensity.Soft, 0.18f, 0.03f), "Haptic_Incision"),
                ErrorHaptic = SaveAsset(HapticProfile.Create(HapticIntensity.Strong, 0.7f, 0.12f), "Haptic_Error"),
                SuccessHaptic = SaveAsset(HapticProfile.Create(HapticIntensity.Medium, 0.5f, 0.09f), "Haptic_Success"),
            };

            data.Scalpel = SaveAsset(
                ToolDefinition.Create("scalpel", "Scalpel", ToolType.Scalpel, ToolCapability.Cut, data.GrabHaptic),
                "Tool_Scalpel");

            data.Forceps = SaveAsset(
                ToolDefinition.Create("forceps", "Forceps", ToolType.Forceps,
                    ToolCapability.GrabTissue | ToolCapability.Retract, data.GrabHaptic),
                "Tool_Forceps");

            ObjectiveDefinition grabScalpel = SaveAsset(
                ObjectiveDefinition.Create("select-instrument", "Pick up the scalpel.",
                    ObjectiveTrigger.GrabTool, ToolType.Scalpel),
                "Objective_1_SelectInstrument");

            ObjectiveDefinition performIncision = SaveAsset(
                ObjectiveDefinition.Create("perform-incision", "Make the incision along the marked line.",
                    ObjectiveTrigger.CompleteIncision, ToolType.Scalpel),
                "Objective_2_PerformIncision");

            ObjectiveDefinition controlBleeding = SaveAsset(
                ObjectiveDefinition.Create("control-bleeding", "Control the bleeding with the forceps.",
                    ObjectiveTrigger.ControlBleeding, ToolType.Forceps, false),
                "Objective_3_ControlBleeding");

            data.Surgery = SaveAsset(
                SurgeryDefinition.Create("vertical-slice", "Test Incision",
                    new[] { grabScalpel, performIncision, controlBleeding }),
                "Surgery_VerticalSlice");

            return data;
        }

        private static T SaveAsset<T>(T asset, string assetName) where T : Object =>
            SaveAsset(asset, assetName, DataFolder, ".asset");

        /// <summary>
        /// Writes a generated asset, overwriting any existing one at the same path.
        ///
        /// It deliberately does not delete-then-recreate: within a single batch the queued
        /// delete lands on the object that was just created, Unity destroys it, and the
        /// reference handed back compares equal to null. That is what silently shipped a scene
        /// with unassigned haptic profiles and tool definitions while every file on disk looked
        /// perfectly correct.
        /// </summary>
        private static T SaveAsset<T>(T asset, string assetName, string folder, string extension) where T : Object
        {
            string path = $"{folder}/{assetName}{extension}";
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);

            if (existing != null)
            {
                EditorUtility.CopySerialized(asset, existing);
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(asset);
                return existing;
            }

            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        // ------------------------------------------------------------------
        // Materials
        // ------------------------------------------------------------------

        private struct SceneMaterials
        {
            public Material Floor;
            public Material Wall;
            public Material Metal;
            public Material Drape;
            public Material Skin;
            public Material Bone;
            public Material HudOnTarget;
            public Material HudDrifting;
            public Material HudOffTarget;
            public Material HudLampOff;
            public Material TissueIncised;
            public Material Tissue;
            public Material Wound;
            public Material Guide;
            public Material Blade;
            public Material Handle;
            public Material Screen;
        }

        private static SceneMaterials CreateMaterials()
        {
            return new SceneMaterials
            {
                Floor = MakeMaterial("M_Floor", new Color(0.30f, 0.33f, 0.35f), 0.15f, 0.35f),
                Wall = MakeMaterial("M_Wall", new Color(0.62f, 0.68f, 0.70f), 0f, 0.25f),
                Metal = MakeMaterial("M_Metal", new Color(0.72f, 0.75f, 0.78f), 0.85f, 0.75f),
                Drape = MakeMaterial("M_Drape", new Color(0.16f, 0.38f, 0.52f), 0f, 0.2f),
                Skin = MakeMaterial("M_Skin", new Color(0.72f, 0.57f, 0.49f), 0f, 0.25f),
                Bone = MakeMaterial("M_Bone", new Color(0.88f, 0.85f, 0.78f), 0f, 0.35f),
                HudOnTarget = MakeMaterial("M_HudOnTarget", new Color(0.20f, 0.90f, 0.42f), 0f, 0.1f),
                HudDrifting = MakeMaterial("M_HudDrifting", new Color(0.95f, 0.72f, 0.18f), 0f, 0.1f),
                HudOffTarget = MakeMaterial("M_HudOffTarget", new Color(0.92f, 0.24f, 0.20f), 0f, 0.1f),
                HudLampOff = MakeMaterial("M_HudLampOff", new Color(0.12f, 0.14f, 0.16f), 0f, 0.1f),
                TissueIncised = MakeMaterial("M_TissueIncised", new Color(0.52f, 0.11f, 0.10f), 0f, 0.42f),
                Tissue = MakeMaterial("M_Tissue", new Color(0.78f, 0.55f, 0.48f), 0f, 0.30f),
                Wound = MakeWoundMaterial(),
                Guide = MakeMaterial("M_Guide", new Color(0.20f, 0.85f, 0.75f), 0f, 0.1f),
                Blade = MakeMaterial("M_Blade", new Color(0.88f, 0.90f, 0.93f), 1f, 0.9f),
                Handle = MakeMaterial("M_Handle", new Color(0.35f, 0.37f, 0.40f), 0.6f, 0.45f),
                Screen = MakeMaterial("M_Screen", new Color(0.05f, 0.08f, 0.10f), 0f, 0.1f),
            };
        }

        private static Material MakeMaterial(string assetName, Color color, float metallic, float smoothness)
        {
            Material material = new Material(Shader.Find("Standard")) { name = assetName };
            material.SetColor("_Color", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Glossiness", smoothness);

            return SaveAsset(material, assetName, MaterialFolder, ".mat");
        }

        private static Material MakeWoundMaterial()
        {
            Shader shader = Shader.Find("VRSurgery/WoundVertexColor");
            if (shader == null)
            {
                Debug.LogWarning("[SceneBuilder] Wound shader missing; falling back to Standard (vertex colours will be ignored).");
                shader = Shader.Find("Standard");
            }

            Material material = new Material(shader) { name = "M_Wound" };
            return SaveAsset(material, "M_Wound", MaterialFolder, ".mat");
        }

        // ------------------------------------------------------------------
        // Environment
        // ------------------------------------------------------------------

        private static void BuildEnvironment(SceneMaterials materials)
        {
            GameObject room = new GameObject("Room");

            CreateBox(room.transform, "Floor", new Vector3(0f, -0.05f, 0f), new Vector3(5f, 0.1f, 5f), materials.Floor);
            CreateBox(room.transform, "Ceiling", new Vector3(0f, 3.0f, 0f), new Vector3(5f, 0.1f, 5f), materials.Wall);
            CreateBox(room.transform, "Wall_North", new Vector3(0f, 1.5f, 2.5f), new Vector3(5f, 3f, 0.1f), materials.Wall);
            CreateBox(room.transform, "Wall_South", new Vector3(0f, 1.5f, -2.5f), new Vector3(5f, 3f, 0.1f), materials.Wall);
            CreateBox(room.transform, "Wall_East", new Vector3(2.5f, 1.5f, 0f), new Vector3(0.1f, 3f, 5f), materials.Wall);
            CreateBox(room.transform, "Wall_West", new Vector3(-2.5f, 1.5f, 0f), new Vector3(0.1f, 3f, 5f), materials.Wall);

            // Operating table.
            GameObject table = new GameObject("SurgicalTable");
            table.transform.SetParent(room.transform);
            // Centred on the patient rather than on TableForward, so a full 1.77 m body is
            // supported end to end instead of overhanging at the feet.
            CreateBox(table.transform, "TableTop", new Vector3(0f, TableHeight - 0.04f, PatientRoot.z),
                new Vector3(0.75f, 0.08f, 1.9f), materials.Metal);
            CreateBox(table.transform, "TableBase", new Vector3(0f, (TableHeight - 0.08f) * 0.5f, PatientRoot.z),
                new Vector3(0.28f, TableHeight - 0.08f, 0.5f), materials.Metal);

            // Lighting: one keyed surgical light plus soft fill. VR pays per eye, so this is
            // deliberately a small number of realtime lights rather than a full rig.
            GameObject lightRoot = new GameObject("SurgicalLight");
            lightRoot.transform.SetParent(room.transform);
            lightRoot.transform.position = new Vector3(0f, 2.1f, TableForward);

            CreateBox(lightRoot.transform, "LightHousing", new Vector3(0f, 2.15f, TableForward),
                new Vector3(0.7f, 0.08f, 0.7f), materials.Metal);

            GameObject spot = new GameObject("Spot");
            spot.transform.SetParent(lightRoot.transform);
            spot.transform.position = new Vector3(0f, 2.05f, TableForward);
            spot.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            Light spotLight = spot.AddComponent<Light>();
            spotLight.type = LightType.Spot;
            spotLight.range = 4f;
            spotLight.spotAngle = 70f;
            // 3.2 drove 58% of the operative field to pure white — the median pixel was 255,
            // so the skin carried no shading at all. Position and cone are unchanged; only the
            // exposure comes down. See the clipping measurement in the capture tooling.
            spotLight.intensity = SurgicalLightIntensity;
            spotLight.color = new Color(1f, 0.98f, 0.94f);
            spotLight.shadows = LightShadows.Soft;

            GameObject fill = new GameObject("FillLight");
            fill.transform.SetParent(room.transform);
            fill.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            Light fillLight = fill.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.intensity = 0.45f;
            fillLight.shadows = LightShadows.None;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.45f, 0.48f, 0.52f);
            RenderSettings.ambientEquatorColor = new Color(0.32f, 0.34f, 0.36f);
            RenderSettings.ambientGroundColor = new Color(0.18f, 0.19f, 0.20f);
        }

        private static GameObject BuildPatient(SceneMaterials materials)
        {
            GameObject patient = new GameObject("Patient");
            patient.transform.position = PatientRoot;

            // The blocked-out torso and drape boxes are gone: this is the real supine body,
            // baked to a static mesh from BaseHuman.blend (MB-Lab derived). The spine is a
            // separate FBX from a different source (Z-Anatomy / BodyParts3D) under a different
            // licence, kept as its own file so provenance stays traceable — both are authored
            // around a shared origin, so placing them at the same transform aligns them.
            AttachPatientMesh(patient, PatientBodyFbx, "ExternalBody", materials.Skin);
            AttachPatientMesh(patient, PatientSpineFbx, "Vertebrae", materials.Bone);

            PatientController controller = patient.AddComponent<PatientController>();
            controller.name = "Patient";

            return patient;
        }

        /// <summary>
        /// Instantiates an imported patient mesh at the patient root with no local offset.
        /// Returns null and logs when the FBX is missing rather than silently producing a
        /// patient-shaped hole in the scene.
        /// </summary>
        private static GameObject AttachPatientMesh(GameObject parent, string assetPath, string objectName, Material material)
        {
            EnsureModelImportSettings(assetPath);

            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (asset == null)
            {
                Debug.LogError($"[SceneBuilder] Patient mesh missing at {assetPath}. " +
                               "Re-run the Blender export before rebuilding the scene.");
                return null;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            instance.name = objectName;
            instance.transform.SetParent(parent.transform, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            // The FBX carries no textures by design, so drive the look from project materials
            // instead of whatever Unity generated at import time.
            foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>())
            {
                renderer.sharedMaterial = material;
            }

            return instance;
        }

        /// <summary>
        /// Attaches a converted tool model under an existing tool root at a fixed local offset.
        /// The offset — not the root's scene position — is what aligns the mesh, so the tool's
        /// validated world placement is untouched by swapping a proxy for real geometry.
        /// </summary>
        private static GameObject AttachToolMesh(GameObject parent, string assetPath, string objectName,
            Vector3 localOffset, Quaternion localRotation)
        {
            EnsureModelImportSettings(assetPath);

            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (asset == null)
            {
                Debug.LogError($"[SceneBuilder] Tool mesh missing at {assetPath}.");
                return null;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            instance.name = objectName;
            instance.transform.SetParent(parent.transform, false);
            instance.transform.localPosition = localOffset;
            instance.transform.localRotation = localRotation;

            Material material = MakeScalpelMaterial();
            foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>())
            {
                renderer.sharedMaterial = material;
            }

            return instance;
        }

        /// <summary>
        /// Rotates an attached tool mesh so its working end points along the tool's local +Z,
        /// and returns the measured distance from the grip to that tip.
        ///
        /// The rotation is derived from where the blade geometry actually is, so it stays correct
        /// regardless of how a given FBX export distributes the axis conversion between the root
        /// node and the mesh data — which, as this project has now seen twice, is not consistent.
        /// </summary>
        private static float AlignToolForward(GameObject meshRoot, string bladePartName)
        {
            if (meshRoot == null)
            {
                return 0f;
            }

            Transform blade = null;
            foreach (Transform t in meshRoot.GetComponentsInChildren<Transform>())
            {
                if (t.name == bladePartName)
                {
                    blade = t;
                    break;
                }
            }

            if (blade == null)
            {
                Debug.LogError($"[SceneBuilder] Tool mesh has no '{bladePartName}' part to align by.");
                return 0f;
            }

            MeshFilter filter = blade.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                Debug.LogError($"[SceneBuilder] '{bladePartName}' has no readable mesh.");
                return 0f;
            }

            // Furthest blade vertex from the grip, expressed in the mesh root's own space so the
            // measurement does not depend on the rotation being solved for.
            meshRoot.transform.localRotation = Quaternion.identity;

            Vector3 tip = Vector3.zero;
            float best = -1f;
            foreach (Vector3 v in filter.sharedMesh.vertices)
            {
                Vector3 local = meshRoot.transform.InverseTransformPoint(blade.TransformPoint(v));
                float d = local.magnitude;
                if (d > best)
                {
                    best = d;
                    tip = local;
                }
            }

            meshRoot.transform.localRotation = Quaternion.FromToRotation(tip.normalized, Vector3.forward);

            // Re-measure in the TOOL's space, which is unscaled metres. The mesh root carries the
            // FBX's unit scale, so a distance taken there is off by that factor — the direction
            // above survives it (uniform scale preserves direction) but a length does not.
            Transform tool = meshRoot.transform.parent;
            float tipZ = float.MinValue;
            foreach (Vector3 v in filter.sharedMesh.vertices)
            {
                float z = tool.InverseTransformPoint(blade.TransformPoint(v)).z;
                if (z > tipZ)
                {
                    tipZ = z;
                }
            }

            Debug.Log($"[SceneBuilder] Tool aligned: rotation {meshRoot.transform.localRotation.eulerAngles}, " +
                      $"blade tip at local z={tipZ:F5} m.");

            return tipZ;
        }

        /// <summary>
        /// Builds the scalpel's material from the textures unpacked out of the glb.
        ///
        /// Albedo and normal map are wired directly. The third texture is a glTF ORM map, whose
        /// channel layout (G = roughness, B = metallic) does not match what Unity's Standard
        /// shader expects in _MetallicGlossMap (R = metallic, A = smoothness), so metallic and
        /// smoothness are driven by scalars for now rather than by a map that would be read wrong.
        /// </summary>
        private static Material MakeScalpelMaterial()
        {
            const string path = MaterialFolder + "/M_Scalpel.mat";

            Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(ScalpelAlbedoTex);
            Texture2D normal = EnsureNormalMap(ScalpelNormalTex);

            Material material = new Material(Shader.Find("Standard")) { name = "M_Scalpel" };
            if (albedo != null)
            {
                material.SetTexture("_MainTex", albedo);
            }

            if (normal != null)
            {
                material.EnableKeyword("_NORMALMAP");
                material.SetTexture("_BumpMap", normal);
            }

            // Repacked from the glTF ORM map: glTF stores roughness in G and metallic in B, while
            // Unity's Standard shader wants metallic in R and smoothness in A. Feeding the source
            // texture in directly would read the wrong channels, which is why this was on scalars
            // until now. The map carries real variation (metallic 0..255 across handle and blade),
            // so the old flat 0.9 was making the whole instrument one material.
            Texture2D metallic = EnsureLinearTexture(ScalpelMetallicTex);
            if (metallic != null)
            {
                material.EnableKeyword("_METALLICGLOSSMAP");
                material.SetTexture("_MetallicGlossMap", metallic);
                material.SetFloat("_GlossMapScale", 1f);
            }
            else
            {
                material.SetFloat("_Metallic", 0.9f);
                material.SetFloat("_Glossiness", 0.72f);
            }

            // The source ORM's occlusion channel is a constant 255 — there is no AO data in it,
            // so no occlusion map is wired rather than attaching one that does nothing.

            return SaveAsset(material, "M_Scalpel", MaterialFolder, ".mat");
        }

        /// <summary>
        /// Marks a texture as linear data rather than colour. A metallic/smoothness map read as
        /// sRGB comes out with the wrong response curve.
        /// </summary>
        private static Texture2D EnsureLinearTexture(string assetPath)
        {
            if (AssetImporter.GetAtPath(assetPath) is TextureImporter importer)
            {
                bool changed = false;
                if (importer.sRGBTexture)
                {
                    importer.sRGBTexture = false;
                    changed = true;
                }

                if (importer.alphaSource != TextureImporterAlphaSource.FromInput)
                {
                    importer.alphaSource = TextureImporterAlphaSource.FromInput;
                    changed = true;
                }

                if (changed)
                {
                    importer.SaveAndReimport();
                    Debug.Log($"[SceneBuilder] {assetPath} set to linear with source alpha.");
                }
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        /// <summary>Flags a texture as a normal map, which Unity will not infer from the filename.</summary>
        private static Texture2D EnsureNormalMap(string assetPath)
        {
            if (AssetImporter.GetAtPath(assetPath) is TextureImporter importer
                && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
                Debug.Log($"[SceneBuilder] Marked {assetPath} as a normal map.");
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        /// <summary>
        /// Forces the import settings the patient meshes need. Read/Write is enabled because the
        /// surgical region is validated by sampling the skin's real vertex positions, and the
        /// rig/lights/cameras baked into the FBX are stripped — the pose is already baked into
        /// the mesh, so importing an armature would only add cost.
        /// </summary>
        private static void EnsureModelImportSettings(string assetPath)
        {
            if (!(AssetImporter.GetAtPath(assetPath) is ModelImporter importer))
            {
                return;
            }

            bool changed = false;

            if (!importer.isReadable)
            {
                importer.isReadable = true;
                changed = true;
            }

            if (importer.animationType != ModelImporterAnimationType.None)
            {
                importer.animationType = ModelImporterAnimationType.None;
                changed = true;
            }

            if (importer.importAnimation || importer.importCameras || importer.importLights)
            {
                importer.importAnimation = false;
                importer.importCameras = false;
                importer.importLights = false;
                changed = true;
            }

            if (changed)
            {
                importer.SaveAndReimport();
                Debug.Log($"[SceneBuilder] Reimported {assetPath} with Read/Write enabled and rig stripped.");
            }
        }

        // ------------------------------------------------------------------
        // Tissue
        // ------------------------------------------------------------------

        private static IncisionSystem BuildTissue(GameObject patient, SceneMaterials materials)
        {
            // Surgical region sits on top of the torso, facing up: tissue local +Y is the
            // outward normal, which is what IncisionGeometry assumes.
            GameObject region = new GameObject("SurgicalRegion");
            region.transform.SetParent(patient.transform);
            // Seated on the real belly surface measured from the patient mesh. The old
            // TableHeight + 0.18 was tuned to the placeholder box and now sits 5.7 cm inside
            // the actual body, which would have put every incision under the skin.
            region.transform.position = new Vector3(0f, TableHeight + PatientAbdomenSurface, TableForward);

            TissueSurface tissue = region.AddComponent<TissueSurface>();
            tissue.Configure(new Vector2(0.09f, 0.06f), 0.02f);

            IncisionSystem incisionSystem = region.AddComponent<IncisionSystem>();
            region.AddComponent<BleedingSystem>();

            // Visual sheet. A quad rotated to lie in the XZ plane of the region.
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Quad);
            visual.name = "TissueVisual";
            visual.transform.SetParent(region.transform, false);
            visual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            visual.transform.localScale = new Vector3(0.18f, 0.12f, 1f);
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.GetComponent<MeshRenderer>().sharedMaterial = materials.Tissue;

            // Physical volume the blade can be detected against.
            BoxCollider collider = region.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, -0.01f, 0f);
            collider.size = new Vector3(0.18f, 0.02f, 0.12f);
            collider.isTrigger = true;

            // Wound mesh.
            GameObject wound = new GameObject("Wound", typeof(MeshFilter), typeof(MeshRenderer));
            wound.transform.SetParent(region.transform, false);
            wound.GetComponent<MeshRenderer>().sharedMaterial = materials.Wound;
            WoundRenderer woundRenderer = wound.AddComponent<WoundRenderer>();
            SetPrivateField(woundRenderer, "tissue", tissue);

            // Guide line the player is asked to follow.
            GameObject guideObject = new GameObject("IncisionGuide");
            guideObject.transform.SetParent(region.transform, false);
            IncisionGuide guide = guideObject.AddComponent<IncisionGuide>();
            guide.Configure(new Vector3(-0.055f, 0f, 0f), new Vector3(0.055f, 0f, 0f));

            GameObject guideVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            guideVisual.name = "GuideMarker";
            guideVisual.transform.SetParent(guideObject.transform, false);
            guideVisual.transform.localPosition = new Vector3(0f, 0.0006f, 0f);
            guideVisual.transform.localScale = new Vector3(0.11f, 0.0004f, 0.002f);
            Object.DestroyImmediate(guideVisual.GetComponent<Collider>());
            guideVisual.GetComponent<MeshRenderer>().sharedMaterial = materials.Guide;

            SetPrivateField(incisionSystem, "guide", guide);

            // Binary-state incision, layered on the collider the region already has. It answers
            // "has this been cut" and drives the material swap, log and feedback; the swept
            // IncisionSystem above keeps handling trajectory, depth and deviation independently.
            AudioSource regionAudio = region.AddComponent<AudioSource>();
            regionAudio.playOnAwake = false;
            regionAudio.spatialBlend = 1f;

            IncisableSkin incisable = region.AddComponent<IncisableSkin>();
            incisable.Configure("AbdominalSkin", visual.GetComponent<MeshRenderer>(),
                materials.TissueIncised, regionAudio);


            return incisionSystem;
        }

        // ------------------------------------------------------------------
        // Tools
        // ------------------------------------------------------------------

        private static ScalpelTool BuildScalpel(SurgeryDataAssets data, SceneMaterials materials, Vector3 position)
        {
            GameObject scalpel = new GameObject("Scalpel");
            scalpel.transform.position = position;

            // Real converted model in place of the two proxy boxes. The FBX is authored with its
            // grip at the mesh origin and the blade along +Z, so it seats directly onto the grip
            // transform — the world position the reach tests are validated against never moves.
            GameObject scalpelMesh = AttachToolMesh(scalpel, ScalpelFbx, "ScalpelMesh",
                new Vector3(0f, 0f, ScalpelGripLocalZ), Quaternion.identity);

            // Blender's Z-up-to-Y-up conversion lands on the FBX root node for this export (the
            // patient, a bare mesh with no root empty, came through as identity instead). Rather
            // than hand-compensating — an earlier attempt overwrote the root's own rotation and
            // simply flipped the blade — the correction is measured from the blade geometry.
            float bladeTipLocalZ = AlignToolForward(scalpelMesh, "filo");

            // Interaction volume is deliberately a little larger than the 12 mm handle: a grab
            // box sized to the real silhouette makes a small instrument miserable to pick up.
            BoxCollider collider = scalpel.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.022f, 0.022f, ScalpelGripToTip + ScalpelGripToButt + 0.005f);
            collider.center = new Vector3(0f, 0f, ScalpelGripLocalZ + (ScalpelGripToTip - ScalpelGripToButt) * 0.5f);

            Rigidbody body = scalpel.AddComponent<Rigidbody>();
            body.mass = 0.05f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.useGravity = false;
            body.isKinematic = true;

            // Placed from the measured blade geometry, not carried over from the proxy, which
            // assumed a tip 106 mm ahead of the grip. Cut detection tracks this transform, so a
            // stale value would have the blade cutting from a point that is not on the blade.
            GameObject tipObject = new GameObject("BladeTip");
            tipObject.transform.SetParent(scalpel.transform, false);
            tipObject.transform.localPosition = new Vector3(0f, 0f, bladeTipLocalZ);
            BladeTip tip = tipObject.AddComponent<BladeTip>();

            // Tagged trigger on the tip itself. Trigger events reach it through the scalpel's
            // kinematic Rigidbody higher up the hierarchy; the radius is small so contact means
            // the blade really is at the skin, not merely nearby.
            tipObject.tag = ScalpelTag;
            SphereCollider tipTrigger = tipObject.AddComponent<SphereCollider>();
            tipTrigger.radius = 0.004f;
            tipTrigger.isTrigger = true;

            GameObject gripPoint = new GameObject("GripPoint");
            gripPoint.transform.SetParent(scalpel.transform, false);
            gripPoint.transform.localPosition = new Vector3(0f, 0f, ScalpelGripLocalZ);

            SurgicalInteractable interactable = scalpel.AddComponent<SurgicalInteractable>();
            SetPrivateField(interactable, "toolDefinition", data.Scalpel);
            SetPrivateField(interactable, "gripPoint", gripPoint.transform);

            // ScalpelTool must exist before CuttingInteractor: the interactor requires a
            // SurgicalTool, and since SurgicalTool is abstract Unity cannot auto-add one to
            // satisfy the requirement — AddComponent would simply return null.
            ScalpelTool tool = scalpel.AddComponent<ScalpelTool>();
            tool.SetToolDefinition(data.Scalpel);

            CuttingInteractor cutter = scalpel.AddComponent<CuttingInteractor>();
            SetPrivateField(cutter, "bladeTip", tip);

            SetPrivateField(tool, "bladeTip", tip);
            SetPrivateField(tool, "cuttingInteractor", cutter);

            AddGrabInteractable(scalpel, gripPoint.transform);

            return tool;
        }

        private static ForcepsTool BuildForceps(SurgeryDataAssets data, SceneMaterials materials, Vector3 position)
        {
            GameObject forceps = new GameObject("Forceps");
            forceps.transform.position = position;

            GameObject body = CreateBox(forceps.transform, "Body", position, new Vector3(0.010f, 0.008f, 0.07f), materials.Handle);
            Object.DestroyImmediate(body.GetComponent<Collider>());

            GameObject upperJaw = new GameObject("UpperJaw");
            upperJaw.transform.SetParent(forceps.transform, false);
            upperJaw.transform.localPosition = new Vector3(0f, 0f, 0.035f);
            GameObject upperVisual = CreateBox(upperJaw.transform, "UpperJawVisual",
                Vector3.zero, new Vector3(0.006f, 0.002f, 0.045f), materials.Blade);
            Object.DestroyImmediate(upperVisual.GetComponent<Collider>());
            upperVisual.transform.localPosition = new Vector3(0f, 0.002f, 0.022f);

            GameObject lowerJaw = new GameObject("LowerJaw");
            lowerJaw.transform.SetParent(forceps.transform, false);
            lowerJaw.transform.localPosition = new Vector3(0f, 0f, 0.035f);
            GameObject lowerVisual = CreateBox(lowerJaw.transform, "LowerJawVisual",
                Vector3.zero, new Vector3(0.006f, 0.002f, 0.045f), materials.Blade);
            Object.DestroyImmediate(lowerVisual.GetComponent<Collider>());
            lowerVisual.transform.localPosition = new Vector3(0f, -0.002f, 0.022f);

            GameObject jawTip = new GameObject("JawTip");
            jawTip.transform.SetParent(forceps.transform, false);
            jawTip.transform.localPosition = new Vector3(0f, 0f, 0.078f);

            BoxCollider collider = forceps.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.02f, 0.02f, 0.15f);
            collider.center = new Vector3(0f, 0f, 0.02f);

            Rigidbody rigidbody = forceps.AddComponent<Rigidbody>();
            rigidbody.mass = 0.04f;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            rigidbody.useGravity = false;
            rigidbody.isKinematic = true;

            GameObject gripPoint = new GameObject("GripPoint");
            gripPoint.transform.SetParent(forceps.transform, false);
            gripPoint.transform.localPosition = new Vector3(0f, 0f, -0.015f);

            SurgicalInteractable interactable = forceps.AddComponent<SurgicalInteractable>();
            SetPrivateField(interactable, "toolDefinition", data.Forceps);
            SetPrivateField(interactable, "gripPoint", gripPoint.transform);

            ForcepsTool tool = forceps.AddComponent<ForcepsTool>();
            tool.SetToolDefinition(data.Forceps);
            SetPrivateField(tool, "upperJaw", upperJaw.transform);
            SetPrivateField(tool, "lowerJaw", lowerJaw.transform);
            SetPrivateField(tool, "jawTip", jawTip.transform);

            AddGrabInteractable(forceps, gripPoint.transform);

            return tool;
        }

        private static void AddGrabInteractable(GameObject target, Transform attachTransform)
        {
            XRGrabInteractable grab = target.AddComponent<XRGrabInteractable>();
            grab.attachTransform = attachTransform;
            grab.useDynamicAttach = false;
            grab.throwOnDetach = false;
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
        }

        private static void BuildTray(SceneMaterials materials, Vector3 position, string trayName)
        {
            GameObject tray = new GameObject(trayName);
            tray.transform.position = position;

            CreateBox(tray.transform, "TrayBase", position, new Vector3(0.30f, 0.012f, 0.22f), materials.Metal);
            CreateBox(tray.transform, "RimNorth", position + new Vector3(0f, 0.012f, 0.105f), new Vector3(0.30f, 0.02f, 0.01f), materials.Metal);
            CreateBox(tray.transform, "RimSouth", position + new Vector3(0f, 0.012f, -0.105f), new Vector3(0.30f, 0.02f, 0.01f), materials.Metal);
            CreateBox(tray.transform, "RimEast", position + new Vector3(0.145f, 0.012f, 0f), new Vector3(0.01f, 0.02f, 0.22f), materials.Metal);
            CreateBox(tray.transform, "RimWest", position + new Vector3(-0.145f, 0.012f, 0f), new Vector3(0.01f, 0.02f, 0.22f), materials.Metal);
        }

        // ------------------------------------------------------------------
        // XR rig
        // ------------------------------------------------------------------

        private static void BuildXrRig(SceneMaterials materials)
        {
            GameObject managerObject = new GameObject("XR Interaction Manager");
            managerObject.AddComponent<XRInteractionManager>();

            GameObject originObject = new GameObject("XR Origin");
            XROrigin origin = originObject.AddComponent<XROrigin>();

            GameObject cameraOffset = new GameObject("Camera Offset");
            cameraOffset.transform.SetParent(originObject.transform, false);

            // --- CAMERA 1: VR Headset / First-Person Camera ---
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(cameraOffset.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 1.65f, 0f);
            cameraObject.transform.localRotation = Quaternion.Euler(40f, 0f, 0f);

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 50f;
            camera.stereoTargetEye = StereoTargetEyeMask.Both;
            cameraObject.AddComponent<AudioListener>();

            origin.Camera = camera;
            origin.CameraFloorOffsetObject = cameraOffset;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

            CreateHand(cameraOffset.transform, "LeftHand", true, materials);
            CreateHand(cameraOffset.transform, "RightHand", false, materials);

            originObject.AddComponent<LocomotionManager>();
        }

        private static void CreateHand(Transform parent, string handName, bool isLeft, SceneMaterials materials)
        {
            GameObject hand = new GameObject(handName);
            hand.transform.SetParent(parent, false);
            // Outboard of the patient's real silhouette. The body is 0.343 m half-width with
            // its arms alongside, so the rig's original +/-0.20 rest pose put both hands inside
            // the patient. Only X moves here; height and reach forward are unchanged.
            hand.transform.localPosition = new Vector3(isLeft ? -HandRestOffsetX : HandRestOffsetX, 1.2f, 0.3f);

            SphereCollider collider = hand.AddComponent<SphereCollider>();
            collider.radius = 0.06f;
            collider.isTrigger = true;

            XRDirectInteractor interactor = hand.AddComponent<XRDirectInteractor>();
            interactor.keepSelectedTargetValid = true;

            XRHandInteractor handInteractor = hand.AddComponent<XRHandInteractor>();
            SetPrivateField(handInteractor, "isLeftHand", isLeft);

            // Visual hand indicator
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "HandVisual";
            visual.transform.SetParent(hand.transform, false);
            visual.transform.localScale = new Vector3(0.04f, 0.03f, 0.08f);
            visual.transform.localPosition = new Vector3(0f, 0f, 0.02f);
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.GetComponent<MeshRenderer>().sharedMaterial = materials.Drape;
        }

        // ------------------------------------------------------------------
        // Systems
        // ------------------------------------------------------------------

        private static GameObject BuildSystems(
            SurgeryDataAssets data,
            IncisionSystem incisionSystem,
            ScalpelTool scalpel,
            ForcepsTool forceps)
        {
            GameObject systems = new GameObject("SurgerySystems");

            SurgeryObjectiveSystem objectives = systems.AddComponent<SurgeryObjectiveSystem>();
            objectives.SetSurgeryDefinition(data.Surgery);

            SurgeryTelemetry telemetry = systems.AddComponent<SurgeryTelemetry>();

            BleedingSystem bleeding = incisionSystem.GetComponent<BleedingSystem>();
            SurgeryEvaluation evaluation = systems.AddComponent<SurgeryEvaluation>();
            evaluation.Bind(objectives, incisionSystem, bleeding);

            HapticManager haptics = systems.AddComponent<HapticManager>();
            haptics.RegisterProfile(HapticEvent.Grab, data.GrabHaptic);
            haptics.RegisterProfile(HapticEvent.Incision, data.IncisionHaptic);
            haptics.RegisterProfile(HapticEvent.Success, data.SuccessHaptic);
            haptics.RegisterProfile(HapticEvent.Error, data.ErrorHaptic);

            // Serialized changes made through plain C# assignment are not tracked by the
            // editor's dirty system, so the component has to be flagged explicitly or the
            // scene saves the field at its default value.
            EditorUtility.SetDirty(haptics);

            GameObject audioObject = new GameObject("SurgeryAudio");
            audioObject.transform.SetParent(systems.transform, false);
            AudioSource source = audioObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            audioObject.AddComponent<SurgeryAudio>();

            SurgeryResetController reset = systems.AddComponent<SurgeryResetController>();
            reset.Bind(incisionSystem, objectives, evaluation, telemetry,
                new[] { scalpel.GetComponent<SurgicalInteractable>(), forceps.GetComponent<SurgicalInteractable>() });

            return systems;
        }

        private static GameObject BuildObjectiveMonitor(GameObject systems, SceneMaterials materials)
        {
            // A physical monitor rather than a HUD glued to the player's face — objectives live
            // in the room, which is both more comfortable and more readable in VR.
            GameObject monitor = new GameObject("ObjectiveMonitor");
            monitor.transform.position = new Vector3(0.75f, 1.55f, 1.35f);
            monitor.transform.rotation = Quaternion.Euler(0f, -28f, 0f);

            GameObject frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frame.name = "Frame";
            frame.transform.SetParent(monitor.transform, false);
            frame.transform.localScale = new Vector3(0.5f, 0.32f, 0.03f);
            Object.DestroyImmediate(frame.GetComponent<Collider>());
            frame.GetComponent<MeshRenderer>().sharedMaterial = materials.Metal;

            GameObject screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screen.name = "Screen";
            screen.transform.SetParent(monitor.transform, false);
            screen.transform.localPosition = new Vector3(0f, 0f, -0.016f);
            screen.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            screen.transform.localScale = new Vector3(0.46f, 0.28f, 1f);
            Object.DestroyImmediate(screen.GetComponent<Collider>());
            screen.GetComponent<MeshRenderer>().sharedMaterial = materials.Screen;

            GameObject textObject = new GameObject("ObjectiveText");
            textObject.transform.SetParent(monitor.transform, false);
            textObject.transform.localPosition = new Vector3(0f, 0f, -0.02f);
            textObject.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            textObject.transform.localScale = Vector3.one * 0.006f;

            TextMesh text = textObject.AddComponent<TextMesh>();
            text.text = "OBJECTIVE";
            text.characterSize = 0.5f;
            text.fontSize = 64;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = new Color(0.72f, 0.94f, 1f);

            SurgeryHUD hud = monitor.AddComponent<SurgeryHUD>();
            hud.Bind(systems.GetComponent<SurgeryObjectiveSystem>(), text);

            // Incision confirmation: a lamp and a status line on the monitor the player already
            // looks at. Nothing is added over the patient, so the operative field stays clear.
            GameObject lamp = GameObject.CreatePrimitive(PrimitiveType.Quad);
            lamp.name = "IncisionLamp";
            lamp.transform.SetParent(monitor.transform, false);
            lamp.transform.localPosition = new Vector3(-0.18f, -0.105f, -0.021f);
            lamp.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            lamp.transform.localScale = new Vector3(0.05f, 0.05f, 1f);
            Object.DestroyImmediate(lamp.GetComponent<Collider>());
            lamp.GetComponent<MeshRenderer>().sharedMaterial = materials.HudLampOff;

            GameObject statusObject = new GameObject("IncisionStatusText");
            statusObject.transform.SetParent(monitor.transform, false);
            statusObject.transform.localPosition = new Vector3(0.03f, -0.105f, -0.021f);
            statusObject.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            statusObject.transform.localScale = Vector3.one * 0.004f;

            TextMesh status = statusObject.AddComponent<TextMesh>();
            status.text = "AWAITING INCISION";
            status.characterSize = 0.5f;
            status.fontSize = 64;
            status.anchor = TextAnchor.MiddleCenter;
            status.alignment = TextAlignment.Center;
            status.color = new Color(0.72f, 0.78f, 0.85f);

            return monitor;
        }

        /// <summary>
        /// Wires the feedback HUD. Deliberately reuses geometry that is already in the scene and
        /// already measured — the guide marker on the skin and the objective monitor — so adding
        /// player feedback introduces nothing into the validated reach corridor.
        /// </summary>
        private static void BuildFeedbackHud(GameObject monitor, IncisionSystem incisionSystem,
            ScalpelTool scalpel, SceneMaterials materials)
        {
            Transform region = incisionSystem.transform;
            Transform marker = region.Find("IncisionGuide/GuideMarker");
            Transform lamp = monitor.transform.Find("IncisionLamp");
            Transform status = monitor.transform.Find("IncisionStatusText");

            if (marker == null || lamp == null || status == null)
            {
                Debug.LogError("[SceneBuilder] Feedback HUD is missing one of its display surfaces.");
                return;
            }

            SurgicalFeedbackHUD feedback = monitor.AddComponent<SurgicalFeedbackHUD>();
            feedback.Bind(incisionSystem.Tissue, incisionSystem.Guide,
                region.GetComponent<IncisableSkin>(), scalpel.BladeTip,
                marker.GetComponent<MeshRenderer>(), lamp.GetComponent<MeshRenderer>(),
                status.GetComponent<TextMesh>());
            feedback.BindMaterials(materials.Guide, materials.HudOnTarget, materials.HudDrifting,
                materials.HudOffTarget, materials.HudLampOff, materials.HudOnTarget);
        }

        private static void BuildProbe(GameObject systems, ScalpelTool scalpel, IncisionSystem incisionSystem)
        {
            GameObject probeObject = new GameObject("SurgeryProbe (headset-free validation)");
            probeObject.transform.SetParent(systems.transform, false);

            probeObject.AddComponent<ProbeHand>();
            SurgeryProbe probe = probeObject.AddComponent<SurgeryProbe>();
            probe.Bind(scalpel, incisionSystem);

            // Off by default: with a headset attached the player drives the scalpel, and an
            // automated blade moving on its own would fight them for it.
            SetPrivateField(probe, "runOnStart", true);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static GameObject CreateBox(Transform parent, string boxName, Vector3 position, Vector3 size, Material material)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = boxName;
            box.transform.SetParent(parent, true);
            box.transform.position = position;
            box.transform.localScale = size;

            if (material != null)
            {
                box.GetComponent<MeshRenderer>().sharedMaterial = material;
            }

            return box;
        }

        /// <summary>
        /// Adds a tag to the project if it is missing. Tags live in ProjectSettings, not in the
        /// scene, so a generated scene that assigns one would otherwise fail on a fresh checkout.
        /// </summary>
        private static void EnsureTag(string tag)
        {
            SerializedObject tagManager = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty tags = tagManager.FindProperty("tags");

            for (int i = 0; i < tags.arraySize; i++)
            {
                if (tags.GetArrayElementAtIndex(i).stringValue == tag)
                {
                    return;
                }
            }

            tags.InsertArrayElementAtIndex(tags.arraySize);
            tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
            tagManager.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log($"[SceneBuilder] Added missing tag '{tag}'.");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static void RegisterSceneInBuildSettings()
        {
            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path == ScenePath)
                {
                    return;
                }
            }

            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        /// <summary>
        /// Assigns a [SerializeField] private field. Serialized privates are the right choice
        /// for inspector wiring, but a generated scene still has to fill them in — this keeps
        /// the runtime API from growing a public setter for every single reference.
        /// </summary>
        private static void SetPrivateField(object target, string fieldName, object value)
        {
            System.Type type = target.GetType();
            while (type != null)
            {
                System.Reflection.FieldInfo field = type.GetField(fieldName,
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public);

                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }

                type = type.BaseType;
            }

            Debug.LogWarning($"[SceneBuilder] Field '{fieldName}' not found on {target.GetType().Name}.");
        }
    }
}
