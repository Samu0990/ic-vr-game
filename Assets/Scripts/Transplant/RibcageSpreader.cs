using UnityEngine;

namespace VRSurgery.Transplant
{
    /// <summary>
    /// Spreads the two sides of the ribcage with the sternotomy. The sternum alone moving out of
    /// the way leaves the thoracic cage visually closed and makes the heart look unreachable;
    /// a Finochietto retracts the cut sternum and the attached ribs together.
    /// </summary>
    public class RibcageSpreader : MonoBehaviour
    {
        [SerializeField] private SternotomyController sternotomy;
        [SerializeField] private Transform leftSide;
        [SerializeField] private Transform rightSide;

        [Tooltip("Outward travel of each side at full opening, in metres.")]
        [SerializeField, Min(0f)] private float openSpread = 0.055f;

        [Tooltip("Small outward roll of each side, in degrees, so the opening reads as a retracted chest rather than sliding bones.")]
        [SerializeField, Range(0f, 25f)] private float openRoll = 8f;

        private Vector3 _leftClosed;
        private Vector3 _rightClosed;
        private Quaternion _leftRotation;
        private Quaternion _rightRotation;
        private bool _captured;
        private float _drawn = -1f;

        public float SpreadPerSide => openSpread;

        private void Awake()
        {
            CaptureClosedPose();
            Apply(sternotomy != null ? sternotomy.Openness01 : 0f);
        }

        private void LateUpdate() => Apply(sternotomy != null ? sternotomy.Openness01 : 0f);

        private void CaptureClosedPose()
        {
            if (_captured || leftSide == null || rightSide == null) { return; }
            _leftClosed = leftSide.localPosition;
            _rightClosed = rightSide.localPosition;
            _leftRotation = leftSide.localRotation;
            _rightRotation = rightSide.localRotation;
            _captured = true;
        }

        public void Apply(float openness)
        {
            CaptureClosedPose();
            if (!_captured) { return; }

            openness = Mathf.Clamp01(openness);
            if (Mathf.Approximately(openness, _drawn)) { return; }
            _drawn = openness;

            float eased = Mathf.SmoothStep(0f, 1f, openness);
            float spread = openSpread * eased;
            float roll = openRoll * eased;

            leftSide.localPosition = _leftClosed + Vector3.left * spread;
            rightSide.localPosition = _rightClosed + Vector3.right * spread;
            leftSide.localRotation = _leftRotation * Quaternion.Euler(0f, 0f, roll);
            rightSide.localRotation = _rightRotation * Quaternion.Euler(0f, 0f, -roll);
        }

        public void Bind(SternotomyController controller, Transform left, Transform right)
        {
            sternotomy = controller;
            leftSide = left;
            rightSide = right;
            _captured = false;
            _drawn = -1f;
            CaptureClosedPose();
        }
    }
}
