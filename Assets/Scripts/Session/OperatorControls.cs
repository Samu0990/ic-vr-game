using System;
using UnityEngine;
using UnityEngine.InputSystem;
using VRSurgery.VR;

namespace VRSurgery.Session
{
    /// <summary>
    /// What the person running the stand can do without a keyboard.
    ///
    /// The headset runs standalone at the event: there is no PC to press a key on. So the operator
    /// works it through the left controller's menu button, held — long enough that a visitor
    /// pressing it by accident does nothing:
    ///
    ///   held 2 s — recentre the table on whoever is wearing the headset;
    ///   held 4 s — end this turn and get the patient ready for the next visitor.
    ///
    /// Each threshold is felt as a click in the hand, so the operator knows when to let go. On a
    /// PC (Editor or Link) the keys do the same: R recentres, N ends the turn.
    /// </summary>
    public class OperatorControls : MonoBehaviour
    {
        [SerializeField] private EventSessionController session;
        [SerializeField] private VisitorFit fit;

        [SerializeField, Min(0.5f)] private float recentreSeconds = 2f;
        [SerializeField, Min(1f)] private float nextVisitorSeconds = 4f;

        private float _held;
        private bool _recentred;
        private bool _ended;

        public event Action Recentred;
        public event Action TurnEnded;

        /// <summary>Seconds the button has been held so far.</summary>
        public float Held => _held;

        private void Update()
        {
            bool pressed = MenuHeld();
            Tick(Time.deltaTime, pressed);

            if (Keyboard.current == null) { return; }
            if (Keyboard.current.nKey.wasPressedThisFrame) { EndTurn(); }
        }

        /// <summary>Advances the hold. Public so the thresholds can be tested without a controller.</summary>
        public void Tick(float deltaTime, bool pressed)
        {
            if (!pressed)
            {
                _held = 0f;
                _recentred = false;
                _ended = false;
                return;
            }

            _held += Mathf.Max(0f, deltaTime);

            if (!_recentred && _held >= recentreSeconds)
            {
                _recentred = true;
                if (fit != null) { fit.Fit(); }
                Click(0.4f);
                Recentred?.Invoke();
            }

            if (!_ended && _held >= nextVisitorSeconds)
            {
                _ended = true;
                Click(0.9f);
                EndTurn();
            }
        }

        /// <summary>Ends the visitor's turn: a running round is lost, anything else goes back to attract.</summary>
        public void EndTurn()
        {
            if (session != null)
            {
                if (session.State == SessionState.Running) { session.AbortRound(); }
                else { session.ReturnToAttract(); }
            }

            TurnEnded?.Invoke();
        }

        private static bool MenuHeld()
        {
            // Fully qualified: the Input System has types of the same names.
            UnityEngine.XR.InputDevice left = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.LeftHand);
            return left.isValid && left.TryGetFeatureValue(UnityEngine.XR.CommonUsages.menuButton, out bool pressed) && pressed;
        }

        private static void Click(float amplitude)
        {
            UnityEngine.XR.InputDevice left = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.LeftHand);
            if (left.isValid) { left.SendHapticImpulse(0u, amplitude, 0.12f); }
        }

        public void Bind(EventSessionController controller, VisitorFit visitorFit)
        {
            session = controller;
            fit = visitorFit;
        }
    }
}
