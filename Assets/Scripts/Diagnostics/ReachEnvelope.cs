using UnityEngine;

namespace VRSurgery.Diagnostics
{
    public enum ReachClass
    {
        /// <summary>Close enough for fine, controlled manipulation.</summary>
        Precision,

        /// <summary>Easy to grab, but not where delicate work should happen.</summary>
        Comfortable,

        /// <summary>Reachable only by extending and leaning. Causes shoulder strain over time.</summary>
        Strained,

        /// <summary>Cannot be reached without stepping — forbidden for anything the procedure needs.</summary>
        OutOfReach,
    }

    /// <summary>
    /// Ergonomic model of what the player can actually reach while standing still.
    ///
    /// This exists because the workstation layout is a gameplay system, not decoration: the
    /// design forbids locomotion, so every object the procedure needs must be inside the arm
    /// envelope. Numbers are approximations for an adult at the calibrated eye height and are
    /// meant to catch layout mistakes early, not to model anatomy.
    /// </summary>
    public static class ReachEnvelope
    {
        /// <summary>Half the distance between shoulder joints.</summary>
        public const float ShoulderHalfWidth = 0.19f;

        /// <summary>How far the shoulder sits below eye level.</summary>
        public const float ShoulderDropFromEye = 0.25f;

        /// <summary>Shoulders sit slightly behind the eyes.</summary>
        public const float ShoulderBehindEye = 0.04f;

        public const float PrecisionReach = 0.55f;
        public const float ComfortableReach = 0.70f;
        public const float MaximumReach = 0.82f;

        /// <summary>
        /// Shoulder position derived from the head pose. Yaw is taken from the head so the
        /// envelope follows the player turning on the spot, which is allowed; pitch and roll
        /// are ignored because looking down does not move the shoulders.
        /// </summary>
        public static Vector3 ShoulderPosition(Vector3 eyePosition, Quaternion headRotation, bool leftShoulder)
        {
            Vector3 flatForward = Vector3.ProjectOnPlane(headRotation * Vector3.forward, Vector3.up);
            if (flatForward.sqrMagnitude < 1e-6f)
            {
                flatForward = Vector3.forward;
            }

            flatForward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, flatForward).normalized;

            float side = leftShoulder ? -ShoulderHalfWidth : ShoulderHalfWidth;

            return eyePosition
                 + right * side
                 - Vector3.up * ShoulderDropFromEye
                 - flatForward * ShoulderBehindEye;
        }

        /// <summary>Distance from whichever shoulder is closer — a two-handed player uses both.</summary>
        public static float DistanceFromNearestShoulder(Vector3 eyePosition, Quaternion headRotation, Vector3 target)
        {
            float left = Vector3.Distance(ShoulderPosition(eyePosition, headRotation, true), target);
            float right = Vector3.Distance(ShoulderPosition(eyePosition, headRotation, false), target);
            return Mathf.Min(left, right);
        }

        public static ReachClass Classify(float distanceFromShoulder)
        {
            if (distanceFromShoulder <= PrecisionReach) return ReachClass.Precision;
            if (distanceFromShoulder <= ComfortableReach) return ReachClass.Comfortable;
            if (distanceFromShoulder <= MaximumReach) return ReachClass.Strained;
            return ReachClass.OutOfReach;
        }

        public static ReachClass ClassifyTarget(Vector3 eyePosition, Quaternion headRotation, Vector3 target) =>
            Classify(DistanceFromNearestShoulder(eyePosition, headRotation, target));

        /// <summary>
        /// Downward gaze angle in degrees needed to look at a point. Sustained neck flexion is
        /// the fastest way to make a stationary VR experience uncomfortable, so the operative
        /// field must not sit steeply below the player.
        /// </summary>
        public static float GazePitchDegrees(Vector3 eyePosition, Quaternion headRotation, Vector3 target)
        {
            Vector3 toTarget = target - eyePosition;
            if (toTarget.sqrMagnitude < 1e-8f)
            {
                return 0f;
            }

            Vector3 flatForward = Vector3.ProjectOnPlane(headRotation * Vector3.forward, Vector3.up);
            if (flatForward.sqrMagnitude < 1e-6f)
            {
                flatForward = Vector3.forward;
            }

            flatForward.Normalize();

            Vector3 flatToTarget = Vector3.ProjectOnPlane(toTarget, Vector3.up);
            float horizontal = flatToTarget.magnitude;
            float vertical = toTarget.y;

            return -Mathf.Atan2(vertical, horizontal) * Mathf.Rad2Deg;
        }

        /// <summary>Horizontal angle off the player's forward axis, in degrees.</summary>
        public static float GazeYawDegrees(Vector3 eyePosition, Quaternion headRotation, Vector3 target)
        {
            Vector3 flatForward = Vector3.ProjectOnPlane(headRotation * Vector3.forward, Vector3.up);
            if (flatForward.sqrMagnitude < 1e-6f)
            {
                flatForward = Vector3.forward;
            }

            flatForward.Normalize();

            Vector3 flatToTarget = Vector3.ProjectOnPlane(target - eyePosition, Vector3.up);
            if (flatToTarget.sqrMagnitude < 1e-8f)
            {
                return 0f;
            }

            return Vector3.Angle(flatForward, flatToTarget.normalized);
        }
    }
}
