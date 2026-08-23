using System;
using UnityEngine;
using VRSurgery.Tissue;

namespace VRSurgery.Surgery
{
    /// <summary>
    /// The fictional patient's simulation state, kept off the 3D model's GameObject so the
    /// visual representation can be replaced without touching any of this.
    ///
    /// Everything here is a gameplay abstraction. It does not model human physiology and must
    /// never be presented as doing so.
    /// </summary>
    public class PatientController : MonoBehaviour
    {
        public enum PatientStatus
        {
            Stable,
            Strained,
            Critical,
        }

        [Header("Fictional vitals")]
        [SerializeField, Range(0f, 1f)] private float stability = 1f;
        [SerializeField] private float stabilityLossPerBloodUnit = 0.04f;
        [SerializeField] private float recoveryPerSecond = 0.02f;

        [Header("Region")]
        [SerializeField] private TissueSurface primaryRegion;
        [SerializeField] private BleedingSystem bleeding;

        public float Stability01 => stability;
        public PatientStatus Status { get; private set; } = PatientStatus.Stable;

        public event Action<PatientStatus> StatusChanged;

        private float _lastBloodLoss;

        private void Awake()
        {
            if (primaryRegion == null)
            {
                primaryRegion = GetComponentInChildren<TissueSurface>();
            }

            if (bleeding == null && primaryRegion != null)
            {
                bleeding = primaryRegion.GetComponent<BleedingSystem>();
            }
        }

        private void Update()
        {
            if (bleeding != null)
            {
                float delta = bleeding.TotalBloodLoss - _lastBloodLoss;
                _lastBloodLoss = bleeding.TotalBloodLoss;

                if (delta > 0f)
                {
                    stability = Mathf.Clamp01(stability - delta * stabilityLossPerBloodUnit);
                }
                else
                {
                    stability = Mathf.Clamp01(stability + recoveryPerSecond * Time.deltaTime);
                }
            }

            EvaluateStatus();
        }

        private void EvaluateStatus()
        {
            PatientStatus next = stability switch
            {
                >= 0.7f => PatientStatus.Stable,
                >= 0.35f => PatientStatus.Strained,
                _ => PatientStatus.Critical,
            };

            if (next == Status)
            {
                return;
            }

            Status = next;
            StatusChanged?.Invoke(Status);

            if (Status == PatientStatus.Critical)
            {
                SurgeryEvents.RaiseError(ErrorSeverity.CriticalError, "Patient stability critical.");
            }
            else if (Status == PatientStatus.Strained)
            {
                SurgeryEvents.RaiseError(ErrorSeverity.Warning, "Patient stability falling.");
            }
        }

        public void ResetPatient()
        {
            stability = 1f;
            _lastBloodLoss = 0f;
            Status = PatientStatus.Stable;
            StatusChanged?.Invoke(Status);
        }
    }
}
