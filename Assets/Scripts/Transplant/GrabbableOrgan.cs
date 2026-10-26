using System;
using UnityEngine;
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
    /// </summary>
    [RequireComponent(typeof(SurgicalInteractable))]
    public class GrabbableOrgan : MonoBehaviour
    {
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

        [Tooltip("Optional. Told when this organ's stage is finished.")]
        [SerializeField] private TransplantProcedure procedure;

        private SurgicalInteractable _interactable;
        private bool _reported;
        private Vector3 _seatedLocalPosition;
        private Quaternion _seatedLocalRotation;
        private bool _restCaptured;

        /// <summary>Distance from where this organ belongs, in metres. Negative with no seat.</summary>
        public float DistanceFromSeat =>
            seat != null ? Vector3.Distance(transform.position, seat.position) : -1f;

        /// <summary>True once this organ has done what the operation needed it to do.</summary>
        public bool Satisfied { get; private set; }

        public bool IsHeld => _interactable != null && _interactable.IsHeld;

        public event Action<GrabbableOrgan> SatisfiedChanged;

        private void Awake()
        {
            _interactable = GetComponent<SurgicalInteractable>();
            CaptureRest();
        }

        /// <summary>
        /// Onde o orgao comeca. Para o coracao doente isso e o assento dele, e e para ca que ele
        /// volta se for arrancado antes da hora.
        /// </summary>
        private void CaptureRest()
        {
            if (_restCaptured) { return; }

            _seatedLocalPosition = transform.position;
            _seatedLocalRotation = transform.rotation;
            _restCaptured = true;
        }

        private void OnEnable()
        {
            _interactable.Released += HandleReleased;
        }

        private void OnDisable()
        {
            _interactable.Released -= HandleReleased;
        }

        private void Update()
        {
            // The donor heart is judged continuously, because a visitor lowering it into the chest
            // should see it seat as they arrive rather than having to let go to find out.
            if (role == OrganRole.Donor && !Satisfied)
            {
                Evaluate();
            }
        }

        /// <summary>
        /// Decides whether this organ is where the operation wants it. The native heart is judged
        /// on release rather than continuously: carrying it across the room and back should not
        /// count as an explant the moment it passes the threshold mid-swing.
        /// </summary>
        private void HandleReleased(SurgicalInteractable source, IHandInteractor previousHolder)
        {
            if (role == OrganRole.Native)
            {
                // Antes da bomba o coracao doente nao sai, e o visitante precisa ouvir por que.
                // Arrancar um coracao de um paciente que ainda circula o proprio sangue nao e um
                // atalho, e o fim da operacao — e silenciosamente nao fazer nada ensina menos
                // ainda do que recusar.
                if (procedure != null && procedure.Stage != TransplantStage.RemoveNativeHeart)
                {
                    procedure.SetUrgentInstruction(
                        "O coração ainda não pode sair: o paciente precisa estar na bomba primeiro");
                    ReturnToRest();
                    return;
                }

                Evaluate();
                return;
            }

            // O doador assenta ao ser solto perto o bastante, e assenta direito.
            if (!Satisfied && seat != null && DistanceFromSeat <= seatTolerance)
            {
                SnapToSeat();
            }
        }

        /// <summary>Poe o orgao exatamente no lugar dele, para o encaixe se ler como encaixe.</summary>
        private void SnapToSeat()
        {
            if (!snapIntoSeat || seat == null) { return; }

            transform.SetPositionAndRotation(seat.position, seat.rotation);
        }

        /// <summary>Devolve o orgao ao lugar de onde foi tirado fora de hora.</summary>
        private void ReturnToRest()
        {
            CaptureRest();
            transform.SetPositionAndRotation(_seatedLocalPosition, _seatedLocalRotation);
        }

        private void Evaluate()
        {
            if (Satisfied || seat == null)
            {
                return;
            }

            float distance = DistanceFromSeat;

            bool done = role == OrganRole.Donor
                ? distance <= seatTolerance && !IsHeld
                : distance >= removalDistance;

            if (!done)
            {
                return;
            }

            if (role == OrganRole.Donor) { SnapToSeat(); }

            Satisfied = true;
            SatisfiedChanged?.Invoke(this);
            Report();
        }

        private void Report()
        {
            if (_reported || procedure == null)
            {
                return;
            }

            _reported = true;
            procedure.CompleteStage(role == OrganRole.Native
                ? TransplantStage.RemoveNativeHeart
                : TransplantStage.PlaceDonorHeart);
        }

        /// <summary>Puts the organ back in play for the next visitor.</summary>
        public void ResetOrgan()
        {
            Satisfied = false;
            _reported = false;
            ReturnToRest();
        }

        public void Bind(OrganRole organRole, Transform organSeat, TransplantProcedure transplant)
        {
            role = organRole;
            seat = organSeat;
            procedure = transplant;
        }
    }
}
