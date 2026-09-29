using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRSurgery.Surgery;
using VRSurgery.Transplant;

namespace VRSurgery.Tests
{
    /// <summary>
    /// The pericardium is opened with the cautery pen after the sternotomy and before the pump.
    /// A 10 x 14 cm cap at 1 m, a transform for the pen tip, two transforms for the halves.
    /// </summary>
    public class PericardiumTests
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

        private TransplantProcedure AtThePericardium()
        {
            TransplantProcedure procedure = Spawn("Procedure").AddComponent<TransplantProcedure>();
            procedure.SetPericardiumStage(true);
            procedure.Begin();
            Assert.IsTrue(procedure.CompleteStage(TransplantStage.OpenChest), "test setup: sternum open");
            return procedure;
        }

        private PericardiumWorker Worker(TransplantProcedure procedure, out Transform tip, out Transform left, out Transform right)
        {
            tip = Spawn("PenTip").transform;
            left = Spawn("Left").transform;
            right = Spawn("Right").transform;

            PericardiumWorker worker = Spawn("Pericardium").AddComponent<PericardiumWorker>();
            worker.Bind(tip, null, procedure, left, right, null, null, null);
            worker.Shape(new Vector3(0f, 1f, 0f), 0.05f, 0.07f, 1f, 0.02f);
            return worker;
        }

        private static void Stroke(PericardiumWorker worker, Transform tip, float lateral, float lift, int frames = 40)
        {
            for (int i = 0; i <= frames; i++)
            {
                tip.position = worker.LinePoint(i / (float)frames, lift) + new Vector3(lateral, 0f, 0f);
                worker.Tick(Step);
            }
        }

        [Test]
        public void ThePericardiumComesBetweenTheSawAndThePump()
        {
            TransplantProcedure procedure = Spawn("Procedure").AddComponent<TransplantProcedure>();
            procedure.SetSkinStages(true);
            procedure.SetPericardiumStage(true);
            procedure.Begin();

            int saw = -1, sac = -1, pump = -1;
            for (int i = 0; i < procedure.Order.Count; i++)
            {
                if (procedure.Order[i] == TransplantStage.OpenChest) { saw = i; }
                if (procedure.Order[i] == TransplantStage.OpenPericardium) { sac = i; }
                if (procedure.Order[i] == TransplantStage.GoOnBypass) { pump = i; }
            }

            Assert.AreEqual(saw + 1, sac);
            Assert.AreEqual(sac + 1, pump);

            procedure.CompleteStage(TransplantStage.SkinIncision);
            procedure.CompleteStage(TransplantStage.OpenChest);
            Assert.AreEqual(TransplantStage.OpenPericardium, procedure.Stage);
            StringAssert.Contains("pericárdio", procedure.CurrentInstruction);
        }

        [Test]
        public void APenDrawnDownTheLineOpensItAndTheHalvesFoldBack()
        {
            TransplantProcedure procedure = AtThePericardium();
            PericardiumWorker worker = Worker(procedure, out Transform tip, out Transform left, out Transform right);

            Stroke(worker, tip, 0.002f, -0.002f);

            Assert.IsTrue(worker.IsComplete);
            Assert.AreEqual(TransplantStage.GoOnBypass, procedure.Stage, "Opened: on to the pump.");

            for (int i = 0; i < 90; i++) { worker.Tick(Step); }

            Assert.AreEqual(1f, worker.Openness01, 1e-4f);
            Assert.Greater(Quaternion.Angle(Quaternion.identity, left.localRotation), 90f, "Each half folds back over its hinge,");
            Assert.Greater(Quaternion.Angle(Quaternion.identity, right.localRotation), 90f);
            Assert.Greater(left.TransformPoint(Vector3.right * 0.03f).x, -0.03f - 1e-3f);
            Assert.Less(left.TransformPoint(Vector3.right * 0.03f).y, 0.03f + 1e-3f);
            Assert.Greater(left.TransformPoint(Vector3.right * 0.03f).y, 0f,
                "up and away from the heart, not down into it.");
        }

        [Test]
        public void APenBesideTheLineOrAboveTheMembraneOpensNothing()
        {
            TransplantProcedure procedure = AtThePericardium();
            PericardiumWorker worker = Worker(procedure, out Transform tip, out _, out _);

            Stroke(worker, tip, 0.03f, -0.002f);
            Assert.AreEqual(0f, worker.Progress01, "Three centimetres to the side is not the line.");

            Stroke(worker, tip, 0f, 0.03f);
            Assert.AreEqual(0f, worker.Progress01, "and pointing at it from three centimetres up is not touching it.");
        }

        [Test]
        public void ItWaitsForTheSternum()
        {
            TransplantProcedure procedure = Spawn("Procedure").AddComponent<TransplantProcedure>();
            procedure.SetPericardiumStage(true);
            procedure.Begin();
            PericardiumWorker worker = Worker(procedure, out Transform tip, out _, out _);

            Stroke(worker, tip, 0f, -0.002f);

            Assert.AreEqual(0f, worker.Progress01);
            Assert.AreEqual(TransplantStage.OpenChest, procedure.Stage);
        }

        [Test]
        public void TheNextVisitorGetsAClosedPericardium()
        {
            TransplantProcedure procedure = AtThePericardium();
            PericardiumWorker worker = Worker(procedure, out Transform tip, out Transform left, out _);

            Stroke(worker, tip, 0f, -0.002f);
            for (int i = 0; i < 90; i++) { worker.Tick(Step); }

            worker.ResetPericardium();

            Assert.IsFalse(worker.IsComplete);
            Assert.AreEqual(0f, worker.Openness01);
            Assert.AreEqual(0f, worker.Progress01);
            Assert.Less(Quaternion.Angle(Quaternion.identity, left.localRotation), 0.01f);
        }
    }
}
