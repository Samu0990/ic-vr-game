using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRSurgery.Feedback;
using VRSurgery.Interaction;
using VRSurgery.Session;
using VRSurgery.VR;

namespace VRSurgery.Surgery
{
    /// <summary>
    /// The stand's start screen, the way VR games open: a dark room of its own with the Meta
    /// Health logo and one button, INICIAR. It is up whenever no turn is being played — when the
    /// stand opens and every time a turn ends — and pressing the button takes the visitor into
    /// the operating room with a fresh patient.
    ///
    /// The room is a separate place far from the theatre, not the theatre switched off: the
    /// operating room, the projector over the mannequin and everything wired to them stay
    /// exactly as they are. Only the visitor is moved, through a fade to black, by giving the
    /// visitor fit the menu's floor point instead of the surgeon's. Without a headset running
    /// (the Editor, the test runner) nobody is moved and the operator starts the turn with I.
    ///
    /// The button works like the restart button in the theatre: touched with either hand, or
    /// aimed at with the trigger pulled, held for a beat so a hand brushing past does not start a
    /// turn.
    /// </summary>
    public class StartMenu : MonoBehaviour
    {
        private enum Pending { None, Enter, Start, Leave }

        [Header("Sources")]
        [SerializeField] private EventSessionController session;
        [SerializeField] private VisitorFit fit;
        [SerializeField] private Transform head;
        [SerializeField] private ControllerHand[] hands = new ControllerHand[0];

        [Header("Room")]
        [Tooltip("The whole menu room; on only while the visitor is in it.")]
        [SerializeField] private GameObject room;

        [Tooltip("Floor point the visitor stands on in the menu room, facing the logo along +Z.")]
        [SerializeField] private Transform stance;

        [Tooltip("The logo and the button, kept at the visitor's eye height.")]
        [SerializeField] private Transform layout;

        [SerializeField] private Transform logo;
        [SerializeField] private Renderer logoGlow;

        [Header("Button")]
        [SerializeField] private Transform button;
        [SerializeField] private Vector2 buttonSize = new Vector2(0.34f, 0.106f);
        [SerializeField] private Renderer buttonFace;
        [SerializeField] private Renderer buttonGlow;

        [Tooltip("Scales from empty to full along its local X as the press charges.")]
        [SerializeField] private Transform progress;

        [Header("Fade")]
        [Tooltip("A black quad just in front of the headset's eyes.")]
        [SerializeField] private Renderer fade;

        [SerializeField, Min(0.05f)] private float fadeSeconds = 0.35f;

        [Header("Press")]
        [Tooltip("How far beyond the button's edge a hand still counts as on it, in metres.")]
        [SerializeField, Min(0f)] private float touchMargin = 0.04f;

        [Tooltip("How far in front of or behind the button a hand still counts as on it.")]
        [SerializeField, Min(0.01f)] private float touchDepth = 0.07f;

        [Tooltip("How near a hand has to be for the button to light up and invite the press.")]
        [SerializeField, Min(0.01f)] private float hoverDistance = 0.18f;

        [SerializeField, Range(0.1f, 1f)] private float triggerThreshold = 0.5f;

        [Tooltip("Degrees between the controller's grip axis and where it actually points (see RestartButton).")]
        [SerializeField, Range(-90f, 90f)] private float aimPitchDegrees = -40f;

        [SerializeField, Min(0.05f)] private float holdSeconds = 0.45f;

        [Tooltip("Seconds after the menu comes up before the button takes a press: a hand that " +
                 "happens to be where the button appears is not a visitor choosing to start.")]
        [SerializeField, Min(0f)] private float armSeconds = 0.8f;

        [Header("Look")]
        [SerializeField] private Color glowColour = new Color(0.52f, 0.09f, 0.98f, 0.45f);

        [Tooltip("Eye height used while nobody is measured: the room was laid out for it.")]
        [SerializeField] private float defaultEyeHeight = 1.6f;

        [Tooltip("How far the visitor's eyes may drift from the layout before it follows them.")]
        [SerializeField, Min(0f)] private float eyeFollowThreshold = 0.1f;

