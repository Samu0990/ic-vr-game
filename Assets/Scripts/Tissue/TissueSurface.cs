using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRSurgery.Tissue
{
    /// <summary>
    /// A cuttable sheet of tissue. Owns the tissue's dimensions, its accumulated damage and
    /// its incision state — but not the visual mesh generation (see <see cref="WoundRenderer"/>)
    /// and not the cut detection (see <see cref="IncisionSystem"/>). The surface is the XZ
    /// plane of this transform at local y = 0, with +Y as the outward normal.
    /// </summary>
    public class TissueSurface : MonoBehaviour
    {
        [Header("Dimensions")]
        [Tooltip("Half-size of the sheet on local X and local Z, in metres.")]
        [SerializeField] private Vector2 halfExtents = new Vector2(0.09f, 0.06f);

        [Tooltip("Penetration at which depth reads as 1.0. Gameplay abstraction, not a real force model.")]
        [SerializeField] private float maxPenetration = 0.02f;

        [Header("Thresholds")]
        [SerializeField, Range(0f, 1f)] private float superficialThreshold = 0.15f;
        [SerializeField, Range(0f, 1f)] private float openThreshold = 0.35f;
        [SerializeField, Range(0f, 1f)] private float bleedingThreshold = 0.5f;
        [SerializeField, Range(0f, 1f)] private float damageThreshold = 0.85f;

        [Header("Incision recording")]
        [Tooltip("Minimum travel between recorded incision points, in metres.")]
        [SerializeField] private float pointSpacing = 0.004f;

        private readonly List<Vector3> _incisionPoints = new List<Vector3>();
        private readonly List<float> _incisionDepths = new List<float>();

        public Vector2 HalfExtents => halfExtents;
        public float MaxPenetration => maxPenetration;
        public float BleedingThreshold => bleedingThreshold;

        public IncisionState State { get; private set; } = IncisionState.Intact;

        /// <summary>Total length of the recorded incision polyline, in metres.</summary>
        public float IncisionLength { get; private set; }

        /// <summary>Deepest normalised penetration reached so far.</summary>
        public float PeakDepth01 { get; private set; }

        /// <summary>Accumulated unnecessary trauma, 0..1. Driven by over-deep cuts.</summary>
        public float Damage01 { get; private set; }

        public IReadOnlyList<Vector3> IncisionPoints => _incisionPoints;
        public IReadOnlyList<float> IncisionDepths => _incisionDepths;
        public bool HasIncision => _incisionPoints.Count > 0;

        public event Action<TissueSurface, IncisionState> StateChanged;
        public event Action<TissueSurface, Vector3, float> IncisionPointAdded;

        public Vector3 WorldToTissueLocal(Vector3 worldPoint) => transform.InverseTransformPoint(worldPoint);

        public Vector3 TissueLocalToWorld(Vector3 localPoint) => transform.TransformPoint(localPoint);

        /// <summary>
        /// Distance from a world point to the recorded incision, in metres, measured against the
        /// polyline rather than the nearest recorded vertex — points are only laid down every few
        /// millimetres, so a vertex-only test would report a sawtooth distance along the cut.
        ///
        /// Returns <see cref="float.PositiveInfinity"/> while no incision exists: there is nothing
        /// to press on yet, and returning 0 would make an untouched patient count as a hit.
        /// </summary>
        public float DistanceToIncision(Vector3 worldPoint)
        {
            if (_incisionPoints.Count == 0)
            {
                return float.PositiveInfinity;
            }

            Vector3 local = WorldToTissueLocal(worldPoint);
            local.y = 0f;

            if (_incisionPoints.Count == 1)
            {
                return Vector3.Distance(local, _incisionPoints[0]);
            }

            return IncisionGeometry.DistanceToPolyline(local, _incisionPoints);
        }

        public void Configure(Vector2 newHalfExtents, float newMaxPenetration)
        {
            halfExtents = newHalfExtents;
            maxPenetration = Mathf.Max(0.001f, newMaxPenetration);
        }

        /// <summary>
        /// Records a point of contact along the incision. Returns true when the point was far
        /// enough from the previous one to be added to the polyline (which keeps the wound from
        /// accumulating hundreds of coincident vertices while the blade is held still).
        /// </summary>
        public bool ApplyIncision(Vector3 localPoint, float depth01)
        {
            localPoint.y = 0f;
            depth01 = Mathf.Clamp01(depth01);

            PeakDepth01 = Mathf.Max(PeakDepth01, depth01);

            if (depth01 > damageThreshold)
            {
                // Everything past the damage threshold contributes trauma, scaled by how far past.
                float excess = (depth01 - damageThreshold) / Mathf.Max(0.0001f, 1f - damageThreshold);
                Damage01 = Mathf.Clamp01(Damage01 + excess * 0.05f);
            }

            bool added = false;
            if (_incisionPoints.Count == 0)
            {
                _incisionPoints.Add(localPoint);
                _incisionDepths.Add(depth01);
                added = true;
            }
            else
            {
                Vector3 last = _incisionPoints[_incisionPoints.Count - 1];
                float travelled = Vector3.Distance(last, localPoint);
                if (travelled >= pointSpacing)
                {
                    _incisionPoints.Add(localPoint);
                    _incisionDepths.Add(depth01);
                    IncisionLength += travelled;
                    added = true;
                }
                else
                {
                    // Still deepen the existing point even if the blade barely moved.
                    _incisionDepths[_incisionDepths.Count - 1] =
                        Mathf.Max(_incisionDepths[_incisionDepths.Count - 1], depth01);
                }
            }

            EvaluateState();

            if (added)
            {
                IncisionPointAdded?.Invoke(this, localPoint, depth01);
            }

            return added;
        }

        private void EvaluateState()
        {
            IncisionState next = State;

            if (Damage01 >= 0.6f)
            {
                next = IncisionState.Damaged;
            }
            else if (PeakDepth01 >= bleedingThreshold)
            {
                next = IncisionState.Bleeding;
            }
            else if (PeakDepth01 >= openThreshold)
            {
                next = IncisionState.Open;
            }
            else if (PeakDepth01 >= superficialThreshold)
            {
                next = IncisionState.Superficial;
            }

            SetState(next);
        }

        public void SetState(IncisionState next)
        {
            if (next == State)
            {
                return;
            }

            State = next;
            StateChanged?.Invoke(this, State);
        }

        public void ResetTissue()
        {
            _incisionPoints.Clear();
            _incisionDepths.Clear();
            IncisionLength = 0f;
            PeakDepth01 = 0f;
            Damage01 = 0f;
            State = IncisionState.Intact;
            StateChanged?.Invoke(this, State);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.2f, 1f, 0.6f, 0.5f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(halfExtents.x * 2f, 0.0005f, halfExtents.y * 2f));
            Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.4f);
            Gizmos.DrawWireCube(
                new Vector3(0f, -maxPenetration * 0.5f, 0f),
                new Vector3(halfExtents.x * 2f, maxPenetration, halfExtents.y * 2f));
        }
#endif
    }
}
