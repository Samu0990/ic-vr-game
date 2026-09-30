using Unity.XR.CoreUtils;
using UnityEngine;

namespace VRSurgery.VR
{
    /// <summary>
    /// Forces the XR Origin to use floor-relative tracking.
    ///
    /// The template rig ships with TrackingOriginMode "Not Specified". On Quest (OpenXR) that
    /// resolves to a local, eye-level reference space: the headset starts at Y = 0 relative to
    /// the rig, so the camera sits on the floor of the scene. Requesting Floor makes the
    /// runtime report the real head height above the play-area floor, which is what the stance
    /// and reach calculations assume.
    ///
    /// Runs on its own after scene load, so no scene or prefab edit is needed.
    /// </summary>
    public static class FloorTrackingOrigin
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Apply()
        {
            XROrigin origin = Object.FindFirstObjectByType<XROrigin>();
            if (origin == null)
            {
                Debug.LogWarning("[FloorTrackingOrigin] No XROrigin in the scene; tracking origin unchanged.");
                return;
            }

            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            Debug.Log("[FloorTrackingOrigin] Requested Floor tracking origin on " + origin.name);
        }
    }
}
