using System;
using UnityEngine;
using VRSurgery.Interaction;
using VRSurgery.Surgery;
using VRSurgery.Tools;

namespace VRSurgery.Session
{
    public enum SessionState
    {
        /// <summary>Nobody in the headset. The screen belongs to whatever draws a crowd.</summary>
        Attract,

        /// <summary>Headset is on and the briefing is up, but the clock has not started yet.</summary>
        Briefing,

        /// <summary>The clock is running and can be lost.</summary>
        Running,

        /// <summary>Bleeding controlled in time.</summary>
        Success,

        /// <summary>The clock ran out, or the operator ended the turn.</summary>
        Failure,

        /// <summary>Scores shown between visitors, inviting the next person.</summary>
        Scoreboard,
    }

    /// <summary>What one visitor's turn produced. Ranked on time, shown as points.</summary>
    public readonly struct SessionResult
    {
        public readonly bool Succeeded;

        /// <summary>Seconds the visitor took. This is the number the scoreboard ranks on.</summary>
        public readonly float ElapsedSeconds;

        public readonly float RemainingSeconds;
        public readonly int Score;

        public SessionResult(bool succeeded, float elapsedSeconds, float remainingSeconds, int score)
        {
            Succeeded = succeeded;
            ElapsedSeconds = elapsedSeconds;
            RemainingSeconds = remainingSeconds;
            Score = score;
        }

        /// <summary>The time taken, to the thousandth, the way the stand shows it: "3:12,457".</summary>
        public string ElapsedWithMillis => WithMillis(ElapsedSeconds);

        public static string WithMillis(float seconds)
        {
            long ms = (long)Math.Round(Math.Max(0f, seconds) * 1000.0);
            return $"{ms / 60000}:{ms / 1000 % 60:00},{ms % 1000:000}";
        }

        public override string ToString() =>
            Succeeded
                ? $"controlled in {ElapsedSeconds:F1}s — {Score} pts"
                : $"lost after {ElapsedSeconds:F1}s";
    }

    /// <summary>
    /// Runs one visitor's turn at the stand: attract, briefing, a countdown that can be lost, a
    /// result, a scoreboard, and back to attract for the next person in the queue.
    ///
    /// The procedure does not know it is being timed — success is simply the surgery finishing
    /// while the clock still has time on it. Everything else the stand shows (projection, HUD,
    /// urgency audio, scoreboard) reads this controller rather than tracking the flow itself.
    /// </summary>
    public class EventSessionController : MonoBehaviour
    {
        private const float DefaultRoundSeconds = 90f;
        private const float DefaultBriefingTimeout = 45f;
        private const float DefaultResultHold = 5f;
        private const float DefaultScoreboardHold = 8f;
        private const float DefaultPointsPerSecond = 100f;

        [SerializeField] private EventSessionDefinition definition;

        [Tooltip("Reset at the start of every session so each visitor gets an untouched patient.")]
        [SerializeField] private SurgeryResetController resetController;

        [Tooltip("Optional. Session transitions land in the same on-site log as the surgery events.")]
        [SerializeField] private SurgeryTelemetry telemetry;

        private float _stateElapsed;
        private float _roundOverride;
        private float _runSeconds;

        public SessionState State { get; private set; } = SessionState.Attract;

        /// <summary>Seconds left on the clock. Only moves while the round is running.</summary>
        public float RemainingSeconds { get; private set; }

        /// <summary>
        /// Seconds on the clock when a turn starts. The definition's, unless the turn's scoring set
        /// its own: with seconds won and lost along the way, the start is what keeps a whole turn
        /// within the stand's limit.
        /// </summary>
        public float RoundSeconds => _roundOverride > 0f ? _roundOverride
            : definition != null ? definition.RoundSeconds : DefaultRoundSeconds;

        /// <summary>
        /// Real seconds this turn has run. Not the clock's own count any more: the clock gains and
        /// loses seconds as the visitor works, the visitor's time does not.
        /// </summary>
        public float ElapsedSeconds => _runSeconds;

