using NUnit.Framework;
using UnityEngine;
using VRSurgery.Session;
using VRSurgery.Surgery;

namespace VRSurgery.Tests
{
    /// <summary>
    /// What the crowd around the stand sees: the table that ranks the day, and the room colour
    /// that rises with the clock.
    ///
    /// Both are driven off the session rather than tracked separately, so these tests step the
    /// session by hand and assert that the screen agreed — the failure they exist to catch is a
    /// projection that says something different from the round it is showing.
    /// </summary>
    public class ProjectionScreenTests
    {
        private const float Round = 60f;

        private GameObject _host;
        private EventSessionController _session;
        private EventSessionDefinition _definition;
        private Leaderboard _leaderboard;

        [SetUp]
        public void SetUp()
        {
            SurgeryEvents.ResetAll();

            // The table persists through PlayerPrefs, so a leftover run from a previous execution
            // would otherwise decide these assertions.
            PlayerPrefs.DeleteKey("VRSurgery.Leaderboard");

            _definition = EventSessionDefinition.Create(Round, 20f, 4f, 6f, true, 100f);

            _host = new GameObject("Stand");
            _host.SetActive(false);
            _session = _host.AddComponent<EventSessionController>();
            _session.Bind(_definition, null);
            _leaderboard = _host.AddComponent<Leaderboard>();
            _host.SetActive(true);

            _leaderboard.Bind(_session);
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) { Object.DestroyImmediate(_host); }
            if (_definition != null) { Object.DestroyImmediate(_definition); }

            PlayerPrefs.DeleteKey("VRSurgery.Leaderboard");
            SurgeryEvents.ResetAll();
        }

        private static SessionResult Finished(float seconds) =>
            new SessionResult(true, seconds, Round - seconds, Mathf.RoundToInt((Round - seconds) * 100f));

        [Test]
        public void TheFastestRunLeadsTheTable()
        {
            _leaderboard.Submit("Ana", Finished(31.5f));
            _leaderboard.Submit("Bruno", Finished(18.2f));
            _leaderboard.Submit("Caio", Finished(44.0f));

            Assert.AreEqual("Bruno", _leaderboard.Best.Value.Name,
                "The scoreboard ranks on time, so the quickest control of the bleeding leads it.");
            Assert.AreEqual(3, _leaderboard.Entries.Count);
        }

        [Test]
        public void RunningOutOfTimeEarnsNoPlace()
        {
            _leaderboard.Submit("Ana", new SessionResult(false, Round, 0f, 0));

            Assert.IsFalse(_leaderboard.HasAny,
                "A visitor who never controlled the bleeding has no time to rank; putting failures " +
                "on the board would bury the times worth beating.");
        }

        [Test]
        public void TheTableKeepsOnlyWhatTheScreenCanShow()
        {
            for (int i = 0; i < 12; i++)
            {
                _leaderboard.Submit($"V{i}", Finished(50f - i));
            }

            Assert.AreEqual(5, _leaderboard.Entries.Count, "Default capacity is five rows.");
            Assert.AreEqual(39f, _leaderboard.Best.Value.Seconds, 0.01f,
                "The fastest of the twelve has to survive the trimming.");
        }

        [Test]
        public void FinishingARoundFilesItWithoutBeingAsked()
        {
            _session.BeginSession();
            _session.StartRound();
            _session.Tick(20f);
            _session.Tick(0f);

            // Completing the surgery is what ends the round as a win.
            SurgeryEvents.RaiseSurgeryCompleted();

            Assert.IsTrue(_leaderboard.HasAny,
                "The round ended in a win, so the stand's table should already know about it.");
            Assert.AreEqual(20f, _leaderboard.Best.Value.Seconds, 0.1f);
        }

        [Test]
        public void TheRoomStaysCalmEarlyAndRedddensLate()
        {
            GameObject lightHost = new GameObject("Key");
            Light key = lightHost.AddComponent<Light>();
            key.type = LightType.Directional;

            GameObject tintHost = new GameObject("Tint");
            tintHost.SetActive(false);
            SceneUrgencyTint tint = tintHost.AddComponent<SceneUrgencyTint>();
            tint.Bind(_session, key);
            tintHost.SetActive(true);

            _session.BeginSession();
            _session.StartRound();

            // A quarter of the way in: well below the onset, the room must be untouched.
            _session.Tick(Round * 0.25f);
            tint.Tick(5f);
            Assert.AreEqual(0f, tint.Tint01, 0.001f,
                "Reddening from the first seconds would spend the effect before the round is tense.");

            // Nearly out of time: the tint has to be up.
            _session.Tick(Round * 0.70f);
            tint.Tick(5f);
            Assert.Greater(tint.Tint01, 0.9f,
                "With the clock almost gone the room should be at full alarm.");

            Object.DestroyImmediate(tintHost);
            Object.DestroyImmediate(lightHost);
        }

        [Test]
        public void TheTintLetsGoWhenTheRoundIsNotRunning()
        {
            GameObject lightHost = new GameObject("Key");
            Light key = lightHost.AddComponent<Light>();
            key.type = LightType.Directional;

            GameObject tintHost = new GameObject("Tint");
            tintHost.SetActive(false);
            SceneUrgencyTint tint = tintHost.AddComponent<SceneUrgencyTint>();
            tint.Bind(_session, key);
            tintHost.SetActive(true);

            _session.BeginSession();
            _session.StartRound();
            _session.Tick(Round * 0.95f);
            tint.Tick(5f);
            Assert.Greater(tint.Tint01, 0.9f);

            // The clock ran out; the next visitor must not inherit a red room.
            _session.Tick(Round);
            tint.Tick(5f);
            Assert.AreEqual(0f, tint.Tint01, 0.001f,
                "Once the round is over the room has to return to operating-room white.");

            Object.DestroyImmediate(tintHost);
            Object.DestroyImmediate(lightHost);
        }
    }
}
