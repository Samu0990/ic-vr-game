using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRSurgery.Interaction;

namespace VRSurgery.EditorTools
{
    /// <summary>
    /// The visitor's hands: gloved hands instead of controllers, grabbing from the palm, and a
    /// light-blue outline on what the hand is about to take.
    /// </summary>
    public static partial class TransplanteSceneBuilder
    {
        private const string HandModelFolder = "Assets/Samples/XR Hands/1.8.1/HandVisualizer/Models";
        private const string OutlineMaterialPath = "Assets/Materials/CONTORNO_Pega.mat";

        /// <summary>Light blue, bright enough to read against the blue drapes and the steel.</summary>
        private static readonly Color OutlineColour = new Color(0.45f, 0.85f, 1f, 1f);

        /// <summary>
        /// Puts a gloved hand on each controller and hides the controller model.
        ///
        /// The hand is Unity's own rigged hand from the XR Hands sample already in the project
        /// (the one the hand-tracking visual uses), dressed in the nitrile glove. It follows the
        /// controller's grip pose, so it sits where the visitor's hand is; the near grab and the
        /// fingertip are moved onto it (see <see cref="ControllerHand"/>), and grabbing is made
        /// near-only, as in Job Simulator: the far ray that could pull an instrument off the far
        /// side of the table is switched off, and among close instruments the one whose surface
        /// is nearest the palm is the one taken.
        /// </summary>
        private static void BuildControllerHands()
        {
            GameObject rig = GameObject.Find("XR Origin");
            if (rig == null) { return; }

            Material glove = SurgicalGloveMaterial.CreateGloveMaterial();
            int built = 0;

            foreach (string side in new[] { "Left", "Right" })
            {
                bool left = side == "Left";
                Transform controller = rig.transform.Find($"Camera Offset/{side} Controller");
                if (controller == null)
                {
                    Debug.LogWarning($"[Transplante] rig sem '{side} Controller': essa mão fica sem luva.");
                    continue;
                }

                string modelPath = HandModelPath(left);
                if (modelPath == null)
                {
                    Debug.LogWarning("[Transplante] modelo de mão do XR Hands não encontrado " +
                                     "(importe o sample 'HandVisualizer' do pacote XR Hands); ficam os controles.");
                    return;
                }

                // The controller model gives way to the hand.
                Transform visual = controller.Find($"{side} Controller Visual");
                if (visual != null) { visual.gameObject.SetActive(false); }

                GameObject root = new GameObject(left ? "MaoEsquerda" : "MaoDireita");
                root.transform.SetParent(controller.parent, false);
                root.transform.SetPositionAndRotation(controller.position, controller.rotation);

                string device = left ? "LeftHand" : "RightHand";
                TrackedPoseDriver driver = root.AddComponent<TrackedPoseDriver>();
                driver.positionInput = new InputActionProperty(new InputAction($"{side} Grip Position",
                    InputActionType.PassThrough, $"<XRController>{{{device}}}/devicePosition", expectedControlType: "Vector3"));
                driver.rotationInput = new InputActionProperty(new InputAction($"{side} Grip Rotation",
                    InputActionType.PassThrough, $"<XRController>{{{device}}}/deviceRotation", expectedControlType: "Quaternion"));
                driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
                driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;

                GameObject model = Instantiate(modelPath, root.transform);
                model.name = left ? "LuvaEsquerda" : "LuvaDireita";
                foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    if (glove != null)
                    {
                        Material[] materials = renderer.sharedMaterials;
                        for (int i = 0; i < materials.Length; i++) { materials[i] = glove; }
                        renderer.sharedMaterials = materials;
                    }

                    // A skinned hand casting shadows is a second skinning pass on a Quest for a
                    // shadow nobody looks at.
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }

                HandPoser poser = model.AddComponent<HandPoser>();
                poser.Bind(left);

                Transform nearFar = controller.Find("Near-Far Interactor");
                XRBaseInputInteractor inputs = nearFar != null ? nearFar.GetComponent<XRBaseInputInteractor>() : null;

                GameObject fist = new GameObject("PontoDePega");
                fist.transform.SetParent(controller, false);

                ControllerHand hand = root.AddComponent<ControllerHand>();
                hand.Bind(left, controller, model.transform, poser, inputs, fist.transform,
                    controller.Find("Poke Interactor/Poke Point"));
                hand.BindGripDriver(driver);
                hand.AlignModel();

                if (nearFar != null) { MakeGrabNearOnly(nearFar, fist.transform); }
                built++;
            }

            // The template's floating labels point at controller buttons that are no longer drawn.
            foreach (Transform t in rig.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("Affordance Callouts")) { t.gameObject.SetActive(false); }
            }

