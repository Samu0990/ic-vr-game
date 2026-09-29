using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VRSurgery.Interaction
{
    /// <summary>
    /// The light-blue rim round something that can be picked up.
    ///
    /// Each renderer of the object gets a twin, a child drawing the same mesh with the outline
    /// material (<c>VRSurgery/GrabOutline</c>): an inverted hull a couple of millimetres bigger,
    /// so only its rim shows. The twins are made the first time the outline is needed and are
    /// simply switched off otherwise, so an object nobody reaches for costs nothing.
    ///
    /// Only solid parts are outlined: glows, glass, text and particle effects are left alone,
    /// or the rim would trace a halo instead of the instrument.
    /// </summary>
    [DisallowMultipleComponent]
    public class InteractableOutline : MonoBehaviour
    {
        public const string ShellName = "Contorno";

        private readonly List<Renderer> _shells = new List<Renderer>();
        private bool _built;

        public bool IsShown { get; private set; }
        public int ShellCount => _shells.Count;

        /// <summary>Shows or hides the rim, building it with <paramref name="material"/> the first time.</summary>
        public void Show(bool on, Material material)
        {
            if (on && !_built) { Build(material); }
            if (on == IsShown) { return; }

            IsShown = on;
            for (int i = 0; i < _shells.Count; i++)
            {
                if (_shells[i] != null) { _shells[i].enabled = on; }
            }
        }

        private void Build(Material material)
        {
            _built = true;
            if (material == null) { return; }

            foreach (Renderer source in GetComponentsInChildren<Renderer>(true))
            {
                if (!Outlines(source)) { continue; }

                GameObject twin = new GameObject(ShellName) { layer = source.gameObject.layer };
                twin.transform.SetParent(source.transform, false);

                Renderer shell;
                int subMeshes;
                if (source is SkinnedMeshRenderer skinned)
                {
                    SkinnedMeshRenderer copy = twin.AddComponent<SkinnedMeshRenderer>();
                    copy.sharedMesh = skinned.sharedMesh;
                    copy.bones = skinned.bones;
                    copy.rootBone = skinned.rootBone;
                    copy.localBounds = skinned.localBounds;
                    subMeshes = skinned.sharedMesh.subMeshCount;
                    shell = copy;
                }
                else
                {
                    Mesh mesh = source.GetComponent<MeshFilter>().sharedMesh;
                    twin.AddComponent<MeshFilter>().sharedMesh = mesh;
                    shell = twin.AddComponent<MeshRenderer>();
                    subMeshes = mesh.subMeshCount;
                }

                Material[] materials = new Material[Mathf.Max(1, subMeshes)];
                for (int i = 0; i < materials.Length; i++) { materials[i] = material; }
                shell.sharedMaterials = materials;
                shell.shadowCastingMode = ShadowCastingMode.Off;
                shell.receiveShadows = false;
                shell.lightProbeUsage = LightProbeUsage.Off;
                shell.reflectionProbeUsage = ReflectionProbeUsage.Off;
                shell.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                shell.enabled = false;
                _shells.Add(shell);
            }
        }

        /// <summary>Solid, visible mesh parts only.</summary>
        private static bool Outlines(Renderer source)
        {
            if (source == null || !source.enabled || source.name == ShellName) { return false; }
            if (source.isPartOfStaticBatch) { return false; }
            if (source.GetComponent<TextMesh>() != null) { return false; }

            if (source is SkinnedMeshRenderer skinned)
            {
                if (skinned.sharedMesh == null) { return false; }
            }
            else if (source is MeshRenderer)
            {
                MeshFilter filter = source.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) { return false; }
            }
            else
            {
                return false;
            }

            foreach (Material m in source.sharedMaterials)
            {
                // Transparent and additive materials are glows and glass, not the object.
                if (m != null && m.renderQueue > (int)RenderQueue.GeometryLast) { return false; }
            }

            return true;
        }
    }
}
