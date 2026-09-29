using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRSurgery.Feedback;
using VRSurgery.Interaction;
using VRSurgery.Surgery;
using VRSurgery.Transplant;

namespace VRSurgery.Tests
{
    /// <summary>
    /// The reperfused donor heart fibrillates when the cross-clamp comes off, and the internal
    /// paddles bring it back. Driven by hand with fixed steps: a procedure walked to the unclamp,
    /// a transform for the heart, a transform for the point between the paddles.
    /// </summary>
    public class DefibrillationTests
    {
        private const float Step = 1f / 60f;

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
            {
                if (go != null) { Object.DestroyImmediate(go); }
            }

            _spawned.Clear();
            SurgeryEvents.ResetAll();
        }

        private GameObject Spawn(string name)
        {
            GameObject go = new GameObject(name);
            _spawned.Add(go);
            return go;
        }

        /// <summary>A procedure on the pump, arrested, implanted, with the clamp just taken off.</summary>
        private TransplantProcedure Unclamped()
        {
            TransplantProcedure procedure = Spawn("Procedure").AddComponent<TransplantProcedure>();
            procedure.Begin();
            Assert.IsTrue(procedure.Bypass.Attempt(BypassStep.Cannulate).Accepted);
            Assert.IsTrue(procedure.Bypass.Attempt(BypassStep.ClampAorta).Accepted);
            Assert.IsTrue(procedure.Bypass.Attempt(BypassStep.Cardioplegia).Accepted);
            Assert.IsTrue(procedure.Bypass.Attempt(BypassStep.Unclamp, true).Accepted, "test setup: unclamped");
            return procedure;
        }

        private DefibrillationWorker Defib(TransplantProcedure procedure, out Heartbeat heart, out Transform centre,
            out SurgicalInteractable paddles, float firstShockWorks = 0f)
        {
            GameObject organ = Spawn("DonorHeart");
            organ.transform.position = new Vector3(0f, 1f, 0f);
            heart = organ.AddComponent<Heartbeat>();

            centre = Spawn("PaddleCentre").transform;
            centre.position = new Vector3(0.5f, 1.3f, 0f);
            paddles = Spawn("Paddles").AddComponent<SurgicalInteractable>();

            DefibrillationWorker worker = Spawn("Defib").AddComponent<DefibrillationWorker>();
            worker.Bind(centre, paddles, procedure, heart, organ.transform);
            worker.Configure(1f, firstShockWorks, 2);
            return worker;
        }

        private static void Run(DefibrillationWorker worker, float seconds)
        {
            for (float t = 0f; t < seconds; t += Step) { worker.Tick(Step); }
        }

        [Test]
        public void TheNewHeartFibrillatesWhenBloodReachesIt()
        {
            TransplantProcedure procedure = Unclamped();
            DefibrillationWorker worker = Defib(procedure, out Heartbeat heart, out _, out _);

            worker.Tick(Step);

            Assert.IsTrue(worker.IsFibrillating);
            Assert.IsFalse(heart.IsBeating, "A fibrillating ventricle does not beat, it writhes.");
            StringAssert.Contains("Fibrilação", procedure.CurrentInstruction,
                "The room says what is happening instead of the pump's next step.");
            Assert.IsNotNull(worker.Blocks(BypassStep.DeAir), "A fibrillating heart cannot take the circulation back.");
            Assert.IsNotNull(worker.Blocks(BypassStep.Wean));
        }

        [Test]
        public void NothingHappensBeforeTheClampComesOff()
        {
            TransplantProcedure procedure = Spawn("Procedure").AddComponent<TransplantProcedure>();
            procedure.Begin();
            procedure.Bypass.Attempt(BypassStep.Cannulate);
            DefibrillationWorker worker = Defib(procedure, out _, out _, out _);

            Run(worker, 1f);

            Assert.IsFalse(worker.IsFibrillating);
            Assert.IsNull(worker.Blocks(BypassStep.DeAir));
        }

        [Test]
        public void PaddlesHeldOnTheHeartChargeThenShock_TheSecondShockAlwaysConverts()
        {
            TransplantProcedure procedure = Unclamped();
            DefibrillationWorker worker = Defib(procedure, out Heartbeat heart, out Transform centre,
                out SurgicalInteractable paddles, firstShockWorks: 0f);

            int converted = 0;
            worker.Converted += shocks => converted = shocks;

            worker.Tick(Step);
            paddles.OnGrabbed(null);
            centre.position = heart.transform.position;

            Run(worker, 1.4f);
            Assert.AreEqual(1, worker.Shocks, "About a second of charge, then the shock.");
            Assert.AreEqual(10, worker.LastJoules, "Internal shocks start at 10 J.");
            Assert.IsTrue(worker.IsFibrillating, "This first shock was set to fail, as about four in ten do.");

            Run(worker, 2.5f);
            Assert.AreEqual(2, worker.Shocks);
            Assert.AreEqual(20, worker.LastJoules, "and the next one escalates.");
            Assert.IsFalse(worker.IsFibrillating);
            Assert.IsTrue(heart.IsBeating, "Converted: the new heart beats on its own.");
            Assert.AreEqual(2, converted);
            Assert.IsNull(worker.Blocks(BypassStep.DeAir));
            StringAssert.DoesNotContain("Fibrilação", procedure.CurrentInstruction);
        }

