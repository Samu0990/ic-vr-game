using System.Collections.Generic;
using UnityEngine;
using Skyborne.Flight;

namespace Skyborne.Grab
{
    public enum GrabState
    {
        Idle,
        Reaching,
        Holding,
    }

    /// <summary>
    /// The flyer's hands. A grab here is a real physical constraint, not a re-parent: the
    /// carried body hangs off a ConfigurableJoint, so it swings under you, its struggling
    /// shoves you back through the solver, its mass slows your flight, and slamming it into
    /// a wall rips it out of your grip.
    /// </summary>
    [RequireComponent(typeof(FlyerController))]
    [DefaultExecutionOrder(10)]
    public class GrabController : MonoBehaviour
    {
        [Header("Detection")]
        [SerializeField] private Camera aimCamera;

        [Tooltip("How far ahead the flyer can snatch something.")]
        [SerializeField] private float reachDistance = 3.4f;

        [Tooltip("Radius of the grab cast. Generous on purpose: you are grabbing a whole person.")]
        [SerializeField] private float castRadius = 0.6f;

        [SerializeField] private LayerMask grabbableMask = ~0;

        [Header("Reach")]
        [Tooltip("Metres per second the hand travels toward the chosen bone. The hand really " +
                 "travels: nothing is teleported into your grip.")]
        [SerializeField] private float reachSpeed = 7f;

        [Tooltip("Distance at which the grip closes.")]
        [SerializeField] private float graspDistance = 0.3f;

        [Tooltip("Seconds before an unreachable target is given up on.")]
        [SerializeField] private float reachTimeout = 0.9f;

        [Header("Grip")]
        [Tooltip("Metres of slack in the grip, so the hold has give instead of being welded.")]
        [SerializeField] private float gripSlack = 0.06f;

        [Tooltip("Drive spring the instant the grip closes. Deliberately soft: this is the " +
                 "shock absorber that stops a 40 m/s catch from snapping the body.")]
        [SerializeField] private float catchSpring = 450f;

        [Tooltip("Drive spring once the catch has been absorbed.")]
        [SerializeField] private float holdSpring = 4200f;

        [SerializeField] private float gripDamper = 140f;

        [Tooltip("Seconds to ramp from catch spring to hold spring.")]
        [SerializeField] private float catchDuration = 0.25f;

        [Header("Swing")]
        [Tooltip("Degrees of swing allowed by the most secure grip (a torso hold).")]
        [SerializeField] private float swingLimitStable = 22f;

        [Tooltip("Degrees of swing allowed by the loosest grip (a wrist or ankle).")]
        [SerializeField] private float swingLimitLoose = 85f;

        [Header("Grip strength")]
        [SerializeField] private float breakForceLoose = 26000f;
        [SerializeField] private float breakForceStable = 120000f;

        [Header("Load")]
        [Tooltip("How much of the carried mass actually taxes the flight. 0 = weightless, " +
                 "1 = the full mass ratio is felt.")]
        [Range(0f, 1f)] [SerializeField] private float loadCoupling = 0.6f;

        [Header("Throw")]
        [Tooltip("Seconds of wind-up for a full-power throw.")]
        [SerializeField] private float throwChargeTime = 0.9f;

        [Tooltip("Delta-v (m/s) imparted by a flick throw.")]
        [SerializeField] private float minThrowSpeed = 7f;

        [Tooltip("Delta-v (m/s) imparted by a fully charged throw.")]
        [SerializeField] private float maxThrowSpeed = 42f;

        private readonly Collider[] _overlapBuffer = new Collider[32];
        private readonly List<Collider> _ownColliders = new List<Collider>();
        private readonly List<Collider> _targetColliders = new List<Collider>();

        private FlyerController _flyer;
        private Transform _hand;
        private Vector3 _handRestLocal;

        private Grabbable _target;
        private Rigidbody _grippedBone;
        private ConfigurableJoint _joint;
        private float _stability;
        private float _catchTimer;
        private float _reachTimer;
        private float _throwCharge;

        public GrabState State { get; private set; } = GrabState.Idle;

        /// <summary>0..1 wind-up on the throw. Drives the HUD.</summary>
        public float ThrowCharge => _throwCharge;

        /// <summary>Mass currently being carried, in kg. 0 when empty handed.</summary>
        public float CarriedMass => _target != null ? _target.Mass : 0f;

        /// <summary>The body currently held, or null.</summary>
        public Grabbable Carried => State == GrabState.Holding ? _target : null;

        private void Awake()
        {
            _flyer = GetComponent<FlyerController>();
            _hand = _flyer.HandAnchor;
            _handRestLocal = _hand.localPosition;

            if (aimCamera == null)
            {
                aimCamera = GetComponentInChildren<Camera>();
            }

            GetComponentsInChildren(true, _ownColliders);
        }

