using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VRSurgery.Interaction;
using VRSurgery.Session;
using VRSurgery.Surgery;

namespace VRSurgery.Transplant
{
    /// <summary>One change to the clock: how many seconds, why, and where and when it happened.</summary>
    public readonly struct TimeAdjustment
    {
        public readonly float Seconds;
        public readonly string Reason;
        public readonly TransplantStage Stage;
        public readonly SurgicalDifficulty Level;
        public readonly Vector3 Where;
        public readonly DateTime At;

        public TimeAdjustment(float seconds, string reason, TransplantStage stage, SurgicalDifficulty level,
            Vector3 where, DateTime at)
        {
            Seconds = seconds;
            Reason = reason;
            Stage = stage;
            Level = level;
            Where = where;
            At = at;
        }

        public bool Gained => Seconds > 0f;

        /// <summary>"+10 s · Esterno aberto" / "−5 s · Fora da linha".</summary>
        public string Label => $"{(Gained ? "+" : "−")}{Mathf.Abs(Seconds):0} s · {Reason}";
    }

    /// <summary>
    /// The clock as the score: seconds won for every stage the visitor finishes, seconds lost for
    /// every mistake that is theirs to make.
    ///
    /// What counts as a mistake is narrow on purpose. A cut off the line or too deep, a join left
    /// leaking by a shaking hand, a pump step out of turn, reaching for the heart before its
    /// time, a heart let fall or lost: the visitor's hands did those. A heart that fibrillates
    /// when the clamp comes off, a shock that does not take, the team's offers of help, the hint
    /// to turn the blade, an instrument dropped on the drape — those are physiology, or help, or
    /// clumsiness with no consequence for the patient, and punishing them would teach the wrong
    /// thing. The same mistake counts once in a while, not once a frame; nothing counts before
    /// the clock runs; the clock never goes below zero, and reaching zero ends the turn.
    ///
    /// The turn has to fit the stand: the time a turn starts with plus every second it could win
    /// stays within the limit, so a visitor who never slows down still gives up the headset in
    /// about four minutes.
    /// </summary>
    public class TimeScoring : MonoBehaviour
    {
        [SerializeField] private EventSessionController session;
        [SerializeField] private TransplantProcedure procedure;

        [Header("What is scored")]
        [SerializeField] private SkinIncisionWorker incision;
        [SerializeField] private VesselAnastomosis[] vessels = new VesselAnastomosis[0];
        [SerializeField] private BypassWorker bypass;
        [SerializeField] private DefibrillationWorker defibrillation;
        [SerializeField] private GrabbableOrgan[] organs = new GrabbableOrgan[0];

        [Tooltip("Where the work is happening: the surgeon's working hand. The pop-ups appear there.")]
        [SerializeField] private Transform workPoint;

        [SerializeField] private SurgeryTelemetry telemetry;

        [Header("Rules")]
        [SerializeField, Min(0f)] private float bonusSeconds = 10f;
        [SerializeField, Min(0f)] private float penaltySeconds = 5f;

        [Tooltip("The same mistake counts at most once in this many seconds.")]
        [SerializeField, Min(0f)] private float repeatGapSeconds = 3f;

        [Tooltip("The longest a turn may last: its starting time plus every second it could win.")]
        [SerializeField, Min(30f)] private float maxTurnSeconds = 240f;

        [Tooltip("Also append every adjustment to a CSV on the stand's PC, for the research.")]
        [SerializeField] private bool writeCsv = true;

        public const string CsvFileName = "ajustes_de_tempo.csv";

        private readonly Dictionary<string, float> _lastCounted = new Dictionary<string, float>();
        private readonly List<(ReturnHomeOnRelease recovery, Action lost)> _recoveries =
            new List<(ReturnHomeOnRelease, Action)>();
        private float _clock;
        private bool _subscribed;

        public event Action<TimeAdjustment> Adjusted;

        public float BonusSeconds => bonusSeconds;
        public float PenaltySeconds => penaltySeconds;
        public float MaxTurnSeconds => maxTurnSeconds;

        /// <summary>Stages that win seconds this round: all of them, less those the team did.</summary>
        public static int BonusStages(IReadOnlyList<TransplantStage> order, BypassPlan plan)
        {
            int count = order != null ? order.Count : 0;
            if (plan == null || order == null || !Contains(order, TransplantStage.GoOnBypass)) { return count; }

            // Fácil hands the visitor a patient already on the pump: that stage completes by
            // itself on arrival and earns nothing.
            BypassProcedure team = new BypassProcedure();
            team.ApplyTeamPreparation(plan.DoneByTeam);
            return team.IsArrested ? count - 1 : count;
        }

