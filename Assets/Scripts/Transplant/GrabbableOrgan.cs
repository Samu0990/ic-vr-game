using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRSurgery.Interaction;

namespace VRSurgery.Transplant
{
    public enum OrganRole
    {
        /// <summary>The failing heart. The operation wants it out.</summary>
        Native,

        /// <summary>The replacement. The operation wants it seated in the empty pericardium.</summary>
        Donor,
    }

    /// <summary>
    /// An organ the player can pick up, on the same grab contract as every instrument in the
    /// project — SurgicalInteractable for identity and grip, XRGrabInteractable for the hand,
    /// ToolReleasePhysics for letting go.
    ///
    /// It deliberately does NOT derive from SurgicalTool, and that is not a style preference.
    /// SurgicalTool republishes every grab onto SurgeryEvents.OnToolGrabbed, which
    /// EventSessionController listens to in order to start the round, and which
    /// SurgeryObjectiveSystem uses to tick off "pick up the instrument" objectives. Making a heart
    /// a tool would start the visitor's clock the moment they touched the organ and would satisfy
    /// objectives meant for a scalpel. Same contract, different meaning.
    ///
    /// Each heart has a short life and it only goes one way. The sick one sits in its seat until
    /// the pump has arrested it, comes out in the hand during the cardiectomy and nowhere else,
    /// and then lies still in the removal basin. The new one waits in its basin, may be picked
    /// up there, and seats only when its stage comes — after which it stays seated. Both are
    /// held where they belong by refusing the hand (a select filter) and by being pinned, not
    /// by trusting what physics and the release logic do with a dropped object.
    /// </summary>
    [RequireComponent(typeof(SurgicalInteractable))]
    public class GrabbableOrgan : MonoBehaviour, IXRSelectFilter
    {
        /// <summary>Said when the hand closes on the sick heart before its stage.</summary>
        public const string TooEarlyNotice = "O coração ainda não pode sair: o paciente precisa estar na bomba primeiro";

        /// <summary>Said when the hand closes on the sick heart lying in the removal basin.</summary>
        public const string AlreadyRemovedNotice = "O coração doente já foi retirado";

        /// <summary>Said when the new heart is let go in the chest before the old one is out.</summary>
        public const string DonorTooEarlyNotice = "O coração novo só entra depois que o doente sair";

        /// <summary>Said when the hand closes on the new heart once it is seated.</summary>
        public const string SeatedNotice = "O coração novo já está no lugar";

        [SerializeField] private OrganRole role = OrganRole.Donor;

        [Tooltip("Where this organ belongs. For the donor heart, the empty pericardium; for the " +
                 "native heart, the seat it has to be lifted away from.")]
        [SerializeField] private Transform seat;

        [Tooltip("How close the donor heart has to sit to count as implanted, in metres.")]
        //
        // 3,5 cm era uma janela menor que um quarto do proprio orgao, que tem cerca de 16 cm. Em
        // VR, com a caixa toracica em volta e sem peso nenhum na mao para dizer onde o coracao
        // esta, acertar isso e sorte: o visitante solta, nada acontece, e ele nao tem como saber
        // o que faltou. A janela agora e do tamanho do orgao.
        [SerializeField, Min(0.005f)] private float seatTolerance = 0.09f;

        [Tooltip("Ao soltar dentro da janela, o coracao assenta sozinho no lugar. Sem isto ele " +
                 "fica onde a mao largou, meio torto, e o encaixe nao se le como encaixe.")]
        [SerializeField] private bool snapIntoSeat = true;

        [Tooltip("How far the native heart has to travel from its seat to count as explanted.")]
        [SerializeField, Min(0.02f)] private float removalDistance = 0.20f;

        [Tooltip("Native heart only: the basin beside the table where it lies once it is out.")]
        [SerializeField] private Transform removalBasin;

        [Tooltip("Optional. Told when this organ's stage is finished.")]
        [SerializeField] private TransplantProcedure procedure;

        private SurgicalInteractable _interactable;
        private XRGrabInteractable _grab;
        private Rigidbody _body;
        private ToolReleasePhysics _releasePhysics;
        private ReturnHomeOnRelease _returnHome;
        private bool _reported;
        private Vector3 _restPosition;
        private Quaternion _restRotation;
        private bool _restCaptured;
        private bool _attempting;
        private int _attemptFrame;
        private bool _sendHome;
        private RigidbodyInterpolation _interpolation;

        /// <summary>Distance from where this organ belongs, in metres. Negative with no seat.</summary>
        public float DistanceFromSeat =>
            seat != null ? Vector3.Distance(transform.position, seat.position) : -1f;

        /// <summary>True once this organ has done what the operation needed it to do.</summary>
        public bool Satisfied { get; private set; }