        private void Update()
        {
            FlightInputSnapshot input = _flyer.CurrentInput;
            float dt = Time.deltaTime;

            switch (State)
            {
                case GrabState.Idle:
                    ReturnHandToRest(dt);
                    if (input.grabPressed)
                    {
                        TryBeginReach();
                    }

                    break;

                case GrabState.Reaching:
                    TickReach(dt);
                    if (input.grabPressed)
                    {
                        AbortReach();
                    }

                    break;

                case GrabState.Holding:
                    TrackGripPoint();
                    TickThrow(input, dt);
                    if (input.grabPressed)
                    {
                        Release(Vector3.zero);
                    }

                    break;
            }
        }

        private void FixedUpdate()
        {
            if (State != GrabState.Holding)
            {
                return;
            }

            // Unity destroys a joint that exceeds its break force, so a null joint here means
            // the grip was physically torn loose.
            if (_joint == null || _target == null)
            {
                OnGripTorn();
                return;
            }

            _catchTimer += Time.fixedDeltaTime;
            ApplyGripDrive(Mathf.Lerp(catchSpring, holdSpring, Mathf.Clamp01(_catchTimer / Mathf.Max(0.01f, catchDuration))));

            float massRatio = _target.Mass / Mathf.Max(1f, _flyer.Body.mass);
            _flyer.SetLoadFactor(1f + massRatio * loadCoupling);
        }

        private void TryBeginReach()
        {
            if (!TryFindTarget(out Grabbable target))
            {
                return;
            }

            _target = target;
            _reachTimer = reachTimeout;
            State = GrabState.Reaching;
        }

        /// <summary>
        /// Looks for a body to grab: first an overlap around the hand, because at contact range
        /// a sphere cast starts already inside the target and reports nothing, then a cast along
        /// the aim for anything further out.
        /// </summary>
        private bool TryFindTarget(out Grabbable target)
        {
            target = null;

            int count = Physics.OverlapSphereNonAlloc(
                _hand.position, castRadius, _overlapBuffer, grabbableMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Grabbable candidate = _overlapBuffer[i] != null
                    ? _overlapBuffer[i].GetComponentInParent<Grabbable>()
                    : null;

                if (candidate != null && !candidate.IsHeld)
                {
                    target = candidate;
                    return true;
                }
            }

            if (aimCamera == null)
            {
                return false;
            }

            Transform eye = aimCamera.transform;
            if (Physics.SphereCast(eye.position, castRadius, eye.forward, out RaycastHit hit,
                    reachDistance, grabbableMask, QueryTriggerInteraction.Ignore))
            {
                Grabbable candidate = hit.collider.GetComponentInParent<Grabbable>();
                if (candidate != null && !candidate.IsHeld)
                {
                    target = candidate;
                    return true;
                }
            }

            return false;
        }

        private void TickReach(float dt)
        {
            _reachTimer -= dt;

            if (_target == null || _target.IsHeld)
            {
                AbortReach();
                return;
            }

            if (!_target.TryPickGrip(_hand.position, out Rigidbody bone, out float stability))
            {
                AbortReach();
                return;
            }

            Vector3 gripTarget = bone.worldCenterOfMass;
            _hand.position = Vector3.MoveTowards(_hand.position, gripTarget, reachSpeed * dt);

            if (Vector3.Distance(_hand.position, gripTarget) <= graspDistance)
            {
                CloseGrip(bone, stability);
                return;
            }

            // Target ran, or the flyer drifted off: give up rather than trailing a phantom hand.
            if (_reachTimer <= 0f || Vector3.Distance(transform.position, gripTarget) > reachDistance * 1.6f)
            {
                AbortReach();
            }
        }

        private void AbortReach()
        {
            _target = null;
            State = GrabState.Idle;
        }

