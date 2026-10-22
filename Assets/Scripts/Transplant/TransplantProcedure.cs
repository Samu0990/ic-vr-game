using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRSurgery.Transplant
{
    /// <summary>
    /// The stages of a heart transplant, in the order a transplant actually happens.
    ///
    /// A real transplant runs four to six hours; a stand gives each visitor ninety seconds. So
    /// this is the procedure's arc rather than its full length — the five beats an audience can
    /// follow and a first-timer can perform, each one a real step of the operation and not a
    /// mini-game bolted on.
    /// </summary>
    public enum TransplantStage
    {
        /// <summary>Nothing started. The chest is closed.</summary>
        Idle,

        /// <summary>Sternotomy: the chest has to be opened before anything else is possible.</summary>
        OpenChest,

        /// <summary>
        /// Onto cardiopulmonary bypass: cannulate, cross-clamp, cardioplegia. Without this the
        /// patient has no circulation at all from the moment the heart comes out until the new
        /// one starts — the pump is what keeps them alive across that gap.
        /// </summary>
        GoOnBypass,

        /// <summary>Cardiectomy: the failing heart comes out.</summary>
        RemoveNativeHeart,

        /// <summary>The donor heart is placed in the empty pericardium.</summary>
        PlaceDonorHeart,

        /// <summary>The connections that make it a transplant rather than a placement.</summary>
        ConnectVessels,

        /// <summary>
        /// Off bypass: unclamp, de-air, wean. The new heart takes the circulation back, and only
        /// at the end of this does it beat.
        /// </summary>
        Restart,

        /// <summary>Every stage cleared.</summary>
        Complete,

        /// <summary>
        /// Skin incision with the scalpel along the sternal midline. Only part of the operation when
        /// the scene enables the skin stages; appended after Complete so existing values keep
        /// their numbers.
        /// </summary>
        SkinIncision,

        /// <summary>
        /// Closing: sternum back, retractor out, skin sutured. Only part of the operation when the
        /// scene enables the skin stages.
        /// </summary>
        CloseSkin,

        /// <summary>
        /// Pericardiotomy: the sac around the heart opened, between the sternotomy and the pump.
        /// Only part of the operation when the scene enables it; appended so existing values keep
        /// their numbers.
        /// </summary>
        OpenPericardium,
    }

    /// <summary>
    /// Owns the order of the operation and nothing else.
    ///
    /// Stages advance strictly in sequence, because the sequence is the teaching: you cannot lift
    /// a heart out of a closed chest, and a heart that is in place but unconnected is not a
    /// transplant. Each stage reports its own completion; this only decides what comes next and
    /// when the whole thing is done.
    ///
    /// It deliberately owns no clock. The booth's session owns that, the same way it does for
    /// every other procedure in this project, so the scoreboard and the projection keep reading
    /// one source of truth.
    /// </summary>
    public class TransplantProcedure : MonoBehaviour
    {
        [Tooltip("How many vessel connections the implant stage requires. Five is the real " +
                 "count: left atrium, inferior and superior vena cava, aorta and pulmonary artery.")]
        [SerializeField, Range(2, 5)] private int vesselCount = 5;

        [Tooltip("Adds the scalpel incision before the sternotomy and the skin suture after the " +
                 "heart restarts. Off keeps the original six-stage operation, which is what every " +
                 "test that builds a procedure by hand expects.")]
        [SerializeField] private bool includeSkinStages;

        [Tooltip("Adds opening the pericardium, with the cautery pen, after the sternotomy.")]
        [SerializeField] private bool includePericardium;

        [Tooltip("The level this scene was built at. It decides which bypass steps the team has " +
                 "already done before the visitor arrives, and how long the round lasts.")]
        //
        // The level has to be stored, not just the rules derived from it. BypassPlan is a plain
        // C# object on purpose — it has no Unity in it — which also means Unity does not write it
        // to the scene. Without this field the saved scene came back as Difícil whatever it was
        // built as, and a Médio scene then expected a cannulation the player's single gesture no
        // longer performed.
        [SerializeField] private SurgicalDifficulty difficulty = SurgicalDifficulty.Dificil;

        private static readonly TransplantStage[] CoreOrder =
        {
            TransplantStage.OpenChest,
            TransplantStage.GoOnBypass,
            TransplantStage.RemoveNativeHeart,
            TransplantStage.PlaceDonorHeart,
            TransplantStage.ConnectVessels,
            TransplantStage.Restart,
        };

        private readonly List<TransplantStage> _order = new List<TransplantStage>(CoreOrder);

        /// <summary>True when the operation opens with the scalpel and closes with the needle.</summary>
        public bool IncludesSkinStages => includeSkinStages;

        /// <summary>True when the pericardium has to be opened before the pump.</summary>
        public bool IncludesPericardium => includePericardium;

        /// <summary>Switches the pericardium stage on or off. Takes effect immediately.</summary>
        public void SetPericardiumStage(bool enabled)
        {
            includePericardium = enabled;
            RebuildOrder();
        }

        /// <summary>The stages this operation runs, in order.</summary>
        public IReadOnlyList<TransplantStage> Order => _order;

        /// <summary>Switches the incision and suture stages on or off. Takes effect immediately.</summary>
        public void SetSkinStages(bool enabled)
        {
            includeSkinStages = enabled;
            RebuildOrder();
        }

        private void RebuildOrder()
        {
            _order.Clear();
            if (includeSkinStages) { _order.Add(TransplantStage.SkinIncision); }
            _order.AddRange(CoreOrder);
            if (includePericardium)
            {
                _order.Insert(_order.IndexOf(TransplantStage.OpenChest) + 1, TransplantStage.OpenPericardium);
            }

            if (includeSkinStages) { _order.Add(TransplantStage.CloseSkin); }
        }

        private void Awake()
        {
            // Rebuilt from the stored level, because the plan itself does not survive the scene.
            Plan = BypassPlan.For(difficulty);
            RebuildOrder();
        }

        public TransplantStage Stage { get; private set; } = TransplantStage.Idle;

        /// <summary>
        /// The pump. Owned here because its two halves bracket the operation, and exposed so the
        /// worker that knows where the surgeon's hands are can drive it without this class
        /// learning anything about hands.
        /// </summary>
        public BypassProcedure Bypass { get; } = new BypassProcedure();

        /// <summary>
        /// Which level is being run. Decides how much of the pump the visitor works themselves and
        /// how long the round lasts; the clinical rules are the same at all three.
        /// </summary>
        public BypassPlan Plan { get; private set; } = BypassPlan.For(SurgicalDifficulty.Dificil);

        /// <summary>Round length for this level. The session reads it instead of a constant.</summary>
        public float RoundSeconds => Plan.RoundSeconds;

        /// <summary>What the room says about what the team did before the visitor arrived.</summary>
        public string Briefing => Plan.Briefing;

        /// <summary>Vessels connected so far. Only meaningful during ConnectVessels.</summary>
        public int VesselsConnected { get; private set; }

        public int VesselCount => vesselCount;

        public bool IsComplete => Stage == TransplantStage.Complete;

        /// <summary>
        /// True once every vessel is sewn. The pump asks this before it will let the cross-clamp
        /// off, because unclamping onto open anastomoses empties the patient into the chest.
        /// </summary>
        public bool IsImplantComplete => VesselsConnected >= vesselCount;

        /// <summary>0..1 across the whole operation, for the audience's progress readout.</summary>
        public float Progress01
        {
            get
            {
                if (Stage == TransplantStage.Idle) { return 0f; }
                if (Stage == TransplantStage.Complete) { return 1f; }

                int index = _order.IndexOf(Stage);
                float within = Stage == TransplantStage.ConnectVessels && vesselCount > 0
                    ? VesselsConnected / (float)vesselCount
                    : 0f;

                return Mathf.Clamp01((index + within) / _order.Count);
            }
        }

        private string _urgent = string.Empty;

        /// <summary>
        /// Something the patient needs right now that the stage's own instruction does not say
        /// — a heart in fibrillation. Replaces the instruction until cleared with null.
        /// </summary>
        public void SetUrgentInstruction(string text) => _urgent = text ?? string.Empty;

        /// <summary>How long a notice stays up before the stage's own instruction comes back.</summary>
        public const float NoticeSeconds = 4f;

        /// <summary>What the room says on Fácil when the visitor reaches the pump the team already ran.</summary>
        public const string TeamOnBypassNotice = "A equipe já colocou o paciente em bomba";

        private string _notice = string.Empty;
        private float _noticeLeft;

        /// <summary>
        /// A passing message that stands in for the instruction for a few seconds: a refusal, or
        /// what the team did. It goes by itself, and at the next stage at the latest, because an
        /// instruction that outlives its moment sits over the one the visitor needs next — on the
        /// monitor and on the card alike. Fibrillation is not a notice: it stays until a shock
        /// clears it (SetUrgentInstruction).
        /// </summary>
        public void ShowNotice(string text, float seconds = NoticeSeconds)
        {
            _notice = text ?? string.Empty;
            _noticeLeft = string.IsNullOrEmpty(_notice) ? 0f : Mathf.Max(0.1f, seconds);
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Ages the notice. Stepped by hand in tests.</summary>
        public void Tick(float deltaTime)
        {
            if (_noticeLeft <= 0f) { return; }

            _noticeLeft -= Mathf.Max(0f, deltaTime);
            if (_noticeLeft <= 0f) { _notice = string.Empty; }
        }

        public event Action<TransplantStage> StageChanged;
        public event Action<int, int> VesselConnected;
        public event Action ProcedureCompleted;

        /// <summary>
        /// A stage finished, and whether the team finished it before the visitor got there (the
        /// pump on Fácil) rather than the visitor's hands. Raised before the next stage begins,
        /// so the last stage is counted before the operation is declared complete.
        /// </summary>
        public event Action<TransplantStage, bool> StageCompleted;

        public void Begin()
        {
            // Again here, not only in Awake: another component's Awake may have read Plan before
            // ours ran, and a round that starts on the wrong plan refuses a step the player can
            // actually perform.
            Plan = BypassPlan.For(difficulty);
            RebuildOrder();
            _urgent = string.Empty;
            ShowNotice(null);
            VesselsConnected = 0;
            Bypass.Reset();

            // Whatever the team did runs through the pump's own rules, in order. A level that
            // asked for an impossible starting point would be caught here rather than producing
            // a patient the procedure could never have produced.
            BypassAttempt prepared = Bypass.ApplyTeamPreparation(Plan.DoneByTeam);
            if (!prepared.Accepted)
            {
                Debug.LogError($"[Transplante] {prepared.Reason}");
            }

            // On a level where the team already went on bypass, the GoOnBypass stage passes its
            // own check the moment it is reached, because Bypass.IsArrested is already true. It is
            // satisfied by asking the pump, never skipped (see MoveOnFrom).
            SetStage(_order[0]);
        }

        /// <summary>Sets the level. Takes effect at the next Begin.</summary>
        public void SetDifficulty(SurgicalDifficulty level)
        {
            difficulty = level;
            Plan = BypassPlan.For(level);
        }

        public void ResetProcedure()
        {
            _urgent = string.Empty;
            ShowNotice(null);
            VesselsConnected = 0;
            Bypass.Reset();
            SetStage(TransplantStage.Idle);
        }

        /// <summary>
        /// Reports a stage finished. Ignored unless it is the stage actually in progress, so an
        /// instrument that fires twice, or fires for a step the visitor has not reached, cannot
        /// skip the operation forward.
        /// </summary>
        public bool CompleteStage(TransplantStage stage)
        {
            if (Stage != stage || Stage == TransplantStage.Complete)
            {
                return false;
            }

            int index = _order.IndexOf(stage);
            if (index < 0)
            {
                return false;
            }

            // The two stages the pump brackets cannot be declared finished by whatever performed
            // them; the pump decides. Cutting a heart out of a patient who is not arrested, or
            // calling the operation done while still on bypass, are the two ways this procedure
            // could quietly kill someone.
            if (stage == TransplantStage.GoOnBypass && !Bypass.IsArrested)
            {
                return false;
            }

            if (stage == TransplantStage.Restart && !Bypass.IsOff)
            {
                return false;
            }

            StageCompleted?.Invoke(stage, false);
            MoveOnFrom(index);
            return true;
        }

        /// <summary>
        /// Goes to the stage after the one at <paramref name="index"/>, completing on arrival any
        /// the team has already done. On Fácil the patient comes in on the pump, and the only
        /// gesture the visitor has there is coming OFF it — refused before the implant, rightly —
        /// so nothing could ever finish GoOnBypass and the operation stopped after the
        /// pericardium. It is finished by asking the pump whether the patient is arrested, the
        /// same check CompleteStage makes, and the room says why it went by.
        /// </summary>
        private void MoveOnFrom(int index)
        {
            string notice = null;
            TransplantStage next = After(index);

            while (next == TransplantStage.GoOnBypass && Bypass.IsArrested)
            {
                StageCompleted?.Invoke(next, true);
                notice = TeamOnBypassNotice;
                next = After(_order.IndexOf(next));
            }

            SetStage(next, notice);
        }

        private TransplantStage After(int index) =>
            index + 1 < _order.Count ? _order[index + 1] : TransplantStage.Complete;

        /// <summary>One vessel anastomosed. Advances the stage once all of them are done.</summary>
        public bool ConnectVessel()
        {
            if (Stage != TransplantStage.ConnectVessels || VesselsConnected >= vesselCount)
            {
                return false;
            }

            VesselsConnected++;
            VesselConnected?.Invoke(VesselsConnected, vesselCount);

            if (VesselsConnected >= vesselCount)
            {
                CompleteStage(TransplantStage.ConnectVessels);
            }

            return true;
        }

        /// <summary>What the room should be telling the visitor to do right now, in pt-BR.</summary>
        public string CurrentInstruction =>
            !string.IsNullOrEmpty(_urgent) ? _urgent :
            !string.IsNullOrEmpty(_notice) ? _notice :
            StageInstruction;

        /// <summary>The stage's own instruction, without urgencies or passing notices.</summary>
        public string StageInstruction => Stage switch
        {
            TransplantStage.SkinIncision => "Pegue o bisturi e corte a pele sobre a linha roxa",
            // The scene that adds the scalpel and the needle also puts a sternal saw on the tray.
            TransplantStage.OpenChest => includeSkinStages
                ? "Cauterize os pontos que sangram e abra o esterno com a serra na marca dourada"
                : "Abra o esterno: pressione a mão sobre a marca dourada",
            TransplantStage.OpenPericardium => "Abra o pericárdio: bisturi elétrico sobre a linha tracejada",
            TransplantStage.GoOnBypass => Bypass.CurrentInstruction,
            TransplantStage.RemoveNativeHeart => "Retire o coração doente",
            TransplantStage.PlaceDonorHeart => "Posicione o coração do doador",
            TransplantStage.ConnectVessels => $"Conecte os vasos — {VesselsConnected}/{vesselCount}",
            TransplantStage.Restart => Bypass.CurrentInstruction,
            TransplantStage.CloseSkin => "Coração batendo! Feche a pele: porta-agulha nos pontos azuis",
            TransplantStage.Complete => "Coração batendo",
            _ => "Aguardando",
        };

        private void SetStage(TransplantStage next, string notice = null)
        {
            if (next == Stage)
            {
                return;
            }

            Stage = next;

            // A notice belongs to the stage it was raised in; the new one starts clean, or with
            // what the team did on the way to it.
            ShowNotice(notice);
            StageChanged?.Invoke(Stage);

            if (Stage == TransplantStage.Complete)
            {
                ProcedureCompleted?.Invoke();
            }
        }
    }
}
