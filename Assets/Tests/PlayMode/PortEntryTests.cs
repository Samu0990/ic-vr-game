using System;
using NUnit.Framework;
using UnityEngine;
using VRSurgery.Ports;
using VRSurgery.Session;
using VRSurgery.Surgery;

namespace VRSurgery.Tests
{
    /// <summary>
    /// "A Porta de Entrada": the decision at each port site, and the round built on top of it.
    ///
    /// The mechanic the concept is teaching lives entirely in the overlap between the safe zone
    /// and the vessel, so that is what most of this measures — including the case where a point
    /// is inside both, which is the only one where the rule is not obvious.
    /// </summary>
    public class PortEntryTests
    {
        private GameObject _host;
        private InsertionPort _port;

        private const float SafeR = 0.020f;
        private const float VesselR = 0.010f;
        private static readonly Vector2 VesselAt = new Vector2(0.015f, 0f);

        [SetUp]
        public void SetUp()
        {
            SurgeryEvents.ResetAll();
            PlayerPrefs.DeleteKey("VRSurgery.Leaderboard");

            _host = new GameObject("Port");
            _port = _host.AddComponent<InsertionPort>();
            _port.Configure("teste", SafeR, VesselAt, VesselR);
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) { UnityEngine.Object.DestroyImmediate(_host); }
            PlayerPrefs.DeleteKey("VRSurgery.Leaderboard");
            SurgeryEvents.ResetAll();
        }

        private Vector3 At(float x, float z) => _host.transform.TransformPoint(new Vector3(x, 0f, z));

        [Test]
        public void EnteringClearOfTheVesselClosesTheSite()
        {
            // Opposite side of the site from the vessel, well inside the safe zone.
            Assert.AreEqual(PortState.Closed, _port.Insert(At(-0.012f, 0f)));
            Assert.IsTrue(_port.IsResolved);
        }

        [Test]
        public void EnteringOnTheVesselBleeds()
        {
            Assert.AreEqual(PortState.Bleeding, _port.Insert(At(0.015f, 0f)));
            Assert.IsFalse(_port.IsResolved, "A bleeding site is not dealt with until it is packed.");
        }

        [Test]
        public void TheVesselWinsWhereTheZonesOverlap()
        {
            // This point is inside the safe radius AND inside the vessel radius. The safe zone is
            // only safe where the vessel is not; scoring it the other way would teach the exact
            // opposite of the lesson.
            Vector3 both = At(0.012f, 0f);
            Vector3 local = _host.transform.InverseTransformPoint(both);
            Assert.Less(new Vector2(local.x, local.z).magnitude, SafeR, "sanity: inside the safe zone");
            Assert.Less(Vector2.Distance(new Vector2(local.x, local.z), VesselAt), VesselR,
                "sanity: also on the vessel");

            Assert.AreEqual(PortState.Bleeding, _port.Insert(both));
        }

        [Test]
        public void MissingTheSiteEntirelyAlsoBleeds()
        {
            // Outside both zones. Unplanned abdominal wall is not safer wall, just unplanned.
            Assert.AreEqual(PortState.Bleeding, _port.Insert(At(-0.05f, 0.04f)));
        }

        [Test]
        public void ASiteIsOnlyDecidedOnce()
        {
            Assert.AreEqual(PortState.Closed, _port.Insert(At(-0.012f, 0f)));
            // A second push must not re-roll a site that is already closed.
            Assert.AreEqual(PortState.Closed, _port.Insert(At(0.015f, 0f)));
        }

        [Test]
        public void GauzeOnlyHelpsASiteThatIsBleeding()
        {
            Assert.IsFalse(_port.ApplyGauze(), "Nothing to pack on an untouched site.");

            _port.Insert(At(0.015f, 0f));
            Assert.IsTrue(_port.ApplyGauze());
            Assert.AreEqual(PortState.Controlled, _port.State);
            Assert.IsTrue(_port.IsResolved, "A packed site counts as dealt with — it just cost time.");

            Assert.IsFalse(_port.ApplyGauze(), "Packing it twice changes nothing.");
        }