            Debug.Log($"[Transplante] {built} mão(s) de luva no lugar dos controles; pega só de perto, pela palma");
        }

        private static string HandModelPath(bool left)
        {
            string file = left ? "LeftHand" : "RightHand";
            string fixedPath = $"{HandModelFolder}/{file}.fbx";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(fixedPath) != null) { return fixedPath; }

            // Another XR Hands version puts the sample under another folder name.
            foreach (string guid in AssetDatabase.FindAssets(file + " t:Model"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("HandVisualizer") && path.EndsWith("/" + file + ".fbx")) { return path; }
            }

            return null;
        }

        /// <summary>
        /// Near grab from the fist, nearest surface first, and no far ray. The sphere that finds
        /// instruments is centred on the fist instead of the controller's tip, and held
        /// instruments sit there too.
        /// </summary>
        private static void MakeGrabNearOnly(Transform nearFar, Transform fist)
        {
            NearFarInteractor interactor = nearFar.GetComponent<NearFarInteractor>();
            if (interactor != null)
            {
                SerializedObject settings = new SerializedObject(interactor);
                SetSerialized(settings, "m_EnableFarCasting", p => p.boolValue = false);
                // NearCasterSortingStrategy.ClosestPointOnCollider: the instrument whose surface is
                // nearest, not the one whose grip point happens to be.
                SetSerialized(settings, "m_NearCasterSortingStrategy", p => p.intValue = 3);
                settings.ApplyModifiedPropertiesWithoutUndo();
            }

            Component caster = nearFar.GetComponent("SphereInteractionCaster");
            if (caster != null)
            {
                SerializedObject settings = new SerializedObject(caster);
                SetSerialized(settings, "m_CastOrigin", p => p.objectReferenceValue = fist);
                SetSerialized(settings, "m_CastRadius", p => p.floatValue = 0.1f);
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning("[Transplante] Near-Far sem SphereInteractionCaster; a pega continua na ponta do controle.");
            }

            Component attach = nearFar.GetComponent("InteractionAttachController");
            if (attach != null)
            {
                SerializedObject settings = new SerializedObject(attach);
                SetSerialized(settings, "m_TransformToFollow", p => p.objectReferenceValue = fist);
                settings.ApplyModifiedPropertiesWithoutUndo();
            }

            Transform line = nearFar.Find("LineVisual");
            if (line != null) { line.gameObject.SetActive(false); }
        }

        private static void SetSerialized(SerializedObject target, string property, System.Action<SerializedProperty> set)
        {
            SerializedProperty p = target.FindProperty(property);
            if (p == null)
            {
                Debug.LogWarning($"[Transplante] {target.targetObject.GetType().Name} sem '{property}' (outra versão do XRI?)");
                return;
            }

            set(p);
        }

        /// <summary>
        /// The light-blue outline on the instrument each hand would take, and the averaged
        /// normals it needs on the hard-edged instrument meshes.
        /// </summary>
        private static void WireGrabOutlines()
        {
            Shader shader = Shader.Find("VRSurgery/GrabOutline");
            if (shader == null)
            {
                Debug.LogWarning("[Transplante] shader VRSurgery/GrabOutline ausente; sem contorno de pega.");
                return;
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(OutlineMaterialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, OutlineMaterialPath);
            }

            material.shader = shader;
            material.SetColor("_Color", OutlineColour);
            material.SetFloat("_Width", 0.0025f);
            EditorUtility.SetDirty(material);

            List<XRBaseInteractor> hands = new List<XRBaseInteractor>();
            foreach (XRHandInteractor hand in RigHands())
            {
                XRBaseInteractor interactor = hand.GetComponent<XRBaseInteractor>();
                if (interactor != null) { hands.Add(interactor); }
            }

            GameObject go = new GameObject("ContornosDePega");
            go.AddComponent<GrabOutlines>().Bind(material, hands.ToArray());

            int baked = BakeOutlineNormals();
            Debug.Log($"[Transplante] contorno azul-claro no que a mão vai pegar: {hands.Count} mão(s), " +
                      $"{baked} malha(s) com normais suavizadas para o contorno");
        }

        /// <summary>
        /// Averages the normals of vertices that share a position into UV channel 3, where the
        /// outline shader looks for them, so the rim does not crack at the corners of boxes and
        /// rods. Only meshes this builder made: imported models are left as they come.
        /// </summary>
        private static int BakeOutlineNormals()
        {
            HashSet<Mesh> done = new HashSet<Mesh>();
            int baked = 0;

            foreach (XRBaseInteractable interactable in
                     Object.FindObjectsByType<XRBaseInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                foreach (MeshFilter filter in interactable.GetComponentsInChildren<MeshFilter>(true))
                {
                    Mesh mesh = filter.sharedMesh;
                    if (mesh == null || !done.Add(mesh) || !mesh.isReadable) { continue; }

                    string path = AssetDatabase.GetAssetPath(mesh);
                    if (!string.IsNullOrEmpty(path) && !path.EndsWith(".asset")) { continue; }

                    mesh.SetUVs(3, SmoothNormals(mesh));
                    baked++;
                }
            }

            return baked;
        }

        private static List<Vector3> SmoothNormals(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            List<Vector3> smooth = new List<Vector3>(vertices.Length);
            if (normals == null || normals.Length != vertices.Length)
            {
                for (int i = 0; i < vertices.Length; i++) { smooth.Add(Vector3.zero); }
                return smooth;
            }

            Dictionary<Vector3Int, Vector3> sums = new Dictionary<Vector3Int, Vector3>();
            Vector3Int[] keys = new Vector3Int[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                // A tenth of a millimetre: the corners a box duplicates to split its normals.
                Vector3 p = vertices[i] * 10000f;
                keys[i] = new Vector3Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), Mathf.RoundToInt(p.z));
                sums.TryGetValue(keys[i], out Vector3 sum);
                sums[keys[i]] = sum + normals[i];
            }

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 n = sums[keys[i]];
                smooth.Add(n.sqrMagnitude > 1e-8f ? n.normalized : normals[i]);
            }

            return smooth;
        }
    }
}
