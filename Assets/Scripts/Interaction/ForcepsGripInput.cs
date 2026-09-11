using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using VRSurgery.Tools;

namespace VRSurgery.Interaction
{
    /// <summary>
    /// Turns XRI's activate input into jaw closure on the forceps.
    ///
    /// ForcepsTool exposes SetGripInput and never learns where the value came from, which is what
    /// lets a test drive the jaws with no interactor in the scene. Activate is the right source:
    /// XRI routes it to the interactable the hand already holds, so squeezing the trigger closes
    /// the jaws of the forceps in that hand and nothing else.
    /// </summary>
    [RequireComponent(typeof(ForcepsTool))]
    [RequireComponent(typeof(XRGrabInteractable))]
    public class ForcepsGripInput : MonoBehaviour
    {
        [SerializeField] private ForcepsTool forceps;
        [SerializeField] private XRGrabInteractable grab;

        private void Awake()
        {
            if (forceps == null) { forceps = GetComponent<ForcepsTool>(); }
            if (grab == null) { grab = GetComponent<XRGrabInteractable>(); }
        }

        private void OnEnable()
        {
            grab.activated.AddListener(HandleActivated);
            grab.deactivated.AddListener(HandleDeactivated);
            grab.selectExited.AddListener(HandleReleased);
        }

        private void OnDisable()
        {
            grab.activated.RemoveListener(HandleActivated);
            grab.deactivated.RemoveListener(HandleDeactivated);
            grab.selectExited.RemoveListener(HandleReleased);
        }

        private void HandleActivated(ActivateEventArgs args) => forceps.SetGripInput(1f);

        private void HandleDeactivated(DeactivateEventArgs args) => forceps.SetGripInput(0f);

        /// <summary>
        /// Letting go has to open the jaws as well. Without this, a forceps dropped while the
        /// trigger is still down lands on the tray clamped shut and the next visitor picks up an
        /// instrument that is already closed.
        /// </summary>
        private void HandleReleased(SelectExitEventArgs args) => forceps.SetGripInput(0f);
    }
}
