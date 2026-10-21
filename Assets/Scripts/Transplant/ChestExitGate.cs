using UnityEngine;

namespace VRSurgery.Transplant
{
    /// <summary>
    /// The opening a sternotomy leaves, as a rectangle on a plane over the chest.
    ///
    /// A real heart comes out between the two halves of the sternum, spread by the retractor; it
    /// does not pass through a rib. Nothing in the scene stops an organ from being carried through
    /// bone, so this is the rule that stands in for the bone: an organ leaving the chest has to
    /// cross the plane inside this rectangle.
    ///
    /// The transform's up axis points out of the chest, its X is across the opening and its Z runs
    /// along it. The width follows the sternotomy, so a chest that is still closed admits nothing.
    /// </summary>
    public class ChestExitGate : MonoBehaviour
    {
        [SerializeField] private SternotomyController sternotomy;

        [Tooltip("Half the width of the fully open gap, in metres, across the chest.")]
        [SerializeField, Min(0f)] private float halfWidth = 0.05f;

        [Tooltip("Half the length of the opening, in metres, along the sternum.")]
        [SerializeField, Min(0f)] private float halfLength = 0.1f;

        public const string BlockedMessage =
            "As costelas barram o coração: leve-o para fora pela abertura entre as metades do esterno";

        /// <summary>Half the width the gap has right now. Zero while the chest is closed.</summary>
        public float CurrentHalfWidth =>
            sternotomy != null ? halfWidth * Mathf.Clamp01(sternotomy.Openness01) : halfWidth;

        public float HalfLength => halfLength;

        /// <summary>Signed distance of a point from the plane; positive is outside the chest.</summary>
        public float HeightAbove(Vector3 worldPoint) => transform.InverseTransformPoint(worldPoint).y;

        /// <summary>True when the point lies over the opening, whatever its height.</summary>
        public bool Admits(Vector3 worldPoint)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            float width = CurrentHalfWidth;
            return width > 0f && Mathf.Abs(local.x) <= width && Mathf.Abs(local.z) <= halfLength;
        }

        public void Bind(SternotomyController controller, float openingHalfWidth, float openingHalfLength)
        {
            sternotomy = controller;
            halfWidth = Mathf.Max(0f, openingHalfWidth);
            halfLength = Mathf.Max(0f, openingHalfLength);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(halfWidth * 2f, 0f, halfLength * 2f));
        }
    }
}
