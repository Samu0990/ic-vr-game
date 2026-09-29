using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRSurgery.Surgery;
using VRSurgery.Transplant;

namespace VRSurgery.Tests
{
    /// <summary>
    /// The scalpel incision that opens the operation and the suture that closes it, plus the
    /// fixes to the advisor's anastomosis feedback that the same session made.
    ///
    /// Everything is driven by hand with fixed steps and no rig: a flat 20 x 30 cm patch of skin
    /// at 1 m, a transform for the blade, a transform for the needle.
    /// </summary>
    public class SkinStagesTests
    {
        private const float Step = 1f / 60f;
        private const float SkinY = 1f;

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

        private MeshFilter Filter(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            return go.GetComponent<MeshFilter>();
        }

        /// <summary>A flat patch at SkinY, 20 cm across and 30 cm long, with the incision over its middle 72%.</summary>
        private ChestSkinPatch FlatPatch(out GameObject cavity)
        {
            const int columns = 6, rows = 11;
            GameObject root = Spawn("Patch");
            float[] heights = new float[rows * (columns * 2 - 1)];
            for (int i = 0; i < heights.Length; i++) { heights[i] = SkinY; }

            cavity = Spawn("Cavity");
            ChestSkinPatch patch = root.AddComponent<ChestSkinPatch>();
            patch.Bind(columns, rows, 0.10f, 0.15f, heights,
                Filter("L", root.transform), Filter("R", root.transform),
                Filter("WL", root.transform), Filter("WR", root.transform), new[] { cavity });
            return patch;
        }

        private TransplantProcedure ProcedureWithSkinStages()
        {
            TransplantProcedure procedure = Spawn("Procedure").AddComponent<TransplantProcedure>();
            procedure.SetSkinStages(true);
            procedure.Begin();
            return procedure;
        }

        // ------------------------------------------------------------------ procedure

        [Test]
        public void WithoutSkinStages_TheOperationIsUnchanged()
        {
            TransplantProcedure procedure = Spawn("Procedure").AddComponent<TransplantProcedure>();
            procedure.Begin();

            Assert.AreEqual(TransplantStage.OpenChest, procedure.Stage,
                "Scenes and tests that never asked for the scalpel must still start at the sternotomy.");
            Assert.AreEqual(6, procedure.Order.Count);
        }

        [Test]
        public void WithSkinStages_TheScalpelOpensAndTheNeedleCloses()
        {
            TransplantProcedure procedure = ProcedureWithSkinStages();

            Assert.AreEqual(TransplantStage.SkinIncision, procedure.Stage);
            Assert.AreEqual(TransplantStage.SkinIncision, procedure.Order[0]);
            Assert.AreEqual(TransplantStage.CloseSkin, procedure.Order[procedure.Order.Count - 1]);

            Assert.IsFalse(procedure.CompleteStage(TransplantStage.OpenChest),
                "The sternum cannot be opened through skin that has not been cut.");
            Assert.IsTrue(procedure.CompleteStage(TransplantStage.SkinIncision));
            Assert.AreEqual(TransplantStage.OpenChest, procedure.Stage);
        }

        [Test]
        public void TheRoundIsOnlyWonOnceTheSkinIsClosed()
        {
            TransplantProcedure procedure = ProcedureWithSkinStages();
            bool completed = false;
            procedure.ProcedureCompleted += () => completed = true;

            procedure.CompleteStage(TransplantStage.SkinIncision);
            procedure.CompleteStage(TransplantStage.OpenChest);
            procedure.Bypass.Attempt(BypassStep.Cannulate);
            procedure.Bypass.Attempt(BypassStep.ClampAorta);
            procedure.Bypass.Attempt(BypassStep.Cardioplegia);
            procedure.CompleteStage(TransplantStage.GoOnBypass);
            procedure.CompleteStage(TransplantStage.RemoveNativeHeart);
            procedure.CompleteStage(TransplantStage.PlaceDonorHeart);
            for (int i = 0; i < procedure.VesselCount; i++) { procedure.ConnectVessel(); }
            procedure.Bypass.Attempt(BypassStep.Unclamp, true);
            procedure.Bypass.Attempt(BypassStep.DeAir, true);
            procedure.Bypass.Attempt(BypassStep.Wean, true);
            Assert.IsTrue(procedure.CompleteStage(TransplantStage.Restart));

            Assert.AreEqual(TransplantStage.CloseSkin, procedure.Stage);
            Assert.IsFalse(completed, "A beating heart under an open chest is not a finished operation.");

            Assert.IsTrue(procedure.CompleteStage(TransplantStage.CloseSkin));
            Assert.IsTrue(completed);
            Assert.IsTrue(procedure.IsComplete);
        }

