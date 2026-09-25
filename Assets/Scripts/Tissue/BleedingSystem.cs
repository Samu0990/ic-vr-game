using System;
using UnityEngine;
using VRSurgery.Surgery;

namespace VRSurgery.Tissue
{
    public enum BleedingState
    {
        None,
        Minimal,
        Active,
        Heavy,
        Controlled,
    }

    /// <summary>
    /// Bleeding as persistent state rather than a particle system that plays while something
    /// is touching something. Intensity is driven by how deep and how long the incision is;
    /// it only stops when the player actually applies a control action.
    /// </summary>
    [RequireComponent(typeof(TissueSurface))]
    public class BleedingSystem : MonoBehaviour
    {
        [Header("Thresholds (normalised incision depth)")]
        [SerializeField, Range(0f, 1f)] private float minimalThreshold = 0.5f;
        [SerializeField, Range(0f, 1f)] private float activeThreshold = 0.7f;
        [SerializeField, Range(0f, 1f)] private float heavyThreshold = 0.9f;

        [Header("Severity")]
        [Tooltip("Incision length, in metres, at which the length term reaches its full weight. " +
                 "A longer cut opens more vessels, so a careless 9 cm slash must bleed harder " +
                 "than a controlled one.")]
        [SerializeField, Min(0.001f)] private float lengthAtFullSeverity = 0.09f;

        [Tooltip("How much of the final intensity comes from the incision's length rather than " +
                 "its depth.")]
        [SerializeField, Range(0f, 1f)] private float lengthWeight = 0.35f;

        [Tooltip("Seconds of uncontrolled bleeding after which the time term is fully applied. " +
                 "Hesitating has to cost something, or the clock is the only pressure in the game.")]
        [SerializeField, Min(0.1f)] private float secondsToWorsen = 45f;

        [Tooltip("How much the intensity can grow on its own while the bleeding goes unanswered.")]
        [SerializeField, Range(0f, 1f)] private float worseningWeight = 0.3f;

        [Header("Control")]
        [Tooltip("Seconds of sustained pressure needed to bring bleeding under control.")]
        [SerializeField] private float pressureDurationToControl = 1.5f;

        [Header("Effects (optional)")]
        [SerializeField] private ParticleSystem bleedingParticles;

        private TissueSurface _tissue;
        private float _pressureHeld;
        private bool _bleedingAnnounced;

        public BleedingState State { get; private set; } = BleedingState.None;

        /// <summary>0..1 bleed rate, used by scoring and by effect intensity.</summary>
        public float Intensity01 { get; private set; }

        /// <summary>Total blood lost, arbitrary game units. Feeds the safety score.</summary>
        public float TotalBloodLoss { get; private set; }

        /// <summary>Seconds the wound has been bleeding without being brought under control.</summary>
        public float UncontrolledSeconds { get; private set; }

        public event Action<BleedingState> StateChanged;

        private void Awake()
        {
            _tissue = GetComponent<TissueSurface>();
        }

        private void OnEnable()
        {
            if (_tissue != null)
            {
                _tissue.StateChanged += HandleTissueStateChanged;
            }
        }

        private void OnDisable()
        {
            if (_tissue != null)
            {
                _tissue.StateChanged -= HandleTissueStateChanged;
            }
        }

        private void HandleTissueStateChanged(TissueSurface source, IncisionState state)
        {
            if (state == IncisionState.Intact)
            {
                SetState(BleedingState.None);
                Intensity01 = 0f;
                TotalBloodLoss = 0f;
                UncontrolledSeconds = 0f;
                _bleedingAnnounced = false;
                _pressureHeld = 0f;
            }
        }

        private void Update()
        {
            if (_tissue == null || State == BleedingState.Controlled)
            {
                return;
            }

            // Counted before the evaluation, because the severity formula reads it: the wound gets
            // worse the longer it is left, and that has to be true on the same frame it is asked.
            if (State != BleedingState.None)
            {
                UncontrolledSeconds += Time.deltaTime;
            }

            EvaluateFromTissue();

            if (State != BleedingState.None)
            {
                TotalBloodLoss += Intensity01 * Time.deltaTime;
            }
        }

