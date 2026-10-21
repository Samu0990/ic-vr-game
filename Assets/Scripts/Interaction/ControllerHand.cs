using UnityEngine;
using UnityEngine.InputSystem.XR;
using VRSurgery.Data;
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

        [Tooltip("How far forward, in degrees, an instrument is tipped in the fist. 0 lays it " +
                 "along the handle, out past the thumb; 40 is the angle a scalpel or a pen makes " +
                 "with the hand that holds it.")]
        [SerializeField, Range(0f, 80f)] private float toolPitch = 40f;

        [Tooltip("The hold used for something that declares none of its own: slim, pencil-grip. " +
                 "A tool's own ToolDefinition.Hold overrides this.")]
        [SerializeField] private HandHold defaultHold = HandHold.Default;

        [Tooltip("How quickly the instrument's own hold angle takes over when one is picked up, " +
                 "per second. Keeps the tool from popping to a new angle the frame it is grabbed.")]
        [SerializeField, Min(1f)] private float holdBlendSpeed = 10f;

        [SerializeField, HideInInspector] private Vector3 _modelRestPosition;
        [SerializeField, HideInInspector] private Quaternion _modelRestRotation = Quaternion.identity;
        [SerializeField, HideInInspector] private bool _restStored;

        private Renderer[] _renderers = new Renderer[0];
        private HandHold _hold;
        private bool _holding;
        private float _pitchNow = float.NaN;
        private readonly float[] _closures = new float[HandPoser.FingerCount];
        private readonly float[] _frame = new float[HandPoser.FingerCount];
        private Vector3 _seatLocal;
        private bool _seatSolved;
        private SurgicalInteractable _heldLast;
        private bool _closuresSolved;

        /// <summary>Closure the fingers are put at while working out where a handle would sit.</summary>
        private const float SeatReferenceClosure = 0.72f;


        public bool IsLeftHand => isLeftHand;
        public Transform Model => model;
        public Transform GrabPoint => grabPoint;
        public HandPoser Poser => poser;

        /// <summary>
        /// The middle of the fist in world space: where the handle of a held instrument lies.
        ///
        /// Taken from the rig — the hollow between the palm and the curled fingers — rather than
        /// from the hand's origin. The origin is the controller's grip axis, and the hand model
        /// is deliberately slid <see cref="palmDepth"/> off that axis so the palm appears to wrap
        /// a handle. Seating instruments on the origin therefore parked them in mid-air beside a
        /// hand that was closing on nothing: on this rig the fingertips ended up 40 to 76 mm from
        /// a handle 8 mm thick.
        /// </summary>
        public Vector3 FistPosition => transform.TransformPoint((_seatSolved ? _seatLocal : Vector3.zero) + fistOffset);

        /// <summary>
        /// Where this particular instrument's handle is seated. A few millimetres matter on a
        /// scalpel: the generic centre of the fist is suitable for a cylindrical grip, but a
        /// precision pinch sits between thumb, index and middle instead.
        /// </summary>
        public Vector3 HoldPosition(in HandHold hold)
        {
            Vector3 offset = hold.seatOffset;
            if (isLeftHand) { offset.x = -offset.x; }
            return FistPosition + transform.TransformVector(offset);
        }

        /// <summary>
        /// How an instrument sits in this fist: the grip frame, tipped forward by
        /// <see cref="toolPitch"/>. A tool's own +Z — the blade, the nib, the needle — ends up
        /// pointing out past the thumb and down towards the fingers, which is how a hand holds
        /// one.
        /// </summary>
        public Quaternion ToolHold => transform.rotation * Quaternion.Euler(CurrentPitch, 0f, 0f);

        /// <summary>
        /// The angle in use right now: the held instrument's own, eased in, or this hand's
        /// default when nothing is held.
        /// </summary>
        public float CurrentPitch => float.IsNaN(_pitchNow) ? toolPitch : _pitchNow;

        /// <summary>The hold driving the fingers right now. Empty-handed this is the default.</summary>
        public HandHold CurrentHold => _holding ? _hold : SafeDefaultHold;

        /// <summary>
        /// This hand's fallback hold, guarded against the scene having been saved before the
        /// field existed. Unity reads a struct that is absent from the YAML as all zeros, and an
        /// all-zero hold would hold every instrument with a flat open palm.
        /// </summary>
        private HandHold SafeDefaultHold => defaultHold.IsConfigured ? defaultHold : HandHold.Default;

        /// <summary>True while an instrument is in this fist.</summary>
        public bool IsHolding => _holding;

        /// <summary>
        /// How far the trigger is pulled, 0 to 1. Already read every frame to pose the index
        /// finger; exposed so a control the hand cannot physically touch — a panel across the
        /// room — can still be worked by pointing at it and squeezing.
        /// </summary>
        public float Trigger => inputs != null ? inputs.activateInput.ReadValue() : 0f;

        /// <summary>
        /// The hold to use for something being held: the instrument's own if it authored one,
        /// otherwise the fallback. Pure and static so the choice can be tested without an
        /// interactor, a rig or a headset.
        /// </summary>
        public static HandHold HoldFor(SurgicalInteractable held, in HandHold fallback)
        {
            ToolDefinition definition = held != null ? held.ToolDefinition : null;
            return definition != null && definition.Hold.IsConfigured ? definition.Hold : fallback;
        }

        private void Awake()
        {
            if (model != null) { _renderers = model.GetComponentsInChildren<Renderer>(true); }
            AlignModel();
            if (poser != null) { poser.Calibrate(); }
            MovePokePointToFingertip();

            // _hold só é preenchido ao segurar algo; sem isto DeclaredClosure leria o struct
            // zerado e semearia zeros, que é exatamente o que se quer evitar.
            _hold = SafeDefaultHold;

            // Sem isto as aberturas começam em zero, que é mão aberta, não mão relaxada.
            for (int finger = 0; finger < _closures.Length; finger++)
            {
                _closures[finger] = DeclaredClosure(finger);
            }
        }

        private void OnEnable()
        {
            // This glove is driven by its own grip-pose TrackedPoseDriver. The XR template may
            // deactivate the old controller GameObject when its input modality changes; that
            // object is only an input/interactor reference and must not make this independent
            // glove disappear with it.
            SetShown(true);
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

            SolveGripSeat(palmBone);
        }

        /// <summary>
        /// Works out where a handle sits inside this fist, once, from the model's own bones.
        ///
        /// The fingers are put in a reference grip and the seat is taken as the middle of what
        /// they enclose, so it follows whatever hand model is dropped in instead of being a
        /// hand-tuned offset that only suits one rig.
        /// </summary>
        private void SolveGripSeat(Transform palmBone)
        {
            _seatSolved = false;
            if (poser == null || model == null) { return; }

            poser.Calibrate();
            if (!poser.IsCalibrated) { return; }

            for (int finger = 0; finger < HandPoser.FingerCount; finger++)
            {
                poser.SetFingerImmediate(finger, SeatReferenceClosure);
            }

            Transform wrist = HandPoser.FindBone(model, "Wrist");
            Vector3 sum = palmBone != null ? palmBone.position : (wrist != null ? wrist.position : transform.position);
            int count = 1;

            // The seat has to lie between both sides of the grip. Averaging only the three
            // curled fingers parked the handle against them and left the opposing thumb
            // 18-40 mm away. Include thumb and index so a slim instrument rests in the actual
            // hollow enclosed by the whole hand, not on one wall of it.
            for (int finger = 0; finger < HandPoser.FingerCount; finger++)
            {
                Vector3 tip = poser.TipPosition(finger);
                if (tip != Vector3.zero) { sum += tip; count++; }
            }

            _seatLocal = transform.InverseTransformPoint(sum / count);
            _seatSolved = true;

            poser.Restore();
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
            // Keep the custom glove visible while this component is active. In particular, do
            // not copy the active state of the template controller: XRInputModalityManager can
            // turn that object off even though the grip-pose driver still has a valid device.
            bool active = isActiveAndEnabled;
            SetShown(active);

            // No grip pose from this device: ride on the controller instead.
            if (!GripTracked() && controller != null)
            {
                transform.SetPositionAndRotation(controller.position, controller.rotation);
            }

            ResolveHeldTool(Time.deltaTime);
            PlaceGrabPoint();

            if (poser != null && active)
            {
                float trigger = inputs != null ? inputs.activateInput.ReadValue() : 0f;

                if (_holding)
                {
                    // An instrument is in the fist, so the handle decides where the fingers stop,
                    // not the button. Squeezing harder used to drive the grip to a full fist, which
                    // closed the fingers straight through a 6 mm scalpel shank.
                    //
                    // The index still answers the trigger, curling from where the instrument rests
                    // it: that is what working a pair of forceps looks like from inside the glove.
                    for (int finger = 0; finger < _closures.Length; finger++)
                    {
                        _frame[finger] = _closures[finger];
                    }

                    // A scalpel is stabilised by the index finger; the controller trigger must
                    // not suddenly clamp that finger through its thin handle. Hinged tools keep
                    // the trigger response they already had.
                    _frame[HandPoser.Index] = _hold.precisionPinch
                        ? _closures[HandPoser.Index]
                        : Mathf.Lerp(_closures[HandPoser.Index], 1f, trigger);
                    poser.TickFingers(Time.deltaTime, _frame);
                }
                else
                {
                    float grip = inputs != null ? inputs.selectInput.ReadValue() : 0f;
                    poser.Tick(Time.deltaTime, grip, trigger);
                }
            }
        }

        /// <summary>
        /// Works out what is being held and eases this hand's hold angle toward that
        /// instrument's own. Falls back to <see cref="defaultHold"/> for anything that declares
        /// no hold, which is still a grip on a handle rather than a clenched fist.
        /// </summary>
        private void ResolveHeldTool(float deltaTime)
        {
            _holding = inputs != null && inputs.hasSelection;

            if (_holding)
            {
                SurgicalInteractable held = FindHeldInstrument();
                _hold = HoldFor(held, SafeDefaultHold);

                // Solving is a short sweep per finger, so it runs when what is held changes, not
                // every frame.
                // O flag explícito importa: segurar algo que não é um instrumento nosso devolve
                // null aqui, e comparar null com null pularia o solve, deixando todo dedo no zero
                // do array — mão aberta e reta agarrada ao objeto.
                if (!_closuresSolved || held != _heldLast)
                {
                    _heldLast = held;
                    SolveHoldClosures(held);
                    _closuresSolved = true;
                }
            }
            else
            {
                // An empty hand closes as a whole finger again. Leaving the pencil-grip share on
                // would keep the knuckles straight after the instrument is gone.
                if (_heldLast != null && poser != null)
                {
                    poser.SetKnuckleShare(HandPoser.Thumb, 1f);
                    poser.SetKnuckleShare(HandPoser.Index, 1f);
                }

                _heldLast = null;
                _closuresSolved = false;
            }

            float target = _holding ? _hold.pitchDegrees : toolPitch;
            _pitchNow = float.IsNaN(_pitchNow)
                ? target
                : Mathf.Lerp(_pitchNow, target, deltaTime <= 0f ? 1f : Mathf.Clamp01(deltaTime * holdBlendSpeed));
        }

        /// <summary>
        /// Closes each finger exactly as far as it has to go to reach this instrument's handle,
        /// solved against the rig.
        ///
        /// One number for the whole hand cannot do this: on this hand the middle finger meets a
        /// scalpel at about 0.8 closed, the index only at 1.0, and the little finger never gets
        /// nearer than 30 mm. A finger that cannot reach keeps the pose the instrument asked for
        /// instead of closing uselessly through thin air.
        /// </summary>
        private void SolveHoldClosures(SurgicalInteractable held)
        {
            if (poser == null || !poser.IsCalibrated)
            {
                for (int finger = 0; finger < _closures.Length; finger++)
                {
                    _closures[finger] = DeclaredClosure(finger);
                }

                return;
            }

            Vector3 origin = HoldPosition(_hold);
            Vector3 axis = transform.rotation * Quaternion.Euler(_hold.pitchDegrees, 0f, 0f) * Vector3.forward;
            float radius = HandleRadiusFor(held);

            // A pencil grip bends the thumb and index at the phalanges while their knuckles stay
            // nearly straight. Without this the solver can only reach a 7 mm shank by curling the
            // whole finger, and it measured index 1.00 and thumb 0.96 on the scalpel: a clenched
            // fist with a blade poking out of it, not a hand holding a knife.
            float share = _hold.precisionPinch ? _hold.KnuckleShare : 1f;
            poser.SetKnuckleShare(HandPoser.Thumb, share);
            poser.SetKnuckleShare(HandPoser.Index, share);

            for (int finger = 0; finger < _closures.Length; finger++)
            {
                // In a surgical precision grip only thumb, index and middle meet the scalpel.
                // Solving ring and little against the same 7 mm axis twists the whole hand into
                // the unnatural five-finger cage visible in the old capture. They tuck instead of
                // hanging half open out to the side.
                if (_hold.precisionPinch && finger > HandPoser.Middle)
                {
                    _closures[finger] = _hold.Tuck;
                    continue;
                }

                float solved = poser.SolveClosure(finger, origin, axis, radius, out float solvedError);
                float declared = DeclaredClosure(finger);

                // Keep whichever pose actually ends up nearer the handle. A fixed tolerance threw
                // away a solved closure that was closer than the authored one simply for not
                // reaching the steel — which is exactly the thumb's case on this rig.
                float declaredError = Mathf.Abs(
                    poser.DistanceAt(finger, declared, origin, axis) - radius);

                _closures[finger] = solvedError <= declaredError ? solved : declared;
            }
        }

        /// <summary>
        /// How thick the handle is where the hand closes on it: what the instrument declares, or
        /// measured off its own collider when it declares nothing.
        /// </summary>
        private float HandleRadiusFor(SurgicalInteractable held)
        {
            if (_hold.handleRadius > 0f) { return _hold.handleRadius; }

            Collider collider = held != null ? held.GetComponent<Collider>() : null;
            switch (collider)
            {
                case BoxCollider box: return Mathf.Min(box.size.x, box.size.y) * 0.5f;
                case CapsuleCollider capsule: return capsule.radius;
                default: return 0.009f;
            }
        }

        private float DeclaredClosure(int finger)
        {
            if (finger == HandPoser.Thumb) { return _hold.thumbClosure; }
            if (finger == HandPoser.Index) { return _hold.indexClosure; }
            return _hold.gripClosure;
        }

        /// <summary>The instrument this hand has selected, if it is one of ours.</summary>
        private SurgicalInteractable FindHeldInstrument()
        {
            if (inputs == null) { return null; }

            var selected = inputs.interactablesSelected;
            for (int i = 0; i < selected.Count; i++)
            {
                Component component = selected[i] as Component;
                if (component == null) { continue; }

                SurgicalInteractable instrument = component.GetComponentInParent<SurgicalInteractable>();
                if (instrument != null) { return instrument; }
            }

            return null;
        }

        /// <summary>
        /// Puts the grab point in the fist, turned the way this hand holds an instrument.
        ///
        /// Position AND rotation come from the hand. Only the position used to: the grab point is
        /// parented to the rig's controller transform, which follows the POINTING pose, so a tool
        /// was handed the aim direction while the glove closed around it on the GRIP pose. Those
        /// two differ by some 40 degrees on a Touch controller, and that gap is why a held
        /// instrument looked skewered through the hand instead of gripped by it — worst on the
        /// scalpel, which is long, thin, and the first thing anyone picks up.
        /// </summary>
        public void PlaceGrabPoint()
        {
            if (grabPoint == null) { return; }
            grabPoint.SetPositionAndRotation(HoldPosition(CurrentHold), ToolHold);
        }

        private bool GripTracked()
        {
            if (gripDriver == null || !gripDriver.isActiveAndEnabled) { return false; }
            UnityEngine.InputSystem.InputAction action = gripDriver.positionInput.action;
            return action != null && action.controls.Count > 0;
        }

        private void SetShown(bool shown)
        {
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
