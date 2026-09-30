using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace VRSurgery.VR
{
    /// <summary>
    /// Keeps the camera at a sensible height above the floor on Quest.
    ///
    /// The template rig ships with TrackingOriginMode "Not Specified", which on Quest (OpenXR)
    /// resolves to an eye-level local space: the headset starts at Y = 0 relative to the rig and
    /// the camera sits on the floor of the scene. This asks for Floor tracking instead, which
    /// reports the real head height above the play-area floor.
    ///
    /// If the runtime does not honour Floor (the head still reads below MinEyeHeight a moment
    /// after the headset is put on), it falls back to Device tracking with a fixed camera
    /// offset. That is the case VisitorFit already treats as "height is a guess, no lift".
    ///
    /// Creates itself after the scene loads, so no scene or prefab edit is needed.
    /// </summary>
    public class FloorTrackingOrigin : MonoBehaviour
    {
        private const float MinEyeHeight = 0.5f;
        private const float LowSecondsBeforeFallback = 1.5f;
        private const float FallbackEyeHeight = 1.6f;
        private const float LogEverySeconds = 2f;

        private XROrigin _origin;
        private float _lowSeconds;
        private float _logTimer;
        private bool _fellBack;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            GameObject go = new GameObject("FloorTrackingOrigin");
            DontDestroyOnLoad(go);
            go.AddComponent<FloorTrackingOrigin>();
        }

        private void Update()
        {
            if (_origin == null)
            {
                _origin = FindFirstObjectByType<XROrigin>();
                if (_origin == null) { return; }

                _origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
                _fellBack = false;
                _lowSeconds = 0f;
                Debug.Log("[FloorTrackingOrigin] Requested Floor tracking origin on " + _origin.name);
            }

            // Every frame and cheap: the actions can be disabled again by a scene reload.
            EnableHeadActions(_origin.Camera);

            Camera cam = _origin.Camera;
            if (cam == null) { return; }

            float eye = cam.transform.position.y - _origin.transform.position.y;
            bool worn = HeadsetWorn();

            _logTimer += Time.unscaledDeltaTime;
            if (_logTimer >= LogEverySeconds)
            {
                _logTimer = 0f;
                Debug.Log($"[FloorTrackingOrigin] worn={worn} eye={eye:F2} m mode={_origin.CurrentTrackingOriginMode} fallback={_fellBack} " + Probe(cam));
            }

            if (_fellBack || !worn || eye >= MinEyeHeight)
            {
                _lowSeconds = 0f;
                return;
            }

            _lowSeconds += Time.unscaledDeltaTime;
            if (_lowSeconds < LowSecondsBeforeFallback) { return; }

            _fellBack = true;
            _origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
            _origin.CameraYOffset = FallbackEyeHeight;
            Debug.LogWarning($"[FloorTrackingOrigin] Floor not honoured (eye {eye:F2} m). Using Device tracking with a {FallbackEyeHeight:F1} m camera offset.");
        }

        /// <summary>
        /// The head's TrackedPoseDriver reads InputActionReferences into the XRI default action
        /// asset. Those only receive data while something enables them, normally an
        /// InputActionManager in the scene. The Transplante scene has none (the hands use their own
        /// inline actions, which the driver enables itself), so the head never moved. Enabling the
        /// actions here is harmless when a manager already did it.
        /// </summary>
        private static void EnableHeadActions(Camera cam)
        {
            if (cam == null) { return; }

            TrackedPoseDriver driver = cam.GetComponent<TrackedPoseDriver>();
            if (driver == null) { return; }

            Enable(driver.positionInput);
            Enable(driver.rotationInput);
            Enable(driver.trackingStateInput);
        }

        private static void Enable(InputActionProperty property)
        {
            InputAction action = property.action;
            if (action != null && !action.enabled)
            {
                action.Enable();
                Debug.Log("[FloorTrackingOrigin] Enabled head input action " + action.name);
            }
        }

        /// <summary>Raw device state, to tell "tracking never arrives" apart from "tracking arrives at the wrong height".</summary>
        private static string Probe(Camera cam)
        {
            XRManagerSettings manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            string loader = manager != null && manager.activeLoader != null ? manager.activeLoader.name : "none";

            UnityEngine.XR.InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            string device = "head=invalid";
            if (head.isValid)
            {
                head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracked);
                head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out Vector3 pos);
                head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out Quaternion rot);
                device = $"head.tracked={tracked} head.pos={pos.ToString("F2")} head.yaw={rot.eulerAngles.y:F0}";
            }

            return $"loader={loader} {device} cam.yaw={cam.transform.eulerAngles.y:F0}";
        }

        /// <summary>Whether someone is wearing the headset. True when the device does not say.</summary>
        private static bool HeadsetWorn()
        {
            UnityEngine.XR.InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!head.isValid) { return false; }
            return !head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.userPresence, out bool present) || present;
        }
    }
}
