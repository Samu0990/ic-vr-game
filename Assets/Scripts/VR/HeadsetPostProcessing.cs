using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace VRSurgery.VR
{
    /// <summary>
    /// Decides whether the headset camera runs post-processing. Off everywhere unless asked for.
    ///
    /// It used to be on for a PC driving the headset, to show the clinical Volume's grade. That
    /// made the headset the only camera in the room with a grade: a touch less exposure, more
    /// contrast, a cool filter and a vignette. The laptop mirror and the Quest standalone both
    /// draw without post, so the headset came out darker than either — and in a headset a
    /// vignette is not framing, it closes in round each eye like a tunnel. What the visitor sees
    /// at the stand is the Quest's picture, so the headset on the PC now shows that same picture.
    ///
    /// On a standalone Quest it stays off for performance too: a full-screen post pass breaks
    /// the tiled GPU's fast path and costs frames the 72 Hz budget does not have. Turn a switch
    /// on only to try the grade, and measure the frame time before keeping it on the Quest.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class HeadsetPostProcessing : MonoBehaviour
    {
        // Named enableOnDesktop until it was turned off: a new name, so scenes built while it
        // defaulted to on come up off on the next Play instead of waiting for a rebuild.
        [Tooltip("Headset driven by a PC (Link, Air Link, Play in the Editor). Off: the same picture as the Quest.")]
        [SerializeField] private bool enableOnPc = false;

        [Tooltip("Standalone Quest. Off by default: measure the frame time before turning this on.")]
        [SerializeField] private bool enableOnMobile = false;

        /// <summary>What was applied, for tests and for the console.</summary>
        public bool Applied { get; private set; }

        private void Awake() => Apply(Application.isMobilePlatform);

        public void Apply(bool mobile)
        {
            Applied = mobile ? enableOnMobile : enableOnPc;

            UniversalAdditionalCameraData data = GetComponent<UniversalAdditionalCameraData>();
            if (data != null) { data.renderPostProcessing = Applied; }
        }
    }
}
