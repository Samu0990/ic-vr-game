using UnityEngine;
using VRSurgery.Interaction;
using VRSurgery.Transplant;

namespace VRSurgery.Feedback
{
    /// <summary>
    /// A bouncing arrow over the instrument the operation needs next, gone the moment it is in
    /// the visitor's hand.
    ///
    /// The monitor says "pick up the scalpel", but a first-timer does not know which of the
    /// shiny things on the tray is the scalpel, and the booth has no one to point. Every game
    /// that hands a stranger a set of tools shows them which one to take; this is that, and
    /// nothing more. It never points at the heart or the vessels — those are worked with the
    /// hands, and the rings on the patient already say where.
    /// </summary>
    public class NextToolHint : MonoBehaviour
    {
        [SerializeField] private TransplantProcedure procedure;

        [Header("Instruments")]
        [SerializeField] private SurgicalInteractable scalpel;
        [SerializeField] private SurgicalInteractable cauteryPen;
        [SerializeField] private SurgicalInteractable saw;
        [SerializeField] private SurgicalInteractable needleHolder;
        [SerializeField] private SurgicalInteractable paddles;

        [Header("What decides between them")]
        [SerializeField] private CauteryWorker cautery;
        [SerializeField] private DefibrillationWorker defibrillation;

        [Header("Look")]
        [SerializeField] private Transform arrow;
        [SerializeField] private float height = 0.1f;
        [SerializeField] private float bounce = 0.025f;
        [SerializeField, Min(0.1f)] private float bounceHz = 1.6f;
        [SerializeField] private float spinDegreesPerSecond = 90f;

        private float _clock;

        /// <summary>The instrument being pointed at, or null.</summary>
        public SurgicalInteractable Target { get; private set; }

        private void Awake() => SetArrow(false);

        private void LateUpdate() => Tick(Time.deltaTime);

        public void Tick(float deltaTime)
        {
            _clock += Mathf.Max(0f, deltaTime);
            Target = Wanted();

            bool show = Target != null && !Target.IsHeld;
            SetArrow(show);
            if (!show || arrow == null) { return; }

            Transform at = Target.GripPoint != null ? Target.GripPoint : Target.transform;
            float lift = height + bounce * (0.5f + 0.5f * Mathf.Sin(_clock * bounceHz * Mathf.PI * 2f));
            arrow.position = at.position + Vector3.up * lift;
            arrow.rotation = Quaternion.Euler(0f, _clock * spinDegreesPerSecond, 0f);
        }

        /// <summary>The instrument the current step is done with, if it is done with one.</summary>
        private SurgicalInteractable Wanted()
        {
            if (procedure == null) { return null; }

            switch (procedure.Stage)
            {
                case TransplantStage.SkinIncision:
                    return scalpel;

                case TransplantStage.OpenChest:
                    // Bleeders on the wound edge first, if there are any; then the bone.
                    return cautery != null && cautery.OpenBleeders > 0 && cauteryPen != null ? cauteryPen : saw;

                case TransplantStage.OpenPericardium:
                    return cauteryPen;

                case TransplantStage.ConnectVessels:
                    // The hand sews too, but the needle holder is the real way and quicker.
                    return needleHolder;

                case TransplantStage.Restart:
                    return defibrillation != null && defibrillation.IsFibrillating ? paddles : null;

                case TransplantStage.CloseSkin:
                    return needleHolder;

                default:
                    return null;
            }
        }

        private void SetArrow(bool on)
        {
            if (arrow != null && arrow.gameObject.activeSelf != on) { arrow.gameObject.SetActive(on); }
        }

        public void Bind(TransplantProcedure transplant, Transform arrowObject, SurgicalInteractable scalpelTool,
            SurgicalInteractable pen, SurgicalInteractable sternalSaw, SurgicalInteractable holder,
            SurgicalInteractable defibPaddles, CauteryWorker cauteryWorker, DefibrillationWorker defib)
        {
            procedure = transplant;
            arrow = arrowObject;
            scalpel = scalpelTool;
            cauteryPen = pen;
            saw = sternalSaw;
            needleHolder = holder;
            paddles = defibPaddles;
            cautery = cauteryWorker;
            defibrillation = defib;
            SetArrow(false);
        }
    }
}