        private static readonly int BaseColour = Shader.PropertyToID("_BaseColor");
        private MaterialPropertyBlock _block;
        private Pending _pending;
        private Vector3 _logoHome;
        private Vector3 _buttonHome;
        private bool _homesTaken;
        private float _clock;
        private float _anchorEye;
        private float _eyeTarget;
        private float _hover;
        private float _sinceOpened;
        private bool _fogBefore;

        /// <summary>The menu is the screen right now (whether or not anyone was moved to it).</summary>
        public bool IsOpen { get; private set; }

        /// <summary>The visitor is standing in the menu room.</summary>
        public bool InRoom { get; private set; }

        /// <summary>How far into the press, 0 to 1.</summary>
        public float Charge { get; private set; }

        /// <summary>A hand is on the button, near it, or aimed at it.</summary>
        public bool Hovered { get; private set; }

        /// <summary>How black the headset is, 0 to 1.</summary>
        public float Fade01 { get; private set; }

        /// <summary>Turns started from this menu. For tests and telemetry.</summary>
        public int Starts { get; private set; }

        /// <summary>Move the visitor even without a headset running. For tests.</summary>
        public bool ForceTravel { get; set; }

        private bool Travels => ForceTravel || HeadsetRunning();

        private static bool HeadsetRunning() =>
            XRSettings.isDeviceActive || InputDevices.GetDeviceAtXRNode(XRNode.Head).isValid;

        private void Start()
        {
            // The stand opens on the menu: start black so the theatre never flashes up first.
            if (Wanted && Travels) { Fade01 = 1f; }
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.iKey.wasPressedThisFrame) { StartTurn(); }
            Tick(Time.deltaTime);
        }

        private bool Wanted => session != null && session.State is SessionState.Attract or SessionState.Scoreboard;

        /// <summary>The operator's I, or a test: the same as pressing INICIAR.</summary>
        public void StartTurn()
        {
            if (IsOpen && _pending == Pending.None) { Request(Pending.Start); }
        }

        /// <summary>Advances the menu. Public and taking its own delta so a test can drive it.</summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f) { return; }
            TakeHomes();
            _clock += deltaTime;

            if (_pending == Pending.None)
            {
                bool wanted = Wanted;
                if (wanted && (!IsOpen || (!InRoom && Travels))) { Request(Pending.Enter); }
                else if (!wanted && IsOpen) { Request(Pending.Leave); }
            }

            if (IsOpen && _pending == Pending.None && Fade01 <= 0f) { _sinceOpened += deltaTime; }
            if (IsOpen && _pending == Pending.None && _sinceOpened >= armSeconds) { Press(deltaTime); }
            else { Charge = 0f; Hovered = false; }

            if (_pending != Pending.None && (!Travels || Fade01 >= 1f))
            {
                Pending now = _pending;
                _pending = Pending.None;
                Execute(now);
            }

            float target = _pending != Pending.None && Travels ? 1f : 0f;
            Fade01 = Travels ? Mathf.MoveTowards(Fade01, target, deltaTime / fadeSeconds) : 0f;

