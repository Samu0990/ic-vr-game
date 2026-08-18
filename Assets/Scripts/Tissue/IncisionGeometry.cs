using UnityEngine;

namespace VRSurgery.Tissue
{
    /// <summary>
    /// Pure geometry for cut detection. Deliberately has no Unity component dependencies
    /// so it can be unit-tested without a scene, and so the swept-segment logic stays in
    /// one place instead of being reimplemented per tool.
    ///
    /// Tissue local space convention: the surface is the XZ plane at y = 0, the outward
    /// normal is +Y, and the sheet spans [-halfExtents.x, +halfExtents.x] on X and
    /// [-halfExtents.y, +halfExtents.y] on Z.
    /// </summary>
    public static class IncisionGeometry
    {
        public struct SweepResult
        {
            /// <summary>Tip was inside the tissue sheet at the end of this sweep.</summary>
            public bool InContact;

            /// <summary>The sweep crossed the surface plane downward during this step.</summary>
            public bool CrossedSurface;

            /// <summary>Point on the surface plane where the sweep entered, in tissue local space.</summary>
            public Vector3 SurfacePoint;

            /// <summary>How far below the surface the tip ended up, normalised by maxPenetration.</summary>
            public float Depth01;

            /// <summary>Raw penetration in metres (positive = below the surface).</summary>
            public float Penetration;
        }

        /// <summary>
        /// Tests the segment previous -> current (both in tissue local space) against the
        /// tissue sheet. Using the whole segment rather than a point sample is what keeps a
        /// fast blade from tunnelling straight through the surface between physics steps.
        /// </summary>
        public static SweepResult Sweep(
            Vector3 previousLocal,
            Vector3 currentLocal,
            Vector2 halfExtents,
            float maxPenetration)
        {
            SweepResult result = default;

            if (maxPenetration <= 0f)
            {
                maxPenetration = 0.001f;
            }

            bool previousAbove = previousLocal.y > 0f;
            bool currentAbove = currentLocal.y > 0f;

            // Entry point: where the segment pierces y = 0, or the current point if the tip
            // was already submerged when the step began (a drag along an existing cut).
            Vector3 surfacePoint;
            if (previousAbove && !currentAbove)
            {
                float denominator = previousLocal.y - currentLocal.y;
                float t = Mathf.Approximately(denominator, 0f) ? 0f : previousLocal.y / denominator;
                t = Mathf.Clamp01(t);
                surfacePoint = Vector3.Lerp(previousLocal, currentLocal, t);
                result.CrossedSurface = true;
            }
            else
            {
                surfacePoint = new Vector3(currentLocal.x, 0f, currentLocal.z);
            }

            result.SurfacePoint = surfacePoint;

            if (currentAbove)
            {
                // Ended above the surface: no contact this step, even if it crossed and came back.
                return result;
            }

            if (!IsWithinSheet(surfacePoint, halfExtents))
            {
                return result;
            }

            result.Penetration = -currentLocal.y;
            result.Depth01 = Mathf.Clamp01(result.Penetration / maxPenetration);
            result.InContact = true;
            return result;
        }

        public static bool IsWithinSheet(Vector3 localPoint, Vector2 halfExtents)
        {
            return Mathf.Abs(localPoint.x) <= halfExtents.x
                && Mathf.Abs(localPoint.z) <= halfExtents.y;
        }

        /// <summary>
        /// Speed gate for cutting. Too slow reads as pressing rather than slicing; too fast is a
        /// slash the tissue should not cleanly accept. Returns a 0..1 quality factor.
        ///
        /// The usable band is wide and flat on purpose. A deliberate surgical stroke runs around
        /// 3-10 cm/s while the upper bound exists to reject wild swings, so a curve that peaked
        /// mid-band would score every controlled cut as mediocre.
        /// </summary>
        public static float EvaluateCutSpeed(float speed, float minimumCutSpeed, float maximumCutSpeed)
        {
            if (speed < minimumCutSpeed || speed > maximumCutSpeed)
            {
                return 0f;
            }

            // Ramp in just above the minimum, so barely-moving contact does not cut at full depth.
            float rampEnd = Mathf.Min(minimumCutSpeed * 2.5f, maximumCutSpeed);
            if (speed < rampEnd)
            {
                return Mathf.InverseLerp(minimumCutSpeed, rampEnd, speed);
            }

            // Taper off approaching the maximum: a fast slash still cuts, but less cleanly.
            float taperStart = maximumCutSpeed * 0.75f;
            if (speed > taperStart)
            {
                return Mathf.Lerp(1f, 0.4f, Mathf.InverseLerp(taperStart, maximumCutSpeed, speed));
            }

            return 1f;
        }

        /// <summary>
        /// Shortest distance from a point to a polyline, used to score how closely the player
        /// followed the guided incision path. Returns float.MaxValue for an empty path.
        /// </summary>
        public static float DistanceToPolyline(Vector3 point, System.Collections.Generic.IReadOnlyList<Vector3> path)
        {
            if (path == null || path.Count == 0)
            {
                return float.MaxValue;
            }

            if (path.Count == 1)
            {
                return Vector3.Distance(point, path[0]);
            }

            float best = float.MaxValue;
            for (int i = 0; i < path.Count - 1; i++)
            {
                float distance = DistanceToSegment(point, path[i], path[i + 1]);
                if (distance < best)
                {
                    best = distance;
                }
            }

            return best;
        }

        public static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float lengthSquared = ab.sqrMagnitude;
            if (lengthSquared <= Mathf.Epsilon)
            {
                return Vector3.Distance(point, a);
            }

            float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / lengthSquared);
            return Vector3.Distance(point, a + ab * t);
        }
    }
}
