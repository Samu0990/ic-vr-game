using System;
using UnityEngine;
using VRSurgery.Interaction;

namespace VRSurgery.Transplant
{
    public enum OrganRole
    {
        /// <summary>The failing heart. The operation wants it out.</summary>
        Native,

        /// <summary>The replacement. The operation wants it seated in the empty pericardium.</summary>
        Donor,
    }

    /// <summary>
    /// An organ the player can pick up, on the same grab contract as every instrument in the
    /// project — SurgicalInteractable for identity and grip, XRGrabInteractable for the hand,
    /// ToolReleasePhysics for letting go.
    ///
    /// It deliberately does NOT derive from SurgicalTool, and that is not a style preference.
    /// SurgicalTool republishes every grab onto SurgeryEvents.OnToolGrabbed, which
    /// EventSessionController listens to in order to start the round, and which
    /// SurgeryObjectiveSystem uses to tick off "pick up the instrument" objectives. Making a heart
    /// a tool would start the visitor's clock the moment they touched the organ and would satisfy
    /// objectives meant for a scalpel. Same contract, different meaning.
    /// </summary>
    [RequireComponent(typeof(SurgicalInteractable))]
    public class GrabbableOrgan : MonoBehaviour
    {
        [SerializeField] private OrganRole role = OrganRole.Donor;

        [Tooltip("Where this organ belongs. For the donor heart, the empty pericardium; for the " +
                 "native heart, the seat it has to be lifted away from.")]
        [SerializeField] private Transform seat;

        [Tooltip("How close the donor heart has to sit to count as implanted, in metres.")]
        [SerializeField, Min(0.005f)] private float seatTolerance = 0.035f;

        [Tooltip("How far the native heart has to travel from its seat to count as explanted.")]
        [SerializeField, Min(0.02f)] private float removalDistance = 0.20f;

        [Tooltip("Optional. Told when this organ's stage is finished.")]
        [SerializeField] private TransplantProcedure procedure;

        private SurgicalInteractable _interactable;
        private bool _reported;

        /// <summary>Distance from where this organ belongs, in metres. Negative with no seat.</summary>
        public float DistanceFromSeat =>
            seat != null ? Vector3.Distance(transform.position, seat.position) : -1f;

        /// <summary>True once this organ has done what the operation needed it to do.</summary>
        public bool Satisfied { get; private set; }

        public bool IsHeld => _interactable != null && _interactable.IsHeld;

        public event Action<GrabbableOrgan> SatisfiedChanged;

        private void Awake()
        {
            _interactable = GetComponent<SurgicalInteractable>();
        }

        private void OnEnable()
        {
            _interactable.Released += HandleReleased;
        }

        private void OnDisable()
        {
            _interactable.Released -= HandleReleased;
        }

        private void Update()
        {
            // The donor heart is judged continuously, because a visitor lowering it into the chest
            // should see it seat as they arrive rather than having to let go to find out.
            if (role == OrganRole.Donor && !Satisfied)
            {
                Evaluate();
            }
        }

        /// <summary>
        /// Decides whether this organ is where the operation wants it. The native heart is judged
        /// on release rather than continuously: carrying it across the room and back should not
        /// count as an explant the moment it passes the threshold mid-swing.
        /// </summary>
        private void HandleReleased(SurgicalInteractable source, IHandInteractor previousHolder)
        {
            if (role == OrganRole.Native)
            {
                Evaluate();
            }
        }

        private void Evaluate()
        {
            if (Satisfied || seat == null)
            {
                return;
            }

            float distance = DistanceFromSeat;

            bool done = role == OrganRole.Donor
                ? distance <= seatTolerance && !IsHeld
                : distance >= removalDistance;

            if (!done)
            {
                return;
            }

            Satisfied = true;
            SatisfiedChanged?.Invoke(this);
            Report();
        }

        private void Report()
        {
            if (_reported || procedure == null)
            {
                return;
            }

            _reported = true;
            procedure.CompleteStage(role == OrganRole.Native
                ? TransplantStage.RemoveNativeHeart
                : TransplantStage.PlaceDonorHeart);
        }

        /// <summary>Puts the organ back in play for the next visitor.</summary>
        public void ResetOrgan()
        {
            Satisfied = false;
            _reported = false;
        }

        public void Bind(OrganRole organRole, Transform organSeat, TransplantProcedure transplant)
        {
            role = organRole;
            seat = organSeat;
            procedure = transplant;
        }
    }
}