        /// <summary>The time a turn starts with, so that start plus every bonus fits in the turn's limit.</summary>
        public static float InitialSeconds(float maxTurn, float bonus, int bonusStages) =>
            Mathf.Max(30f, maxTurn - bonus * bonusStages);

        private static bool Contains(IReadOnlyList<TransplantStage> order, TransplantStage stage)
        {
            for (int i = 0; i < order.Count; i++) { if (order[i] == stage) { return true; } }
            return false;
        }

        private void OnEnable() => Subscribe();

        private void OnDisable() => Unsubscribe();

        private void Start() => ApplyRoundLength();

        /// <summary>Gives the session this level's starting time and the turn's limit.</summary>
        public void ApplyRoundLength()
        {
            if (session == null || procedure == null) { return; }
            int stages = BonusStages(procedure.Order, procedure.Plan);
            float initial = InitialSeconds(maxTurnSeconds, bonusSeconds, stages);
            session.SetRoundSeconds(initial, maxTurnSeconds);
            Debug.Log($"[Tempo] nível {procedure.Plan.Difficulty}: {initial:F0} s de início + {stages} etapa(s) × " +
                      $"{bonusSeconds:F0} s = até {initial + stages * bonusSeconds:F0} s por vez (limite {maxTurnSeconds:F0} s)");
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Advances the clock the repeat gap is measured on. Stepped by hand in tests.</summary>
        public void Tick(float deltaTime) => _clock += Mathf.Max(0f, deltaTime);

        private void Subscribe()
        {
            if (_subscribed) { return; }
            _subscribed = true;

            if (procedure != null) { procedure.StageCompleted += HandleStageCompleted; }
            if (session != null) { session.RoundStarted += ResetScoring; }

            if (incision != null)
            {
                incision.Deviated += HandleOffLine;
                incision.DeepCut += HandleTooDeep;
            }

            foreach (VesselAnastomosis vessel in vessels)
            {
                if (vessel != null) { vessel.BleedingStarted += HandleLeak; }
            }

            if (bypass != null) { bypass.StepRefused += HandlePumpRefused; }

            _recoveries.Clear();
            foreach (GrabbableOrgan organ in organs)
            {
                if (organ == null) { continue; }
                organ.GrabRefused += HandleGrabRefused;

                ReturnHomeOnRelease recovery = organ.GetComponent<ReturnHomeOnRelease>();
                if (recovery == null) { continue; }
                Action lost = HandleHeartLost;
                recovery.Lost += lost;
                _recoveries.Add((recovery, lost));
            }
        }

        private void Unsubscribe()
        {
            if (!_subscribed) { return; }
            _subscribed = false;

            if (procedure != null) { procedure.StageCompleted -= HandleStageCompleted; }
            if (session != null) { session.RoundStarted -= ResetScoring; }

            if (incision != null)
            {
                incision.Deviated -= HandleOffLine;
                incision.DeepCut -= HandleTooDeep;
            }

            foreach (VesselAnastomosis vessel in vessels)
            {
                if (vessel != null) { vessel.BleedingStarted -= HandleLeak; }
            }

            if (bypass != null) { bypass.StepRefused -= HandlePumpRefused; }

            foreach (GrabbableOrgan organ in organs)
            {
                if (organ != null) { organ.GrabRefused -= HandleGrabRefused; }
            }

            foreach ((ReturnHomeOnRelease recovery, Action lost) in _recoveries)
            {
                if (recovery != null) { recovery.Lost -= lost; }
            }

            _recoveries.Clear();
        }

        // ------------------------------------------------------------------ what wins and loses

        private void HandleStageCompleted(TransplantStage stage, bool byTeam)
        {
            // What the team did before the visitor arrived is not the visitor's to be paid for.
            if (byTeam) { return; }
            Apply(bonusSeconds, StageText.Done(stage), null);
        }

        private void HandleOffLine() => Apply(-penaltySeconds, "Fora da linha", "linha");

        private void HandleTooDeep() => Apply(-penaltySeconds, "Corte fundo demais", "fundo");

        private void HandleLeak(VesselAnastomosis vessel) => Apply(-penaltySeconds, "Vaso vazando", "vaso");

        /// <summary>
        /// A pump step out of turn — but only while the pump is the work. The pump's sites sit on
        /// the same vessels the joins are sewn to, so sewing brushes past them and the pump
        /// refuses; that is the hand passing by, not a wrong move. Nor is it a mistake while the
        /// new heart fibrillates: the pump holds back because of the heart, not the hand.
        /// </summary>
        private void HandlePumpRefused(string reason)
        {
            if (procedure == null) { return; }
            if (procedure.Stage != TransplantStage.GoOnBypass && procedure.Stage != TransplantStage.Restart) { return; }
            if (defibrillation != null && defibrillation.IsFibrillating) { return; }
            Apply(-penaltySeconds, "Passo da bomba fora de ordem", "bomba");
        }

        /// <summary>Reaching for the sick heart before its stage. Not the seated or the removed one: those are just in the way.</summary>
        private void HandleGrabRefused(GrabbableOrgan organ, string reason)
        {
            if (organ == null || organ.Role != OrganRole.Native || organ.Satisfied) { return; }
            Apply(-penaltySeconds, "Coração antes da hora", "coracao-cedo");
        }

        private void HandleHeartLost() => Apply(-penaltySeconds, "Coração caiu", "coracao-caiu");

        /// <summary>
        /// Changes the clock and tells everyone. Returns false when nothing counted: the clock is
        /// not running, or the same mistake counted moments ago.
        /// </summary>
        public bool Apply(float seconds, string reason, string repeatKey)
        {
            if (session == null || !session.IsRunning) { return false; }

            if (!string.IsNullOrEmpty(repeatKey))
            {
                if (_lastCounted.TryGetValue(repeatKey, out float last) && _clock - last < repeatGapSeconds) { return false; }
                _lastCounted[repeatKey] = _clock;
            }

            TransplantStage stage = procedure != null ? procedure.Stage : TransplantStage.Idle;
            SurgicalDifficulty level = procedure != null ? procedure.Plan.Difficulty : SurgicalDifficulty.Medio;
            Vector3 where = workPoint != null ? workPoint.position : transform.position;
            TimeAdjustment adjustment = new TimeAdjustment(seconds, reason, stage, level, where, DateTime.Now);

            session.AdjustTime(seconds);
            Record(adjustment);
            Adjusted?.Invoke(adjustment);
            return true;
        }

        private void Record(TimeAdjustment a)
        {
            string line = $"{a.At:HH:mm:ss} | {a.Label} | etapa {a.Stage} | nível {a.Level} | " +
                          $"restam {session.RemainingSeconds:F1} s";
            if (telemetry != null) { telemetry.Log("Ajuste de tempo: " + line); }
            else { Debug.Log("[Tempo] " + line); }

            if (!writeCsv || !Application.isPlaying) { return; }

            try
            {
                string path = Path.Combine(Application.persistentDataPath, CsvFileName);
                bool header = !File.Exists(path);
                using StreamWriter csv = new StreamWriter(path, true);
                if (header) { csv.WriteLine("data_hora;vez;nivel;etapa;segundos;motivo;restante"); }
                csv.WriteLine($"{a.At:yyyy-MM-dd HH:mm:ss};{session.SessionCount};{a.Level};{a.Stage};" +
                              $"{a.Seconds:+0;-0};{a.Reason};{session.RemainingSeconds:F1}");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Tempo] não deu para gravar o ajuste no CSV: {exception.Message}");
            }
        }

