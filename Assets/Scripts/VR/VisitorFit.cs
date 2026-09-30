using UnityEngine;
using UnityEngine.InputSystem;
using VRSurgery.Session;

namespace VRSurgery.VR
{
    /// <summary>
    /// Brings the operating table to the visitor, instead of asking the visitor to find it.
    ///
    /// Nobody at the stand walks: everything is laid out within arm's reach of one spot. But each
    /// visitor puts the headset on standing somewhere slightly different in the booth, facing
    /// somewhere slightly different, and is a different height — at NEXT, from children to tall
    /// adults. So at the start of each turn the rig is moved (never the room) so that the
    /// visitor's head is exactly over the stance and facing the patient, the same thing Job
    /// Simulator does when it recentres you at your station.
    ///
    /// Short mode, also after Job Simulator: a visitor whose eyes are well below the height the
    /// room was laid out for is lifted, so the table meets their hands where it meets an adult's.
    /// Only the virtual floor moves; nothing is on it that anyone needs to reach.
    ///
    /// The operator can refit at any moment with R on the PC keyboard (or by calling Fit), e.g.
    /// after helping someone adjust the strap.
    /// </summary>
    public class VisitorFit : MonoBehaviour
    {
        [Tooltip("The XR Origin: what gets moved.")]
        [SerializeField] private Transform rig;

        [Tooltip("The headset camera, a child of the rig.")]
        [SerializeField] private Transform head;

        [Tooltip("Floor point the head should be over, facing along its +Z.")]
        [SerializeField] private Transform stance;

        [Tooltip("Refits at the start of each visitor's briefing.")]
        [SerializeField] private EventSessionController session;

        [Header("Eye height")]
        [Tooltip("Puts the camera at exactly Locked Eye Height above the stance on every fit, so the headset starts where Play without a headset starts. Overrides short mode.")]
        [SerializeField] private bool lockEyeHeight = true;

        [Tooltip("Camera height above the stance in metres. Match the Main Camera Y when pressing Play without a headset.")]
        [SerializeField] private float lockedEyeHeight = 1.36144f;

        [Header("Short mode")]
        [SerializeField] private bool shortMode = true;

        [Tooltip("Eye height the room was laid out for, in metres.")]
        [SerializeField] private float designEyeHeight = 1.6f;

        [Tooltip("Visitors whose eyes are below this are lifted.")]
        [SerializeField] private float shortBelow = 1.48f;

        [Tooltip("Most the floor is ever lifted, in metres.")]
        [SerializeField] private float maxLift = 0.4f;

        [Tooltip("Seconds into the briefing for a second fit, once the headset is surely on.")]
        [SerializeField, Min(0f)] private float settleSeconds = 3f;

        [Tooltip("Seconds after the headset's presence sensor says someone put it on before fitting.")]
        [SerializeField, Min(0f)] private float putOnSettleSeconds = 1.2f;

        private const float MeasurableEye = 0.5f;
        private bool _eyeUnmeasured;
        private float _sinceBriefing = -1f;
        private float _sincePutOn = -1f;
        private bool _present = true;

        /// <summary>How far the floor was lifted at the last fit, in metres.</summary>
        public float Lift { get; private set; }

        public int Fits { get; private set; }

        private void OnEnable()
        {
            if (session != null) { session.StateChanged += HandleState; }
        }

        private void OnDisable()
        {
            if (session != null) { session.StateChanged -= HandleState; }
        }

        private void HandleState(SessionState state)
        {
            if (state != SessionState.Briefing) { return; }

            // A headset lying on the table is not a visitor: measure only a head that is in it.
            if (HeadsetOn()) { Fit(); }
            _sinceBriefing = 0f;
        }

        /// <summary>
        /// Whether someone is wearing the headset, from its presence sensor. True when the device
        /// does not say (the Editor, a headset without the sensor), so fitting still works there.
        /// </summary>
        private static bool HeadsetOn()
        {
            // Fully qualified: the Input System has its own InputDevice and CommonUsages.
            UnityEngine.XR.InputDevice headset = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.Head);
            if (!headset.isValid) { return true; }
            return !headset.TryGetFeatureValue(UnityEngine.XR.CommonUsages.userPresence, out bool present) || present;
        }

