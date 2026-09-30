using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace VRSurgery.VR
{
    /// <summary>
    /// Makes sure the headset camera follows the head on Quest.
    ///
    /// 1. Once a headset is running, asks for Floor tracking, so the head reports its real height
    ///    above the play area. With no headset the template's camera offset is left alone.
    /// 2. Enables the head's input actions. The head TrackedPoseDriver reads InputActionReferences
    ///    that only receive data while something enables them, normally an InputActionManager in
    ///    the scene. The Transplante scene has none (the hands use inline actions that their
    ///    driver enables itself), so the head never moved.
    /// 3. If the camera still does not follow a tracked headset, copies the pose from the XR
    ///    device onto the camera itself.
    ///
    /// Creates itself after the scene loads, so no scene or prefab edit is needed.
    /// </summary>
    public class FloorTrackingOrigin : MonoBehaviour
    {
        private const float MismatchDegrees = 3f;
        private const float MismatchSecondsBeforeManual = 1f;
        private const float LogEverySeconds = 2f;

        private XROrigin _origin;
        private float _mismatchSeconds;
        private float _logTimer;
        private bool _manual;
        private bool _floorRequested;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            GameObject go = new GameObject("FloorTrackingOrigin");
            DontDestroyOnLoad(go);
            go.AddComponent<FloorTrackingOrigin>();
        }

        private void OnEnable() => Application.onBeforeRender += DriveCamera;

        private void OnDisable() => Application.onBeforeRender -= DriveCamera;

        private void Update()
        {
            if (_origin == null)
            {
                _origin = FindFirstObjectByType<XROrigin>();
                if (_origin == null) { return; }

                _floorRequested = false;
                _manual = false;
                _mismatchSeconds = 0f;
            }

            // Floor only once a headset is running. Without one (the Editor with no headset) the
            // template's camera Y offset is what puts the camera at head height, and Floor drops
            // that offset and leaves the camera on the floor.
            if (!_floorRequested && HeadsetRunning())
            {
                _origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
                _floorRequested = true;
                Debug.Log("[FloorTrackingOrigin] Headset running: requested Floor tracking origin on " + _origin.name);
            }

            Camera cam = _origin.Camera;
            if (cam == null) { return; }

            // Every frame and cheap: a scene reload can leave the actions disabled again.
            EnableHeadActions(cam);

            bool tracked = TryReadHead(out Vector3 pos, out Quaternion rot);
            if (tracked && !_manual)
            {
                float off = Quaternion.Angle(rot, cam.transform.localRotation);
                _mismatchSeconds = off > MismatchDegrees ? _mismatchSeconds + Time.unscaledDeltaTime : 0f;
                if (_mismatchSeconds >= MismatchSecondsBeforeManual)
                {
                    _manual = true;
                    Debug.LogWarning($"[FloorTrackingOrigin] Camera not following the tracked head (off by {off:F0} deg). Driving it from the XR device.");
                }
            }

            _logTimer += Time.unscaledDeltaTime;
            if (_logTimer >= LogEverySeconds)
            {
                _logTimer = 0f;
                float eye = cam.transform.position.y - _origin.transform.position.y;
                Debug.Log($"[FloorTrackingOrigin] eye={eye:F2} m mode={_origin.CurrentTrackingOriginMode} manual={_manual} floorRequested={_floorRequested} " + Probe(cam));
            }
        }

        private void LateUpdate() => DriveCamera();

        private void DriveCamera()
        {
            if (!_manual || _origin == null || _origin.Camera == null) { return; }
            if (!TryReadHead(out Vector3 pos, out Quaternion rot)) { return; }

            _origin.Camera.transform.SetLocalPositionAndRotation(pos, rot);
        }

        private static bool HeadsetRunning()
        {
            return XRSettings.isDeviceActive || InputDevices.GetDeviceAtXRNode(XRNode.Head).isValid;
        }

        private static bool TryReadHead(out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero;
            rot = Quaternion.identity;

            UnityEngine.XR.InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!head.isValid) { return false; }
            if (!head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracked) || !tracked) { return false; }

            return head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out pos)
                && head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out rot);
        }

        private static void EnableHeadActions(Camera cam)
        {
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
            bool driver = cam.GetComponent<TrackedPoseDriver>() != null;

            string device = "head=invalid";
            UnityEngine.XR.InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (head.isValid)
            {
                head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracked);
                head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out Vector3 pos);
                head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out Quaternion rot);
                device = $"head.tracked={tracked} head.pos={pos.ToString("F2")} head.yaw={rot.eulerAngles.y:F0}";
            }

            return $"loader={loader} tpd={driver} {device} cam.yaw={cam.transform.eulerAngles.y:F0}";
        }
    }
}
