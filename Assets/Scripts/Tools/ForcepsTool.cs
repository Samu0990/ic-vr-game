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

            if (upperJaw != null)
            {
                upperJaw.localRotation = Quaternion.Euler(angle, 0f, 0f);
            }

            if (lowerJaw != null)
            {
                lowerJaw.localRotation = Quaternion.Euler(-angle, 0f, 0f);
            }
        }

        private void UpdateTissueContact()
        {
            if (jawTip == null)
            {
                IsGrippingTissue = false;
                return;
            }

            bool closedEnough = Closure01 >= closedThreshold;
            if (!closedEnough)
            {
                if (_contactBleeding != null)
                {
                    _contactBleeding.ReleasePressure();
                }

                IsGrippingTissue = false;
                _contactBleeding = null;
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

                TissueSurface tissue = collider.GetComponentInParent<TissueSurface>();
                if (tissue == null)
                {
                    continue;
                }

                IsGrippingTissue = true;
                _contactBleeding = tissue.GetComponent<BleedingSystem>();
                return;
            }

            IsGrippingTissue = false;
            _contactBleeding = null;
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
