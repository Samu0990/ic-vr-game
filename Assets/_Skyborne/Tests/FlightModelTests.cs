using NUnit.Framework;
using UnityEngine;
using Skyborne.Flight;

namespace Skyborne.Tests
{
    /// <summary>
    /// Covers the flight solver, which is pure maths and therefore the part worth pinning down.
    /// No scene, no Rigidbody, no play mode.
    /// </summary>
    public class FlightModelTests
    {
        private static FlightCommand Throttle(Vector3 aim, float throttle = 1f)
        {
            return new FlightCommand { aimDirection = aim, throttle = throttle };
        }

        [Test]
        public void FullThrottleFromRest_AcceleratesAlongAimAtTunedRate()
        {
            FlightTuning tuning = FlightTuning.Default;

            FlightSolution solution = FlightModel.Solve(
                tuning, Throttle(Vector3.forward), Quaternion.identity, Vector3.zero, 1f);

            Assert.That(solution.acceleration.z, Is.EqualTo(tuning.thrustAcceleration).Within(0.01f));
            Assert.That(solution.acceleration.x, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void LoadFactor_DividesThrust()
        {
            FlightTuning tuning = FlightTuning.Default;

            FlightSolution empty = FlightModel.Solve(
                tuning, Throttle(Vector3.forward), Quaternion.identity, Vector3.zero, 1f);
            FlightSolution loaded = FlightModel.Solve(
                tuning, Throttle(Vector3.forward), Quaternion.identity, Vector3.zero, 2f);

            Assert.That(loaded.acceleration.z, Is.EqualTo(empty.acceleration.z * 0.5f).Within(0.01f));
        }

        [Test]
        public void LoadFactorBelowOne_IsClampedAndNeverGivesFreeThrust()
        {
            FlightTuning tuning = FlightTuning.Default;

            FlightSolution normal = FlightModel.Solve(
                tuning, Throttle(Vector3.forward), Quaternion.identity, Vector3.zero, 1f);
            FlightSolution cheated = FlightModel.Solve(
                tuning, Throttle(Vector3.forward), Quaternion.identity, Vector3.zero, 0.1f);

            Assert.That(cheated.acceleration.z, Is.EqualTo(normal.acceleration.z).Within(0.01f));
        }

        [Test]
        public void Drag_OpposesVelocity()
        {
            FlightTuning tuning = FlightTuning.Default;
            Vector3 velocity = new Vector3(0f, 0f, 60f);

            FlightSolution solution = FlightModel.Solve(
                tuning, new FlightCommand { aimDirection = Vector3.forward }, Quaternion.identity, velocity, 1f);

            Assert.That(solution.acceleration.z, Is.LessThan(0f));
        }

        [Test]
        public void Drag_IsAnisotropic_SidewaysBleedsFasterThanForward()
        {
            FlightTuning tuning = FlightTuning.Default;
            const float speed = 40f;

            FlightSolution forward = FlightModel.Solve(
                tuning, default, Quaternion.identity, new Vector3(0f, 0f, speed), 1f);
            FlightSolution sideways = FlightModel.Solve(
                tuning, default, Quaternion.identity, new Vector3(speed, 0f, 0f), 1f);

            Assert.That(Mathf.Abs(sideways.acceleration.x), Is.GreaterThan(Mathf.Abs(forward.acceleration.z) * 5f),
                "Sideslip must bleed off far faster than forward speed, or turns feel like ice.");
        }

        [Test]
        public void AirBrake_IncreasesForwardDrag()
        {
            FlightTuning tuning = FlightTuning.Default;
            Vector3 velocity = new Vector3(0f, 0f, 80f);

            FlightSolution coasting = FlightModel.Solve(tuning, default, Quaternion.identity, velocity, 1f);
            FlightSolution braking = FlightModel.Solve(
                tuning, new FlightCommand { airBrake = 1f }, Quaternion.identity, velocity, 1f);

            Assert.That(braking.acceleration.z, Is.LessThan(coasting.acceleration.z));
        }

        [Test]
        public void Hover_CancelsGravity()
        {
            FlightTuning tuning = FlightTuning.Default;

            FlightSolution hovering = FlightModel.Solve(
                tuning, new FlightCommand { hover = true }, Quaternion.identity, Vector3.zero, 1f);
            FlightSolution falling = FlightModel.Solve(
                tuning, new FlightCommand { hover = false }, Quaternion.identity, Vector3.zero, 1f);

            Assert.That(hovering.residualGravityScale, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(falling.residualGravityScale, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void Hover_DampsDrift()
        {
            FlightTuning tuning = FlightTuning.Default;
            Vector3 drift = new Vector3(3f, 0f, 0f);

            FlightSolution solution = FlightModel.Solve(
                tuning, new FlightCommand { hover = true }, Quaternion.identity, drift, 1f);

            Assert.That(solution.acceleration.x, Is.LessThan(0f), "Hovering must kill sideways drift.");
        }

        [Test]
        public void TerminalForwardSpeed_IsWhereThrustEqualsDrag()
        {
            FlightTuning tuning = FlightTuning.Default;

            float terminal = FlightModel.TerminalForwardSpeed(tuning, 1f, 0f, 1f);

            // At terminal velocity the net forward acceleration must be ~0.
            FlightSolution solution = FlightModel.Solve(
                tuning, Throttle(Vector3.forward), Quaternion.identity, new Vector3(0f, 0f, terminal), 1f);

            Assert.That(solution.acceleration.z, Is.EqualTo(0f).Within(0.05f));
        }

        [Test]
        public void Boost_RaisesTerminalSpeed()
        {
            FlightTuning tuning = FlightTuning.Default;

            float cruise = FlightModel.TerminalForwardSpeed(tuning, 1f, 0f, 1f);
            float boosted = FlightModel.TerminalForwardSpeed(tuning, 1f, 1f, 1f);

            Assert.That(boosted, Is.GreaterThan(cruise));
        }

        [Test]
        public void CarryingSomeone_LowersTopSpeed()
        {
            FlightTuning tuning = FlightTuning.Default;

            float empty = FlightModel.TerminalForwardSpeed(tuning, 1f, 0f, 1f);
            float loaded = FlightModel.TerminalForwardSpeed(tuning, 1f, 0f, 1.8f);

            Assert.That(loaded, Is.LessThan(empty));
        }

        [Test]
        public void LateralThrust_BanksTheBody()
        {
            FlightTuning tuning = FlightTuning.Default;

            FlightSolution right = FlightModel.Solve(
                tuning, Throttle(Vector3.right), Quaternion.identity, Vector3.zero, 1f);
            FlightSolution left = FlightModel.Solve(
                tuning, Throttle(Vector3.left), Quaternion.identity, Vector3.zero, 1f);

            Assert.That(right.bankAngleDegrees, Is.Not.EqualTo(0f).Within(0.5f));
            Assert.That(Mathf.Sign(right.bankAngleDegrees), Is.Not.EqualTo(Mathf.Sign(left.bankAngleDegrees)));
        }

        [Test]
        public void BankAngle_IsClampedToTuning()
        {
            FlightTuning tuning = FlightTuning.Default;
            tuning.rollPerLateralAcceleration = 50f;

            FlightSolution solution = FlightModel.Solve(
                tuning, Throttle(Vector3.right), Quaternion.identity, Vector3.zero, 1f);

            Assert.That(Mathf.Abs(solution.bankAngleDegrees), Is.LessThanOrEqualTo(tuning.maxRollAngle + 1e-3f));
        }

        [Test]
        public void ZeroThrottle_ProducesNoThrust()
        {
            FlightTuning tuning = FlightTuning.Default;

            FlightSolution solution = FlightModel.Solve(
                tuning, Throttle(Vector3.forward, 0f), Quaternion.identity, Vector3.zero, 1f);

            Assert.That(solution.acceleration.magnitude, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void DegenerateAimDirection_DoesNotProduceNaN()
        {
            FlightTuning tuning = FlightTuning.Default;

            FlightSolution solution = FlightModel.Solve(
                tuning, Throttle(Vector3.zero), Quaternion.identity, Vector3.zero, 1f);

            Assert.That(float.IsNaN(solution.acceleration.x), Is.False);
            Assert.That(solution.acceleration.magnitude, Is.GreaterThan(0f));
        }
    }
}
