using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace VRSurgery.VR
{
    /// <summary>
    /// Turns post-processing on for the headset camera where the hardware can pay for it.
    ///
    /// The clinical Volume the scene builder adds did nothing at all: the XR rig's camera comes
    /// from the XRI prefab with post-processing switched off, and nothing switched it on. Turning
    /// it on unconditionally would be the opposite mistake on a standalone Quest, where a full
    /// screen post pass breaks the tiled GPU's fast path and costs frames the 72 Hz budget does
    /// not have. So: on for a PC driving the headset (Link / Air Link — the stand with the
    /// projector and the vitals TV runs this way), off on a mobile headset unless asked for.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class HeadsetPostProcessing : MonoBehaviour
    {
        [SerializeField] private bool enableOnDesktop = true;

        [Tooltip("Standalone Quest. Off by default: measure the frame time before turning this on.")]
        [SerializeField] private bool enableOnMobile = false;

        /// <summary>What was applied, for tests and for the console.</summary>
        public bool Applied { get; private set; }

        private void Awake() => Apply(Application.isMobilePlatform);

        public void Apply(bool mobile)
        {
            UniversalAdditionalCameraData data = GetComponent<UniversalAdditionalCameraData>();
            if (data == null) { return; }

            Applied = mobile ? enableOnMobile : enableOnDesktop;
            data.renderPostProcessing = Applied;
        }
    }
}
