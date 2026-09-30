using UnityEngine;

namespace Skyborne.NPC
{
    /// <summary>What the body is doing right now.</summary>
    public enum VictimState
    {
        /// <summary>Standing under its own power.</summary>
        Composed,

        /// <summary>Held, and fighting it.</summary>
        Struggling,

        /// <summary>Held, and out of fight.</summary>
        Exhausted,

        /// <summary>Unsupported and falling.</summary>
        Falling,

        /// <summary>Knocked out. Fully limp until reset.</summary>
        Limp,
    }

    /// <summary>
    /// The person on the other end of the grab. Everything this does goes through the ragdoll's
    /// muscle tone and joint targets, so the flyer feels it through the physics solver rather
    /// than through a scripted reaction.
    /// </summary>
    [RequireComponent(typeof(RagdollRig))]
    public class NpcVictim : MonoBehaviour
    {
        [Header("Stamina")]
        [Tooltip("Seconds of full-strength struggling before the body is spent.")]
        [SerializeField] private float struggleDuration = 7f;

        [Tooltip("Seconds to recover a full bar once back on the ground.")]
        [SerializeField] private float recoveryDuration = 12f;

        [Header("Struggle")]
        [Tooltip("Degrees a limb swings away from rest at full strength.")]
        [SerializeField] private float struggleAmplitude = 42f;

        [Tooltip("Thrashes per second at full strength.")]
        [SerializeField] private float struggleFrequency = 3.2f;

        [Tooltip("Impulse (N) kicked through the body on each thrash. This is what shakes the carrier.")]
        [SerializeField] private float struggleImpulse = 70f;

        [Header("Muscle tone")]
        [SerializeField] private float composedTone = 1f;
        [SerializeField] private float exhaustedTone = 0.12f;

        [Header("Injury")]
        [Tooltip("Impact speed (m/s) that knocks the body out cold.")]
        [SerializeField] private float knockoutSpeed = 14f;

        private RagdollRig _rig;
        private float _stamina = 1f;
        private float _noiseSeed;
        private float _thrashTimer;

        public VictimState State { get; private set; } = VictimState.Composed;

        /// <summary>1 = fresh, 0 = spent. Drives struggle strength and muscle tone.</summary>
        public float Stamina => _stamina;

        public RagdollRig Rig => _rig;

        /// <summary>True while the body is being carried.</summary>
        public bool IsHeld { get; private set; }

        private void Awake()
        {
            _rig = GetComponent<RagdollRig>();
            _noiseSeed = Random.value * 100f;
        }

        private void OnEnable()
        {
            RagdollImpactRelay[] relays = GetComponentsInChildren<RagdollImpactRelay>();
            for (int i = 0; i < relays.Length; i++)
            {
                relays[i].Impacted += OnBoneImpact;
            }
        }

        private void OnDisable()
        {
            RagdollImpactRelay[] relays = GetComponentsInChildren<RagdollImpactRelay>();
            for (int i = 0; i < relays.Length; i++)
            {
                relays[i].Impacted -= OnBoneImpact;
            }
        }

        /// <summary>Called by the grab system the moment the grip closes.</summary>
        public void OnGrabbed()
        {
            IsHeld = true;
            if (State != VictimState.Limp)
            {
                State = _stamina > 0.05f ? VictimState.Struggling : VictimState.Exhausted;
            }
        }

        /// <summary>Called by the grab system on release, whether dropped or thrown.</summary>
        public void OnReleased()
        {
            IsHeld = false;
            if (State != VictimState.Limp)
            {
                State = VictimState.Falling;
            }

            _rig.ResetJointTargets();
        }

        /// <summary>Knocks the body out permanently until <see cref="ResetVictim"/>.</summary>
        public void KnockOut()
        {
            State = VictimState.Limp;
            _stamina = 0f;
            _rig.ResetJointTargets();
            _rig.SetMuscleTone(0f);
        }

        /// <summary>Puts the body back with a full bar. Used by the playground reset.</summary>
        public void ResetVictim()
        {
            State = VictimState.Composed;
            _stamina = 1f;
            IsHeld = false;
            _rig.ResetJointTargets();
            _rig.SetMuscleTone(composedTone);
        }

