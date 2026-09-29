using System.Collections.Generic;
using UnityEngine;
using VRSurgery.Interaction;

namespace VRSurgery.Transplant
{
    /// <summary>
    /// Makes the surgical drapes cloth: they give under a hand or an instrument pressed into them
    /// and the edges hanging off the table sway, instead of hands and tools passing through a
    /// painted sheet.
    ///
    /// Unity's Cloth only collides with sphere and capsule colliders it is handed explicitly, so
    /// this collects them — a sphere on each fingertip and one on each instrument's working end —
    /// and gives them to the cloth. The fingertip spheres are solid (Cloth is not documented to
    /// see triggers), so they are told to ignore every instrument and organ: a hand must dent the
    /// drape, never shove the scalpel it is holding.
    ///
    /// The cloth is held near its draped shape by per-vertex limits the scene builder bakes in,
    /// which is what keeps it on the patient; a watchdog swaps back to the static drape if the
    /// simulation ever strays anyway, so the worst case is a still drape, not a sheet on the floor.
    /// </summary>
    public class DrapeCloth : MonoBehaviour
    {
        [SerializeField] private Cloth cloth;

        [Tooltip("The same drape as a plain mesh, shown instead if the cloth has to be switched off.")]
        [SerializeField] private Renderer staticFallback;

        [SerializeField] private List<SphereCollider> fingertips = new List<SphereCollider>();
        [SerializeField] private List<SphereCollider> toolTips = new List<SphereCollider>();

        [Tooltip("Rest positions of a sample of cloth vertices, in the cloth's local space.")]
        [SerializeField] private Vector3[] restSample = new Vector3[0];

        [SerializeField] private int[] sampleIndices = new int[0];

        [Tooltip("Farthest any sampled vertex may drift from its rest position before the cloth is " +
                 "judged broken, in metres.")]
        [SerializeField, Min(0.02f)] private float maxDrift = 0.15f;

        [Tooltip("Off on a standalone headset. Cloth runs on the CPU every frame.")]
        [SerializeField] private bool enableOnMobile = true;

        private float _watchClock;

        /// <summary>False once the cloth has been switched off, by platform or by the watchdog.</summary>
        public bool IsSimulating => cloth != null && cloth.enabled;

        private void Start()
        {
            if (cloth == null) { return; }

            if (Application.isMobilePlatform && !enableOnMobile)
            {
                FallBack("desligado neste aparelho");
                return;
            }

            List<ClothSphereColliderPair> pairs = new List<ClothSphereColliderPair>();
            foreach (SphereCollider s in fingertips) { if (s != null) { pairs.Add(new ClothSphereColliderPair(s)); } }
            foreach (SphereCollider s in toolTips) { if (s != null) { pairs.Add(new ClothSphereColliderPair(s)); } }
            cloth.sphereColliders = pairs.ToArray();

            IgnoreHandsAgainstHeldThings();
        }

        private void IgnoreHandsAgainstHeldThings()
        {
            SurgicalInteractable[] held = FindObjectsByType<SurgicalInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (SphereCollider tip in fingertips)
            {
                if (tip == null) { continue; }
                foreach (SurgicalInteractable thing in held)
                {
                    foreach (Collider c in thing.GetComponentsInChildren<Collider>(true))
                    {
                        Physics.IgnoreCollision(tip, c, true);
                    }
                }
            }
        }

        private void Update()
        {
            if (!IsSimulating) { return; }

            _watchClock += Time.deltaTime;
            if (_watchClock < 1f) { return; }
            _watchClock = 0f;

            Vector3[] vertices = cloth.vertices;
            for (int i = 0; i < sampleIndices.Length && i < restSample.Length; i++)
            {
                int v = sampleIndices[i];
                if (v < 0 || v >= vertices.Length) { continue; }
                if ((vertices[v] - restSample[i]).sqrMagnitude > maxDrift * maxDrift)
                {
                    FallBack($"vértice {v} saiu {Vector3.Distance(vertices[v], restSample[i]) * 100f:F0} cm do lugar");
                    return;
                }
            }
        }

        private void FallBack(string why)
        {
            if (cloth != null) { cloth.enabled = false; }

            SkinnedMeshRenderer skinned = cloth != null ? cloth.GetComponent<SkinnedMeshRenderer>() : null;
            if (skinned != null) { skinned.enabled = false; }
            if (staticFallback != null) { staticFallback.enabled = true; }

            Debug.LogWarning("[Campos] tecido simulado desligado (" + why + "); usando o campo estático.");
        }

        public void Bind(Cloth drape, Renderer fallback, IEnumerable<SphereCollider> hands, IEnumerable<SphereCollider> tools,
            int[] watchIndices, Vector3[] watchRest)
        {
            cloth = drape;
            staticFallback = fallback;
            fingertips = new List<SphereCollider>(hands);
            toolTips = new List<SphereCollider>(tools);
            sampleIndices = watchIndices ?? new int[0];
            restSample = watchRest ?? new Vector3[0];
        }
    }
}
