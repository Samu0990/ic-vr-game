using UnityEngine;

namespace VRSurgery.VR
{
    /// <summary>
    /// Mirrors the headset's viewpoint into a plain 2D camera, so a spectator screen can show
    /// what the person wearing the headset is looking at.
    ///
    /// This is a separate camera rather than a second output of the XR camera on purpose: the XR
    /// camera renders in stereo to the headset, and reusing it for a flat display fights that.
    /// The pose is copied in LateUpdate so it lands after the XR rig has posed the head for the
    /// frame — copying earlier shows the previous frame's pose and reads as lag.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class HeadsetFollowCamera : MonoBehaviour
    {
        [SerializeField] private Transform headset;

        /// <summary>True while a headset transform is known and being mirrored.</summary>
        public bool IsFollowing => headset != null;

        public Transform Headset
        {
            get => headset;
            set => headset = value;
        }

        private void Awake()
        {
            if (headset == null && Camera.main != null)
            {
                headset = Camera.main.transform;
            }

            if (headset == null)
            {
                Debug.LogWarning("[HeadsetFollowCamera] No headset transform; the spectator view will not move.");
            }
        }

        private void LateUpdate()
        {
            if (headset == null) { return; }

            transform.SetPositionAndRotation(headset.position, headset.rotation);
        }
    }
}
