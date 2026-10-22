using UnityEngine;
using VRSurgery.Transplant;

namespace VRSurgery.Feedback
{
    /// <summary>
    /// A seta que diz ONDE agir, com uma frase do que fazer ali.
    ///
    /// A irmã dela, <see cref="NextToolHint"/>, aponta para o instrumento certo na bandeja e se
    /// recusa, por escrito, a apontar para o paciente: "os anéis no paciente já dizem onde".
    /// Testado com gente de fora, não dizem. Saber que é o bisturi não ensina onde encostar o
    /// bisturi, e um visitante de estande não tem a quem perguntar — ele trava, e a fila atrás
    /// dele vê alguém parado sem entender o que o jogo quer.
    ///
    /// Então esta aponta para o lugar: a linha do esterno, o ponto da bomba, o vaso que falta,
    /// o coração que sai e o que entra. E escreve embaixo, em uma linha, o que fazer. As duas
    /// convivem porque respondem a perguntas diferentes — "com o quê" e "onde".
    ///
    /// Some sozinha quando não há o que apontar. Uma seta que fica apontando para algo já feito
    /// é pior do que seta nenhuma: ensina a ignorá-la.
    /// </summary>
    public class NextStepArrow : MonoBehaviour
    {
        [Header("Fontes")]
        [SerializeField] private TransplantProcedure procedure;
        [SerializeField] private BypassWorker bypass;
        [SerializeField] private VesselAnastomosis[] vessels;

        [Tooltip("Optional: while the new heart fibrillates, the place is the heart, between the paddles.")]
        [SerializeField] private DefibrillationWorker defibrillation;

        [Header("Alvos")]
        [Tooltip("A linha do esterno: onde o corte da pele e a serra acontecem.")]
        [SerializeField] private Transform midline;

        [SerializeField] private Transform nativeHeart;
        [SerializeField] private Transform donorHeart;

        [Tooltip("O assento no tórax onde o coração doador entra.")]
        [SerializeField] private Transform heartSeat;

        [Header("Aparência")]
        [SerializeField] private Transform arrow;
        [SerializeField] private TextMesh label;
        [SerializeField] private float height = 0.12f;
        [SerializeField] private float bounce = 0.03f;
        [SerializeField, Min(0.1f)] private float bounceHz = 1.2f;

        private float _clock;

        /// <summary>Para onde a seta aponta agora, ou null.</summary>
        public Transform Target { get; private set; }

        /// <summary>A frase que está escrita embaixo da seta.</summary>
        public string Instruction { get; private set; } = string.Empty;

        private void Awake() => Show(false);

        private void LateUpdate() => Tick(Time.deltaTime);

        /// <summary>
        /// Recalcula alvo e frase. Recebe o próprio delta para um teste poder avançar o tempo
        /// sem esperar quadros de verdade.
        /// </summary>
        public void Tick(float deltaTime)
        {
            _clock += Mathf.Max(0f, deltaTime);
            Target = Where(out string say);
            Instruction = Target != null ? say : string.Empty;

            Show(Target != null);
            if (Target == null || arrow == null) { return; }

            float lift = height + bounce * (0.5f + 0.5f * Mathf.Sin(_clock * bounceHz * Mathf.PI * 2f));
            arrow.position = Target.position + Vector3.up * lift;

            if (label == null) { return; }

            label.text = Instruction;

            // De frente para quem está lendo. Uma frase que o visitante vê de lado, ou de trás,
            // é a mesma coisa que não ter frase nenhuma.
            Camera eye = Camera.main;
            if (eye != null)
            {
                Vector3 toEye = label.transform.position - eye.transform.position;
                toEye.y = 0f;
                if (toEye.sqrMagnitude > 1e-6f)
                {
                    label.transform.rotation = Quaternion.LookRotation(toEye.normalized, Vector3.up);
                }
            }
        }

        /// <summary>
        /// O lugar da etapa atual e a frase que vai com ele. As frases falam com alguém que
        /// nunca viu uma cirurgia: dizem o gesto, não o nome do procedimento.
        /// </summary>
        private Transform Where(out string say)
        {
            say = string.Empty;
            if (procedure == null) { return null; }

            switch (procedure.Stage)
            {
                case TransplantStage.SkinIncision:
                    say = "Corte aqui, de cima a baixo";
                    return midline;

                case TransplantStage.OpenChest:
                    say = "Serre o osso por esta linha";
                    return midline;

                case TransplantStage.OpenPericardium:
                    say = "Abra a bolsa do coração aqui";
                    return nativeHeart;

                case TransplantStage.GoOnBypass:
                case TransplantStage.Restart:
                    // A fibrillating heart is the step now, not the next pump site: the pump
                    // refuses to go on until the paddles have brought the rhythm back.
                    if (defibrillation != null && defibrillation.IsFibrillating && donorHeart != null)
                    {
                        say = "Segure o coração entre as pás";
                        return donorHeart;
                    }

                    say = "Encoste aqui e segure";
                    return bypass != null ? bypass.NextPoint : null;

                case TransplantStage.RemoveNativeHeart:
                    say = "Tire este coração e solte fora do peito";
                    return nativeHeart;

                case TransplantStage.PlaceDonorHeart:
                    // Antes de pegar, aponta o órgão; com ele na mão, aponta o lugar dele.
                    bool held = donorHeart != null && IsHeld(donorHeart);
                    say = held ? "Encaixe o coração aqui" : "Pegue este coração";
                    return held ? heartSeat : donorHeart;

                case TransplantStage.ConnectVessels:
                    Transform vessel = NextVessel();
                    say = "Costure este vaso";
                    return vessel;

                case TransplantStage.CloseSkin:
                    say = "Costure a pele por esta linha";
                    return midline;

                default:
                    return null;
            }
        }

        /// <summary>O primeiro vaso ainda aberto, que é sempre o próximo a costurar.</summary>
        private Transform NextVessel()
        {
            if (vessels == null) { return null; }

            for (int i = 0; i < vessels.Length; i++)
            {
                if (vessels[i] != null && !vessels[i].IsJoined) { return vessels[i].transform; }
            }

            return null;
        }

        private static bool IsHeld(Transform organ)
        {
            GrabbableOrgan grabbable = organ.GetComponentInParent<GrabbableOrgan>();
            return grabbable != null && grabbable.IsHeld;
        }

        private void Show(bool on)
        {
            if (arrow != null && arrow.gameObject.activeSelf != on) { arrow.gameObject.SetActive(on); }
        }

        /// <summary>Liga a seta ao resto da cena, a partir do construtor.</summary>
        /// <summary>The reperfusion fibrillation, so the arrow points at the heart while it lasts.</summary>
        public void BindDefibrillation(DefibrillationWorker worker) => defibrillation = worker;

        public void Bind(TransplantProcedure transplant, BypassWorker pump, VesselAnastomosis[] joins,
            Transform sternalLine, Transform sick, Transform donor, Transform seat,
            Transform arrowObject, TextMesh text)
        {
            procedure = transplant;
            bypass = pump;
            vessels = joins;
            midline = sternalLine;
            nativeHeart = sick;
            donorHeart = donor;
            heartSeat = seat;
            arrow = arrowObject;
            label = text;
        }
    }
}
