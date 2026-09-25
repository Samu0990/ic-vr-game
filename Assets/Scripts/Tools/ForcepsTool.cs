using System;
using UnityEngine;
using VRSurgery.Tissue;

namespace VRSurgery.Tools
{
    public enum PinchState
    {
        Open,
        Closing,
        Closed,
        Holding,
        Releasing,
    }

    /// <summary>
    /// Forceps. Not just another grabbable: the hand's grip axis drives the jaw opening, and
    /// closing the jaws on tissue is what lets the player hold, retract, or apply pressure to
    /// a bleeding wound. The scalpel deliberately cannot do any of this.
    /// </summary>
    public class ForcepsTool : SurgicalTool
    {
        [Header("Jaws")]
        [SerializeField] private Transform upperJaw;
        [SerializeField] private Transform lowerJaw;
        [SerializeField] private float openAngle = 14f;
        [SerializeField] private float closeSpeed = 8f;

        [Header("Tissue contact")]
        [SerializeField] private Transform jawTip;
        [SerializeField] private float contactRadius = 0.02f;
        [SerializeField] private LayerMask tissueLayers = ~0;

        [Tooltip("How close the jaw tip must be to the incision itself for pressure to count, in " +
                 "metres. The tissue collider spans the whole operative field, so without this " +
                 "the jaws control the bleeding from anywhere on the patch — including several " +
                 "centimetres from the wound.")]
        [SerializeField] private float woundRadius = 0.006f;

        [SerializeField, Range(0f, 1f)] private float closedThreshold = 0.85f;

        private readonly Collider[] _overlapResults = new Collider[8];

        /// <summary>0 = fully open, 1 = fully closed.</summary>
        public float Closure01 { get; private set; }

        public PinchState PinchState { get; private set; } = PinchState.Open;

        /// <summary>True while the jaws are closed on a tissue surface.</summary>
        public bool IsGrippingTissue { get; private set; }

        public event Action<PinchState> PinchStateChanged;

        private float _targetClosure;
        private BleedingSystem _contactBleeding;

        // Cache for the per-frame collider -> tissue -> bleeding walk.
        private Collider _cachedCollider;
        private TissueSurface _cachedTissue;
        private BleedingSystem _bleedingByTissue;

        public Transform JawTip => jawTip;

        /// <summary>Called by the hand: 0 = grip released, 1 = grip fully squeezed.</summary>
        public void SetGripInput(float grip01)
        {
            _targetClosure = Mathf.Clamp01(grip01);
        }

        private void Update()
        {
            float previous = Closure01;
            Closure01 = Mathf.MoveTowards(Closure01, _targetClosure, closeSpeed * Time.deltaTime);

            UpdateJawTransforms();
            UpdateTissueContact();
            UpdatePinchState(previous);

            if (PinchState == PinchState.Holding && _contactBleeding != null)
            {
                _contactBleeding.ApplyPressure(Time.deltaTime);
            }
        }

        private void UpdateJawTransforms()
        {
            float angle = Mathf.Lerp(openAngle, 0f, Closure01);

            // A positive rotation about local X pitches the jaw's forward axis downward, so the
            // upper jaw has to take the negative angle to swing away from the lower one. With the
            // signs the other way round the two jaws open through each other.
            if (upperJaw != null)
            {
                upperJaw.localRotation = Quaternion.Euler(-angle, 0f, 0f);
            }

            if (lowerJaw != null)
            {
                lowerJaw.localRotation = Quaternion.Euler(angle, 0f, 0f);
            }
        }

        private void UpdateTissueContact()
        {
            if (jawTip == null)
            {
                LoseContact();
                return;
            }

            if (Closure01 < closedThreshold)
            {
                LoseContact();
                return;
            }

            int count = Physics.OverlapSphereNonAlloc(
                jawTip.position, contactRadius, _overlapResults, tissueLayers, QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                Collider collider = _overlapResults[i];
                if (collider == null)
                {
                    continue;
                }

                TissueSurface tissue = ResolveTissue(collider);
                if (tissue == null)
                {
                    continue;
                }

                // Touching the field is not the same as pressing on the wound. The collider covers
                // the whole 14x10 cm patch, so without this the bleeding could be controlled from
                // the far side of the operative field.
                if (tissue.DistanceToIncision(jawTip.position) > woundRadius)
                {
                    continue;
                }

                IsGrippingTissue = true;
                _contactBleeding = _bleedingByTissue;
                return;
            }

            LoseContact();
        }

        /// <summary>
        /// Drops tissue contact and, crucially, tells the wound the pressure stopped.
        ///
        /// This used to be skipped on the "jaws shut but off the tissue" path, which left the
        /// pressure timer running across gaps in contact: a player could hold the trigger and dab
        /// at the wound, accumulating the required seconds in fragments instead of holding still.
        /// </summary>
        private void LoseContact()
        {
            if (_contactBleeding != null)
            {
                _contactBleeding.ReleasePressure();
            }

            IsGrippingTissue = false;
            _contactBleeding = null;
        }

        /// <summary>
        /// Resolves the collider's tissue, caching the lookup. Called every frame the jaws are
        /// shut, and the component walk plus the BleedingSystem fetch were both being redone from
        /// scratch each time on a headset that cannot spare it.
        /// </summary>
        private TissueSurface ResolveTissue(Collider collider)
        {
            if (collider == _cachedCollider)
            {
                return _cachedTissue;
            }

            _cachedCollider = collider;
            _cachedTissue = collider.GetComponentInParent<TissueSurface>();
            _bleedingByTissue = _cachedTissue != null ? _cachedTissue.GetComponent<BleedingSystem>() : null;
            return _cachedTissue;
        }

        private void UpdatePinchState(float previousClosure)
        {
            PinchState next;

            if (Closure01 >= closedThreshold)
            {
                next = IsGrippingTissue ? PinchState.Holding : PinchState.Closed;
            }
            else if (Closure01 > previousClosure + 0.0001f)
            {
                next = PinchState.Closing;
            }
            else if (Closure01 < previousClosure - 0.0001f)
            {
                next = PinchState.Releasing;
            }
            else if (Closure01 <= 0.01f)
            {
                next = PinchState.Open;
            }
            else
            {
                next = PinchState;
            }

            if (next == PinchState)
            {
                return;
            }

            PinchState = next;
            PinchStateChanged?.Invoke(PinchState);
        }
    }
}
