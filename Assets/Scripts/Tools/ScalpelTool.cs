using UnityEngine;
using VRSurgery.Interaction;

namespace VRSurgery.Tools
{
    /// <summary>
    /// The scalpel. Deliberately thin: cut detection lives in <see cref="CuttingInteractor"/>,
    /// the tip reference in <see cref="BladeTip"/>, and feedback in the haptics/audio listeners.
    /// This class only handles what is specific to the scalpel as an object in the player's hand.
    /// </summary>
    public class ScalpelTool : SurgicalTool
    {
        [SerializeField] private BladeTip bladeTip;
        [SerializeField] private CuttingInteractor cuttingInteractor;

        // Resolved lazily as well as in Awake: when a scalpel is assembled at runtime (scene
        // builder, tests) the components arrive in an order Awake cannot depend on.
        public BladeTip BladeTip =>
            bladeTip != null ? bladeTip : bladeTip = GetComponentInChildren<BladeTip>();

        public CuttingInteractor CuttingInteractor =>
            cuttingInteractor != null ? cuttingInteractor : cuttingInteractor = GetComponent<CuttingInteractor>();

        protected override void Awake()
        {
            base.Awake();

            if (bladeTip == null)
            {
                bladeTip = GetComponentInChildren<BladeTip>();
            }

            if (cuttingInteractor == null)
            {
                cuttingInteractor = GetComponent<CuttingInteractor>();
            }
        }

        protected override void HandleGrabbed(SurgicalInteractable interactable, IHandInteractor holder)
        {
            // The tool snaps to the hand on grab. Without dropping the tip history, the very
            // next swept segment would run from the tray to the patient and cut everything between.
            if (bladeTip != null)
            {
                bladeTip.ResetHistory();
            }
        }

        protected override void HandleReleased(SurgicalInteractable interactable, IHandInteractor previousHolder)
        {
            if (bladeTip != null)
            {
                bladeTip.ResetHistory();
            }
        }
    }
}
