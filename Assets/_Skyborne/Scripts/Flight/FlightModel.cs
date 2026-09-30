using System;
using UnityEngine;

namespace Skyborne.Flight
{
    /// <summary>
    /// What the player is asking the body to do this frame, already normalized and
    /// decoupled from any particular input device.
    /// </summary>
    public struct FlightCommand
    {
        /// <summary>World-space direction the body should thrust along. Expected unit length.</summary>
        public Vector3 aimDirection;

        /// <summary>0..1 throttle along <see cref="aimDirection"/>.</summary>
        public float throttle;

        /// <summary>0..1 boost blended on top of throttle.</summary>
        public float boost;

        /// <summary>0..1 air brake. Spreads the body against the airflow.</summary>
        public float airBrake;

        /// <summary>-1..1 deliberate vertical translation while hovering.</summary>
        public float verticalTrim;

        /// <summary>True while the body holds altitude instead of falling.</summary>
        public bool hover;
    }

    /// <summary>
    /// Aerodynamic constants for the flying body. Plain serializable struct on purpose: the
    /// solver is a pure function of tuning + state, so it can be unit-tested with no scene,
    /// no Rigidbody and no frame loop.
    /// </summary>
    [Serializable]
    public struct FlightTuning
    {
        [Tooltip("Acceleration along the aim direction at full throttle (m/s^2).")]
        public float thrustAcceleration;

        [Tooltip("Multiplier applied to thrust while boosting.")]
        public float boostMultiplier;

        [Tooltip("Quadratic drag along the body's forward axis. Low: this is the streamlined axis.")]
        public float dragForward;

        [Tooltip("Quadratic drag across the body. High: kills sideslip so turns track the aim.")]
        public float dragLateral;

        [Tooltip("Quadratic drag along the body's up axis. Sets belly-down terminal velocity.")]
        public float dragVertical;

        [Tooltip("Extra drag on every axis at full air brake.")]
        public float airBrakeDrag;

        [Tooltip("Fraction of gravity cancelled while hovering. 1 = perfect float.")]
        [Range(0f, 1f)] public float hoverGravityCancel;

        [Tooltip("Vertical acceleration from the climb/dive trim while hovering (m/s^2).")]
        public float hoverTrimAcceleration;

        [Tooltip("Velocity damping applied while hovering, so the body settles instead of drifting.")]
        public float hoverDamping;

        [Tooltip("Degrees of roll per m/s^2 of lateral acceleration, for coordinated turns.")]
        public float rollPerLateralAcceleration;

        [Tooltip("Hard cap on roll angle (degrees).")]
        public float maxRollAngle;

        [Tooltip("Hard ceiling on speed (m/s). Drag normally bites first; this is a safety net.")]
        public float maxSpeed;

        /// <summary>Values that produce a fast, heavy, momentum-carrying flier.</summary>
        public static FlightTuning Default => new FlightTuning
        {
            thrustAcceleration = 85f,
            boostMultiplier = 2.2f,
            dragForward = 0.0085f,
            dragLateral = 0.18f,
            dragVertical = 0.006f,
            airBrakeDrag = 0.45f,
            hoverGravityCancel = 1f,
            hoverTrimAcceleration = 14f,
            hoverDamping = 2.2f,
            rollPerLateralAcceleration = 0.55f,
            maxRollAngle = 70f,
            maxSpeed = 240f,
        };
    }

    /// <summary>Result of one solver step, in world space.</summary>
    public struct FlightSolution
    {
        /// <summary>Total acceleration to apply this step, excluding gravity.</summary>
        public Vector3 acceleration;

        /// <summary>Gravity scale left acting on the body after hover assist (0..1).</summary>
        public float residualGravityScale;

        /// <summary>Roll angle the camera/body should bank to, in degrees.</summary>
        public float bankAngleDegrees;
    }

    /// <summary>
    /// The flight solver. Everything here is a pure function so the feel can be tuned and
    /// regression-tested without entering play mode.
    /// </summary>
    public static class FlightModel
    {
        /// <summary>Solves one step of flight.</summary>
        /// <param name="tuning">Aerodynamic constants.</param>
        /// <param name="command">Player intent for this step.</param>
        /// <param name="bodyRotation">Current orientation of the flying body.</param>
        /// <param name="velocity">Current world velocity (m/s).</param>
        /// <param name="loadFactor">
        /// Effective mass multiplier from anything being carried. 1 = empty handed.
        /// Thrust is divided by this, so hauling a person makes the body sluggish.
        /// </param>
        public static FlightSolution Solve(
            in FlightTuning tuning,
            in FlightCommand command,
            Quaternion bodyRotation,
            Vector3 velocity,
            float loadFactor)
        {
            loadFactor = Mathf.Max(1f, loadFactor);

            Vector3 thrust = SolveThrust(tuning, command, loadFactor);
            Vector3 drag = SolveDrag(tuning, command, bodyRotation, velocity);
            Vector3 hover = SolveHoverAssist(tuning, command, velocity, loadFactor);

            return new FlightSolution
            {
                acceleration = thrust + drag + hover,
                residualGravityScale = command.hover ? 1f - Mathf.Clamp01(tuning.hoverGravityCancel) : 1f,
                bankAngleDegrees = SolveBankAngle(tuning, bodyRotation, thrust),
            };
        }

