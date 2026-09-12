using System;
using UnityEngine;

namespace VRSurgery.Transplant
{
    /// <summary>
    /// Makes the heart beat.
    ///
    /// Cheap and disproportionately effective: an organ that sits perfectly still in the hand
    /// reads as a prop no matter how good the mesh is, and a pulse is the difference between
    /// carrying a model and carrying something alive. It is also the payoff of the whole
    /// operation — the run ends when this starts.
    ///
    /// The waveform is not a sine. A real cycle is a fast squeeze and a longer refill, so systole
    /// takes about a third of the beat and the rest is the heart filling again. A sine spends
    /// equal time in both and reads as breathing rather than beating.
    ///
    /// Scale rather than a skinned rig: the models are static meshes with no bones, and a uniform
    /// pulse of a few percent is what a heart in a chest actually looks like from a surgeon's
    /// distance. Costs a Quest one transform write per frame.
    /// </summary>
    public class Heartbeat : MonoBehaviour
    {
        [SerializeField] private Transform target;

        [Tooltip("Beats per minute. A heart just off bypass runs fast before it settles.")]
        [SerializeField, Range(30f, 140f)] private float beatsPerMinute = 84f;

        [Tooltip("How much the muscle shortens at peak systole, as a fraction. Real ventricular " +
                 "wall motion is larger than this, but the whole organ's outline moves less.")]
        [SerializeField, Range(0f, 0.12f)] private float amplitude = 0.045f;

        [Tooltip("Fraction of the cycle spent contracting. The rest is filling.")]
        [SerializeField, Range(0.15f, 0.6f)] private float systoleFraction = 0.35f;

        [Tooltip("Seconds to fade in when the heart restarts, so it does not snap into rhythm.")]
        [SerializeField, Min(0f)] private float startupSeconds = 2.5f;

        /// <summary>True while the heart is beating.</summary>
        public bool IsBeating { get; private set; }

        /// <summary>0..1 through the current cycle. Read by the audience's trace.</summary>
        public float Phase01 { get; private set; }

        /// <summary>Current scale multiplier. 1 at rest, smallest at peak systole.</summary>
        public float Pulse { get; private set; } = 1f;

        public event Action Started;

        private Vector3 _restScale;
        private float _rampIn;

        private void Awake()
        {
            if (target == null) { target = transform; }
            _restScale = target.localScale;
        }

        private void OnDisable()
        {
            // Leaving the organ mid-squeeze would bake the contraction into its resting size the
            // next time the scene is saved.
            if (target != null) { target.localScale = _restScale; }
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Advances the beat. Stepped by hand in tests.</summary>
        public void Tick(float deltaTime)
        {
            if (!IsBeating || deltaTime <= 0f)
            {
                return;
            }

            _rampIn = startupSeconds <= 0f ? 1f : Mathf.Min(1f, _rampIn + deltaTime / startupSeconds);

            float cycle = 60f / Mathf.Max(1f, beatsPerMinute);
            Phase01 = Mathf.Repeat(Phase01 + deltaTime / cycle, 1f);

            // Squeeze fast, refill slow.
            float shape = Phase01 < systoleFraction
                ? Mathf.Sin(Phase01 / systoleFraction * Mathf.PI)
                : 0f;

            Pulse = 1f - shape * amplitude * _rampIn;

            if (target != null)
            {
                target.localScale = _restScale * Pulse;
            }
        }

        /// <summary>The moment the operation is for.</summary>
        public void StartBeating()
        {
            if (IsBeating) { return; }

            IsBeating = true;
            _rampIn = 0f;
            Phase01 = 0f;
            Started?.Invoke();
        }

        public void StopBeating()
        {
            IsBeating = false;
            Pulse = 1f;
            if (target != null) { target.localScale = _restScale; }
        }

        public void Bind(Transform organ)
        {
            target = organ != null ? organ : transform;
            _restScale = target.localScale;
        }
    }
}
