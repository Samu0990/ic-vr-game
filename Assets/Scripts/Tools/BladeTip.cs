using UnityEngine;

namespace VRSurgery.Tools
{
    /// <summary>
    /// Explicit reference point for a cutting tool's functional tip. Cut detection must
    /// track this transform frame-to-frame (swept segment, not a single-point check) —
    /// never substitute the tool's own transform.position, which sits at the handle.
    ///
    /// The tip does not advance its own history in FixedUpdate: whoever consumes the segment
    /// calls <see cref="Sample"/>, which returns the segment and advances in one step. Letting
    /// the tip self-advance made the result depend on script execution order, and a consumer
    /// running after it would always see previous == current (i.e. a zero-length segment that
    /// can never intersect anything).
    /// </summary>
    public class BladeTip : MonoBehaviour
    {
        private Vector3 _previousPosition;
        private bool _hasPreviousPosition;

        public Vector3 PreviousPosition => _hasPreviousPosition ? _previousPosition : transform.position;
        public Vector3 CurrentPosition => transform.position;
        public Vector3 MovementDelta => CurrentPosition - PreviousPosition;

        private void OnEnable()
        {
            ResetHistory();
        }

        /// <summary>
        /// Returns the swept segment since the last call and advances the history.
        /// Call exactly once per physics step from the consuming system.
        /// </summary>
        public void Sample(out Vector3 previous, out Vector3 current)
        {
            current = transform.position;
            previous = _hasPreviousPosition ? _previousPosition : current;

            _previousPosition = current;
            _hasPreviousPosition = true;
        }

        /// <summary>Reads the current segment without advancing it. For debug overlays and tests.</summary>
        public void PeekSegment(out Vector3 previous, out Vector3 current)
        {
            current = transform.position;
            previous = _hasPreviousPosition ? _previousPosition : current;
        }

        /// <summary>
        /// Drops the recorded history so the next Sample reports a zero-length segment.
        /// Call after teleporting the tool (grab snap, reset) to avoid a false cut sweeping
        /// across the room between the old and new position.
        /// </summary>
        public void ResetHistory()
        {
            _previousPosition = transform.position;
            _hasPreviousPosition = false;
        }
    }
}