        /// <summary>Never while the clock runs: the table jumping mid-incision would be far worse than a slightly off stance.</summary>
        private bool MayFit => session == null || session.State != SessionState.Running;

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame) { Fit(); }

            // The head became measurable after the last fit (tracking arrived late): fit again.
            if (lockEyeHeight && _eyeUnmeasured && MayFit && head != null && rig != null
                && head.position.y - rig.position.y >= MeasurableEye)
            {
                Fit();
            }

            // Someone just put the headset on: fit them once it has settled on their face.
            bool present = HeadsetOn();
            if (present && !_present) { _sincePutOn = 0f; }
            _present = present;

            if (_sincePutOn >= 0f)
            {
                _sincePutOn += Time.deltaTime;
                if (_sincePutOn >= putOnSettleSeconds)
                {
                    _sincePutOn = -1f;
                    if (MayFit) { Fit(); }
                }
            }

            if (_sinceBriefing < 0f) { return; }
            _sinceBriefing += Time.deltaTime;
            if (_sinceBriefing >= settleSeconds)
            {
                _sinceBriefing = -1f;
                if ((session == null || session.State == SessionState.Briefing) && present) { Fit(); }
            }
        }

        /// <summary>Moves the rig so the head is over the stance, facing the patient, lifted if short.</summary>
        public void Fit()
        {
            if (rig == null || head == null || stance == null) { return; }

            // Height above the real floor, which is where the rig stands in floor tracking. A
            // camera offset means device tracking, where the height is a guess: no lift then.
            float eye = head.position.y - rig.position.y;
            bool floorTracking = head.parent == null || head.parent == rig || Mathf.Abs(head.parent.localPosition.y) < 0.05f;
            _eyeUnmeasured = eye < MeasurableEye;
            if (lockEyeHeight)
            {
                // Whatever height the runtime reports, end with the camera at the locked height.
                // A head not yet tracked reads near 0: lifting by a guess would add to the real
                // height once tracking arrives and put the camera in the ceiling. Wait instead.
                Lift = _eyeUnmeasured ? 0f : lockedEyeHeight - eye;
            }
            else
            {
                Lift = shortMode && floorTracking ? LiftFor(eye) : 0f;
            }

            Solve(head.position, head.forward, rig.position, rig.rotation, stance.position, stance.forward, Lift,
                out Vector3 position, out Quaternion rotation);
            rig.SetPositionAndRotation(position, rotation);
            Fits++;
        }

        /// <summary>How far to lift a visitor whose eyes are this high above the real floor.</summary>
        public float LiftFor(float eyeHeight)
        {
            if (eyeHeight <= 0.5f || eyeHeight >= shortBelow) { return 0f; }
            return Mathf.Clamp(designEyeHeight - eyeHeight, 0f, maxLift);
        }

        /// <summary>
        /// The rig pose that puts the head over <paramref name="stancePosition"/> looking along
        /// <paramref name="stanceForward"/>, turning only about the vertical and keeping the
        /// head's own height above the floor, plus <paramref name="lift"/>.
        /// </summary>
        public static void Solve(Vector3 headPosition, Vector3 headForward, Vector3 rigPosition, Quaternion rigRotation,
            Vector3 stancePosition, Vector3 stanceForward, float lift, out Vector3 position, out Quaternion rotation)
        {
            float headYaw = Yaw(headForward);
            float wantYaw = Yaw(stanceForward);
            Quaternion turn = Quaternion.Euler(0f, Mathf.DeltaAngle(headYaw, wantYaw), 0f);

            // Turn the rig about the vertical through the head, so the head stays put while it turns.
            Vector3 pivot = new Vector3(headPosition.x, rigPosition.y, headPosition.z);
            position = pivot + turn * (rigPosition - pivot);
            rotation = turn * rigRotation;

            // Then slide it so the head is over the stance, and set the floor.
            position += new Vector3(stancePosition.x - headPosition.x, 0f, stancePosition.z - headPosition.z);
            position.y = stancePosition.y + lift;
        }

        private static float Yaw(Vector3 forward)
        {
            forward.y = 0f;
            return forward.sqrMagnitude < 1e-8f ? 0f : Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        }

        public void Bind(Transform xrOrigin, Transform camera, Transform stancePoint, EventSessionController controller)
        {
            bool live = Application.isPlaying && isActiveAndEnabled;
            if (live) { OnDisable(); }
            rig = xrOrigin;
            head = camera;
            stance = stancePoint;
            session = controller;
            if (live) { OnEnable(); }
        }
    }
}