        public bool IsHeld => _interactable != null && _interactable.IsHeld;

        public OrganRole Role => role;

        /// <summary>
        /// True while it is kept exactly where it is — seated in the chest, back in its seat, or
        /// lying in the removal basin: no gravity, no drop on release, no respawn.
        /// </summary>
        public bool IsPinned { get; private set; }

        public event Action<GrabbableOrgan> SatisfiedChanged;

        /// <summary>A hand closed on this organ when it could not be taken, with what the room said. Once per attempt.</summary>
        public event Action<GrabbableOrgan, string> GrabRefused;

        private void Awake()
        {
            _interactable = GetComponent<SurgicalInteractable>();
            _grab = GetComponent<XRGrabInteractable>();
            _body = GetComponent<Rigidbody>();
            _releasePhysics = GetComponent<ToolReleasePhysics>();
            _returnHome = GetComponent<ReturnHomeOnRelease>();
            _interpolation = _body != null ? _body.interpolation : RigidbodyInterpolation.None;
            CaptureRest();
        }

        /// <summary>
        /// Onde o orgao comeca. Para o coracao doente isso e o assento dele, e e para ca que ele
        /// volta se for largado antes de sair do peito.
        /// </summary>
        private void CaptureRest()
        {
            if (_restCaptured) { return; }

            _restPosition = transform.position;
            _restRotation = transform.rotation;
            _restCaptured = true;
        }

        private void OnEnable()
        {
            _interactable.Grabbed += HandleGrabbed;
            _interactable.Released += HandleReleased;

            // Asked by the interaction manager whenever a hand wants this organ: the lock is
            // decided before the grab, so a heart that may not move never leaves its place.
            if (_grab != null) { _grab.selectFilters.Add(this); }
        }

        private void OnDisable()
        {
            _interactable.Grabbed -= HandleGrabbed;
            _interactable.Released -= HandleReleased;
            if (_grab != null) { _grab.selectFilters.Remove(this); }
            _attempting = false;
        }

        /// <summary>In the hand it moves by physics, and smoothly: interpolation back on.</summary>
        private void HandleGrabbed(SurgicalInteractable source, IHandInteractor holder)
        {
            _sendHome = false;
            if (_body != null) { _body.interpolation = _interpolation; }
        }

        private void Update()
        {
            // Sent home a frame after the release, once the release physics has armed its drop:
            // done during the release, the drop it armed afterwards would take it off the basin.
            if (_sendHome)
            {
                _sendHome = false;
                if (_returnHome != null) { _returnHome.Respawn(); }
            }

            // The donor heart is judged continuously while loose in its stage, so one lowered or
            // dropped into the seat seats as it arrives rather than only when let go right there.
            if (role == OrganRole.Donor && !Satisfied && !IsHeld && InDonorStage() &&
                seat != null && DistanceFromSeat <= seatTolerance)
            {
                Seat();
            }
        }

        // ------------------------------------------------------------------ the lock

        bool IXRSelectFilter.canProcess => isActiveAndEnabled;

        bool IXRSelectFilter.Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable) =>
            ConsiderGrab(interactor != null && interactor.isSelectActive);

        /// <summary>Why this organ may not be taken right now, or null when it may.</summary>
        public string LockReason()
        {
            if (role == OrganRole.Native)
            {
                if (Satisfied) { return AlreadyRemovedNotice; }
                if (procedure != null && procedure.Stage != TransplantStage.RemoveNativeHeart) { return TooEarlyNotice; }
                return null;
            }

            return Satisfied ? SeatedNotice : null;
        }

        /// <summary>
        /// Whether a hand may take this organ now. A hand that is actually closing on it while it
        /// may not hears why — once for the squeeze, not once per frame of it: the manager asks
        /// every frame for as long as the grip is held, and an attempt ends when the hand opens,
        /// leaves, or a frame goes by without it asking.
        /// </summary>
        public bool ConsiderGrab(bool attempting)
        {
            string reason = LockReason();
            if (reason == null)
            {
                _attempting = false;
                return true;
            }

            if (!attempting)
            {
                _attempting = false;
                return false;
            }

            bool sameAttempt = _attempting && Time.frameCount - _attemptFrame <= 2;
            _attempting = true;
            _attemptFrame = Time.frameCount;

            if (!sameAttempt)
            {
                if (procedure != null) { procedure.ShowNotice(reason); }
                GrabRefused?.Invoke(this, reason);
            }

            return false;
        }

        // ------------------------------------------------------------------ letting go

        private void HandleReleased(SurgicalInteractable source, IHandInteractor previousHolder)
        {
            if (role == OrganRole.Native) { ReleaseNative(); }
            else { ReleaseDonor(); }
        }

