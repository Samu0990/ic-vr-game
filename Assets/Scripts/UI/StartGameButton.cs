using UnityEngine;
using VRSurgery.Interaction;
using VRSurgery.Session;

namespace VRSurgery.Surgery
{
    /// <summary>
    /// The button that starts a visitor's session from the attract menu.
    ///
    /// Hold, not tap. A one-touch reset within arm's reach of the operating field is a reset
    /// that will eventually be hit with an elbow halfway through somebody's turn, and there is
    /// no undo for that. Holding it for a beat costs nothing when it is wanted and saves the
    /// round every time it is not.
    ///
    /// Detection follows the rest of the room: distance from the shared work tip to a point,
    /// inside a radius, accumulating while it stays there and decaying when it leaves. No
    /// collider and no physics — the scene has no dynamic bodies and this is not where to
    /// introduce the first one.
    ///
    /// It drives the session, never the patient directly: <see cref="EventSessionController"/>
    /// going to Briefing is what the whole room already listens to in order to reset itself.
    /// </summary>
    public class StartGameButton : MonoBehaviour
    {
        [Header("Sources")]
        [SerializeField] private EventSessionController session;

        [Tooltip("Both hands. Not the shared work tip the operation steps use: that tip sits on " +
                 "whichever hand is nearest the patient, so it can be parked on the left hand " +
                 "while the right one is the one reaching for this button.")]
        [SerializeField] private ControllerHand[] hands;

        [Header("Press")]
        [Tooltip("How near a hand has to come to press it by touch, in metres.")]
        [SerializeField, Min(0.01f)] private float radius = 0.055f;

        [Tooltip("This button lives on the instruction screen, beyond arm's reach on purpose, " +
                 "so it is also worked from where the visitor stands: point at it and squeeze. " +
                 "How close the aim has to be, in degrees.")]
        [SerializeField, Range(1f, 30f)] private float aimDegrees = 7f;

        [Tooltip("How far the trigger counts as pulled.")]
        [SerializeField, Range(0.1f, 1f)] private float triggerThreshold = 0.5f;

        [Tooltip("Degrees between the controller's grip axis and where it actually points. On " +
                 "Touch controllers the two are about forty degrees apart, and aiming down the " +
                 "grip axis makes the visitor point at the floor to hit something at eye level.")]
        [SerializeField, Range(-90f, 90f)] private float aimPitchDegrees = -40f;

        [Tooltip("How long the hand stays on it before the round restarts.")]
        [SerializeField, Min(0.1f)] private float holdSeconds = 1.2f;

        [Tooltip("How fast the charge drains once the hand leaves, as a multiple of the fill " +
                 "rate. Faster than it fills, so brushing past it twice does not add up.")]
        [SerializeField, Min(0.1f)] private float decayRate = 2.5f;

        [Tooltip("Seconds the button ignores the hand after firing, so one long press is one " +
                 "restart and not a stutter of them.")]
        [SerializeField, Min(0f)] private float cooldownSeconds = 1.5f;

        [Header("Menu")]
        [Tooltip("Everything hidden outside attract mode.")]
        [SerializeField] private GameObject body;

        [Header("Screen")]
        [Tooltip("Scales from empty to full along its local X as the press charges.")]
        [SerializeField] private Transform fill;

        [SerializeField] private TextMesh label;
        [SerializeField] private Renderer face;

        [SerializeField] private Color idleColour = new Color(0.46f, 0.89f, 1f);

        [Tooltip("What it turns as it charges, so the room can see a reset coming.")]
        [SerializeField] private Color armingColour = new Color(1f, 0.70f, 0.36f);

        private static readonly int BaseColour = Shader.PropertyToID("_BaseColor");
        private MaterialPropertyBlock _block;
        private float _cooldown;

        /// <summary>How far into the press it is, 0 to 1.</summary>
        public float Charge { get; private set; }

        /// <summary>Whether the hand is on the button right now.</summary>
        public bool IsPressed { get; private set; }

        /// <summary>How many restarts this button has fired. For tests and telemetry.</summary>
        public int StartCount { get; private set; }

        /// <summary>Whether the button is there to be pressed at all right now.</summary>
        public bool IsAvailable { get; private set; }

        /// <summary>Wires the button up from the scene builder.</summary>
        public void Bind(EventSessionController controller, params ControllerHand[] pressing)
        {
            session = controller;
            hands = pressing;
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>
        /// Advances the press. Public and taking its own delta so a test can drive it without
        /// waiting for real seconds to pass.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f) { return; }

            IsAvailable = session != null && session.State == SessionState.Attract;

            if (body != null && body.activeSelf != IsAvailable) { body.SetActive(IsAvailable); }

            if (!IsAvailable)
            {
                IsPressed = false;
                Charge = 0f;
                _cooldown = 0f;
                Paint();
                return;
            }

            if (_cooldown > 0f)
            {
                _cooldown = Mathf.Max(0f, _cooldown - deltaTime);
                IsPressed = false;
                Charge = 0f;
                Paint();
                return;
            }

            IsPressed = false;
            if (hands != null)
            {
                for (int i = 0; i < hands.Length; i++)
                {
                    if (Working(hands[i])) { IsPressed = true; break; }
                }
            }

            Charge = IsPressed
                ? Mathf.Min(1f, Charge + deltaTime / holdSeconds)
                : Mathf.Max(0f, Charge - deltaTime / holdSeconds * decayRate);

            if (Charge >= 1f)
            {
                Fire();
            }

            Paint();
        }

        /// <summary>
        /// Whether this hand is on the button: touching it, or aiming at it with the trigger
        /// down. The aim needs the trigger because a visitor looking around sweeps the
        /// controller across everything in the room, and a button that charges on being pointed
        /// at would reset the operation while somebody is simply getting their bearings.
        /// </summary>
        private bool Working(ControllerHand hand)
        {
            if (hand == null) { return false; }

            Vector3 toButton = transform.position - hand.transform.position;
            if (toButton.sqrMagnitude <= radius * radius) { return true; }

            if (hand.Trigger < triggerThreshold) { return false; }

            Vector3 pointing = hand.transform.rotation *
                               Quaternion.Euler(aimPitchDegrees, 0f, 0f) * Vector3.forward;
            return Vector3.Angle(pointing, toButton) <= aimDegrees;
        }

        private void Fire()
        {
            Charge = 0f;
            IsPressed = false;
            _cooldown = cooldownSeconds;
            StartCount++;

            // BeginSession resets the patient and opens the briefing before the round starts.
            if (session != null) { session.BeginSession(); }
        }

        private void Paint()
        {
            if (fill != null)
            {
                Vector3 scale = fill.localScale;
                fill.localScale = new Vector3(Mathf.Max(0.0001f, Charge), scale.y, scale.z);
            }

            Color colour = Color.Lerp(idleColour, armingColour, Charge);
            if (label != null) { label.color = colour; }

            if (face == null) { return; }

            _block ??= new MaterialPropertyBlock();
            face.GetPropertyBlock(_block);
            _block.SetColor(BaseColour, colour);
            face.SetPropertyBlock(_block);
        }
    }
}