        /// <summary>Forgets the repeat gaps. A new visitor's first mistake always counts.</summary>
        public void ResetScoring()
        {
            _lastCounted.Clear();
        }

        public void Bind(EventSessionController controller, TransplantProcedure transplant, Transform hand,
            SurgeryTelemetry log)
        {
            bool live = Application.isPlaying && isActiveAndEnabled;
            if (live) { Unsubscribe(); }
            session = controller;
            procedure = transplant;
            workPoint = hand;
            telemetry = log;
            if (live) { Subscribe(); }
        }

        public void BindSources(SkinIncisionWorker skinIncision, VesselAnastomosis[] joins, BypassWorker pump,
            DefibrillationWorker defibrillator, GrabbableOrgan[] hearts)
        {
            bool live = Application.isPlaying && isActiveAndEnabled;
            if (live) { Unsubscribe(); }
            incision = skinIncision;
            vessels = joins ?? new VesselAnastomosis[0];
            bypass = pump;
            defibrillation = defibrillator;
            organs = hearts ?? new GrabbableOrgan[0];
            if (live) { Subscribe(); }
        }

        /// <summary>For tests and the stand's settings: the rules' numbers.</summary>
        public void Configure(float bonus, float penalty, float repeatGap, float maxTurn)
        {
            bonusSeconds = Mathf.Max(0f, bonus);
            penaltySeconds = Mathf.Max(0f, penalty);
            repeatGapSeconds = Mathf.Max(0f, repeatGap);
            maxTurnSeconds = Mathf.Max(30f, maxTurn);
        }
    }
}
