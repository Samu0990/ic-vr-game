using System.Collections.Generic;
using UnityEngine;

namespace VRSurgery.Tissue
{
    /// <summary>
    /// The intended incision path for a procedure, expressed in tissue local space.
    /// The player is never required to trace it exactly — this only supplies a reference
    /// the evaluation system measures deviation against.
    /// </summary>
    public class IncisionGuide : MonoBehaviour
    {
        [Header("Path (tissue local space, y is ignored)")]
        [SerializeField] private Vector3 startPoint = new Vector3(-0.06f, 0f, 0f);
        [SerializeField] private Vector3 endPoint = new Vector3(0.06f, 0f, 0f);
        [SerializeField, Min(2)] private int sampleCount = 8;

        [Header("Tolerances")]
        // Surgical, not forgiving. The guided path is 9 cm long; the previous failure band was
        // 5 cm, which is more than half that length — a cut running almost perpendicular to the
        // guide still passed, so accuracy was never actually demanded of the player.
        //
        // These are the real thing: a 1.5 mm deviation is a clean line, 4 mm is a surgeon having
        // a bad day, and 10 mm is off the mark. Expect most first attempts to miss.
        [Tooltip("Deviation that still counts as a clean line, in metres.")]
        [SerializeField] private float perfectTolerance = 0.0015f;

        [Tooltip("Deviation that still scores, in metres.")]
        [SerializeField] private float acceptableTolerance = 0.004f;

        [Tooltip("Deviation that fails the incision, in metres.")]
        [SerializeField] private float failureTolerance = 0.010f;

        private readonly List<Vector3> _path = new List<Vector3>();

        public IReadOnlyList<Vector3> Path
        {
            get
            {
                if (_path.Count == 0)
                {
                    RebuildPath();
                }

                return _path;
            }
        }

        public float PerfectTolerance => perfectTolerance;
        public float AcceptableTolerance => acceptableTolerance;
        public float FailureTolerance => failureTolerance;

        /// <summary>Straight-line length of the guided path, in metres.</summary>
        public float PathLength
        {
            get
            {
                IReadOnlyList<Vector3> path = Path;
                float total = 0f;
                for (int i = 0; i < path.Count - 1; i++)
                {
                    total += Vector3.Distance(path[i], path[i + 1]);
                }

                return total;
            }
        }

        private void Awake()
        {
            RebuildPath();
        }

        public void RebuildPath()
        {
            _path.Clear();
            int count = Mathf.Max(2, sampleCount);
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                Vector3 point = Vector3.Lerp(startPoint, endPoint, t);
                point.y = 0f;
                _path.Add(point);
            }
        }

        public void Configure(Vector3 start, Vector3 end, int samples = 8)
        {
            startPoint = start;
            endPoint = end;
            sampleCount = Mathf.Max(2, samples);
            RebuildPath();
        }

        /// <summary>
        /// Sets the accuracy bands explicitly. The scene is generated from code, so the numbers
        /// that decide whether a cut is good belong in the build alongside the path itself rather
        /// than in whatever the prefab's serialized defaults happen to be.
        /// </summary>
        public void SetTolerances(float perfect, float acceptable, float failure)
        {
            perfectTolerance = Mathf.Max(0.0001f, perfect);
            acceptableTolerance = Mathf.Max(perfectTolerance, acceptable);
            failureTolerance = Mathf.Max(acceptableTolerance, failure);
        }

        public float DeviationAt(Vector3 localPoint)
        {
            Vector3 flattened = new Vector3(localPoint.x, 0f, localPoint.z);
            return IncisionGeometry.DistanceToPolyline(flattened, Path);
        }

        /// <summary>Maps a deviation in metres onto a 0..1 accuracy score.</summary>
        public float ScoreDeviation(float deviation)
        {
            if (deviation <= perfectTolerance)
            {
                return 1f;
            }

            if (deviation >= failureTolerance)
            {
                return 0f;
            }

            if (deviation <= acceptableTolerance)
            {
                float span = Mathf.Max(0.0001f, acceptableTolerance - perfectTolerance);
                return Mathf.Lerp(1f, 0.6f, (deviation - perfectTolerance) / span);
            }

            float outerSpan = Mathf.Max(0.0001f, failureTolerance - acceptableTolerance);
            return Mathf.Lerp(0.6f, 0f, (deviation - acceptableTolerance) / outerSpan);
        }

        public bool IsOutsideValidArea(float deviation) => deviation > failureTolerance;
    }
}