        /// <summary>Raised when seconds are added to or taken from a running clock, with the amount applied.</summary>
        public event Action<float> TimeAdjusted;

        /// <summary>
        /// 0 when the round starts, 1 when the clock runs out. The single number the HUD, the
        /// scene tint and the urgency audio should read, so they all rise together.
        /// </summary>
        public float Urgency01 =>
            RoundSeconds <= 0f ? 0f : Mathf.Clamp01(1f - RemainingSeconds / RoundSeconds);

        public bool IsRunning => State == SessionState.Running;

        /// <summary>What the visitor would score if the operation finished right now.</summary>
        public int PointsNow => Mathf.RoundToInt(RemainingSeconds * PointsPerSecondRemaining);

        public SessionResult LastResult { get; private set; }
        public bool HasResult { get; private set; }

        /// <summary>How many turns have been started since the stand opened.</summary>
        public int SessionCount { get; private set; }

        public string EducationalFact => definition != null ? definition.EducationalFact : string.Empty;

        public event Action<SessionState> StateChanged;
        public event Action RoundStarted;
        public event Action<SessionResult> RoundEnded;

        private float BriefingTimeoutSeconds =>
            definition != null ? definition.BriefingTimeoutSeconds : DefaultBriefingTimeout;

        private float ResultHoldSeconds =>
            definition != null ? definition.ResultHoldSeconds : DefaultResultHold;

        private float ScoreboardHoldSeconds =>
            definition != null ? definition.ScoreboardHoldSeconds : DefaultScoreboardHold;

        private bool StartOnFirstToolGrab =>
            definition == null || definition.StartOnFirstToolGrab;

        private float PointsPerSecondRemaining =>
            definition != null ? definition.PointsPerSecondRemaining : DefaultPointsPerSecond;

        private void Awake()
        {
            RemainingSeconds = RoundSeconds;
        }

        private void OnEnable()
        {
            SurgeryEvents.OnToolGrabbed += HandleToolGrabbed;
            SurgeryEvents.OnSurgeryCompleted += HandleSurgeryCompleted;
        }

        private void OnDisable()
        {
            SurgeryEvents.OnToolGrabbed -= HandleToolGrabbed;
            SurgeryEvents.OnSurgeryCompleted -= HandleSurgeryCompleted;
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>
        /// Advances the session. Update() feeds it real time; tests feed it fixed steps, so the
        /// stand's pacing can be verified without sitting through ninety real seconds.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            switch (State)
            {
                case SessionState.Briefing:
                    _stateElapsed += deltaTime;
                    if (BriefingTimeoutSeconds > 0f && _stateElapsed >= BriefingTimeoutSeconds)
                    {
                        ReturnToAttract();
                    }

                    break;

                case SessionState.Running:
                    _runSeconds += deltaTime;
                    RemainingSeconds = Mathf.Max(0f, RemainingSeconds - deltaTime);
                    if (RemainingSeconds <= 0f)
                    {
                        EndRound(false);
                    }

                    break;

                case SessionState.Success:
                case SessionState.Failure:
                    _stateElapsed += deltaTime;
                    if (_stateElapsed >= ResultHoldSeconds)
                    {
                        SetState(SessionState.Scoreboard);
                    }

                    break;

                case SessionState.Scoreboard:
                    _stateElapsed += deltaTime;
                    if (_stateElapsed >= ScoreboardHoldSeconds)
                    {
                        ReturnToAttract();
                    }

                    break;
            }
        }

        /// <summary>
        /// A visitor has the headset on. Wipes the previous turn off the patient and puts the
        /// briefing up; the clock does not start until they reach for an instrument.
        /// </summary>
        public void BeginSession()
        {
            SessionCount++;
            HasResult = false;
            RemainingSeconds = RoundSeconds;
            _runSeconds = 0f;

            if (resetController != null)
            {
                resetController.ResetSurgery();
            }

            SetState(SessionState.Briefing);
        }