        [Test]
        public void PaddlesOnlyChargeWhileHeld()
        {
            TransplantProcedure procedure = Unclamped();
            DefibrillationWorker worker = Defib(procedure, out Heartbeat heart, out Transform centre, out _);

            centre.position = heart.transform.position;
            Run(worker, 2f);

            Assert.AreEqual(0, worker.Shocks, "Paddles lying on the heart do nothing until someone holds them.");
        }

        [Test]
        public void LiftingThePaddlesDumpsTheCharge()
        {
            TransplantProcedure procedure = Unclamped();
            DefibrillationWorker worker = Defib(procedure, out Heartbeat heart, out Transform centre,
                out SurgicalInteractable paddles);

            paddles.OnGrabbed(null);
            centre.position = heart.transform.position;
            Run(worker, 0.6f);
            Assert.Greater(worker.Charge01, 0.3f);

            centre.position = heart.transform.position + new Vector3(0.2f, 0f, 0f);
            Run(worker, 1f);

            Assert.AreEqual(0f, worker.Charge01, 1e-4f);
            Assert.AreEqual(0, worker.Shocks);
        }

        [Test]
        public void ThePumpWillNotDeAirAFibrillatingHeart()
        {
            TransplantProcedure procedure = Unclamped();
            DefibrillationWorker defib = Defib(procedure, out _, out _, out _);
            defib.Tick(Step);

            Transform tip = Spawn("Tip").transform;
            Transform site = Spawn("DeAirSite").transform;
            tip.position = site.position;

            BypassWorker pump = Spawn("Pump").AddComponent<BypassWorker>();
            pump.Bind(tip, new List<BypassSite> { new BypassSite { Step = BypassStep.DeAir, Point = site, Seconds = 1f } }, procedure);
            pump.AddGate(defib);

            string refused = null;
            pump.StepRefused += reason => refused = reason;
            for (int i = 0; i < 180; i++) { pump.Tick(Step); }

            Assert.AreEqual(BypassStep.Unclamp, procedure.Bypass.Step, "Held for three seconds, still not de-aired.");
            StringAssert.Contains("fibrilando", refused, "and the room is told why.");
        }

        [Test]
        public void TheMonitorShowsFibrillationAndThenSinusRhythm()
        {
            TransplantProcedure procedure = Unclamped();
            DefibrillationWorker worker = Defib(procedure, out Heartbeat heart, out Transform centre,
                out SurgicalInteractable paddles, firstShockWorks: 1f);

            VitalSignsMonitor monitor = Spawn("Monitor").AddComponent<VitalSignsMonitor>();
            monitor.Bind(procedure, heart, null, null, null, null, null, null, null, null);
            monitor.BindDefibrillation(worker);

            worker.Tick(Step);
            monitor.Tick(Step);
            Assert.AreEqual(VitalSignsMonitor.VitalsState.Fibrillation, monitor.State);

            paddles.OnGrabbed(null);
            centre.position = heart.transform.position;
            Run(worker, 1.4f);
            monitor.Tick(Step);

            Assert.AreEqual(1, worker.Shocks);
            Assert.AreEqual(VitalSignsMonitor.VitalsState.NewHeart, monitor.State);
        }

        [Test]
        public void ANewVisitorGetsAHeartThatIsNotFibrillating()
        {
            TransplantProcedure procedure = Unclamped();
            DefibrillationWorker worker = Defib(procedure, out Heartbeat heart, out _, out _);
            Vector3 rest = heart.transform.localScale;

            Run(worker, 0.5f);
            Assert.IsTrue(worker.IsFibrillating);

            procedure.ResetProcedure();
            worker.Tick(Step);

            Assert.IsFalse(worker.IsFibrillating);
            Assert.AreEqual(0, worker.Shocks);
            Assert.AreEqual(rest, heart.transform.localScale, "The quiver is not baked into the organ's size.");
        }

        [Test]
        public void TheFibrillationTraceHasNoBeats()
        {
            // VF is a coarse irregular wave, never the tall narrow spike of an R wave.
            float highest = float.MinValue;
            for (int i = 0; i < 400; i++)
            {
                highest = Mathf.Max(highest, VitalSignsMonitor.Fibrillation(i * 0.01f));
            }

            Assert.Less(highest, 0.7f);
            Assert.Greater(highest, 0.15f, "but it is not flat either: that would read as asystole.");
        }
    }
}
