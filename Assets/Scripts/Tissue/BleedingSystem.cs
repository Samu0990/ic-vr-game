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

            Intensity01 = next switch
            {
                BleedingState.Minimal => 0.25f,
                BleedingState.Active => 0.6f,
                BleedingState.Heavy => 1f,
                _ => 0f,
            };

            SetState(next);
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