        // ------------------------------------------------------------------ skin patch

        [Test]
        public void ThePatchOpensRevealsTheCavityAndClosesAgain()
        {
            ChestSkinPatch patch = FlatPatch(out GameObject cavity);
            Assert.IsFalse(cavity.activeSelf, "Nothing under the skin shows through a closed chest.");

            patch.Open();
            for (int i = 0; i < 120; i++) { patch.Tick(Step); }
            Assert.IsTrue(patch.IsOpen);
            Assert.IsTrue(cavity.activeSelf);

            patch.Close();
            for (int i = 0; i < 120; i++) { patch.Tick(Step); }
            Assert.IsTrue(patch.IsClosed);
            Assert.IsFalse(cavity.activeSelf);
        }

        [Test]
        public void IncisionPointsLieOnTheMidlineAtSkinHeight()
        {
            ChestSkinPatch patch = FlatPatch(out _);

            Vector3 start = patch.IncisionPoint(0f, 0f, 0f);
            Vector3 end = patch.IncisionPoint(1f, 0f, 0f);

            Assert.AreEqual(0f, start.x, 1e-4f);
            Assert.AreEqual(SkinY, start.y, 1e-4f);
            Assert.Greater(end.z, start.z, "along = 1 is the head end.");
            Assert.AreEqual(patch.IncisionLength, end.z - start.z, 1e-3f);
        }

        // ------------------------------------------------------------------ incision

        private SkinIncisionWorker Incision(ChestSkinPatch patch, TransplantProcedure procedure, out Transform blade)
        {
            blade = Spawn("Blade").transform;
            SkinIncisionWorker worker = Spawn("Incision").AddComponent<SkinIncisionWorker>();
            worker.Bind(blade, null, patch, procedure, null, null, null);
            return worker;
        }

        [Test]
        public void AStrokeDownTheLineCutsTheSkinAndOpensIt()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            TransplantProcedure procedure = ProcedureWithSkinStages();
            SkinIncisionWorker worker = Incision(patch, procedure, out Transform blade);

            bool started = false;
            worker.Started += () => started = true;

            // Head to feet in 1.5 s, a normal stroke, on the line and just into the skin.
            for (int i = 0; i <= 90; i++)
            {
                blade.position = patch.IncisionPoint(1f - i / 90f, 0.003f, -0.002f);
                worker.Tick(Step);
            }

            Assert.IsTrue(started, "The first contact is what starts the visitor's clock.");
            Assert.IsTrue(worker.IsComplete);
            Assert.AreEqual(TransplantStage.OpenChest, procedure.Stage);
            Assert.IsTrue(patch.IsMoving || patch.IsOpen, "A finished incision is retracted open.");
        }

        [Test]
        public void AFastStrokeIsNotPunishedForSkippingFrames()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            TransplantProcedure procedure = ProcedureWithSkinStages();
            SkinIncisionWorker worker = Incision(patch, procedure, out Transform blade);

            // Twelve frames for the whole line: bins are skipped between samples.
            for (int i = 0; i <= 12; i++)
            {
                blade.position = patch.IncisionPoint(i / 12f, 0f, 0f);
                worker.Tick(Step);
            }