        /// <summary>
        /// Closes the grip by constraining the chosen bone to the flyer with a joint. A joint
        /// rather than a parent is the whole point: forces travel both ways, so the carried
        /// body's weight and struggling are felt, not simulated separately.
        /// </summary>
        private void CloseGrip(Rigidbody bone, float stability)
        {
            _grippedBone = bone;
            _stability = stability;

            Vector3 grip = _hand.position;

            _joint = bone.gameObject.AddComponent<ConfigurableJoint>();
            _joint.autoConfigureConnectedAnchor = false;
            _joint.connectedBody = _flyer.Body;
            _joint.anchor = bone.transform.InverseTransformPoint(grip);
            _joint.connectedAnchor = _flyer.Body.transform.InverseTransformPoint(grip);

            // Preprocessing off keeps the solver stable across the big mass ratio between a
            // flyer and a single forearm.
            _joint.enablePreprocessing = false;

            _joint.xMotion = ConfigurableJointMotion.Limited;
            _joint.yMotion = ConfigurableJointMotion.Limited;
            _joint.zMotion = ConfigurableJointMotion.Limited;
            _joint.linearLimit = new SoftJointLimit { limit = gripSlack, contactDistance = 0.01f };

            // A secure hold barely swings; a wrist hold lets the whole body pendulum under you.
            float swing = Mathf.Lerp(swingLimitLoose, swingLimitStable, stability);
            _joint.angularXMotion = ConfigurableJointMotion.Limited;
            _joint.angularYMotion = ConfigurableJointMotion.Limited;
            _joint.angularZMotion = ConfigurableJointMotion.Limited;
            _joint.lowAngularXLimit = new SoftJointLimit { limit = -swing };
            _joint.highAngularXLimit = new SoftJointLimit { limit = swing };
            _joint.angularYLimit = new SoftJointLimit { limit = swing };
            _joint.angularZLimit = new SoftJointLimit { limit = swing };

            _joint.breakForce = Mathf.Lerp(breakForceLoose, breakForceStable, stability);
            _joint.breakTorque = _joint.breakForce * 0.5f;

            _catchTimer = 0f;
            ApplyGripDrive(catchSpring);

            IgnoreCollisionsWithTarget(true);
            _target.MarkHeld(true);
            State = GrabState.Holding;
        }

        private void ApplyGripDrive(float spring)
        {
            if (_joint == null)
            {
                return;
            }

            JointDrive drive = new JointDrive
            {
                positionSpring = spring,
                positionDamper = gripDamper,
                maximumForce = float.MaxValue,
            };

            _joint.xDrive = drive;
            _joint.yDrive = drive;
            _joint.zDrive = drive;
        }

        /// <summary>Keeps the visible hand sitting on the grip point while carrying.</summary>
        private void TrackGripPoint()
        {
            if (_joint != null && _grippedBone != null)
            {
                _hand.position = _grippedBone.transform.TransformPoint(_joint.anchor);
            }
        }

        private void TickThrow(in FlightInputSnapshot input, float dt)
        {
            if (input.throwHeld)
            {
                _throwCharge = Mathf.Clamp01(_throwCharge + dt / Mathf.Max(0.01f, throwChargeTime));
                return;
            }

            if (input.throwReleased && _throwCharge > 0f)
            {
                Vector3 direction = aimCamera != null ? aimCamera.transform.forward : transform.forward;
                float speed = Mathf.Lerp(minThrowSpeed, maxThrowSpeed, _throwCharge);

                // Impulse = mass * delta-v, so the tuning numbers stay readable as speeds.
                Release(direction * (speed * _target.Mass));
            }
        }

        private void OnGripTorn()
        {
            // Torn out of the hand: no throw impulse, the body simply keeps what it had.
            Release(Vector3.zero);
        }

        private void Release(Vector3 impulse)
        {
            if (_joint != null)
            {
                Destroy(_joint);
            }

            if (_target != null)
            {
                IgnoreCollisionsWithTarget(false);

                // No velocity is written here on purpose. The joint already handed the body the
                // carrier's momentum, so a plain release makes it continue exactly as it was.
                if (impulse != Vector3.zero)
                {
                    _target.Rig.AddImpulse(impulse);
                }

                _target.MarkHeld(false);
            }

            _flyer.SetLoadFactor(1f);

            _joint = null;
            _grippedBone = null;
            _target = null;
            _throwCharge = 0f;
            State = GrabState.Idle;
        }

        private void ReturnHandToRest(float dt)
        {
            _hand.localPosition = Vector3.Lerp(_hand.localPosition, _handRestLocal, 1f - Mathf.Exp(-10f * dt));
        }

        /// <summary>
        /// Stops the flyer's own collider fighting the carried ragdoll. Without this the body
        /// you are holding is permanently intersecting you and the contact solver shakes both.
        /// </summary>
        private void IgnoreCollisionsWithTarget(bool ignore)
        {
            if (_target == null)
            {
                return;
            }

            _target.CollectColliders(_targetColliders);

            for (int i = 0; i < _ownColliders.Count; i++)
            {
                Collider own = _ownColliders[i];
                if (own == null || !own.gameObject.activeInHierarchy)
                {
                    continue;
                }

                for (int j = 0; j < _targetColliders.Count; j++)
                {
                    Collider other = _targetColliders[j];
                    if (other == null || !other.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    Physics.IgnoreCollision(own, other, ignore);
                }
            }
        }

        private void OnDisable()
        {
            if (State == GrabState.Holding)
            {
                Release(Vector3.zero);
            }
        }
    }
}