        /// <summary>Starts the countdown. Normally triggered by the first instrument grab.</summary>
        public void StartRound()
        {
            if (State != SessionState.Briefing)
            {
                return;
            }

            RemainingSeconds = RoundSeconds;
            _runSeconds = 0f;
            SetState(SessionState.Running);
            RoundStarted?.Invoke();
        }

        /// <summary>
        /// Sets how many seconds a turn starts with, overriding the definition. Takes effect at the
        /// next turn; a clock that is not running is shown at the new length straight away.
        /// </summary>
        public void SetRoundSeconds(float seconds, float turnLimit = 0f)
        {
            _roundOverride = Mathf.Max(0f, seconds);
            TurnLimitSeconds = Mathf.Max(0f, turnLimit);
            if (State != SessionState.Running) { RemainingSeconds = RoundSeconds; }
        }

        /// <summary>
        /// The longest a turn can last, counting every second it could win; 0 when nothing is won.
        /// What the adaptive help paces the operation against.
        /// </summary>
        public float TurnLimitSeconds { get; private set; }

        /// <summary>
        /// Adds seconds to a running clock, or takes them away. Never below zero; a clock taken to
        /// zero ends the turn as a loss, exactly as running out does. Does nothing while the clock
        /// is not running — nothing is won or lost during the briefing.
        /// Returns the seconds actually applied.
        /// </summary>
        public float AdjustTime(float seconds)
        {
            if (State != SessionState.Running || Mathf.Approximately(seconds, 0f)) { return 0f; }

            float before = RemainingSeconds;
            RemainingSeconds = Mathf.Max(0f, RemainingSeconds + seconds);
            float applied = RemainingSeconds - before;
            TimeAdjusted?.Invoke(applied);

            if (RemainingSeconds <= 0f) { EndRound(false); }
            return applied;
        }

        /// <summary>
        /// Ends the turn as a loss without waiting for the clock. The operator's escape hatch for
        /// when someone takes the headset off mid-round, which at a stand happens constantly.
        /// </summary>
        public void AbortRound()
        {
            if (State == SessionState.Running)
            {
                EndRound(false);
            }
        }

        /// <summary>Drops straight back to attract mode from wherever the session is.</summary>
        public void ReturnToAttract()
        {
            RemainingSeconds = RoundSeconds;
            SetState(SessionState.Attract);
        }

        public void Bind(
            EventSessionDefinition sessionDefinition,
            SurgeryResetController reset,
            SurgeryTelemetry surgeryTelemetry = null)
        {
            definition = sessionDefinition;
            resetController = reset;
            telemetry = surgeryTelemetry;

            if (State == SessionState.Attract)
            {
                RemainingSeconds = RoundSeconds;
            }
        }

        private void EndRound(bool succeeded)
        {
            float remaining = RemainingSeconds;
            int score = succeeded ? Mathf.RoundToInt(remaining * PointsPerSecondRemaining) : 0;

            LastResult = new SessionResult(succeeded, _runSeconds, remaining, score);
            HasResult = true;

            SetState(succeeded ? SessionState.Success : SessionState.Failure);
            RoundEnded?.Invoke(LastResult);
        }

        private void SetState(SessionState next)
        {
            if (next == State)
            {
                return;
            }

            State = next;
            _stateElapsed = 0f;

            if (telemetry != null)
            {
                telemetry.Log($"Session {SessionCount}: {next}" +
                              (next is SessionState.Success or SessionState.Failure ? $" ({LastResult})" : string.Empty));
            }

            StateChanged?.Invoke(next);
        }

        private void HandleToolGrabbed(SurgicalTool tool, IHandInteractor holder)
        {
            if (State == SessionState.Briefing && StartOnFirstToolGrab)
            {
                StartRound();
            }
        }

        private void HandleSurgeryCompleted()
        {
            if (State == SessionState.Running)
            {
                EndRound(true);
            }
        }
    }
}
