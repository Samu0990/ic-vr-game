using UnityEngine;
using VRSurgery.Tissue;

namespace VRSurgery.Tools
{
    /// <summary>
    /// Drives cut detection for one cutting tool. Every physics step it takes the blade tip's
    /// swept segment and offers it to whichever tissue surfaces are in range.
    ///
    /// Tissue surfaces are found once via an overlap query around the tip rather than through
    /// scene-wide lookups, and cached while the tool stays near them.
    /// </summary>
    [RequireComponent(typeof(SurgicalTool))]
    public class CuttingInteractor : MonoBehaviour
    {
        [SerializeField] private BladeTip bladeTip;

        [Header("Detection")]
        [Tooltip("Radius around the tip searched for tissue, in metres.")]
        [SerializeField] private float searchRadius = 0.25f;
        [SerializeField] private LayerMask tissueLayers = ~0;

        [Tooltip("Only cut while the tool is actually held. Off is useful for automated tests.")]
        [SerializeField] private bool requireHeld = true;

        private SurgicalTool _tool;
        private readonly Collider[] _overlapResults = new Collider[8];
        private TissueSurface _cachedTissue;
        private IncisionSystem _cachedIncisionSystem;

        /// <summary>True on any step where the blade was inside tissue.</summary>
        public bool IsInContact { get; private set; }

        public BladeTip BladeTip => bladeTip;

        private void Awake()
        {
            _tool = GetComponent<SurgicalTool>();
            if (bladeTip == null)
            {
                bladeTip = GetComponentInChildren<BladeTip>();
            }
        }

        private void FixedUpdate()
        {
            if (bladeTip == null)
            {
                // Components assembled at runtime may add the tip after this one.
                bladeTip = GetComponentInChildren<BladeTip>();
                if (bladeTip == null)
                {
                    return;
                }
            }

            // Always sample so the history stays continuous, even when not cutting —
            // otherwise the first step after release produces a huge phantom segment.
            bladeTip.Sample(out Vector3 previous, out Vector3 current);

            if (!_tool.HasCapability(ToolCapability.Cut))
            {
                IsInContact = false;
                return;
            }

            if (requireHeld && !_tool.IsHeld)
            {
                IsInContact = false;
                return;
            }

            IncisionSystem system = ResolveIncisionSystem(current);
            if (system == null)
            {
                IsInContact = false;
                return;
            }

            IsInContact = system.ProcessBladeSegment(previous, current, Time.fixedDeltaTime, _tool);
        }

        private IncisionSystem ResolveIncisionSystem(Vector3 tipPosition)
        {
            if (_cachedTissue != null && _cachedIncisionSystem != null)
            {
                Vector3 local = _cachedTissue.WorldToTissueLocal(tipPosition);
                bool stillNear = Mathf.Abs(local.x) <= _cachedTissue.HalfExtents.x + searchRadius
                              && Mathf.Abs(local.z) <= _cachedTissue.HalfExtents.y + searchRadius
                              && Mathf.Abs(local.y) <= searchRadius;

                if (stillNear)
                {
                    return _cachedIncisionSystem;
                }

                _cachedTissue = null;
                _cachedIncisionSystem = null;
            }

            int count = Physics.OverlapSphereNonAlloc(
                tipPosition, searchRadius, _overlapResults, tissueLayers, QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                Collider collider = _overlapResults[i];
                if (collider == null)
                {
                    continue;
                }

                IncisionSystem system = collider.GetComponentInParent<IncisionSystem>();
                if (system == null)
                {
                    continue;
                }

                _cachedIncisionSystem = system;
                _cachedTissue = system.Tissue;
                return system;
            }

            return null;
        }

        /// <summary>Lets tests and the tray-reset flow bind a tissue target without a physics query.</summary>
        public void BindTissue(IncisionSystem system)
        {
            _cachedIncisionSystem = system;
            _cachedTissue = system != null ? system.Tissue : null;
        }

        public void SetRequireHeld(bool value) => requireHeld = value;
    }
}