        private void EvaluateFromTissue()
        {
            float depth = _tissue.PeakDepth01;

            BleedingState next;
            if (depth >= heavyThreshold)
            {
                next = BleedingState.Heavy;
            }
            else if (depth >= activeThreshold)
            {
                next = BleedingState.Active;
            }
            else if (depth >= minimalThreshold)
            {
                next = BleedingState.Minimal;
            }
            else
            {
                next = BleedingState.None;
            }

            Intensity01 = next == BleedingState.None ? 0f : SeverityFrom(depth);
            SetState(next);
        }

        /// <summary>
        /// Continuous 0..1 severity.
        ///
        /// The state enum is still what the rest of the game listens to, but the number behind it
        /// is no longer one of three constants. It used to jump 0.25 -> 0.6 -> 1.0, which made the
        /// stand's bleeding bar pop between three positions instead of growing, and made depth the
        /// only thing that mattered: a careless 9 cm slash bled exactly like a clean nick of the
        /// same depth, and hesitating cost nothing.
        ///
        /// Three terms, in the order a surgeon would rank them: how deep the cut went, how long it
        /// is, and how long it has been left alone.
        /// </summary>
        private float SeverityFrom(float depth01)
        {
            // Depth maps from the point bleeding starts to the point it is as bad as it gets, so
            // a cut that only just breaks the threshold starts near zero rather than at 0.25.
            float depthSpan = Mathf.Max(0.0001f, 1f - minimalThreshold);
            float fromDepth = Mathf.Clamp01((depth01 - minimalThreshold) / depthSpan);

            float fromLength = _tissue == null
                ? 0f
                : Mathf.Clamp01(_tissue.IncisionLength / lengthAtFullSeverity);

            float fromTime = Mathf.Clamp01(UncontrolledSeconds / secondsToWorsen);

            // Depth carries whatever the other two do not, so the weights can be retuned on the
            // day without the total ever leaving 0..1.
            float depthWeight = Mathf.Max(0f, 1f - lengthWeight - worseningWeight);

            return Mathf.Clamp01(
                fromDepth * depthWeight +
                fromLength * lengthWeight +
                fromTime * worseningWeight);
        }

        /// <summary>
        /// Applies a bleeding-control action. The tool matters: pressure with an appropriate
        /// instrument works, and the scalpel deliberately cannot solve what it caused.
        /// </summary>
        public void ApplyPressure(float deltaTime)
        {
            if (State == BleedingState.None || State == BleedingState.Controlled)
            {
                return;
            }

            _pressureHeld += deltaTime;
            if (_pressureHeld >= pressureDurationToControl)
            {
                ControlBleeding();
            }
        }

        public void ReleasePressure()
        {
            _pressureHeld = 0f;
        }

        public void ControlBleeding()
        {
            if (State == BleedingState.None || State == BleedingState.Controlled)
            {
                return;
            }

            Intensity01 = 0f;
            SetState(BleedingState.Controlled);
        }

        private void SetState(BleedingState next)
        {
            if (next == State)
            {
                return;
            }

            BleedingState previous = State;
            State = next;

            bool wasBleeding = previous is BleedingState.Minimal or BleedingState.Active or BleedingState.Heavy;
            bool isBleeding = next is BleedingState.Minimal or BleedingState.Active or BleedingState.Heavy;

            if (isBleeding && !_bleedingAnnounced)
            {
                _bleedingAnnounced = true;
                SurgeryEvents.RaiseBleedingStarted();
            }
            else if (wasBleeding && !isBleeding)
            {
                _bleedingAnnounced = false;
                SurgeryEvents.RaiseBleedingStopped();
            }

            UpdateParticles();
            StateChanged?.Invoke(State);
        }

        private void UpdateParticles()
        {
            if (bleedingParticles == null)
            {
                return;
            }

            ParticleSystem.EmissionModule emission = bleedingParticles.emission;
            emission.rateOverTime = Intensity01 * 40f;

            if (Intensity01 > 0f && !bleedingParticles.isPlaying)
            {
                bleedingParticles.Play();
            }
            else if (Intensity01 <= 0f && bleedingParticles.isPlaying)
            {
                bleedingParticles.Stop();
            }
        }
    }
}