        private static Vector3 SolveThrust(in FlightTuning tuning, in FlightCommand command, float loadFactor)
        {
            float throttle = Mathf.Clamp01(command.throttle);
            if (throttle <= 0f)
            {
                return Vector3.zero;
            }

            float boostScale = Mathf.Lerp(1f, Mathf.Max(1f, tuning.boostMultiplier), Mathf.Clamp01(command.boost));
            Vector3 direction = command.aimDirection.sqrMagnitude > 1e-6f
                ? command.aimDirection.normalized
                : Vector3.forward;

            // Carrying someone does not give you more muscle: the same thrust now has to move
            // more mass, so acceleration drops by the load factor.
            return direction * (tuning.thrustAcceleration * throttle * boostScale / loadFactor);
        }

        /// <summary>
        /// Quadratic drag resolved per body axis. The anisotropy is what makes the body feel
        /// streamlined head-first and draggy broadside, and it is what bleeds off sideslip so
        /// a turn ends up pointing where you aimed.
        /// </summary>
        private static Vector3 SolveDrag(
            in FlightTuning tuning,
            in FlightCommand command,
            Quaternion bodyRotation,
            Vector3 velocity)
        {
            float speed = velocity.magnitude;
            if (speed < 1e-4f)
            {
                return Vector3.zero;
            }

            Vector3 local = Quaternion.Inverse(bodyRotation) * velocity;

            float brake = Mathf.Clamp01(command.airBrake) * Mathf.Max(0f, tuning.airBrakeDrag);
            float kForward = Mathf.Max(0f, tuning.dragForward) + brake;
            float kLateral = Mathf.Max(0f, tuning.dragLateral) + brake;
            float kVertical = Mathf.Max(0f, tuning.dragVertical) + brake;

            // a = -k * |v| * v_axis  ->  magnitude scales with v^2, direction opposes motion.
            Vector3 localDrag = new Vector3(
                -kLateral * speed * local.x,
                -kVertical * speed * local.y,
                -kForward * speed * local.z);

            return bodyRotation * localDrag;
        }

        /// <summary>
        /// Station-keeping while hovering: damps residual velocity so the body parks in the air
        /// instead of sliding, plus the deliberate climb/dive trim.
        /// </summary>
        private static Vector3 SolveHoverAssist(
            in FlightTuning tuning,
            in FlightCommand command,
            Vector3 velocity,
            float loadFactor)
        {
            if (!command.hover)
            {
                return Vector3.zero;
            }

            Vector3 assist = Vector3.up * (command.verticalTrim * tuning.hoverTrimAcceleration / loadFactor);

            // Damping fades out as throttle comes in, otherwise it would fight the thrust.
            float damping = tuning.hoverDamping * (1f - Mathf.Clamp01(command.throttle));
            return assist - velocity * damping;
        }

        /// <summary>
        /// Coordinated turn: bank into the lateral component of thrust, the way a body that
        /// steers by leaning would. Purely cosmetic, but it is most of what sells the flight.
        /// </summary>
        private static float SolveBankAngle(in FlightTuning tuning, Quaternion bodyRotation, Vector3 thrust)
        {
            Vector3 localThrust = Quaternion.Inverse(bodyRotation) * thrust;
            float bank = -localThrust.x * tuning.rollPerLateralAcceleration;
            return Mathf.Clamp(bank, -tuning.maxRollAngle, tuning.maxRollAngle);
        }

        /// <summary>
        /// Terminal velocity along the streamlined axis for a given throttle, i.e. the speed
        /// where thrust and forward drag cancel. Used by tests and for tuning.
        /// </summary>
        public static float TerminalForwardSpeed(in FlightTuning tuning, float throttle, float boost, float loadFactor)
        {
            float boostScale = Mathf.Lerp(1f, Mathf.Max(1f, tuning.boostMultiplier), Mathf.Clamp01(boost));
            float thrust = tuning.thrustAcceleration * Mathf.Clamp01(throttle) * boostScale / Mathf.Max(1f, loadFactor);
            if (tuning.dragForward <= 0f)
            {
                return tuning.maxSpeed;
            }

            return Mathf.Min(tuning.maxSpeed, Mathf.Sqrt(thrust / tuning.dragForward));
        }
    }
}
