using UnityEngine;

namespace VRSurgery.VR
{
    /// <summary>
    /// Activates the secondary display so the projection camera reaches the projector.
    ///
    /// Setting a camera's targetDisplay in the Inspector is not enough on its own: Unity only
    /// renders to a display that has been activated at runtime, which is why a correctly
    /// configured second camera still shows nothing in a build.
    /// </summary>
    public class ProjectionDisplay : MonoBehaviour
    {
        [SerializeField, Min(1)] private int displayIndex = 1;

        /// <summary>True once the display was found and activated.</summary>
        public bool Activated { get; private set; }

        private void Start()
        {
            if (Display.displays.Length <= displayIndex)
            {
                Debug.LogWarning($"[ProjectionDisplay] Display {displayIndex + 1} is not connected; " +
                                 $"only {Display.displays.Length} display(s) available. " +
                                 "The projection camera will render nowhere until a second output is attached.");
                return;
            }

            Display.displays[displayIndex].Activate();
            Activated = true;
            Debug.Log($"[ProjectionDisplay] Activated display {displayIndex + 1}.");
        }
    }
}
