using UnityEngine;

namespace Skyborne.Flight
{
    /// <summary>
    /// First-person flying body. Drives a real Rigidbody through <see cref="FlightModel"/>:
    /// gravity, drag and momentum are all genuine, so the body coasts, sags under load and
    /// cannot stop on a dime.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class FlyerController : MonoBehaviour
    {
        [Header("Aerodynamics")]
        [SerializeField] private FlightTuning tuning = FlightTuning.Default;

        [Header("Aiming")]
        [Tooltip("Degrees per second the body can rotate toward the aim while slow.")]
        [SerializeField] private float turnRateSlow = 540f;

        [Tooltip("Degrees per second the body can rotate toward the aim at top speed. Lower than " +
                 "the slow rate on purpose: at speed you carve a wide arc instead of snapping around.")]
        [SerializeField] private float turnRateFast = 110f;

        [Tooltip("Speed (m/s) at which the fast turn rate is reached.")]
        [SerializeField] private float turnRateReferenceSpeed = 120f;

        [SerializeField] private float minPitch = -89f;
        [SerializeField] private float maxPitch = 89f;

        [Header("Rig")]
        [Tooltip("Transform the grab system reaches from. Usually a child at chest height.")]
        [SerializeField] private Transform handAnchor;

        [Header("Input")]
        [SerializeField] private float mouseSensitivity = 0.12f;
        [SerializeField] private bool invertPitch;
        [SerializeField] private bool lockCursor = true;

        private Rigidbody _body;
        private IFlightInputSource _input;
        private float _yaw;
        private float _pitch;
        private float _loadFactor = 1f;

        /// <summary>Current speed in m/s. Read by the camera rig and the HUD.</summary>
        public float Speed => _body != null ? _body.linearVelocity.magnitude : 0f;

        /// <summary>Coordinated-turn bank angle in degrees, for the camera to lean into.</summary>
        public float BankAngle { get; private set; }

        /// <summary>True while the body is holding altitude rather than falling.</summary>
        public bool IsHovering { get; private set; }

        /// <summary>Effective mass multiplier currently applied by whatever is being carried.</summary>
        public float LoadFactor => _loadFactor;

        /// <summary>Where the grab system anchors a carried body.</summary>
        public Transform HandAnchor => handAnchor != null ? handAnchor : transform;

        /// <summary>The input snapshot read this frame, shared with the grab system.</summary>
        public FlightInputSnapshot CurrentInput { get; private set; }

        public Rigidbody Body => _body;

        public FlightTuning Tuning => tuning;

        /// <summary>Camera-facing aim rotation, without the cosmetic bank.</summary>
        public Quaternion AimRotation => Quaternion.Euler(_pitch, _yaw, 0f);

        /// <summary>
        /// Effective mass multiplier from whatever is being carried. The grab system pushes
        /// this in; the solver divides thrust by it, so a carried adult really does make the
        /// body slower to accelerate and slower to climb.
        /// </summary>
        public void SetLoadFactor(float loadFactor)
        {
            _loadFactor = Mathf.Max(1f, loadFactor);
        }

        /// <summary>Swaps the input driver. Exists so tests and cutscenes can feed intent directly.</summary>
        public void SetInputSource(IFlightInputSource source)
        {
            _input = source;
        }

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();

            // We integrate gravity and drag ourselves so hover can cancel gravity cleanly and
            // so drag can be anisotropic. Unity's built-in isotropic drag would fight both.
            _body.useGravity = false;
            _body.linearDamping = 0f;
            _body.angularDamping = 0f;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _body.constraints = RigidbodyConstraints.FreezeRotation;

            if (_input == null)
            {
                _input = new DesktopFlightInput(mouseSensitivity, invertPitch);
            }

            Vector3 euler = transform.rotation.eulerAngles;
            _yaw = euler.y;
            _pitch = NormalizePitch(euler.x);
        }

        private void OnEnable()
        {
            if (lockCursor)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void OnDisable()
        {
            if (lockCursor)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void Update()
        {
            FlightInputSnapshot snapshot = _input.Read(Time.deltaTime);
            CurrentInput = snapshot;

            _yaw += snapshot.look.x;
            _pitch = Mathf.Clamp(_pitch + snapshot.look.y, minPitch, maxPitch);
        }

        private void FixedUpdate()
        {
            FlightInputSnapshot snapshot = CurrentInput;
            Quaternion aim = Quaternion.Euler(_pitch, _yaw, 0f);

            RotateToward(aim);

            // Hover is the resting state: let go of the throttle and the body parks in the air.
            IsHovering = snapshot.throttle <= 0.01f;

            FlightCommand command = new FlightCommand
            {
                aimDirection = BuildThrustDirection(aim, snapshot),
                throttle = BuildThrottle(snapshot),
                boost = snapshot.boost,
                airBrake = snapshot.airBrake,
                verticalTrim = snapshot.verticalTrim,
                hover = IsHovering,
            };

            FlightSolution solution = FlightModel.Solve(
                tuning, command, _body.rotation, _body.linearVelocity, _loadFactor);

            _body.AddForce(solution.acceleration, ForceMode.Acceleration);
            _body.AddForce(Physics.gravity * solution.residualGravityScale, ForceMode.Acceleration);

            BankAngle = Mathf.Lerp(BankAngle, solution.bankAngleDegrees, 1f - Mathf.Exp(-6f * Time.fixedDeltaTime));

            ClampSpeed();
        }

        /// <summary>
        /// While hovering, A/D translate the body directly. Under throttle the same keys steer
        /// the thrust vector instead, which is what lets you slalom at speed.
        /// </summary>
        private Vector3 BuildThrustDirection(Quaternion aim, in FlightInputSnapshot snapshot)
        {
            Vector3 forward = aim * Vector3.forward;
            if (snapshot.throttle <= 0.01f)
            {
                Vector3 lateral = aim * Vector3.right * snapshot.lateralTrim;
                return lateral.sqrMagnitude > 1e-6f ? lateral.normalized : forward;
            }

            Vector3 steer = aim * Vector3.right * (snapshot.lateralTrim * 0.45f);
            return (forward + steer).normalized;
        }

        private float BuildThrottle(in FlightInputSnapshot snapshot)
        {
            if (snapshot.throttle > 0.01f)
            {
                return snapshot.throttle;
            }

            // Hovering sideways still costs thrust, just a gentler amount.
            return Mathf.Abs(snapshot.lateralTrim) * 0.35f;
        }

        /// <summary>
        /// Rotates the body toward the aim at a speed-dependent rate. The body lagging the
        /// camera is deliberate: it produces real sideslip, which the anisotropic drag then
        /// bleeds off, and that is what a turn actually feels like.
        /// </summary>
        private void RotateToward(Quaternion aim)
        {
            float t = Mathf.Clamp01(_body.linearVelocity.magnitude / Mathf.Max(1f, turnRateReferenceSpeed));
            float rate = Mathf.Lerp(turnRateSlow, turnRateFast, t);
            _body.MoveRotation(Quaternion.RotateTowards(_body.rotation, aim, rate * Time.fixedDeltaTime));
        }

        private void ClampSpeed()
        {
            Vector3 velocity = _body.linearVelocity;
            float max = tuning.maxSpeed;
            if (velocity.sqrMagnitude > max * max)
            {
                _body.linearVelocity = velocity.normalized * max;
            }
        }

        private static float NormalizePitch(float pitch)
        {
            return pitch > 180f ? pitch - 360f : pitch;
        }
    }
}