        [Test]
        public void TheRoundEndsWhenEverySiteIsDealtWith()
        {
            GameObject host = new GameObject("Procedure");
            host.SetActive(false);
            PortProcedure procedure = host.AddComponent<PortProcedure>();

            InsertionPort[] sites = new InsertionPort[3];
            for (int i = 0; i < sites.Length; i++)
            {
                GameObject go = new GameObject("site" + i);
                go.transform.position = new Vector3(i * 0.1f, 0f, 0f);
                sites[i] = go.AddComponent<InsertionPort>();
                sites[i].Configure("s" + i, SafeR, VesselAt, VesselR);
            }

            procedure.Bind(sites);
            host.SetActive(true);

            int completions = 0;
            procedure.ProcedureCompleted += () => completions++;

            sites[0].Insert(sites[0].transform.position + new Vector3(-0.012f, 0f, 0f));
            Assert.IsFalse(procedure.IsComplete);

            // Second one catches a vessel: resolved only after the gauze.
            sites[1].Insert(sites[1].transform.position + new Vector3(0.015f, 0f, 0f));
            Assert.AreEqual(1, procedure.BleedingCount);
            Assert.IsFalse(procedure.IsComplete, "A bleeding site holds the round open.");

            sites[1].ApplyGauze();
            sites[2].Insert(sites[2].transform.position + new Vector3(-0.012f, 0f, 0f));

            Assert.IsTrue(procedure.IsComplete);
            Assert.AreEqual(3, procedure.ResolvedCount);
            Assert.AreEqual(2, procedure.CleanCount, "Only the two that never bled are clean.");
            Assert.AreEqual(1, completions, "Completion is announced exactly once.");

            foreach (InsertionPort p in sites) { UnityEngine.Object.DestroyImmediate(p.gameObject); }
            UnityEngine.Object.DestroyImmediate(host);
        }

        [Test]
        public void TheScoreboardRanksToTheMillisecondAndTheFirstToGetThereKeepsTheLead()
        {
            GameObject host = new GameObject("Board");
            host.SetActive(false);
            Leaderboard board = host.AddComponent<Leaderboard>();
            host.SetActive(true);

            // Two runs the stand would display as identical.
            board.Submit("Primeiro", new SessionResult(true, 12.487f, 77.5f, 7750));
            System.Threading.Thread.Sleep(5);
            board.Submit("Segundo", new SessionResult(true, 12.487f, 77.5f, 7750));
            board.Submit("Rapido", new SessionResult(true, 12.486f, 77.5f, 7751));

            Assert.AreEqual("Rapido", board.Entries[0].Name,
                "One millisecond is a real difference at this stand.");
            Assert.AreEqual("Primeiro", board.Entries[1].Name,
                "On an equal time the visitor who set the bar keeps the higher place.");
            Assert.AreEqual("Segundo", board.Entries[2].Name);

            Assert.AreEqual(12486, board.Entries[0].Milliseconds);

            UnityEngine.Object.DestroyImmediate(host);
        }

        [Test]
        public void UrgencyReachesThePlayerWithoutSound()
        {
            GameObject host = new GameObject("Stand");
            host.SetActive(false);
            EventSessionDefinition definition = EventSessionDefinition.Create(60f, 20f, 4f, 6f, true, 100f);
            EventSessionController session = host.AddComponent<EventSessionController>();
            session.Bind(definition, null);
            UrgencySignals signals = host.AddComponent<UrgencySignals>();
            signals.Bind(session, null);
            host.SetActive(true);

            session.BeginSession();
            session.StartRound();

            // A quarter in: below the onset, the player is left alone.
            session.Tick(15f);
            signals.Tick(0.1f);
            Assert.AreEqual(0f, signals.Intensity01, 0.001f);

            // Nearly out of time: the signal is up, and it is pulsing.
            session.Tick(42f);
            int before = signals.PulseCount;
            for (int i = 0; i < 120; i++) { signals.Tick(1f / 60f); }

            Assert.Greater(signals.Intensity01, 0.9f);
            Assert.Greater(signals.PulseCount, before,
                "The concept puts the urgency in the controllers and the vignette, not in audio, " +
                "because a stand is too loud for sound to carry it.");

            UnityEngine.Object.DestroyImmediate(host);
            UnityEngine.Object.DestroyImmediate(definition);
        }
    }
}
