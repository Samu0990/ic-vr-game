using System;
using UnityEngine;
using VRSurgery.Interaction;

namespace VRSurgery.Transplant
{
    /// <summary>
    /// Faz a incisão sozinho, para conferir o corte sem óculos.
    ///
    /// Não é um atalho nem uma animação do resultado: o piloto só move o bisturi. Quem decide se
    /// cortou, onde cortou, se ficou fundo demais e que nota dar continua sendo o
    /// <see cref="SkinIncisionWorker"/>, pelo mesmo caminho que o visitante percorre — lâmina
    /// segura, de pé, encostada na pele, puxada ao longo da linha. Se o piloto fizer uma passada
    /// que o jogo não aceitaria, a demonstração mostra isso em vez de esconder, que é o motivo de
    /// ela existir.
    ///
    /// O traço é o clínico: da fúrcula esternal para o apêndice xifoide, ou seja, da cabeça para
    /// os pés (<c>along</c> de 1 a 0), numa passada só.
    ///
    /// Roda em Play, e também passo a passo no Editor — <see cref="Tick"/> é público e não lê
    /// <see cref="Time"/>, então o construtor da demonstração consegue avançar o corte quadro a
    /// quadro e fotografar cada etapa sem entrar em Play.
    /// </summary>
    public class IncisionAutopilot : MonoBehaviour
    {
        public enum Phase
        {
            /// <summary>Esperando a cirurgia chegar na etapa da incisão.</summary>
            Esperando,

            /// <summary>Levando o bisturi da bandeja até o começo da linha.</summary>
            Aproximando,

            /// <summary>A passada em si.</summary>
            Cortando,

            /// <summary>Afastando a lâmina para a ferida ficar à vista.</summary>
            Afastando,

            /// <summary>Acabou; a nota já saiu.</summary>
            Pronto,
        }

        [Header("Peças")]
        [SerializeField] private Transform scalpel;
        [SerializeField] private Transform bladeTip;
        [SerializeField] private SurgicalInteractable held;
        [SerializeField] private SkinIncisionWorker incision;
        [SerializeField] private ChestSkinPatch patch;
        [SerializeField] private TransplantProcedure procedure;

        [Header("Como a passada é feita")]
        [Tooltip("Começa a cirurgia sozinho se ela ainda estiver parada.")]
        [SerializeField] private bool beginProcedure = true;

        [Tooltip("Segundos parado antes de encostar a lâmina, para dar tempo de olhar.")]
        [SerializeField, Min(0f)] private float settleSeconds = 0.6f;

        [Tooltip("Segundos levando o bisturi da bandeja até o alto da linha.")]
        [SerializeField, Min(0.1f)] private float approachSeconds = 1.4f;

        [Tooltip("Velocidade da lâmina sobre a pele, m/s. Abaixo de minSliceSpeed do " +
                 "SkinIncisionWorker (2 cm/s) a lâmina apenas repousa e não corta.")]
        [SerializeField, Min(0.005f)] private float strokeSpeed = 0.05f;

        [Tooltip("Profundidade da lâmina abaixo da pele, em metros. O tooDeep do worker é 22 mm.")]
        [SerializeField, Min(0.001f)] private float cutDepth = 0.008f;

        [Tooltip("Inclinação da lâmina em relação à pele, graus. 0 é deitada de lado ao longo da linha.")]
        [SerializeField, Range(0f, 80f)] private float bladePitch = 38f;

        [Tooltip("Desvio lateral da linha média, em metros. Zero é a passada perfeita; " +
                 "aumente para ver o jogo reclamar de incisão torta.")]
        [SerializeField] private float lateralDrift;

        [Tooltip("Segundos afastando a lâmina no fim.")]
        [SerializeField, Min(0.1f)] private float liftSeconds = 1.2f;

        [Tooltip("Altura para onde a lâmina sobe no fim, em metros acima da pele.")]
        [SerializeField, Min(0.02f)] private float liftHeight = 0.12f;

        [Header("Leitura")]
        [Tooltip("Desenha na tela o que o worker está vendo enquanto a lâmina anda.")]
        [SerializeField] private bool showOverlay = true;

