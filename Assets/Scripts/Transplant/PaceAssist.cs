using System;
using System.Collections.Generic;
using UnityEngine;
using VRSurgery.Session;
using VRSurgery.Surgery;

namespace VRSurgery.Transplant
{
    /// <summary>
    /// The adaptive help the project promises instead of difficulty levels: a visitor falling
    /// behind gets help, a visitor keeping up gets none.
    ///
    /// Each stage has a time budget (scaled so the whole operation fits in most of the round).
    /// How far the visitor is behind is measured against it continuously. Past the first
    /// threshold the hand-held steps — the pump and the five vessels, the long ones — count
    /// faster; past the second, faster still. The visitor still performs every step: the help
    /// is that holding works sooner, the way an assistant's hands make a surgeon quicker. It is
    /// announced, so nobody wonders why it got easier, and every level given is written to the
    /// research log, so the data can tell assisted turns apart.
    ///
    /// Help only goes up within a turn and is taken away for the next visitor.
    /// </summary>
    public class PaceAssist : MonoBehaviour
    {
        [Serializable]
        public struct StageBudget
        {
            public TransplantStage Stage;
            public float Seconds;

            public StageBudget(TransplantStage stage, float seconds)
            {
                Stage = stage;
                Seconds = seconds;
            }
        }

        [Tooltip("Expected seconds per stage, before scaling to the round.")]
        [SerializeField] private List<StageBudget> budget = new List<StageBudget>
        {
            new StageBudget(TransplantStage.SkinIncision, 20f),
            new StageBudget(TransplantStage.OpenChest, 25f),
            new StageBudget(TransplantStage.OpenPericardium, 12f),
            new StageBudget(TransplantStage.GoOnBypass, 15f),
            new StageBudget(TransplantStage.RemoveNativeHeart, 12f),
            new StageBudget(TransplantStage.PlaceDonorHeart, 12f),
            new StageBudget(TransplantStage.ConnectVessels, 45f),
            new StageBudget(TransplantStage.Restart, 35f),
            new StageBudget(TransplantStage.CloseSkin, 30f),
        };

        [Tooltip("Fraction of the round the budget is scaled to fill, leaving the rest as slack.")]
        [SerializeField, Range(0.5f, 1f)] private float budgetFraction = 0.85f;

        [SerializeField, Min(1f)] private float helpAfterSeconds = 15f;
        [SerializeField, Min(1f)] private float moreHelpAfterSeconds = 35f;
        [SerializeField, Min(1f)] private float helpSpeed = 1.6f;
        [SerializeField, Min(1f)] private float moreHelpSpeed = 2.5f;

        [Header("Sources")]
        [SerializeField] private EventSessionController session;
        [SerializeField] private TransplantProcedure procedure;
        [SerializeField] private BypassWorker bypass;
        [SerializeField] private VesselAnastomosis[] vessels = new VesselAnastomosis[0];
        [SerializeField] private ResearchLog log;

        private float _spent;
        private float _stageEnteredAt;
        private float _elapsed;
        private TransplantStage _stage = TransplantStage.Idle;
        private bool _subscribed;

        /// <summary>0 no help, 1 some, 2 more.</summary>
        public int Level { get; private set; }

        /// <summary>Seconds behind the budget right now; negative is ahead.</summary>
        public float LatenessSeconds { get; private set; }

        /// <summary>Raised when help goes up, with the new level.</summary>
        public event Action<int> Helped;

        private void OnEnable() => Subscribe();

        private void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            if (_subscribed) { return; }
            _subscribed = true;
            if (procedure != null) { procedure.StageChanged += HandleStage; }
            if (session != null) { session.RoundStarted += ResetAssist; }
        }

        private void Unsubscribe()
        {
            if (!_subscribed) { return; }
            _subscribed = false;
            if (procedure != null) { procedure.StageChanged -= HandleStage; }
            if (session != null) { session.RoundStarted -= ResetAssist; }
        }

        private void Update()
        {
            if (session == null || !session.IsRunning) { return; }
            Tick(session.ElapsedSeconds);
        }

        /// <summary>Re-evaluates how far behind the visitor is. Public so tests can drive the clock.</summary>
        public void Tick(float elapsed)
        {
            _elapsed = elapsed;
            if (procedure == null) { return; }

            float inStage = Mathf.Max(0f, elapsed - _stageEnteredAt);
            float allowed = _spent + Mathf.Min(inStage, Budget(procedure.Stage));
            LatenessSeconds = elapsed - allowed;

            int wanted = LatenessSeconds > moreHelpAfterSeconds ? 2 : LatenessSeconds > helpAfterSeconds ? 1 : 0;
            if (wanted <= Level) { return; }

            Level = wanted;
            Apply(Level == 2 ? moreHelpSpeed : helpSpeed);
            if (log != null) { log.NoteAssist(); }

            SurgeryEvents.RaiseError(ErrorSeverity.Warning, Level == 2
                ? "A equipe está com você: segurar agora vale bem mais rápido."
                : "A equipe vai ajudar: os gestos de segurar ficaram mais rápidos.");
            Helped?.Invoke(Level);
        }

        private void HandleStage(TransplantStage stage)
        {
            if (stage == TransplantStage.Idle)
            {
                ResetAssist();
                return;
            }

            // The stage just left is paid for at its budget, however long it actually took:
            // lateness carries over through inStage, not by inflating what was spent.
            if (_stage != TransplantStage.Idle && _stage != stage) { _spent += Budget(_stage); }
            _stage = stage;
            _stageEnteredAt = _elapsed;
        }

        /// <summary>Budget of a stage in round seconds, scaled so the whole operation fits.</summary>
        public float Budget(TransplantStage stage)
        {
            float total = 0f, own = 0f;
            for (int i = 0; i < budget.Count; i++)
            {
                if (procedure != null && !Contains(procedure.Order, budget[i].Stage)) { continue; }
                total += budget[i].Seconds;
                if (budget[i].Stage == stage) { own = budget[i].Seconds; }
            }

            if (total <= 0f) { return 0f; }
            float round = session != null ? session.RoundSeconds : total / budgetFraction;
            return own * round * budgetFraction / total;
        }

        private static bool Contains(IReadOnlyList<TransplantStage> order, TransplantStage stage)
        {
            for (int i = 0; i < order.Count; i++) { if (order[i] == stage) { return true; } }
            return false;
        }

        private void Apply(float speed)
        {
            if (bypass != null) { bypass.SpeedMultiplier = speed; }
            for (int i = 0; i < vessels.Length; i++)
            {
                if (vessels[i] != null) { vessels[i].SpeedMultiplier = speed; }
            }
        }

        /// <summary>A new visitor: no help, a fresh budget.</summary>
        public void ResetAssist()
        {
            Level = 0;
            LatenessSeconds = 0f;
            _spent = 0f;
            _elapsed = 0f;
            _stageEnteredAt = 0f;
            _stage = procedure != null ? procedure.Stage : TransplantStage.Idle;
            Apply(1f);
        }

        public void Bind(EventSessionController controller, TransplantProcedure transplant, BypassWorker pump,
            VesselAnastomosis[] joins, ResearchLog researchLog)
        {
            bool live = Application.isPlaying && isActiveAndEnabled;
            if (live) { Unsubscribe(); }

            session = controller;
            procedure = transplant;
            bypass = pump;
            vessels = joins ?? new VesselAnastomosis[0];
            log = researchLog;

            if (live) { Subscribe(); }
        }
    }
}
