using UnityEngine;
using UnityEngine.InputSystem;

namespace Skyborne.Flight
{
    /// <summary>One frame of player intent, device agnostic.</summary>
    public struct FlightInputSnapshot
    {
        /// <summary>Look delta in degrees (x = yaw, y = pitch).</summary>
        public Vector2 look;

        /// <summary>0..1 forward thrust.</summary>
        public float throttle;

        /// <summary>-1..1 sideways trim.</summary>
        public float lateralTrim;

        /// <summary>-1..1 climb/dive trim while hovering.</summary>
        public float verticalTrim;

        /// <summary>0..1 boost.</summary>
        public float boost;

        /// <summary>0..1 air brake.</summary>
        public float airBrake;

        /// <summary>Rising edge of the grab/release button.</summary>
        public bool grabPressed;

        /// <summary>Held state of the throw button, used to wind up a throw.</summary>
        public bool throwHeld;

        /// <summary>Falling edge of the throw button.</summary>
        public bool throwReleased;
    }

    /// <summary>
    /// Where flight intent comes from. The controller only ever sees this interface, so
    /// swapping keyboard for a gamepad, a replay or a test stub touches nothing else.
    /// </summary>
    public interface IFlightInputSource
    {
        FlightInputSnapshot Read(float deltaTime);
    }

    /// <summary>
    /// Keyboard + mouse + gamepad driver on the new Input System, which is what this project
    /// has active (activeInputHandler = 1). Reads devices directly rather than through an
    /// .inputactions asset, to keep the sandbox self-contained.
    /// </summary>
    public class DesktopFlightInput : IFlightInputSource
    {
        private readonly float _mouseSensitivity;
        private readonly bool _invertPitch;

        public DesktopFlightInput(float mouseSensitivity = 0.12f, bool invertPitch = false)
        {
            _mouseSensitivity = mouseSensitivity;
            _invertPitch = invertPitch;
        }

        public FlightInputSnapshot Read(float deltaTime)
        {
            FlightInputSnapshot snapshot = default;

            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue() * _mouseSensitivity;
                snapshot.look = new Vector2(delta.x, _invertPitch ? delta.y : -delta.y);
                snapshot.grabPressed = mouse.leftButton.wasPressedThisFrame;
                snapshot.throwHeld = mouse.rightButton.isPressed;
                snapshot.throwReleased = mouse.rightButton.wasReleasedThisFrame;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                snapshot.throttle = keyboard.wKey.isPressed ? 1f : 0f;
                snapshot.boost = keyboard.leftShiftKey.isPressed ? 1f : 0f;
                snapshot.airBrake = keyboard.sKey.isPressed ? 1f : 0f;

                float lateral = 0f;
                if (keyboard.aKey.isPressed) lateral -= 1f;
                if (keyboard.dKey.isPressed) lateral += 1f;
                snapshot.lateralTrim = lateral;

                float vertical = 0f;
                if (keyboard.spaceKey.isPressed) vertical += 1f;
                if (keyboard.leftCtrlKey.isPressed) vertical -= 1f;
                snapshot.verticalTrim = vertical;

                // Keyboard fallbacks so the sandbox is playable without a mouse attached.
                snapshot.grabPressed |= keyboard.eKey.wasPressedThisFrame;
                snapshot.throwHeld |= keyboard.qKey.isPressed;
                snapshot.throwReleased |= keyboard.qKey.wasReleasedThisFrame;
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                Vector2 look = gamepad.rightStick.ReadValue() * (220f * deltaTime);
                snapshot.look += new Vector2(look.x, _invertPitch ? look.y : -look.y);

                Vector2 move = gamepad.leftStick.ReadValue();
                snapshot.throttle = Mathf.Max(snapshot.throttle, Mathf.Clamp01(move.y));
                snapshot.lateralTrim = Mathf.Clamp(snapshot.lateralTrim + move.x, -1f, 1f);

                snapshot.boost = Mathf.Max(snapshot.boost, gamepad.rightTrigger.ReadValue());
                snapshot.airBrake = Mathf.Max(snapshot.airBrake, gamepad.leftTrigger.ReadValue());

                float vertical = 0f;
                if (gamepad.buttonSouth.isPressed) vertical += 1f;
                if (gamepad.buttonEast.isPressed) vertical -= 1f;
                snapshot.verticalTrim = Mathf.Clamp(snapshot.verticalTrim + vertical, -1f, 1f);

                snapshot.grabPressed |= gamepad.rightShoulder.wasPressedThisFrame;
                snapshot.throwHeld |= gamepad.leftShoulder.isPressed;
                snapshot.throwReleased |= gamepad.leftShoulder.wasReleasedThisFrame;
            }

            return snapshot;
        }
    }

    /// <summary>
    /// Input source that replays whatever is written into <see cref="Next"/>. Exists so play
    /// mode tests and cutscenes can drive the flyer without a physical device.
    /// </summary>
    public class ScriptedFlightInput : IFlightInputSource
    {
        public FlightInputSnapshot Next;

        public FlightInputSnapshot Read(float deltaTime) => Next;
    }
}