        [Tooltip("Avança o SkinIncisionWorker junto com a lâmina. Em Play o worker tem o Update " +
                 "dele e isto fica desligado; ligue para conduzir o corte passo a passo de fora, " +
                 "que é como o construtor da demonstração fotografa e como os testes medem.")]
        [SerializeField] private bool stepsTheWorker;

        private Phase _phase = Phase.Esperando;
        private float _inPhase;
        private float _along = 1f;
        private bool _bound;

        private Vector3 _tipLocalPosition;
        private Quaternion _tipLocalRotation = Quaternion.identity;
        private Pose _restPose;
        private Rigidbody _body;
        private ReturnHomeOnRelease _homing;
        private bool _grabbed;

        public Phase Current => _phase;

        /// <summary>Quanto da linha o worker já deu por cortada, 0..1.</summary>
        public float Progress01 => incision != null ? incision.Progress01 : 0f;

        /// <summary>
        /// A nota da incisão, perguntada ao worker. Vinha de assinar o evento Graded dele, e
        /// assinatura é feita em OnEnable, que fora do Play não roda: a demonstração fotografada
        /// terminava com "INCISÃO PERFEITA, 100/100" no log do worker e "não emitiu nota" no
        /// resumo. O worker publica a nota e o IsComplete, e a nota já está pronta quando o
        /// IsComplete vira verdadeiro — então basta perguntar.
        /// </summary>
        public IncisionGrade Grade => incision != null ? incision.Grade : default;

        public bool HasGrade => incision != null && incision.IsComplete;

        /// <summary>Onde a lâmina está na linha: 1 na fúrcula, 0 no xifoide.</summary>
        public float Along => _along;

        /// <summary>Raised quando a passada termina e o piloto para.</summary>
        public event Action Finished;

        private void Awake() => CaptureRig();

        private void OnDisable() => LetGo();

        /// <summary>
        /// Guarda onde a ponta da lâmina fica em relação ao corpo do bisturi. É medido uma vez,
        /// com a ferramenta parada na bandeja: depois disso o piloto posiciona o bisturi a partir
        /// de onde quer a ponta, e não o contrário.
        /// </summary>
        private void CaptureRig()
        {
            if (_bound || scalpel == null || bladeTip == null) { return; }

            _tipLocalPosition = scalpel.InverseTransformPoint(bladeTip.position);
            _tipLocalRotation = Quaternion.Inverse(scalpel.rotation) * bladeTip.rotation;
            _restPose = new Pose(scalpel.position, scalpel.rotation);
            _body = scalpel.GetComponent<Rigidbody>();
            _homing = scalpel.GetComponent<ReturnHomeOnRelease>();
            _bound = true;
        }

        private void Update() => Tick(Time.deltaTime);

        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f || scalpel == null || bladeTip == null || patch == null || incision == null)
            {
                return;
            }

            CaptureRig();
            _inPhase += deltaTime;

            switch (_phase)
            {
                case Phase.Esperando: TickWaiting(); break;
                case Phase.Aproximando: TickApproach(); break;
                case Phase.Cortando: TickStroke(deltaTime); break;
                case Phase.Afastando: TickLift(); break;
            }

