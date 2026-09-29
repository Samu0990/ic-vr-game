using UnityEngine;

namespace VRSurgery.Transplant
{
    /// <summary>
    /// The Finochietto retractor: two blades in the wound, a toothed bar across it, a crank.
    /// The single most recognisable object in open-heart surgery, and the reason an opened chest
    /// stays open.
    ///
    /// It follows the sternotomy rather than owning a state of its own: it appears as the bone
    /// parts, its blades spread with the opening, and it comes out as the sternum closes. The
    /// crank turns as the blades travel, which is how the real one is worked.
    /// </summary>
    public class SternalRetractor : MonoBehaviour
    {
        [SerializeField] private SternotomyController sternotomy;

        [SerializeField] private Transform leftBlade;
        [SerializeField] private Transform rightBlade;
        [SerializeField] private Transform crank;

        [Tooltip("Blade offset from the midline, in metres, closed and fully open.")]
        [SerializeField] private float closedSpread = 0.015f;
        [SerializeField] private float openSpread = 0.06f;

        [SerializeField] private float crankTurns = 3f;

        private Renderer[] _renderers;
        private float _drawn = -1f;

        private void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            Apply(0f);
        }

        private void LateUpdate() => Apply(sternotomy != null ? sternotomy.Openness01 : 0f);

        public void Apply(float openness)
        {
            if (Mathf.Approximately(openness, _drawn)) { return; }
            _drawn = openness;

            bool visible = openness > 0.02f;
            if (_renderers == null) { _renderers = GetComponentsInChildren<Renderer>(true); }
            foreach (Renderer r in _renderers)
            {
                if (r != null && r.enabled != visible) { r.enabled = visible; }
            }

            float eased = Mathf.SmoothStep(0f, 1f, openness);
            float spread = Mathf.Lerp(closedSpread, openSpread, eased);

            if (leftBlade != null)
            {
                Vector3 p = leftBlade.localPosition;
                leftBlade.localPosition = new Vector3(-spread, p.y, p.z);
            }

            if (rightBlade != null)
            {
                Vector3 p = rightBlade.localPosition;
                rightBlade.localPosition = new Vector3(spread, p.y, p.z);
            }

            if (crank != null)
            {
                crank.localRotation = Quaternion.Euler(eased * crankTurns * 360f, 0f, 0f);
            }
        }

        public void Bind(SternotomyController controller, Transform left, Transform right, Transform crankHandle)
        {
            sternotomy = controller;
            leftBlade = left;
            rightBlade = right;
            crank = crankHandle;
            _renderers = null;
            _drawn = -1f;
        }
    }
}
