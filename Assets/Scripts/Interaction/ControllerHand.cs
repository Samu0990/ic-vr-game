using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VRSurgery.Interaction
{
    /// <summary>
    /// A gloved hand where the visitor's real hand is, instead of a floating controller.
    ///
    /// The rig's controller transform follows the controller's pointing pose, which sits out at
    /// the tip of the controller — right for a laser, wrong for a hand. This object follows the
    /// grip pose instead (OpenXR's "grip": the palm, wrapped round the handle), through a
    /// TrackedPoseDriver on the device pose, so the glove is where the fist is. The hand model
    /// is turned to match that pose from its own bones: palm facing the handle, fingers
    /// wrapping round it, index on the trigger side — so any OpenXR-rigged hand drops in.
    ///
    /// Two things are then moved to the hand so that what the visitor sees is what happens:
    /// the grab point (where instruments are found and held) goes into the fist, and the poke
    /// point (the fingertip that presses keys and touches the heart) goes onto the tip of the
    /// index finger. The fingers follow grip and trigger through <see cref="HandPoser"/>.
    ///
    /// If the grip pose is not available (a device or simulator without one), the hand simply
    /// rides on the controller transform.
    /// </summary>
    public class ControllerHand : MonoBehaviour
    {
        [SerializeField] private bool isLeftHand;

        [Tooltip("The rig's tracked controller: the hand shows only while it is active.")]
        [SerializeField] private Transform controller;

        [Tooltip("The rigged hand model, a child of this object.")]
        [SerializeField] private Transform model;

        [SerializeField] private HandPoser poser;

        [Tooltip("Grip button and trigger are read from this interactor's own inputs.")]
        [SerializeField] private XRBaseInputInteractor inputs;

        [Tooltip("Child of the controller, kept in the fist: instruments are found and held here.")]
        [SerializeField] private Transform grabPoint;

        [Tooltip("The poke interactor's point, moved onto the index fingertip.")]
        [SerializeField] private Transform pokePoint;

        [SerializeField] private TrackedPoseDriver gripDriver;

        [Header("Fine tuning in the headset")]
        [Tooltip("From the middle of the handle to the middle of the palm, metres.")]
        [SerializeField] private float palmDepth = 0.025f;

        [Tooltip("Extra shift of the hand, in the grip frame (x right, y up, z along the handle).")]
        [SerializeField] private Vector3 positionOffset = Vector3.zero;

        [Tooltip("Extra turn of the hand, degrees, in the grip frame.")]
        [SerializeField] private Vector3 rotationOffset = Vector3.zero;

        [Tooltip("The middle of the fist, where instruments sit, in the grip frame.")]
        [SerializeField] private Vector3 fistOffset = Vector3.zero;

        [SerializeField, HideInInspector] private Vector3 _modelRestPosition;
        [SerializeField, HideInInspector] private Quaternion _modelRestRotation = Quaternion.identity;
        [SerializeField, HideInInspector] private bool _restStored;

        private Renderer[] _renderers = new Renderer[0];
        private bool _shown = true;

        public bool IsLeftHand => isLeftHand;
        public Transform Model => model;
        public Transform GrabPoint => grabPoint;
        public HandPoser Poser => poser;

        /// <summary>The middle of the fist in world space.</summary>
        public Vector3 FistPosition => transform.TransformPoint(fistOffset);

        private void Awake()
        {
            if (model != null) { _renderers = model.GetComponentsInChildren<Renderer>(true); }
            AlignModel();
            if (poser != null) { poser.Calibrate(); }
            MovePokePointToFingertip();
        }

        /// <summary>
        /// Turns and places the model so its palm wraps the handle of the grip pose. Idempotent:
        /// always starts again from the model's own rest placement.
        /// </summary>
        public void AlignModel()
        {
            if (model == null) { return; }

            if (!_restStored)
            {
                _modelRestPosition = model.localPosition;
                _modelRestRotation = model.localRotation;
                _restStored = true;
            }

            model.localPosition = _modelRestPosition;
            model.localRotation = _modelRestRotation;

            Transform wrist = HandPoser.FindBone(model, "Wrist");
            Transform index = HandPoser.FindBone(model, "IndexProximal");
            Transform middle = HandPoser.FindBone(model, "MiddleProximal");
            Transform little = HandPoser.FindBone(model, "LittleProximal");
            if (wrist == null || index == null || middle == null || little == null)
            {
                Debug.LogWarning($"[Mão] '{name}': modelo sem os ossos do punho e dos nós dos dedos; fica como veio.");
                return;
            }

            // The hand as the model has it, in this object's frame.
            Vector3 w = transform.InverseTransformPoint(wrist.position);
            Vector3 i = transform.InverseTransformPoint(index.position);
            Vector3 m = transform.InverseTransformPoint(middle.position);
            Vector3 l = transform.InverseTransformPoint(little.position);
            Vector3 fingers = (m - w).normalized;
            Vector3 palm = Vector3.ProjectOnPlane(HandPoser.PalmNormal(w, i, l, isLeftHand), fingers).normalized;

            // The grip frame: the palm faces the handle across X (a left palm faces +X, a right
            // one -X), the handle runs little finger to thumb along +Z, and so a flat hand's
            // fingers would point down -Y before wrapping round.
            Vector3 wantFingers = Vector3.down;
            Vector3 wantPalm = isLeftHand ? Vector3.right : Vector3.left;

            Quaternion turn = Quaternion.Euler(rotationOffset)
                * Quaternion.LookRotation(wantFingers, wantPalm)
                * Quaternion.Inverse(Quaternion.LookRotation(fingers, palm));

            model.localRotation = turn * model.localRotation;
            model.localPosition = turn * model.localPosition;

            // Then slide it so the middle of the palm sits palmDepth off the handle's axis.
            Transform palmBone = HandPoser.FindBone(model, "Palm");
            Vector3 palmCentre = palmBone != null
                ? transform.InverseTransformPoint(palmBone.position)
                : (transform.InverseTransformPoint(wrist.position) + transform.InverseTransformPoint(middle.position)) * 0.5f;
            Vector3 target = Quaternion.Euler(rotationOffset) * (-wantPalm * palmDepth) + positionOffset;
            model.localPosition += target - palmCentre;
        }

        private void MovePokePointToFingertip()
        {
            if (pokePoint == null || model == null) { return; }
            Transform tip = HandPoser.FindBone(model, "IndexTip");
            if (tip == null) { return; }

            pokePoint.SetParent(tip, false);
            pokePoint.localPosition = Vector3.zero;
            pokePoint.localRotation = Quaternion.identity;
        }

        private void Update()
        {
            bool active = controller == null || controller.gameObject.activeInHierarchy;
            SetShown(active);

            // No grip pose from this device: ride on the controller instead.
            if (!GripTracked() && controller != null)
            {
                transform.SetPositionAndRotation(controller.position, controller.rotation);
            }

            if (grabPoint != null && controller != null)
            {
                grabPoint.localPosition = controller.InverseTransformPoint(FistPosition);
                grabPoint.localRotation = Quaternion.identity;
            }

            if (poser != null && active)
            {
                float grip = inputs != null ? inputs.selectInput.ReadValue() : 0f;
                float trigger = inputs != null ? inputs.activateInput.ReadValue() : 0f;
                if (inputs != null && inputs.hasSelection) { grip = 1f; }
                poser.Tick(Time.deltaTime, grip, trigger);
            }
        }

        private bool GripTracked()
        {
            if (gripDriver == null || !gripDriver.isActiveAndEnabled) { return false; }
            UnityEngine.InputSystem.InputAction action = gripDriver.positionInput.action;
            return action != null && action.controls.Count > 0;
        }

        private void SetShown(bool shown)
        {
            if (shown == _shown) { return; }
            _shown = shown;
            for (int r = 0; r < _renderers.Length; r++)
            {
                if (_renderers[r] != null) { _renderers[r].enabled = shown; }
            }
        }

        public void Bind(bool leftHand, Transform trackedController, Transform handModel, HandPoser fingers,
            XRBaseInputInteractor buttons, Transform fist, Transform fingertip)
        {
            isLeftHand = leftHand;
            controller = trackedController;
            model = handModel;
            poser = fingers;
            inputs = buttons;
            grabPoint = fist;
            pokePoint = fingertip;
            _restStored = false;
            _renderers = model != null ? model.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
        }

        /// <summary>The driver following the grip pose; without one the hand rides on the controller.</summary>
        public void BindGripDriver(TrackedPoseDriver driver) => gripDriver = driver;
    }
}