            Assert.IsTrue(worker.IsComplete,
                "A confident, quick stroke along the line is the correct technique.");
        }

        [Test]
        public void ABladeInTheAirCutsNothing()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            TransplantProcedure procedure = ProcedureWithSkinStages();
            SkinIncisionWorker worker = Incision(patch, procedure, out Transform blade);

            for (int i = 0; i <= 90; i++)
            {
                blade.position = patch.IncisionPoint(i / 90f, 0f, 0.05f);
                worker.Tick(Step);
            }

            Assert.AreEqual(0f, worker.Progress01, "Five centimetres above the skin is not an incision.");
            Assert.AreEqual(TransplantStage.SkinIncision, procedure.Stage);
        }

        [Test]
        public void CuttingOffTheLineIsAnErrorAndDoesNotCount()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            TransplantProcedure procedure = ProcedureWithSkinStages();
            SkinIncisionWorker worker = Incision(patch, procedure, out Transform blade);

            int deviations = 0;
            worker.Deviated += () => deviations++;

            for (int i = 0; i <= 90; i++)
            {
                blade.position = patch.IncisionPoint(i / 90f, 0.035f, 0f);
                worker.Tick(Step);
            }

            Assert.AreEqual(0f, worker.Progress01);
            Assert.Greater(deviations, 0, "Three and a half centimetres off the midline has to be called out.");
        }

        [Test]
        public void TheIncisionWaitsForItsStage()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            TransplantProcedure procedure = Spawn("Procedure").AddComponent<TransplantProcedure>();
            procedure.Begin(); // no skin stages: straight to the sternotomy
            SkinIncisionWorker worker = Incision(patch, procedure, out Transform blade);

            for (int i = 0; i <= 90; i++)
            {
                blade.position = patch.IncisionPoint(i / 90f, 0f, 0f);
                worker.Tick(Step);
            }

            Assert.AreEqual(0f, worker.Progress01);
        }

        [Test]
        public void ResettingTheIncisionGivesTheNextVisitorUncutSkin()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            TransplantProcedure procedure = ProcedureWithSkinStages();
            SkinIncisionWorker worker = Incision(patch, procedure, out Transform blade);

            for (int i = 0; i <= 90; i++)
            {
                blade.position = patch.IncisionPoint(i / 90f, 0f, 0f);
                worker.Tick(Step);
            }

            worker.ResetIncision();
            patch.ResetClosed();

            Assert.IsFalse(worker.IsComplete);
            Assert.AreEqual(0f, worker.Progress01);
            Assert.IsTrue(patch.IsClosed);
        }

        // ------------------------------------------------------------------ tissue physics

        [Test]
        public void TheCutSpringsOpenBehindTheBlade()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            TransplantProcedure procedure = ProcedureWithSkinStages();
            SkinIncisionWorker worker = Incision(patch, procedure, out Transform blade);

            // Half the line, so the incision is not finished and the chest stays closed.
            for (int i = 0; i <= 30; i++)
            {
                blade.position = patch.IncisionPoint(0.2f + 0.3f * i / 30f, 0f, -0.003f);
                worker.Tick(Step);
                patch.Tick(Step);
            }

            for (int i = 0; i < 30; i++) { patch.Tick(Step); }

            Assert.Greater(patch.GapeAt(0.35f), 0.002f,
                "Skin is under tension: a fresh cut gapes a few millimetres on its own.");
            Assert.AreEqual(0f, patch.GapeAt(0.85f), 1e-5f, "Skin the blade never reached stays shut.");
            Assert.IsFalse(patch.IsOpen);
        }

        [Test]
        public void TheSkinDentsUnderTheBladeAndRecovers()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            TransplantProcedure procedure = ProcedureWithSkinStages();
            SkinIncisionWorker worker = Incision(patch, procedure, out Transform blade);

            blade.position = patch.IncisionPoint(0.5f, 0f, -0.004f);
            for (int i = 0; i < 20; i++)
            {
                worker.Tick(Step);
                patch.Tick(Step);
            }

            Assert.Greater(patch.PressDepth, 0.002f, "The blade pushes the skin in before it parts it.");

            blade.position = patch.IncisionPoint(0.5f, 0f, 0.05f);
            for (int i = 0; i < 60; i++)
            {
                worker.Tick(Step);
                patch.Tick(Step);
            }

            Assert.AreEqual(0f, patch.PressDepth, 1e-5f, "and springs back once the blade lifts.");
        }

        [Test]
        public void ABladeHoveringACentimetreAboveCutsNothing()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            TransplantProcedure procedure = ProcedureWithSkinStages();
            SkinIncisionWorker worker = Incision(patch, procedure, out Transform blade);

            for (int i = 0; i <= 90; i++)
            {
                blade.position = patch.IncisionPoint(i / 90f, 0f, 0.01f);
                worker.Tick(Step);
            }

            Assert.AreEqual(0f, worker.Progress01, "Skin is cut by touching it, not by pointing at it.");
        }

        [Test]
        public void CuttingDownToTheBoneIsAnErrorAndBleedsMore()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            TransplantProcedure procedure = ProcedureWithSkinStages();
            SkinIncisionWorker worker = Incision(patch, procedure, out Transform blade);

            int deep = 0;
            worker.DeepCut += () => deep++;

            for (int i = 0; i <= 20; i++)
            {
                blade.position = patch.IncisionPoint(0.2f + 0.2f * i / 20f, 0f, -0.026f);
                worker.Tick(Step);
            }

            Assert.Greater(deep, 0, "2.6 cm under the skin is through the fat and onto the sternum.");
            Assert.Greater(worker.Progress01, 0f, "It still cuts,");
            Assert.IsTrue(worker.IsBinDeep(Mathf.FloorToInt(0.3f * worker.Bins)), "and that stretch is marked as deep.");
        }

        [Test]
        public void StitchesDrawTheGapingCutShut()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            patch.SetCut(0f, 1f, true);
            for (int i = 0; i < 60; i++) { patch.Tick(Step); }
            Assert.Greater(patch.GapeAt(0.5f), 0.002f, "test setup: the whole line is cut and gaping.");

            SutureWorker worker = Suture(patch, out Transform needle, out _);
            for (int k = 0; k < 5; k++)
            {
                Hold(worker, needle, worker.MarkPosition(k, 0), 0.5f);
                Hold(worker, needle, worker.MarkPosition(k, 1), 0.5f);
            }

            for (int i = 0; i < 60; i++) { patch.Tick(Step); }

            foreach (float along in new[] { 0.05f, 0.3f, 0.5f, 0.7f, 0.95f })
            {
                Assert.AreEqual(0f, patch.GapeAt(along), 1e-5f, $"The stitches leave no gap at {along:F2} of the line.");
            }
        }

        // ------------------------------------------------------------------ suture

        private SutureWorker Suture(ChestSkinPatch patch, out Transform needle, out List<GameObject> knots)
        {
            needle = Spawn("Needle").transform;
            knots = new List<GameObject>();
            for (int i = 0; i < 5; i++)
            {
                GameObject knot = Spawn("Knot" + i);
                knot.SetActive(false);
                knots.Add(knot);
            }

            SutureWorker worker = Spawn("Suture").AddComponent<SutureWorker>();
            worker.Bind(needle, null, patch, null, 5, null, knots);
            return worker;
        }

        private static void Hold(SutureWorker worker, Transform needle, Vector3 at, float seconds)
        {
            needle.position = at;
            for (float t = 0f; t < seconds; t += Step) { worker.Tick(Step); }
        }

        [Test]
        public void EachStitchIsInOnOneSideAndOutOnTheOther()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            SutureWorker worker = Suture(patch, out Transform needle, out List<GameObject> knots);

            Vector3 entry = worker.MarkPosition(0, 0);
            Vector3 exit = worker.MarkPosition(0, 1);
            Assert.Greater(entry.x, 0f, "Entry on the surgeon's side of the incision.");
            Assert.Less(exit.x, 0f, "Exit on the far side.");

            Hold(worker, needle, entry, 0.5f);
            Assert.AreEqual(0, worker.StitchesTied, "One bite is not a stitch.");
            Assert.IsFalse(knots[0].activeSelf);

            Hold(worker, needle, exit, 0.5f);
            Assert.AreEqual(1, worker.StitchesTied);
            Assert.IsTrue(knots[0].activeSelf, "A tied stitch is left visible on the skin.");
        }

        [Test]
        public void FiveStitchesCloseTheSkin()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            SutureWorker worker = Suture(patch, out Transform needle, out _);

            bool completed = false;
            worker.Completed += () => completed = true;

            for (int k = 0; k < 5; k++)
            {
                Hold(worker, needle, worker.MarkPosition(k, 0), 0.5f);
                Hold(worker, needle, worker.MarkPosition(k, 1), 0.5f);
            }

            Assert.IsTrue(worker.IsComplete);
            Assert.IsTrue(completed);
        }

        [Test]
        public void ABrushPastTheMarkDoesNotPassTheNeedle()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            SutureWorker worker = Suture(patch, out Transform needle, out _);

            Hold(worker, needle, worker.MarkPosition(0, 0), 0.1f);
            Hold(worker, needle, worker.MarkPosition(0, 1), 0.5f);

            Assert.AreEqual(0, worker.StitchesTied, "The needle has to rest on the entry mark before the exit counts.");
        }

        [Test]
        public void NoStitchesAcrossAnOpenChest()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            patch.Open();
            for (int i = 0; i < 120; i++) { patch.Tick(Step); }

            SutureWorker worker = Suture(patch, out Transform needle, out _);
            Hold(worker, needle, worker.MarkPosition(0, 0), 0.5f);
            Hold(worker, needle, worker.MarkPosition(0, 1), 0.5f);

            Assert.AreEqual(0, worker.StitchesTied, "Skin is sutured once the edges are brought together.");
        }

        // ------------------------------------------------------------------ advisor PR fixes

        private VesselAnastomosis LeakingSite(TransplantProcedure procedure, out AnastomosisWorker worker, out Transform tip)
        {
            procedure.Begin();
            procedure.CompleteStage(TransplantStage.OpenChest);
            procedure.Bypass.Attempt(BypassStep.Cannulate);
            procedure.Bypass.Attempt(BypassStep.ClampAorta);
            procedure.Bypass.Attempt(BypassStep.Cardioplegia);
            procedure.CompleteStage(TransplantStage.GoOnBypass);
            procedure.CompleteStage(TransplantStage.RemoveNativeHeart);
            procedure.CompleteStage(TransplantStage.PlaceDonorHeart);

            GameObject siteHost = Spawn("Aorta");
            siteHost.transform.position = new Vector3(0f, 1.2f, 0.5f);
            VesselAnastomosis site = siteHost.AddComponent<VesselAnastomosis>();
            site.Bind(VesselSite.Aorta, procedure, 0.025f);

            tip = Spawn("Tip").transform;
            worker = Spawn("Worker").AddComponent<AnastomosisWorker>();
            worker.Bind(tip, new[] { site }, procedure);
            SetRequireHeld(worker, false);

            Vector3 a = siteHost.transform.position + new Vector3(0.01f, 0f, 0f);
            Vector3 b = siteHost.transform.position - new Vector3(0.01f, 0f, 0f);
            for (int i = 0; i < 120; i++)
            {
                tip.position = i % 2 == 0 ? a : b;
                worker.Tick(Step);
            }

            Assert.IsTrue(site.IsBleeding, "test setup: the shaky hold should have caused a leak.");
            return site;
        }

        private static void SetRequireHeld(AnastomosisWorker worker, bool value)
        {
            System.Reflection.FieldInfo field = typeof(AnastomosisWorker).GetField("requireHeldInstrument",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            field.SetValue(worker, value);
        }

        [Test]
        public void TheLeakIsStillNamedAfterTheHandLeavesIt()
        {
            TransplantProcedure procedure = Spawn("Procedure").AddComponent<TransplantProcedure>();
            VesselAnastomosis site = LeakingSite(procedure, out AnastomosisWorker worker, out Transform tip);

            tip.position = site.transform.position + new Vector3(0.3f, 0f, 0f);
            worker.Tick(Step);

            Assert.IsNull(worker.ActiveSite, "test setup: the hand is off every site.");
            Assert.AreEqual(site, worker.BleedingSite,
                "Lifting the hand off a leak is exactly when the visitor needs telling it still leaks.");
        }

        [Test]
        public void TheNextVisitorGetsRestingRingsNotTheLastOnesGreenOnes()
        {
            TransplantProcedure procedure = Spawn("Procedure").AddComponent<TransplantProcedure>();
            procedure.Begin();
            procedure.CompleteStage(TransplantStage.OpenChest);
            procedure.Bypass.Attempt(BypassStep.Cannulate);
            procedure.Bypass.Attempt(BypassStep.ClampAorta);
            procedure.Bypass.Attempt(BypassStep.Cardioplegia);
            procedure.CompleteStage(TransplantStage.GoOnBypass);
            procedure.CompleteStage(TransplantStage.RemoveNativeHeart);
            procedure.CompleteStage(TransplantStage.PlaceDonorHeart);

            GameObject siteHost = Spawn("Aorta");
            siteHost.transform.position = new Vector3(0f, 1.2f, 0.5f);
            VesselAnastomosis site = siteHost.AddComponent<VesselAnastomosis>();
            site.Bind(VesselSite.Aorta, procedure, 0.025f);

            GameObject ringHost = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ringHost.transform.SetParent(siteHost.transform, false);
            Renderer ring = ringHost.GetComponent<Renderer>();
            ring.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));

            Color resting = new Color(0.85f, 0.22f, 0.20f, 0.65f);
            VesselAnastomosisVisual visual = siteHost.AddComponent<VesselAnastomosisVisual>();
            visual.Bind(site, ring, resting);

            for (int i = 0; i < 120; i++) { site.Work(siteHost.transform.position, Step); }
            for (int i = 0; i < 90; i++) { visual.Tick(Step); }
            Assert.IsTrue(site.IsJoined, "test setup: a steady hold joins the vessel.");

            // TransplantRoundBridge does exactly this between visitors, without reloading the scene.
            site.ResetJoin();
            visual.Tick(Step);

            Assert.AreEqual(resting, visual.CurrentColor);
        }
    }
}