        private void FixedUpdate()
        {
            switch (State)
            {
                case VictimState.Struggling:
                    TickStruggle();
                    break;

                case VictimState.Exhausted:
                    TickExhausted();
                    break;

                case VictimState.Falling:
                    TickFalling();
                    break;

                case VictimState.Composed:
                    TickRecovery();
                    break;

                case VictimState.Limp:
                    break;
            }
        }

        private void TickStruggle()
        {
            _stamina = Mathf.Max(0f, _stamina - Time.fixedDeltaTime / Mathf.Max(0.01f, struggleDuration));
            if (_stamina <= 0.001f)
            {
                State = VictimState.Exhausted;
                return;
            }

            // Tone stays high while fighting: a person who is struggling is rigid, not floppy.
            _rig.SetMuscleTone(Mathf.Lerp(exhaustedTone, composedTone, _stamina));
            DriveThrash(_stamina);
        }

        private void TickExhausted()
        {
            _rig.SetMuscleTone(exhaustedTone);

            // Not fully limp: the odd last-ditch kick still comes through.
            DriveThrash(0.12f);
        }

        private void TickFalling()
        {
            // Falling with some fight left means arms come up. Spent means they just drop.
            _rig.SetMuscleTone(Mathf.Lerp(exhaustedTone, 0.65f, _stamina));
            DriveThrash(_stamina * 0.55f);

            if (_rig.AverageVelocity().sqrMagnitude < 1f)
            {
                State = VictimState.Composed;
            }
        }

        private void TickRecovery()
        {
            _stamina = Mathf.Min(1f, _stamina + Time.fixedDeltaTime / Mathf.Max(0.01f, recoveryDuration));
            _rig.SetMuscleTone(Mathf.Lerp(exhaustedTone, composedTone, _stamina));
        }

        /// <summary>
        /// Drives the limbs off their rest pose with continuous noise. Each limb gets its own
        /// phase so the body writhes instead of doing jumping jacks, and every thrash also
        /// kicks a real impulse through the ragdoll, which the carrier's joint has to absorb.
        /// </summary>
        private void DriveThrash(float strength)
        {
            if (strength <= 0.001f)
            {
                _rig.ResetJointTargets();
                return;
            }

            float t = Time.time * struggleFrequency;
            float amplitude = struggleAmplitude * strength;

            SetLimb(RagdollPart.LeftUpperArm, t, 0f, amplitude);
            SetLimb(RagdollPart.RightUpperArm, t, 11f, amplitude);
            SetLimb(RagdollPart.LeftForearm, t, 23f, amplitude * 0.7f);
            SetLimb(RagdollPart.RightForearm, t, 37f, amplitude * 0.7f);
            SetLimb(RagdollPart.LeftThigh, t, 53f, amplitude * 0.85f);
            SetLimb(RagdollPart.RightThigh, t, 71f, amplitude * 0.85f);
            SetLimb(RagdollPart.Chest, t, 89f, amplitude * 0.35f);

            _thrashTimer -= Time.fixedDeltaTime;
            if (_thrashTimer <= 0f)
            {
                _thrashTimer = 1f / Mathf.Max(0.1f, struggleFrequency);
                Vector3 direction = Random.onUnitSphere;
                direction.y = Mathf.Abs(direction.y) * 0.4f;
                _rig.AddImpulse(direction.normalized * (struggleImpulse * strength * 0.1f));
            }
        }

        private void SetLimb(RagdollPart part, float time, float phase, float amplitude)
        {
            float x = (Mathf.PerlinNoise(_noiseSeed + phase, time) - 0.5f) * 2f;
            float y = (Mathf.PerlinNoise(_noiseSeed + phase + 5f, time) - 0.5f) * 2f;
            float z = (Mathf.PerlinNoise(_noiseSeed + phase + 9f, time) - 0.5f) * 2f;
            _rig.SetJointTarget(part, Quaternion.Euler(x * amplitude, y * amplitude, z * amplitude));
        }

        private void OnBoneImpact(float speed, Vector3 point)
        {
            if (State == VictimState.Limp)
            {
                return;
            }

            if (speed >= knockoutSpeed)
            {
                KnockOut();
                return;
            }

            // A hard-but-survivable hit costs stamina. Bounce someone off a wall enough times
            // and they stop fighting you.
            _stamina = Mathf.Max(0f, _stamina - speed / Mathf.Max(1f, knockoutSpeed) * 0.35f);
        }
    }
}
