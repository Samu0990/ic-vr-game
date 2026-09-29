using UnityEngine;
using UnityEngine.XR;

namespace VRSurgery.VR
{
    /// <summary>
    /// Mirrors the headset's viewpoint into a plain 2D camera, so a spectator screen can show
    /// what the person wearing the headset is looking at.
    ///
    /// This is a separate camera rather than a second output of the XR camera on purpose: the XR
    /// camera renders in stereo to the headset, and reusing it for a flat display fights that.
    ///
    /// It only draws when there is a spectator screen to draw to: a headset running from a PC.
    /// Without one it is switched off, and it has to be. In the Editor with the XR simulator the
    /// headset camera itself draws to the Game view, on this same Display 1, and this camera
    /// drew over it with its own field of view and a pose a step behind — the "buggy VR camera"
    /// the Editor showed. On a Quest there is no second screen at all, and it would have been a
    /// whole extra render of the room every frame for nobody.
    ///
    /// The pose is copied right before rendering, after the headset's tracking has posed the
    /// head for the frame, and again in LateUpdate for anything reading it in between.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class HeadsetFollowCamera : MonoBehaviour
    {
        [SerializeField] private Transform headset;

        [Tooltip("Draw only while a headset is running from a PC. Off: always draw (e.g. a mirror recorded in the Editor).")]
        [SerializeField] private bool onlyWithPcHeadset = true;

        private Camera _camera;

        /// <summary>True while a headset transform is known and being mirrored.</summary>
        public bool IsFollowing => headset != null;

        public Transform Headset
        {
            get => headset;
            set => headset = value;
        }

        /// <summary>Whether the mirror is drawing now.</summary>
        public bool IsDrawing => _camera != null && _camera.enabled;

        private void Awake()
        {
            _camera = GetComponent<Camera>();

            if (headset == null && Camera.main != null && Camera.main != _camera)
            {
                headset = Camera.main.transform;
            }

            if (headset == null)
            {
                Debug.LogWarning("[HeadsetFollowCamera] No headset transform; the spectator view will not move.");
            }

            UpdateDrawing();
        }

        private void OnEnable() => Application.onBeforeRender += Follow;

        private void OnDisable() => Application.onBeforeRender -= Follow;

        private void LateUpdate()
        {
            UpdateDrawing();
            Follow();
        }

        private void Follow()
        {
            if (headset == null) { return; }
            transform.SetPositionAndRotation(headset.position, headset.rotation);
        }

        private void UpdateDrawing()
        {
            if (_camera == null) { return; }
            bool draw = !onlyWithPcHeadset || ShouldDraw(XRSettings.isDeviceActive, Application.isMobilePlatform);
            if (_camera.enabled != draw) { _camera.enabled = draw; }
        }

        /// <summary>
        /// A mirror is wanted only for a running headset on a machine that has a screen of its
        /// own: not in the Editor without one (the XR camera already fills the Game view) and not
        /// on a standalone headset (nothing to show it on).
        /// </summary>
        public static bool ShouldDraw(bool headsetRunning, bool standaloneHeadset) => headsetRunning && !standaloneHeadset;
    }
}