            // O worker é quem julga, e é avançado pelo mesmo relógio que moveu a lâmina — mas só
            // quando alguém pediu. Em Play ele tem o próprio Update, e adivinhar isso por
            // Application.isPlaying erraria justamente nos testes, que rodam em Play e avançam o
            // relógio na mão.
            if (stepsTheWorker) { incision.Tick(deltaTime); }
        }

        private void TickWaiting()
        {
            if (procedure != null && beginProcedure && procedure.Stage == TransplantStage.Idle)
            {
                procedure.Begin();
            }

            bool ready = procedure == null || procedure.Stage == TransplantStage.SkinIncision;
            if (ready && _inPhase >= settleSeconds) { Enter(Phase.Aproximando); }
        }

        private void TickApproach()
        {
            TakeHold();

            // Sobe da bandeja e desce na linha por um arco: em linha reta a lâmina atravessaria a
            // borda da mesa e o campo cirúrgico no meio do caminho.
            float t = Mathf.Clamp01(_inPhase / approachSeconds);
            float eased = t * t * (3f - 2f * t);

            Pose target = StrokePose(1f, -0.015f);
            Vector3 mid = Vector3.Lerp(_restPose.position, target.position, 0.5f) + Vector3.up * 0.18f;
            Vector3 position = Quadratic(_restPose.position, mid, target.position, eased);

            Place(position, Quaternion.Slerp(_restPose.rotation, target.rotation, eased));

            if (t >= 1f) { Enter(Phase.Cortando); }
        }

        private void TickStroke(float deltaTime)
        {
            TakeHold();

            float length = Mathf.Max(0.01f, patch.IncisionLength);
            _along = Mathf.Clamp01(_along - strokeSpeed * deltaTime / length);

            Pose pose = StrokePose(_along, cutDepth);
            Place(pose.position, pose.rotation);

            if (_along <= 0f) { Enter(Phase.Afastando); }
        }

        private void TickLift()
        {
            TakeHold();

            float t = Mathf.Clamp01(_inPhase / liftSeconds);
            float eased = t * t * (3f - 2f * t);

            Pose low = StrokePose(0f, cutDepth);
            Pose high = StrokePose(0.5f, -liftHeight);
            Place(Vector3.Lerp(low.position, high.position, eased),
                  Quaternion.Slerp(low.rotation, high.rotation, eased));

            if (t < 1f) { return; }

            LetGo();
            Enter(Phase.Pronto);
            Finished?.Invoke();
        }

        /// <summary>
        /// Onde a ponta da lâmina tem de estar e como tem de estar virada, num ponto da linha.
        /// <paramref name="depth"/> em metros abaixo da pele; negativo é acima dela.
        ///
        /// A altura sai de <see cref="ChestSkinPatch.SurfaceHeightUnder"/> e não do
        /// <c>lift</c> de <see cref="ChestSkinPatch.IncisionPoint"/>, porque é essa a conta que o
        /// worker faz para medir profundidade. Pedir a profundidade na moeda de quem julga evita
        /// um piloto que acredita estar a 8 mm e é reprovado por estar a 20.
        /// </summary>
        private Pose StrokePose(float along, float depth)
        {
            Vector3 up = patch.transform.up;
            float lateral = lateralDrift * Mathf.Sin(Mathf.Clamp01(along) * Mathf.PI);

            Vector3 here = OnLine(along, lateral, depth);

            // O sentido da passada é o da cabeça para os pés, ou seja, o de `along` diminuindo.
            Vector3 ahead = OnLine(Mathf.Clamp01(along - 0.05f), lateral, depth);
            Vector3 travel = Vector3.ProjectOnPlane(ahead - here, up);
            if (travel.sqrMagnitude < 1e-10f)
            {
                travel = Vector3.ProjectOnPlane(here - OnLine(Mathf.Clamp01(along + 0.05f), lateral, depth), up);
            }

            travel = travel.sqrMagnitude > 1e-10f ? travel.normalized : patch.transform.forward;

            // A lâmina inclinada para dentro da pele, girando em torno do eixo que é a própria
            // face dela: assim a face continua horizontal e perpendicular à passada, que são as
            // duas condições que o worker cobra em EdgeAligned.
            Vector3 faceAxis = Vector3.Cross(up, travel);
            Vector3 forward = faceAxis.sqrMagnitude > 1e-10f
                ? Quaternion.AngleAxis(bladePitch, faceAxis.normalized) * travel
                : travel;

            return new Pose(here, Quaternion.LookRotation(forward, up));
        }

        /// <summary>Um ponto na linha da incisão, à profundidade pedida sob a pele.</summary>
        private Vector3 OnLine(float along, float lateral, float depth)
        {
            Vector3 onSkin = patch.IncisionPoint(along, lateral, 0f);
            return new Vector3(onSkin.x, patch.SurfaceHeightUnder(onSkin) - depth, onSkin.z);
        }

        /// <summary>Põe o bisturi de modo que a ponta caia na pose pedida.</summary>
        private void Place(Vector3 tipPosition, Quaternion tipRotation)
        {
            Quaternion rotation = tipRotation * Quaternion.Inverse(_tipLocalRotation);
            scalpel.SetPositionAndRotation(tipPosition - rotation * _tipLocalPosition, rotation);
        }

        /// <summary>
        /// Pega o bisturi. O worker só conta o corte com a ferramenta na mão, então o piloto
        /// precisa mesmo segurá-la, e não fingir. Sem interactor: não há mão nenhuma aqui, e
        /// <see cref="SurgicalInteractable.OnGrabbed"/> aceita nulo justamente para isso.
        /// </summary>
        private void TakeHold()
        {
            if (_grabbed) { return; }
            _grabbed = true;

            if (_body != null) { _body.isKinematic = true; }

            // Quem devolve a ferramenta à bandeja fica de fora: ela nunca foi solta, mas um
            // componente que mexe na mesma transform que o piloto é uma briga esperando acontecer.
            if (_homing != null) { _homing.enabled = false; }

            if (held != null && !held.IsHeld) { held.OnGrabbed(null); }
        }

        private void LetGo()
        {
            if (!_grabbed) { return; }
            _grabbed = false;

            if (held != null && held.IsHeld) { held.OnReleased(); }
            if (_homing != null) { _homing.enabled = true; }
        }

        private void Enter(Phase phase)
        {
            _phase = phase;
            _inPhase = 0f;
        }

        private static Vector3 Quadratic(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * b + t * t * c;
        }

        /// <summary>Volta ao começo, para rever a mesma passada sem reconstruir a cena.</summary>
        public void Restart()
        {
            LetGo();
            _along = 1f;
            // The first pass advances the procedure out of SkinIncision. Resetting only the
            // mesh leaves the pilot waiting forever for a stage that can no longer return.
            if (procedure != null)
            {
                procedure.ResetProcedure();
                if (beginProcedure) { procedure.Begin(); }
            }
            if (incision != null) { incision.ResetIncision(); }
            if (patch != null) { patch.ResetClosed(); }
            if (scalpel != null && _bound) { scalpel.SetPositionAndRotation(_restPose.position, _restPose.rotation); }
            Enter(Phase.Esperando);
        }

        public void Bind(Transform tool, Transform tip, SurgicalInteractable interactable,
            SkinIncisionWorker worker, ChestSkinPatch skin, TransplantProcedure transplant)
        {
            scalpel = tool;
            bladeTip = tip;
            held = interactable;
            incision = worker;
            patch = skin;
            procedure = transplant;
            _bound = false;
            CaptureRig();
        }

        /// <summary>Quanto a passada sai da linha média, em metros. Para demonstrar uma incisão torta.</summary>
        public void SetDrift(float metres) => lateralDrift = metres;

        /// <summary>
        /// Manda o piloto avançar o worker junto. Para conduzir o corte de fora do Play — a
        /// demonstração fotografada e os testes —, onde ninguém mais chama o Update dele.
        /// </summary>
        public void DriveWorker(bool also) => stepsTheWorker = also;

        private void OnGUI()
        {
            if (!showOverlay || incision == null) { return; }

            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(14, Screen.height / 42),
                normal = { textColor = Color.white },
            };

            string text =
                $"PILOTO: {_phase}\n" +
                $"linha cortada: {incision.Progress01 * 100f:F0}%   passadas: {incision.Strokes}   arranhões: {incision.Scratches}\n" +
                $"lâmina no fio: {(incision.BladeOnEdge ? "sim" : "não")}   profundidade: {incision.CutDepth01 * 100f:F0}% do limite" +
                (incision.EdgeRuleRelaxed ? "\nregra do fio afrouxada (ajuda adaptativa)" : string.Empty) +
                (HasGrade ? $"\n\n{Grade.Title} — {Grade.Score}/100 ({Grade.Stars}★)\n{Grade.Detail}" : string.Empty);

            Rect box = new Rect(24f, 24f, Screen.width - 48f, 220f);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(16f, 16f, Mathf.Min(760f, Screen.width - 32f), 200f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(box, text, style);
        }
    }
}
