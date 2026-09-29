using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRSurgery.Surgery;
using VRSurgery.Transplant;

namespace VRSurgery.Tests
{
    /// <summary>Adaptive help: none for a visitor on pace, more for one falling behind, gone for the next visitor.</summary>
    public class PaceAssistTests
    {
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

        private PaceAssist Assist(out TransplantProcedure procedure, out VesselAnastomosis vessel)
        {
            GameObject host = new GameObject("Procedure");
            _spawned.Add(host);
            procedure = host.AddComponent<TransplantProcedure>();
            procedure.Begin();

            GameObject join = new GameObject("Vessel");
            _spawned.Add(join);
            vessel = join.AddComponent<VesselAnastomosis>();

            PaceAssist assist = host.AddComponent<PaceAssist>();
            assist.Bind(null, procedure, null, new[] { vessel });
            assist.ResetAssist();
            return assist;
        }

        [Test]
        public void AVisitorOnPaceGetsNoHelp()
        {
            PaceAssist assist = Assist(out _, out VesselAnastomosis vessel);

            for (float t = 0f; t <= 25f; t += 1f) { assist.Tick(t); }

            Assert.AreEqual(0, assist.Level);
            Assert.AreEqual(1f, vessel.SpeedMultiplier);
        }

        [Test]
        public void FallingBehindBringsHelpThatGrowsAndNeverShrinksMidTurn()
        {
            PaceAssist assist = Assist(out TransplantProcedure procedure, out VesselAnastomosis vessel);
            int announced = 0;
            assist.Helped += _ => announced++;

            // The sternotomy is budgeted 25 s; still on it at 45 s is 20 s behind.
            assist.Tick(45f);
            Assert.AreEqual(1, assist.Level);
            Assert.AreEqual(1.6f, vessel.SpeedMultiplier, 1e-4f, "The long hand-held steps count faster,");

            assist.Tick(65f);
            Assert.AreEqual(2, assist.Level);
            Assert.AreEqual(2.5f, vessel.SpeedMultiplier, 1e-4f, "and faster still further behind.");

            procedure.CompleteStage(TransplantStage.OpenChest);
            assist.Tick(66f);
            Assert.AreEqual(2, assist.Level, "Help is not taken back in the middle of a turn.");
            Assert.AreEqual(2, announced, "Each level is announced once.");
        }

        [Test]
        public void TheNextVisitorStartsWithoutHelp()
        {
            PaceAssist assist = Assist(out TransplantProcedure procedure, out VesselAnastomosis vessel);
            assist.Tick(70f);
            Assert.AreEqual(2, assist.Level);

            procedure.ResetProcedure();

            Assert.AreEqual(0, assist.Level);
            Assert.AreEqual(1f, vessel.SpeedMultiplier);
        }
    }
}