            FollowEyes(deltaTime);
            Animate(deltaTime);
            Paint();
        }

        private void Request(Pending what)
        {
            _pending = what;
            Charge = 0f;
        }

        private void Execute(Pending what)
        {
            switch (what)
            {
                case Pending.Enter:
                    Open();
                    break;

                case Pending.Start:
                    Close();
                    Starts++;

                    // Briefing, exactly as the restart button does: the room resets itself for the
                    // new visitor, and their clock starts on their own first gesture.
                    if (session != null) { session.BeginSession(); }
                    break;

                case Pending.Leave:
                    Close();
                    break;
            }
        }

        private void Open()
        {
            IsOpen = true;
            Charge = 0f;
            _sinceOpened = 0f;

            if (Travels)
            {
                // The theatre's grey fog is not felt across a table, but ten metres of it turn the
                // menu's night sky grey. Off while the visitor is in the menu room, then put back.
                if (!InRoom) { _fogBefore = RenderSettings.fog; }
                RenderSettings.fog = false;

                if (room != null) { room.SetActive(true); }
                if (fit != null && stance != null)
                {
                    fit.StanceOverride = stance;
                    fit.Fit();
                }

                InRoom = true;
            }

            Anchor(EyeHeight());
        }

        private void Close()
        {
            IsOpen = false;
            Charge = 0f;

            if (fit != null && fit.StanceOverride != null)
            {
                fit.StanceOverride = null;
                if (InRoom) { fit.Fit(); }
            }

            if (InRoom) { RenderSettings.fog = _fogBefore; }
            InRoom = false;
            if (room != null) { room.SetActive(false); }
        }

        // ------------------------------------------------------------------ the button

        private void Press(float deltaTime)
        {
            bool pressing = false, near = false;
            ControllerHand presser = null;

            if (button != null && hands != null)
            {
                for (int i = 0; i < hands.Length; i++)
                {
                    if (hands[i] == null) { continue; }
                    Evaluate(hands[i], out bool on, out bool close);
                    near |= close;
                    if (on && !pressing)
                    {
                        pressing = true;
                        presser = hands[i];
                    }
                }
            }

            Hovered = pressing || near;
            Charge = pressing
                ? Mathf.Min(1f, Charge + deltaTime / holdSeconds)
                : Mathf.Max(0f, Charge - deltaTime / holdSeconds * 2.5f);

            if (Charge >= 1f)
            {
                Feedback(presser);
                Request(Pending.Start);
            }
        }

        /// <summary>
        /// Whether this hand is on the button (touching it, or aiming at it with the trigger
        /// pulled) and whether it is near enough for the button to light up.
        /// </summary>
        private void Evaluate(ControllerHand hand, out bool on, out bool near)
        {
            Vector3 local = button.InverseTransformPoint(hand.transform.position);
            float halfX = buttonSize.x * 0.5f + touchMargin;
            float halfY = buttonSize.y * 0.5f + touchMargin;

            bool touching = Mathf.Abs(local.x) <= halfX && Mathf.Abs(local.y) <= halfY && Mathf.Abs(local.z) <= touchDepth;
            near = Mathf.Abs(local.x) <= halfX + hoverDistance && Mathf.Abs(local.y) <= halfY + hoverDistance &&
                   Mathf.Abs(local.z) <= touchDepth + hoverDistance;

            bool aimed = false;
            Vector3 pointing = hand.transform.rotation * Quaternion.Euler(aimPitchDegrees, 0f, 0f) * Vector3.forward;
            Ray ray = new Ray(hand.transform.position, pointing);
            if (new Plane(button.forward, button.position).Raycast(ray, out float distance) && distance < 6f)
            {
                Vector3 hit = button.InverseTransformPoint(ray.GetPoint(distance));
                aimed = Mathf.Abs(hit.x) <= halfX && Mathf.Abs(hit.y) <= halfY;
            }

            near |= aimed;
            on = touching || (aimed && hand.Trigger >= triggerThreshold);
        }

        private void Feedback(ControllerHand hand)
        {
            if (!Application.isPlaying) { return; }

            Vector3 at = button != null ? button.position : transform.position;
            AudioSource.PlayClipAtPoint(ProceduralTones.SoftConfirm, at, 0.8f);

            if (hand == null) { return; }
            XRBaseInputInteractor interactor = hand.GetComponentInChildren<XRBaseInputInteractor>();
            if (interactor == null) { interactor = hand.GetComponentInParent<XRBaseInputInteractor>(); }
            if (interactor != null) { interactor.SendHapticImpulse(0.6f, 0.12f); }
        }

        // ------------------------------------------------------------------ the look

        private void TakeHomes()
        {
            if (_homesTaken) { return; }
            _homesTaken = true;
            if (logo != null) { _logoHome = logo.localPosition; }
            if (button != null) { _buttonHome = button.localPosition; }
        }

        private float EyeHeight()
        {
            if (InRoom && head != null && stance != null && head.position.y - stance.position.y > 0.5f)
            {
                return head.position.y;
            }

            return (stance != null ? stance.position.y : 0f) + defaultEyeHeight;
        }

        /// <summary>Puts the logo and the button at this eye height, over the menu's floor point.</summary>
        private void Anchor(float eye)
        {
            _anchorEye = eye;
            _eyeTarget = eye;
            if (layout == null || stance == null) { return; }

            Vector3 forward = stance.forward;
            forward.y = 0f;
            layout.SetPositionAndRotation(new Vector3(stance.position.x, eye, stance.position.z),
                forward.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(forward.normalized, Vector3.up) : Quaternion.identity);
        }

        /// <summary>
        /// The headset is often put on after the menu came up, lying on the table until then: the
        /// layout follows the visitor's eyes, smoothly, whenever they settle somewhere new — but
        /// never while a hand is on its way to the button.
        /// </summary>
        private void FollowEyes(float deltaTime)
        {
            if (!InRoom || layout == null) { return; }

            float eye = EyeHeight();
            if (!Hovered && Mathf.Abs(eye - _eyeTarget) > eyeFollowThreshold) { _eyeTarget = eye; }
            if (Mathf.Abs(_anchorEye - _eyeTarget) < 0.001f) { return; }

            float moved = Mathf.MoveTowards(_anchorEye, _eyeTarget, deltaTime * 0.8f);
            layout.position += Vector3.up * (moved - _anchorEye);
            _anchorEye = moved;
        }

        private void Animate(float deltaTime)
        {
            _hover = Mathf.MoveTowards(_hover, Hovered ? 1f : 0f, deltaTime * 6f);

            if (logo != null)
            {
                logo.localPosition = _logoHome + Vector3.up * (Mathf.Sin(_clock * Mathf.PI * 2f / 4.5f) * 0.012f);
            }

            if (button != null)
            {
                // Grows a little to invite the hand, and sinks away from it as the press charges.
                button.localScale = Vector3.one * (1f + 0.06f * _hover);
                button.localPosition = _buttonHome;
                button.position += button.forward * (0.012f * Charge);
            }

            if (progress != null)
            {
                Vector3 scale = progress.localScale;
                progress.localScale = new Vector3(Mathf.Max(0.0001f, Charge), scale.y, scale.z);
                progress.gameObject.SetActive(Charge > 0.001f);
            }
        }

        private void Paint()
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(_clock * Mathf.PI * 2f / 2.6f);

            if (buttonFace != null)
            {
                float light = 0.88f + 0.12f * _hover + 0.35f * Charge;
                Tint(buttonFace, new Color(light, light, light, 1f));
            }

            if (buttonGlow != null)
            {
                float alpha = glowColour.a * (0.55f + 0.25f * pulse + 0.6f * _hover + 0.6f * Charge);
                Tint(buttonGlow, new Color(glowColour.r, glowColour.g, glowColour.b, Mathf.Clamp01(alpha)));
            }

            if (logoGlow != null)
            {
                Tint(logoGlow, new Color(glowColour.r, glowColour.g, glowColour.b, glowColour.a * (0.7f + 0.3f * pulse)));
            }

            if (fade != null)
            {
                fade.enabled = Fade01 > 0.001f;
                Tint(fade, new Color(0f, 0f, 0f, Fade01));
            }
        }

        private void Tint(Renderer renderer, Color colour)
        {
            _block ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColour, colour);
            renderer.SetPropertyBlock(_block);
        }

        // ------------------------------------------------------------------ wiring

        public void Bind(EventSessionController controller, VisitorFit visitorFit, Transform camera,
            params ControllerHand[] pressing)
        {
            session = controller;
            fit = visitorFit;
            head = camera;
            hands = pressing ?? new ControllerHand[0];
        }

        public void BindRoom(GameObject menuRoom, Transform menuStance, Transform menuLayout, Transform logoQuad,
            Renderer logoHalo)
        {
            room = menuRoom;
            stance = menuStance;
            layout = menuLayout;
            logo = logoQuad;
            logoGlow = logoHalo;
            _homesTaken = false;
        }

        public void BindButton(Transform buttonRoot, Vector2 size, Renderer face, Renderer halo, Transform bar)
        {
            button = buttonRoot;
            buttonSize = size;
            buttonFace = face;
            buttonGlow = halo;
            progress = bar;
            _homesTaken = false;
        }

        public void BindFade(Renderer fader) => fade = fader;
    }
}
