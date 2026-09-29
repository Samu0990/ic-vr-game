using System;
using UnityEngine;
using VRSurgery.Feedback;

namespace VRSurgery.Interaction
{
    /// <summary>
    /// What happens to an instrument once the visitor lets go of it.
    ///
    /// The visitor cannot walk at the stand, so nothing may end up out of reach. Two ways to
    /// guarantee that:
    ///
    /// <b>Glide</b> — the instrument floats back to its place on the tray after a moment.
    /// Tidy, but it reads as a menu rather than a thing in the world.
    ///
    /// <b>DropAndRespawn</b> — the Job Simulator way. Let go and it falls, bounces and lies where
    /// it landed; it can be thrown; picked up again from the drape or the tray, exactly as a real
    /// object would be. Only if it ends up somewhere the visitor cannot reach — on the floor,
    /// off the far side of the table, flung across the room — does it vanish and pop back into
    /// its place on the tray a moment later. The world behaves physically, and still nothing is
    /// ever lost.
    /// </summary>
    public class ReturnHomeOnRelease : MonoBehaviour
    {
        public enum ReleaseMode { Glide, DropAndRespawn }

        [SerializeField] private SurgicalInteractable interactable;
        [SerializeField] private ReleaseMode mode = ReleaseMode.Glide;

        [Tooltip("Seconds after release before the instrument starts back (Glide).")]
        [SerializeField, Min(0f)] private float delay = 0.8f;

        [Tooltip("Seconds the glide back takes (Glide).")]
        [SerializeField, Min(0.05f)] private float travelSeconds = 0.45f;

        [Header("Drop and respawn")]
        [Tooltip("Centre of what the visitor can reach: between their shoulders at the stance.")]
        [SerializeField] private Vector3 reachCentre;

        [Tooltip("Farther than this from the reach centre is out of reach, in metres.")]
        [SerializeField, Min(0.1f)] private float reachRadius = 0.8f;

        [Tooltip("Lower than this is on the floor or under the table, in world Y.")]
        [SerializeField] private float lostBelowY = 0.6f;

        [Tooltip("Seconds out of reach before it pops back, so a bounce off the edge can come back on its own.")]
        [SerializeField, Min(0f)] private float lostSeconds = 0.6f;

        [Tooltip("Places that count as lost even within reach: inside the patient, under the skin, " +
                 "where a dropped instrument would lie unseen.")]
        [SerializeField] private Bounds[] lostZones = new Bounds[0];

        [SerializeField, Min(0.05f)] private float popSeconds = 0.25f;

        private Vector3 _homePosition;
        private Quaternion _homeRotation;
        private Vector3 _homeScale = Vector3.one;
        private Rigidbody _body;
        private float _sinceRelease = -1f;
        private Vector3 _fromPosition;
        private Quaternion _fromRotation;

        private int _dropFrames;
        private bool _loose;
        private float _lostFor;
        private float _pop = -1f;

        public bool IsReturning => mode == ReleaseMode.Glide && _sinceRelease >= delay;

        /// <summary>True while the instrument is lying or falling somewhere other than its place.</summary>
        public bool IsLoose => _loose;

        public ReleaseMode Mode => mode;

        /// <summary>Raised when a lost instrument reappears in its place.</summary>
        public event Action Respawned;

        private void Awake()
        {
            if (interactable == null) { interactable = GetComponent<SurgicalInteractable>(); }
            _body = GetComponent<Rigidbody>();
            _homePosition = transform.position;
            _homeRotation = transform.rotation;
            _homeScale = transform.localScale;
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

        private void HandleGrabbed(SurgicalInteractable tool, IHandInteractor hand)
        {
            _sinceRelease = -1f;
            _dropFrames = 0;
            _loose = false;
            _lostFor = 0f;
            EndPop();
        }

        private void HandleReleased(SurgicalInteractable tool, IHandInteractor hand)
        {
            if (mode == ReleaseMode.Glide)
            {
                _sinceRelease = 0f;
                return;
            }

            // The grab restores the body's resting state during its own detach, after this event;
            // the drop is applied over the next two frames so that restore cannot undo it.
            _dropFrames = 2;
            _loose = true;
            _lostFor = 0f;
        }

        private void Update() => Tick(Time.deltaTime);

        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f) { return; }

            if (mode == ReleaseMode.DropAndRespawn)
            {
                TickDrop(deltaTime);
                return;
            }

            if (_sinceRelease < 0f) { return; }

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

        private void TickDrop(float deltaTime)
        {
            if (_pop >= 0f)
            {
                // Pops into place: grows from small with a little overshoot.
                _pop += deltaTime;
                float t = Mathf.Clamp01(_pop / popSeconds);
                float s = t < 1f ? Mathf.Sin(t * Mathf.PI * 0.6f) / Mathf.Sin(Mathf.PI * 0.6f) : 1f;
                transform.localScale = _homeScale * Mathf.Max(0.05f, s);
                if (t >= 1f) { EndPop(); }
            }

            if (_dropFrames > 0)
            {
                _dropFrames--;
                if (_body != null)
                {
                    _body.isKinematic = false;
                    _body.useGravity = true;
                }
            }

            if (!_loose || (interactable != null && interactable.IsHeld)) { return; }

            if (IsReachable(transform.position))
            {
                _lostFor = 0f;
                return;
            }

            _lostFor += deltaTime;
            if (_lostFor >= lostSeconds) { Respawn(); }
        }

        /// <summary>Somewhere the visitor could pick it up from without taking a step.</summary>
        public bool IsReachable(Vector3 point)
        {
            if (point.y < lostBelowY) { return false; }
            if (lostZones != null)
            {
                for (int i = 0; i < lostZones.Length; i++)
                {
                    if (lostZones[i].Contains(point)) { return false; }
                }
            }

            return (point - reachCentre).sqrMagnitude <= reachRadius * reachRadius;
        }

        /// <summary>Back in its place, still, with a pop.</summary>
        public void Respawn()
        {
            PlaceHome();
            _pop = 0f;
            transform.localScale = _homeScale * 0.05f;
            if (Application.isPlaying) { AudioSource.PlayClipAtPoint(ProceduralTones.Pop, _homePosition, 0.5f); }
            Respawned?.Invoke();
        }

        private void PlaceHome()
        {
            _sinceRelease = -1f;
            _dropFrames = 0;
            _loose = false;
            _lostFor = 0f;

            if (_body != null)
            {
                if (!_body.isKinematic)
                {
                    _body.linearVelocity = Vector3.zero;
                    _body.angularVelocity = Vector3.zero;
                }

                _body.isKinematic = true;
                _body.useGravity = false;
            }

            transform.SetPositionAndRotation(_homePosition, _homeRotation);
        }

        private void EndPop()
        {
            if (_pop < 0f) { return; }
            _pop = -1f;
            transform.localScale = _homeScale;
        }

        /// <summary>Straight back to its place, no animation: a new visitor gets a laid-out tray.</summary>
        public void SendHomeNow()
        {
            EndPop();
            PlaceHome();
        }

        public void Bind(SurgicalInteractable tool) => interactable = tool;

        /// <summary>Switches to the Job Simulator behaviour: drops where let go, pops back only when out of reach.</summary>
        public void UseDropAndRespawn(Vector3 reachFrom, float radius, float floorY, params Bounds[] hidden)
        {
            mode = ReleaseMode.DropAndRespawn;
            reachCentre = reachFrom;
            reachRadius = radius;
            lostBelowY = floorY;
            lostZones = hidden ?? new Bounds[0];
        }
    }
}
