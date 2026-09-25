using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using VRSurgery.Data;
using VRSurgery.Interaction;
using VRSurgery.Tools;
using VRSurgery.Transplant;

namespace VRSurgery.EditorTools
{
    /// <summary>
    /// Two setup jobs that are fiddly by hand and easy to get subtly wrong: the nitrile glove
    /// material for the tracked hands, and dressing the heart with the project's grab contract.
    /// </summary>
    public static class SurgicalGloveMaterial
    {
        private const string MaterialPath = "Assets/Materials/GLOVE_NitrileBlue.mat";

        /// <summary>
        /// Nitrile blue, measured off the reference hands rather than picked by eye.
        ///
        /// Their packed ORM map reads occlusion 1.0, roughness 0.46, metallic 0 — so smoothness
        /// lands near 0.54, which is the slight sheen of a latex surface rather than the wet look
        /// of plastic. The albedo they carry averages a pale #A6D3EB, closer to a surgical drape
        /// than to a glove, so this sits a few steps darker and more saturated. It is a serialized
        /// asset, so anything here is yours to push around in the Inspector.
        /// </summary>
        private static readonly Color NitrileBlue = new Color32(0x3E, 0x7C, 0xA8, 0xFF);

        [MenuItem("VRSurgery/Criar material de luva cirúrgica")]
        public static Material CreateGloveMaterial()
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
            {
                Debug.LogError("[Glove] URP/Lit não encontrado. O projeto está em URP?");
                return null;
            }

            Material glove = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            bool isNew = glove == null;
            if (isNew)
            {
                glove = new Material(lit);
            }

            glove.SetColor("_BaseColor", NitrileBlue);
            glove.SetFloat("_Metallic", 0f);
            glove.SetFloat("_Smoothness", 0.54f);

            // Gloves are dielectric and thin. A little specular is what makes them read as rubber
            // under a surgical light; any more and they look like painted plastic.
            glove.SetFloat("_SpecularHighlights", 1f);
            glove.SetFloat("_EnvironmentReflections", 1f);

            if (isNew)
            {
                System.IO.Directory.CreateDirectory("Assets/Materials");
                AssetDatabase.CreateAsset(glove, MaterialPath);
            }

            EditorUtility.SetDirty(glove);
            AssetDatabase.SaveAssets();

            Debug.Log($"[Glove] {MaterialPath} — URP/Lit, azul {ColorUtility.ToHtmlStringRGB(NitrileBlue)}, " +
                      "metallic 0, smoothness 0.54. Aplique no renderer da mão do XR Hands.");
            return glove;
        }

        /// <summary>
        /// Dresses a heart with the same grab contract the instruments use.
        ///
        /// Deliberately no SurgicalTool: that base class republishes grabs onto
        /// SurgeryEvents.OnToolGrabbed, which starts the booth's round and satisfies "pick up the
        /// instrument" objectives. Touching an organ must not do either.
        /// </summary>
        [MenuItem("GameObject/VRSurgery/Tornar coração agarrável", false, 10)]
        public static void MakeHeartGrabbable()
        {
            GameObject heart = Selection.activeGameObject;
            if (heart == null)
            {
                EditorUtility.DisplayDialog("Coração agarrável",
                    "Selecione o objeto do coração na Hierarchy primeiro.", "OK");
                return;
            }

            // Collider from the mesh's own bounds, in local space, so moving or rotating the heart
            // in the Editor cannot invalidate it. A convex MeshCollider would be truer to the
            // shape, but a 60k organ as a convex hull costs far more than a stand needs.
            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool any = false;
            foreach (MeshFilter filter in heart.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) { continue; }
                Bounds local = filter.sharedMesh.bounds;
                if (!any) { bounds = local; any = true; } else { bounds.Encapsulate(local); }
            }

            if (!any)
            {
                Debug.LogWarning("[Heart] Nenhuma malha encontrada; o collider foi dimensionado no escuro.");
                bounds = new Bounds(Vector3.zero, new Vector3(0.10f, 0.09f, 0.15f));
            }

            BoxCollider box = heart.GetComponent<BoxCollider>();
            if (box == null) { box = heart.AddComponent<BoxCollider>(); }
            box.center = bounds.center;
            box.size = bounds.size;

            Rigidbody body = heart.GetComponent<Rigidbody>();
            if (body == null) { body = heart.AddComponent<Rigidbody>(); }
            body.mass = 0.3f;                 // um coração adulto pesa ~300 g
            body.useGravity = false;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            // Held in the palm rather than by a modelled handle: an organ has no grip, so the
            // attach point is its own centre and the hand closes around it.
            Transform grip = heart.transform.Find("GripPoint");
            if (grip == null)
            {
                GameObject gripObject = new GameObject("GripPoint");
                gripObject.transform.SetParent(heart.transform, false);
                gripObject.transform.localPosition = bounds.center;
                grip = gripObject.transform;
            }

            SurgicalInteractable interactable = heart.GetComponent<SurgicalInteractable>();
            if (interactable == null) { interactable = heart.AddComponent<SurgicalInteractable>(); }

            ToolDefinition definition = ToolDefinition.Create(
                "heart", "Coração", ToolType.Retractor, ToolCapability.None);
            SetPrivateField(interactable, "toolDefinition", definition);
            SetPrivateField(interactable, "gripPoint", grip);

            XRGrabInteractable grab = heart.GetComponent<XRGrabInteractable>();
            if (grab == null) { grab = heart.AddComponent<XRGrabInteractable>(); }
            grab.attachTransform = grip;
            grab.useDynamicAttach = false;
            grab.throwOnDetach = false;
            grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;

            if (heart.GetComponent<ToolReleasePhysics>() == null)
            {
                heart.AddComponent<ToolReleasePhysics>();
            }

            if (heart.GetComponent<GrabbableOrgan>() == null)
            {
                heart.AddComponent<GrabbableOrgan>();
            }

            EditorUtility.SetDirty(heart);
            Debug.Log($"[Heart] '{heart.name}' agarrável: collider {bounds.size.x * 100f:F1}x" +
                      $"{bounds.size.y * 100f:F1}x{bounds.size.z * 100f:F1} cm, 300 g, " +
                      "SurgicalInteractable + XRGrabInteractable + ToolReleasePhysics + GrabbableOrgan. " +
                      "Sem SurgicalTool, de propósito: pegar um órgão não pode iniciar a rodada.");
        }

        private static void SetPrivateField(object target, string field, object value)
        {
            System.Reflection.FieldInfo info = target.GetType().GetField(field,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (info == null)
            {
                Debug.LogError($"[Setup] {target.GetType().Name} não tem o campo '{field}'.");
                return;
            }

            info.SetValue(target, value);
        }
    }
}
