using NUnit.Framework;
using UnityEngine;
using VRSurgery.Session;
using VRSurgery.Surgery;

namespace VRSurgery.Tests
{
    /// <summary>
    /// The stand's loop: a turn that can be lost to the clock, a score made of the time left, and
    /// a booth that always finds its way back to attract mode for the next person in the queue.
    ///
    /// Time is stepped by hand rather than waited on, so a ninety-second round costs the suite
    /// nothing and the pacing numbers are checked exactly instead of approximately.
    /// </summary>
    public class EventSessionTests
    {
        private const float Round = 60f;
        private const float BriefingTimeout = 20f;
        private const float ResultHold = 4f;
        private const float ScoreboardHold = 6f;
        private const float PointsPerSecond = 100f;

        private GameObject _host;
        private EventSessionController _session;
        private EventSessionDefinition _definition;

        [SetUp]
        public void SetUp()
        {
            SurgeryEvents.ResetAll();

            _definition = EventSessionDefinition.Create(
                Round, BriefingTimeout, ResultHold, ScoreboardHold, true, PointsPerSecond);

            // Built inactive so the definition is in place before Awake reads the round length.
            _host = new GameObject("EventSession");
            _host.SetActive(false);
            _session = _host.AddComponent<EventSessionController>();
            _session.Bind(_definition, null);
            _host.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null)
            {
                Object.DestroyImmediate(_host);
            }

            if (_definition != null)
            {
                Object.DestroyImmediate(_definition);
            }

            SurgeryEvents.ResetAll();
        }

        [Test]
        public void StartsInAttractWithAFullClock()
        {
            Assert.AreEqual(SessionState.Attract, _session.State);
            Assert.AreEqual(Round, _session.RemainingSeconds, 0.001f);
            Assert.IsFalse(_session.HasResult);
        }

        [Test]
        public void TheBriefingDoesNotRunTheClock()
        {
            _session.BeginSession();
            Assert.AreEqual(SessionState.Briefing, _session.State);

            Step(5f);

            Assert.AreEqual(Round, _session.RemainingSeconds, 0.001f,
                "The clock moved while the visitor was still reading the briefing.");
        }

        [Test]
        public void ReachingForTheFirstInstrumentStartsTheClock()
        {
            _session.BeginSession();

            // Null tool is safe here: SurgeryEvents was cleared in SetUp, so the session
            // controller is the only subscriber and it never reads the tool.
            SurgeryEvents.RaiseToolGrabbed(null, null);
            Assert.AreEqual(SessionState.Running, _session.State);

            Step(10f);
            Assert.AreEqual(Round - 10f, _session.RemainingSeconds, 0.01f);
        }

        [Test]
        public void RunningOutOfTimeLosesTheTurn()
        {
            _session.BeginSession();
            _session.StartRound();

            Step(Round + 0.5f);

            Assert.AreEqual(SessionState.Failure, _session.State);
            Assert.IsTrue(_session.HasResult);
            Assert.IsFalse(_session.LastResult.Succeeded);
            Assert.AreEqual(0, _session.LastResult.Score, "A lost turn must not score.");
        }

        [Test]
        public void ControllingTheBleedingInTimeWinsAndBanksWhatIsLeft()
        {
            SessionResult reported = default;
            int endedCount = 0;
            _session.RoundEnded += result => { reported = result; endedCount++; };

            _session.BeginSession();
            _session.StartRound();
            Step(20f);

            SurgeryEvents.RaiseSurgeryCompleted();

            Assert.AreEqual(SessionState.Success, _session.State);
            Assert.AreEqual(1, endedCount, "RoundEnded should fire exactly once per turn.");
            Assert.IsTrue(reported.Succeeded);
            Assert.AreEqual(20f, reported.ElapsedSeconds, 0.05f);
            Assert.That(reported.Score, Is.EqualTo(Mathf.RoundToInt((Round - 20f) * PointsPerSecond)).Within(2));
        }

        [Test]
        public void FinishingFasterScoresHigher()
        {
            _session.BeginSession();
            _session.StartRound();
            Step(12f);
            SurgeryEvents.RaiseSurgeryCompleted();
            int fastScore = _session.LastResult.Score;

            _session.BeginSession();
            _session.StartRound();
            Step(38f);
            SurgeryEvents.RaiseSurgeryCompleted();
            int slowScore = _session.LastResult.Score;

            Assert.Greater(fastScore, slowScore,
                "Time is the score, so the quicker turn has to be worth more.");
        }

        [Test]
        public void FinishingAfterTheClockIsIgnored()
        {
            _session.BeginSession();
            _session.StartRound();
            Step(Round + 0.5f);

            SurgeryEvents.RaiseSurgeryCompleted();

            Assert.AreEqual(SessionState.Failure, _session.State,
                "A surgery finished after time out must not turn a loss into a win.");
            Assert.IsFalse(_session.LastResult.Succeeded);
        }

        [Test]
        public void TheResultGivesWayToTheScoreboardAndThenBackToAttract()
        {
            _session.BeginSession();
            _session.StartRound();
            Step(5f);
            SurgeryEvents.RaiseSurgeryCompleted();

            Step(ResultHold + 0.5f);
            Assert.AreEqual(SessionState.Scoreboard, _session.State);

            Step(ScoreboardHold + 0.5f);
            Assert.AreEqual(SessionState.Attract, _session.State);
            Assert.AreEqual(Round, _session.RemainingSeconds, 0.001f,
                "The clock has to be rearmed before the next visitor arrives.");
        }

        [Test]
        public void TheBriefingGivesUpOnAVisitorWhoNeverStarts()
        {
            _session.BeginSession();

            Step(BriefingTimeout + 0.5f);

            Assert.AreEqual(SessionState.Attract, _session.State,
                "Someone put the headset down without playing; the stand must go back to attracting.");
        }

        [Test]
        public void EveryNewSessionRearmsTheClock()
        {
            _session.BeginSession();
            _session.StartRound();
            Step(25f);

            _session.BeginSession();

            Assert.AreEqual(SessionState.Briefing, _session.State);
            Assert.AreEqual(Round, _session.RemainingSeconds, 0.001f);
            Assert.AreEqual(2, _session.SessionCount);
        }

        [Test]
        public void AbortingEndsTheTurnAsALoss()
        {
            _session.BeginSession();
            _session.StartRound();
            Step(10f);

            _session.AbortRound();

            Assert.AreEqual(SessionState.Failure, _session.State);
            Assert.AreEqual(10f, _session.LastResult.ElapsedSeconds, 0.05f,
                "An aborted turn should report the time actually spent, not the whole round.");
            Assert.AreEqual(0, _session.LastResult.Score);
        }

        [Test]
        public void UrgencyRisesWithTheClock()
        {
            _session.BeginSession();
            _session.StartRound();
            Assert.AreEqual(0f, _session.Urgency01, 0.001f);

            Step(Round * 0.5f);
            Assert.AreEqual(0.5f, _session.Urgency01, 0.01f);

            Step(Round * 0.5f);
            Assert.AreEqual(1f, _session.Urgency01, 0.001f);
        }

        /// <summary>Feeds the session fixed steps. 0.25 is exact in binary, so the clock does not drift.</summary>
        private void Step(float seconds)
        {
            const float stepSize = 0.25f;
            int steps = Mathf.CeilToInt(seconds / stepSize);
            for (int i = 0; i < steps; i++)
            {
                _session.Tick(stepSize);
            }
        }
    }
}
