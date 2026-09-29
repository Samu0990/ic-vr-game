using UnityEngine;

namespace VRSurgery.Interaction
{
    /// <summary>
    /// Sends a let-go instrument back to its place on the tray.
    ///
    /// The transplant's patient is under drapes with no collider — there is nothing over the
    /// chest for a dropped scalpel to land on, so letting it fall would put it through the patient
    /// and onto the floor, out of a visitor's reach for the rest of the round. A theatre nurse
    /// takes the instrument back; this does the same after a short pause, gliding rather than
    /// teleporting so the visitor sees where it went.
    /// </summary>
    public class ReturnHomeOnRelease : MonoBehaviour
    {
        [SerializeField] private SurgicalInteractable interactable;

        [Tooltip("Seconds after release before the instrument starts back.")]
        [SerializeField, Min(0f)] private float delay = 0.8f;

        [Tooltip("Seconds the glide back takes.")]
        [SerializeField, Min(0.05f)] private float travelSeconds = 0.45f;

        private Vector3 _homePosition;
        private Quaternion _homeRotation;
        private Rigidbody _body;
        private float _sinceRelease = -1f;
        private Vector3 _fromPosition;
        private Quaternion _fromRotation;

        /// <summary>True while it is on its way back.</summary>
        public bool IsReturning => _sinceRelease >= delay;

        private void Awake()
        {
            if (interactable == null) { interactable = GetComponent<SurgicalInteractable>(); }
            _body = GetComponent<Rigidbody>();
            _homePosition = transform.position;
            _homeRotation = transform.rotation;
        }

        private void OnEnable()
        {
            if (interactable == null) { return; }
            interactable.Grabbed += HandleGrabbed;
            interactable.Released += HandleReleased;
        }

        private void OnDisable()
        {
            if (interactable == null) { return; }
            interactable.Grabbed -= HandleGrabbed;
            interactable.Released -= HandleReleased;
        }

        private void HandleGrabbed(SurgicalInteractable tool, IHandInteractor hand) => _sinceRelease = -1f;

        private void HandleReleased(SurgicalInteractable tool, IHandInteractor hand) => _sinceRelease = 0f;

        private void Update() => Tick(Time.deltaTime);

        public void Tick(float deltaTime)
        {
            if (_sinceRelease < 0f || deltaTime <= 0f) { return; }

            float before = _sinceRelease;
            _sinceRelease += deltaTime;

            if (_sinceRelease < delay) { return; }

            if (before < delay)
            {
                _fromPosition = transform.position;
                _fromRotation = transform.rotation;
                if (_body != null)
                {
                    _body.isKinematic = true;
                    _body.useGravity = false;
                }
            }

            float t = Mathf.Clamp01((_sinceRelease - delay) / travelSeconds);
            float eased = Mathf.SmoothStep(0f, 1f, t);
            transform.SetPositionAndRotation(
                Vector3.Lerp(_fromPosition, _homePosition, eased),
                Quaternion.Slerp(_fromRotation, _homeRotation, eased));

            if (t >= 1f) { _sinceRelease = -1f; }
        }

        /// <summary>Puts it back at once. Used between visitors.</summary>
        public void SendHomeNow()
        {
            _sinceRelease = -1f;
            transform.SetPositionAndRotation(_homePosition, _homeRotation);
        }

        public void Bind(SurgicalInteractable tool) => interactable = tool;
    }
}
