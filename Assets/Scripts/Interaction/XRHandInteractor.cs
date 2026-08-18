using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRSurgery.Haptics;
using VRSurgery.Tools;

namespace VRSurgery.Interaction
{
    /// <summary>
    /// Bridges XRI's interactor to the game's own <see cref="IHandInteractor"/> contract.
    /// The gameplay code talks to the interface; only this one class knows XRI exists, so
    /// swapping the interaction backend (or driving a hand from a test) touches nothing else.
    /// </summary>
    [RequireComponent(typeof(XRBaseInteractor))]
    public class XRHandInteractor : MonoBehaviour, IHandInteractor
    {
        [SerializeField] private bool isLeftHand;

        private XRBaseInteractor _interactor;
        private XRBaseInputInteractor _inputInteractor;

        public bool IsLeftHand => isLeftHand;
        public SurgicalInteractable HeldObject { get; private set; }

        private void Awake()
        {
            _interactor = GetComponent<XRBaseInteractor>();
            _inputInteractor = _interactor as XRBaseInputInteractor;
        }

        private void OnEnable()
        {
            _interactor.selectEntered.AddListener(HandleSelectEntered);
            _interactor.selectExited.AddListener(HandleSelectExited);
            _interactor.hoverEntered.AddListener(HandleHoverEntered);
        }

        private void OnDisable()
        {
            _interactor.selectEntered.RemoveListener(HandleSelectEntered);
            _interactor.selectExited.RemoveListener(HandleSelectExited);
            _interactor.hoverEntered.RemoveListener(HandleHoverEntered);
        }

        private void HandleSelectEntered(SelectEnterEventArgs args)
        {
            SurgicalInteractable interactable = ResolveInteractable(args.interactableObject);
            if (interactable == null)
            {
                return;
            }

            HeldObject = interactable;
            interactable.OnGrabbed(this);

            HapticProfile profile = interactable.ToolDefinition != null
                ? interactable.ToolDefinition.HapticProfile
                : null;

            if (profile != null)
            {
                SendHapticFeedback(profile);
            }
        }

        private void HandleSelectExited(SelectExitEventArgs args)
        {
            SurgicalInteractable interactable = ResolveInteractable(args.interactableObject);
            if (interactable == null || interactable != HeldObject)
            {
                return;
            }

            HeldObject = null;
            interactable.OnReleased();
        }

        private void HandleHoverEntered(HoverEnterEventArgs args)
        {
            SurgicalInteractable interactable = ResolveInteractable(args.interactableObject);
            if (interactable != null)
            {
                Hover(interactable);
            }
        }

        private static SurgicalInteractable ResolveInteractable(IXRInteractable interactable)
        {
            if (interactable == null)
            {
                return null;
            }

            Transform target = interactable.transform;
            return target != null ? target.GetComponentInParent<SurgicalInteractable>() : null;
        }

        // ---- IHandInteractor ----

        public bool TryGrab(SurgicalInteractable target)
        {
            if (target == null || target.IsHeld)
            {
                return false;
            }

            HeldObject = target;
            target.OnGrabbed(this);
            return true;
        }

        public void Release()
        {
            if (HeldObject == null)
            {
                return;
            }

            SurgicalInteractable previous = HeldObject;
            HeldObject = null;
            previous.OnReleased();
        }

        public void Hover(SurgicalInteractable target)
        {
            // Hover feedback is intentionally light — highlight is driven by the assistance
            // level elsewhere, not forced on by every passing hand.
        }

        public void Activate()
        {
            if (HeldObject == null)
            {
                return;
            }

            ForcepsTool forceps = HeldObject.GetComponent<ForcepsTool>();
            if (forceps != null)
            {
                forceps.SetGripInput(1f);
            }
        }

        public void GetPose(out Vector3 position, out Quaternion rotation)
        {
            Transform attach = _interactor != null ? _interactor.GetAttachTransform(null) : null;
            Transform source = attach != null ? attach : transform;
            position = source.position;
            rotation = source.rotation;
        }

        public void SendHapticFeedback(HapticProfile profile)
        {
            if (profile == null || _inputInteractor == null)
            {
                return;
            }

            _inputInteractor.SendHapticImpulse(profile.Amplitude, profile.Duration);
        }
    }
}
