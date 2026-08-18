using UnityEngine;
using VRSurgery.Tissue;
using VRSurgery.Tools;

namespace VRSurgery.Surgery
{
    /// <summary>
    /// Makes the two things the player currently cannot see visible: how far the blade is from
    /// the guided trajectory, and whether the incision has actually been made.
    ///
    /// Both already existed only as a text log and an automated assertion. Nothing new is placed
    /// in the workspace to show them — the guide marker already lying on the skin is recoloured
    /// in place, and the confirmation lives on the objective monitor, which sits outside the
    /// validated reach corridor. No geometry enters |x| &lt; 0.50 or the tray band.
    ///
    /// The trajectory shown is the incision guide's. There is no screw entity in the project yet;
    /// when one exists this component should read its axis instead of the blade tip.
    /// </summary>
    public class SurgicalFeedbackHUD : MonoBehaviour
    {
        public enum TrajectoryState
        {
            Idle,
            OnTarget,
            Drifting,
            OffTarget,
        }

        [Header("Sources")]
        [SerializeField] private TissueSurface tissue;
        [SerializeField] private IncisionGuide guide;
        [SerializeField] private IncisableSkin skin;
        [SerializeField] private BladeTip bladeTip;

        [Header("Trajectory indicator (the guide marker itself)")]
        [SerializeField] private Renderer guideRenderer;
        [SerializeField] private Material idleMaterial;
        [SerializeField] private Material onTargetMaterial;
        [SerializeField] private Material driftingMaterial;
        [SerializeField] private Material offTargetMaterial;

        [Header("Incision confirmation")]
        [SerializeField] private Renderer confirmationLamp;
        [SerializeField] private Material confirmationOff;
        [SerializeField] private Material confirmationOn;
        [SerializeField] private TextMesh statusText;

        [Tooltip("How close the tip must be to the sheet before the indicator wakes up, in metres.")]
        [SerializeField] private float engageDistance = 0.08f;

        public TrajectoryState State { get; private set; } = TrajectoryState.Idle;

        /// <summary>Live distance from the guided path, in metres. Negative when disengaged.</summary>
        public float CurrentDeviation { get; private set; } = -1f;

        public bool ConfirmationShown { get; private set; }

        private void OnEnable()
        {
            if (skin != null)
            {
                skin.Incised += HandleIncised;
            }

            ApplyTrajectory(TrajectoryState.Idle);
            ShowConfirmation(false);
        }

        private void OnDisable()
        {
            if (skin != null)
            {
                skin.Incised -= HandleIncised;
            }
        }

        private void Update()
        {
            if (tissue == null || guide == null || bladeTip == null)
            {
                return;
            }

            Vector3 local = tissue.WorldToTissueLocal(bladeTip.CurrentPosition);

            bool engaged = Mathf.Abs(local.y) <= engageDistance
                           && Mathf.Abs(local.x) <= tissue.HalfExtents.x + engageDistance
                           && Mathf.Abs(local.z) <= tissue.HalfExtents.y + engageDistance;

            if (!engaged)
            {
                CurrentDeviation = -1f;
                ApplyTrajectory(TrajectoryState.Idle);
                return;
            }

            CurrentDeviation = guide.DeviationAt(local);

            TrajectoryState next;
            if (CurrentDeviation <= guide.AcceptableTolerance)
            {
                next = TrajectoryState.OnTarget;
            }
            else if (CurrentDeviation <= guide.FailureTolerance)
            {
                next = TrajectoryState.Drifting;
            }
            else
            {
                next = TrajectoryState.OffTarget;
            }

            ApplyTrajectory(next);
        }

        private void ApplyTrajectory(TrajectoryState next)
        {
            if (next == State)
            {
                return;
            }

            State = next;

            if (guideRenderer == null)
            {
                return;
            }

            Material material = next switch
            {
                TrajectoryState.OnTarget => onTargetMaterial,
                TrajectoryState.Drifting => driftingMaterial,
                TrajectoryState.OffTarget => offTargetMaterial,
                _ => idleMaterial,
            };

            if (material != null)
            {
                guideRenderer.sharedMaterial = material;
            }
        }

        private void HandleIncised(IncisableSkin source, Vector3 point)
        {
            ShowConfirmation(true);
        }

        private void ShowConfirmation(bool on)
        {
            ConfirmationShown = on;

            if (confirmationLamp != null)
            {
                Material material = on ? confirmationOn : confirmationOff;
                if (material != null)
                {
                    confirmationLamp.sharedMaterial = material;
                }
            }

            if (statusText != null)
            {
                statusText.text = on ? "INCISION CONFIRMED" : "AWAITING INCISION";
                statusText.color = on ? new Color(0.45f, 1f, 0.55f) : new Color(0.72f, 0.78f, 0.85f);
            }
        }

        public void ResetHud()
        {
            ShowConfirmation(false);
            ApplyTrajectory(TrajectoryState.Idle);
        }

        public void Bind(TissueSurface surface, IncisionGuide incisionGuide, IncisableSkin incisable,
            BladeTip tip, Renderer marker, Renderer lamp, TextMesh text)
        {
            tissue = surface;
            guide = incisionGuide;
            skin = incisable;
            bladeTip = tip;
            guideRenderer = marker;
            confirmationLamp = lamp;
            statusText = text;
        }

        public void BindMaterials(Material idle, Material onTarget, Material drifting, Material offTarget,
            Material lampOff, Material lampOn)
        {
            idleMaterial = idle;
            onTargetMaterial = onTarget;
            driftingMaterial = drifting;
            offTargetMaterial = offTarget;
            confirmationOff = lampOff;
            confirmationOn = lampOn;
        }
    }
}