        private void ReleaseNative()
        {
            // Antes da bomba o coracao doente nao sai, e o visitante precisa ouvir por que.
            // A mao nem chega a pegar (o filtro recusa); isto cobre quem o tirou do lugar por
            // outro caminho: volta para o assento, com o motivo no monitor por alguns segundos.
            if (procedure != null && procedure.Stage != TransplantStage.RemoveNativeHeart && !Satisfied)
            {
                procedure.ShowNotice(TooEarlyNotice);
                PinAt(_restPosition, _restRotation);
                return;
            }

            if (Satisfied)
            {
                LayInBasin();
                return;
            }

            if (seat == null || DistanceFromSeat < removalDistance)
            {
                // Lifted and put back down inside the chest: hesitation, not a cardiectomy. It
                // goes back to its seat instead of falling through a body with no colliders.
                PinAt(_restPosition, _restRotation);
                return;
            }

            // Out: into the removal basin, where it stays, still.
            LayInBasin();
            Satisfied = true;
            SatisfiedChanged?.Invoke(this);
            Report();
        }

        private void ReleaseDonor()
        {
            if (Satisfied || seat == null) { return; }

            if (InDonorStage())
            {
                // O doador assenta ao ser solto perto o bastante, e assenta direito.
                if (DistanceFromSeat <= seatTolerance) { Seat(); }
                return;
            }

            // Let go in the chest before the old heart is out: there is nowhere for it yet. It
            // goes back to its basin, and the room says why — seating it here used to mark it
            // done, so when its stage finally came it never reported again and the operation
            // stopped at "Posicione o coração do doador".
            if (DistanceFromSeat <= seatTolerance * 2f)
            {
                if (procedure != null) { procedure.ShowNotice(DonorTooEarlyNotice); }
                if (_returnHome != null) { _sendHome = true; }
                else { PinAt(_restPosition, _restRotation); Unpin(); }
            }
        }

        private bool InDonorStage() => procedure == null || procedure.Stage == TransplantStage.PlaceDonorHeart;

        private void Seat()
        {
            PinAt(snapIntoSeat ? seat.position : transform.position, snapIntoSeat ? seat.rotation : transform.rotation);
            Satisfied = true;
            SatisfiedChanged?.Invoke(this);
            Report();
        }

        /// <summary>The sick heart's last place: the removal basin, or where it was let go if the scene has none.</summary>
        private void LayInBasin()
        {
            if (removalBasin != null) { PinAt(removalBasin.position, removalBasin.rotation); }
            else { PinAt(transform.position, transform.rotation); }
        }

        /// <summary>
        /// Holds the organ exactly here. The release physics would otherwise drop it two frames
        /// later — a seated donor fell out of the chest — and the respawn would treat it as lost.
        /// Interpolation goes off while it rests: on an interpolated body the next frame writes
        /// the last simulated pose back over a teleport, and the organ ends up where it was.
        /// </summary>
        private void PinAt(Vector3 position, Quaternion rotation)
        {
            IsPinned = true;
            _sendHome = false;
            if (_releasePhysics != null) { _releasePhysics.Hold = true; }
            if (_returnHome != null) { _returnHome.Hold = true; }

            if (_body != null)
            {
                if (!_body.isKinematic)
                {
                    _body.linearVelocity = Vector3.zero;
                    _body.angularVelocity = Vector3.zero;
                }

                _body.interpolation = RigidbodyInterpolation.None;
                _body.isKinematic = true;
                _body.useGravity = false;
                _body.position = position;
                _body.rotation = rotation;
            }

            transform.SetPositionAndRotation(position, rotation);
        }

        private void Unpin()
        {
            IsPinned = false;
            if (_releasePhysics != null) { _releasePhysics.Hold = false; }
            if (_returnHome != null) { _returnHome.Hold = false; }
        }

        private void Report()
        {
            if (_reported || procedure == null)
            {
                return;
            }

            // Latched only once the procedure took it: a report it refused must be able to come
            // again when the stage is right.
            _reported = procedure.CompleteStage(role == OrganRole.Native
                ? TransplantStage.RemoveNativeHeart
                : TransplantStage.PlaceDonorHeart);
        }

        /// <summary>Puts the organ back in play for the next visitor.</summary>
        public void ResetOrgan()
        {
            Satisfied = false;
            _reported = false;
            _attempting = false;
            _sendHome = false;
            CaptureRest();

            // Back where it started, still — and free to be taken again when its stage comes.
            PinAt(_restPosition, _restRotation);
            Unpin();
        }

        public void Bind(OrganRole organRole, Transform organSeat, TransplantProcedure transplant)
        {
            role = organRole;
            seat = organSeat;
            procedure = transplant;
        }

        /// <summary>Native heart: where it is laid once it is out of the chest.</summary>
        public void BindRemovalBasin(Transform basin) => removalBasin = basin;
    }
}
