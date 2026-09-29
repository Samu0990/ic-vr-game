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

        // ------------------------------------------------------------------ technique

        /// <summary>Draws the blade along the line from one fraction to another, just into the skin.</summary>
        private static void Stroke(SkinIncisionWorker worker, ChestSkinPatch patch, Transform blade,
            float from, float to, int frames, float lateral = 0f, float depth = -0.002f)
        {
            for (int i = 0; i <= frames; i++)
            {
                blade.position = patch.IncisionPoint(Mathf.Lerp(from, to, i / (float)frames), lateral, depth);
                worker.Tick(Step);
                patch.Tick(Step);
            }
        }

        [Test]
        public void ABladeRestingOnTheLineDentsButDoesNotCut()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            TransplantProcedure procedure = ProcedureWithSkinStages();
            SkinIncisionWorker worker = Incision(patch, procedure, out Transform blade);

            blade.position = patch.IncisionPoint(0.5f, 0f, -0.003f);
            for (int i = 0; i < 60; i++)
            {
                worker.Tick(Step);
                patch.Tick(Step);
            }

            Assert.AreEqual(0f, worker.Progress01, "A scalpel cuts when it is drawn, not when it is pressed.");
            Assert.Greater(patch.PressDepth, 0.001f, "Pressed, the skin only gives.");
        }

        [Test]
        public void AStabStraightDownDoesNotDrawALine()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            TransplantProcedure procedure = ProcedureWithSkinStages();
            SkinIncisionWorker worker = Incision(patch, procedure, out Transform blade);

            for (int i = 0; i <= 20; i++)
            {
                blade.position = patch.IncisionPoint(0.5f, 0f, 0.003f - 0.006f * i / 20f);
                worker.Tick(Step);
            }

            Assert.AreEqual(0f, worker.Progress01);
        }

        [Test]
        public void ABladeLaidFlatScrapesAndTheVisitorIsTold()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            TransplantProcedure procedure = ProcedureWithSkinStages();
            SkinIncisionWorker worker = Incision(patch, procedure, out Transform blade);

            int hints = 0;
            worker.BladeMisaligned += () => hints++;

            // The blade's face (its local X) turned to the sky: the flat of the blade on the skin.
            blade.rotation = Quaternion.Euler(0f, 0f, 90f);
            Stroke(worker, patch, blade, 0.2f, 0.5f, 30);

            Assert.AreEqual(0f, worker.Progress01, "The flat of the blade does not cut skin.");
            Assert.Greater(hints, 0, "and the monitor says how to hold it.");
            Assert.IsFalse(worker.EdgeRuleRelaxed, "Half a second of trying is not yet someone who is stuck.");
        }

        [Test]
        public void ABladePushedSidewaysDoesNotSlice()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            TransplantProcedure procedure = ProcedureWithSkinStages();
            SkinIncisionWorker worker = Incision(patch, procedure, out Transform blade);

            // Edge down, but its face turned into the direction of travel.
            blade.rotation = Quaternion.Euler(0f, 90f, 0f);
            Stroke(worker, patch, blade, 0.2f, 0.5f, 30);

            Assert.AreEqual(0f, worker.Progress01);
        }

        [Test]
        public void AdaptiveHelp_AVisitorStuckOnTheGripGetsAScalpelThatCuts()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            TransplantProcedure procedure = ProcedureWithSkinStages();
            SkinIncisionWorker worker = Incision(patch, procedure, out Transform blade);

            blade.rotation = Quaternion.Euler(0f, 0f, 90f);
            for (int i = 0; i <= 300; i++)
            {
                float phase = Mathf.PingPong(i / 60f, 1f);
                blade.position = patch.IncisionPoint(0.1f + 0.4f * phase, 0f, -0.002f);
                worker.Tick(Step);
            }

            Assert.IsTrue(worker.EdgeRuleRelaxed, "Several seconds of trying with the blade flat is someone who needs help,");
            Assert.Greater(worker.Progress01, 0f, "and from then on the scalpel cuts however it is held.");
        }

        [Test]
        public void TheWoundLiesWhereTheBladeWent()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            TransplantProcedure procedure = ProcedureWithSkinStages();
            SkinIncisionWorker worker = Incision(patch, procedure, out Transform blade);

            Stroke(worker, patch, blade, 0.2f, 0.6f, 40, 0.003f);

            Assert.AreEqual(0.003f, worker.LateralAt(Mathf.FloorToInt(0.4f * worker.Bins)), 5e-4f);
            Assert.Greater(patch.WanderAt(0.4f), 0.002f,
                "The skin parts under the blade, 3 mm to the right of the line, not on the drawn line.");
            Assert.AreEqual(0f, patch.WanderAt(0.9f), 1e-5f, "Uncut skin has no wound to move.");
        }

        [Test]
        public void AWanderingHandCannotPushTheWoundOntoTheStitchMarks()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            TransplantProcedure procedure = ProcedureWithSkinStages();
            SkinIncisionWorker worker = Incision(patch, procedure, out Transform blade);

            Stroke(worker, patch, blade, 0.2f, 0.6f, 40, 0.012f);

            Assert.Greater(worker.Progress01, 0f, "12 mm off is still on the line,");
            Assert.LessOrEqual(Mathf.Abs(patch.WanderAt(0.4f)), 0.0041f,
                "but the wound stays inside the suture's reach.");
        }

        [Test]
        public void CuttingThroughTheSkinOffTheLineLeavesAScratch()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            TransplantProcedure procedure = ProcedureWithSkinStages();
            SkinIncisionWorker worker = Incision(patch, procedure, out Transform blade);

            Stroke(worker, patch, blade, 0.2f, 0.6f, 40, 0.03f);

            Assert.AreEqual(1, worker.Scratches, "One pass off the line is one nick left on the skin.");
            Assert.AreEqual(0f, worker.Progress01);
        }

        [Test]
        public void OneCleanStrokeIsAPerfectIncision()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            TransplantProcedure procedure = ProcedureWithSkinStages();
            SkinIncisionWorker worker = Incision(patch, procedure, out Transform blade);

            IncisionGrade? graded = null;
            worker.Graded += g => graded = g;

            Stroke(worker, patch, blade, 1f, 0f, 90, 0.001f);

            Assert.IsTrue(graded.HasValue, "Finishing the incision grades it.");
            Assert.AreEqual(1, graded.Value.Strokes);
            Assert.AreEqual(3, graded.Value.Stars);
        }

        [Test]
        public void SawingAtTheSkinCostsStars()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            TransplantProcedure procedure = ProcedureWithSkinStages();
            SkinIncisionWorker worker = Incision(patch, procedure, out Transform blade);

            for (int j = 0; j < 5 && !worker.IsComplete; j++)
            {
                Stroke(worker, patch, blade, 0.2f * j, 0.2f * (j + 1), 20);
                blade.position = patch.IncisionPoint(0.2f * (j + 1), 0f, 0.05f);
                for (int i = 0; i < 5; i++) { worker.Tick(Step); }
            }

            Assert.IsTrue(worker.IsComplete);
            Assert.GreaterOrEqual(worker.Grade.Strokes, 4, "Each lift and return is another pass.");
            Assert.Less(worker.Grade.Stars, 3, "A skin incision is one confident stroke.");
        }

        [Test]
        public void TheGradeReadsTheWayAPreceptorWould()
        {
            Assert.AreEqual(3, IncisionGrade.From(1, 1.5f, false, 0, false).Stars);
            Assert.AreEqual(2, IncisionGrade.From(3, 4f, false, 0, false).Stars);
            Assert.AreEqual(1, IncisionGrade.From(5, 10f, true, 2, true).Stars);
        }

        [Test]
        public void TheBladeComesAwayBloodyAndTheNextVisitorGetsItClean()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            TransplantProcedure procedure = ProcedureWithSkinStages();
            SkinIncisionWorker worker = Incision(patch, procedure, out Transform blade);

            Renderer blood = Spawn("Blood").AddComponent<MeshRenderer>();
            worker.BindMarks(null, blood);
            Assert.IsFalse(blood.enabled, "A clean blade on the tray.");

            Stroke(worker, patch, blade, 0.2f, 0.5f, 30);
            Stroke(worker, patch, blade, 0.2f, 0.5f, 30, 0.03f);
            worker.Tick(Step);
            Assert.IsTrue(blood.enabled, "Blood on the blade after the first cut.");

            worker.ResetIncision();
            Assert.IsFalse(blood.enabled);
            Assert.AreEqual(0, worker.Scratches);
            Assert.AreEqual(0, worker.Strokes);
            Assert.IsFalse(worker.EdgeRuleRelaxed);
        }

        [Test]
        public void TwoCardsAtOnceAreShownOneAfterTheOther()
        {
            GameObject root = Spawn("Card");
            Transform body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);

            VRSurgery.Feedback.StageResultPopup popup = root.AddComponent<VRSurgery.Feedback.StageResultPopup>();
            popup.Bind(body, null, null, new Renderer[0], null, null, null, null);

            popup.Show("SUTURA PERFEITA", "5 pontos", 3, Vector3.one);
            popup.Show("TRANSPLANTE CONCLUÍDO!", "incisão 3/3 · sutura 3/3", 3, Vector3.one);

            Assert.AreEqual("SUTURA PERFEITA", popup.ShownTitle, "The first card is not overwritten,");
            Assert.AreEqual(1, popup.Waiting, "the second waits its turn,");

            for (int i = 0; i < 300; i++) { popup.Tick(Step); }
            Assert.AreEqual("TRANSPLANTE CONCLUÍDO!", popup.ShownTitle, "and comes up when the first is done.");
            Assert.IsTrue(popup.IsShowing);
        }

        [Test]
        public void TheResultCardCountsTheStarsOutThenGoes()
        {
            GameObject root = Spawn("Card");
            Transform body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);

            VRSurgery.Feedback.StageResultPopup popup = root.AddComponent<VRSurgery.Feedback.StageResultPopup>();
            popup.Bind(body, null, null, new Renderer[0], null, null, null, null);

            int chimes = 0;
            popup.StarRevealed += _ => chimes++;

            popup.Show("INCISÃO PERFEITA", "1 passada", 3, Vector3.one);
            Assert.IsTrue(popup.IsShowing);
            Assert.AreEqual(0, popup.RevealedStars, "The stars are counted out, not stamped.");

            for (int i = 0; i < 60; i++) { popup.Tick(Step); }
            Assert.AreEqual(3, popup.RevealedStars);
            Assert.AreEqual(3, chimes);

            for (int i = 0; i < 300; i++) { popup.Tick(Step); }
            Assert.IsFalse(popup.IsShowing);
            Assert.IsFalse(body.gameObject.activeSelf);
        }

        // ------------------------------------------------------------------ skin physics

        private static void Settle(ChestSkinPatch patch, float seconds)
        {
            for (float t = 0f; t < seconds; t += Step) { patch.Tick(Step); }
        }

        [Test]
        public void APinchedEdgeLiftsAndTheFarEdgeStaysPut()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            patch.SetCut(0f, 1f, true);
            Settle(patch, 1f);

            float lon = Mathf.Lerp(patch.IncisionStart, patch.IncisionEnd, 0.5f);
            int handle = patch.Grab(patch.IncisionPoint(0.5f, 0.004f, 0f));
            Assert.GreaterOrEqual(handle, 0, "The skin next to the cut can be taken with forceps.");

            patch.Drag(handle, patch.IncisionPoint(0.5f, 0.004f, 0.02f));
            Settle(patch, 0.5f);

            Assert.Greater(patch.SurfacePoint(0.004f, lon, 0f).y, SkinY + 0.012f, "The near edge comes up with the pull,");
            Assert.Less(patch.SurfacePoint(-0.004f, lon, 0f).y, SkinY + 0.002f,
                "and the far edge, a separate piece of skin across the cut, does not.");
        }

        [Test]
        public void SkinGivesOnlySoFar()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            int handle = patch.Grab(patch.IncisionPoint(0.5f, 0.02f, 0f));

            patch.Drag(handle, patch.IncisionPoint(0.5f, 0.02f, 0.2f));
            Settle(patch, 0.5f);

            Assert.LessOrEqual(patch.PullDistance(handle), 0.0301f, "Twenty centimetres of pull is three of stretch.");
        }

        [Test]
        public void LetGoItSpringsBackPastRestAndSettles()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            float lon = Mathf.Lerp(patch.IncisionStart, patch.IncisionEnd, 0.5f);
            int handle = patch.Grab(patch.IncisionPoint(0.5f, 0.02f, 0f));
            patch.Drag(handle, patch.IncisionPoint(0.5f, 0.02f, 0.02f));
            Settle(patch, 0.5f);

            patch.Release(handle);
            float lowest = float.MaxValue;
            for (int i = 0; i < 30; i++)
            {
                patch.Tick(Step);
                lowest = Mathf.Min(lowest, patch.SurfacePoint(0.02f, lon, 0f).y);
            }

            Assert.Less(lowest, SkinY - 0.0005f, "Elastic: it overshoots below where it rests,");

            Settle(patch, 2f);
            Assert.AreEqual(SkinY, patch.SurfacePoint(0.02f, lon, 0f).y, 5e-4f, "then settles back.");
        }

        [Test]
        public void TheBladeDragsTheSkinAlongWithIt()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            TransplantProcedure procedure = ProcedureWithSkinStages();
            SkinIncisionWorker worker = Incision(patch, procedure, out Transform blade);

            Stroke(worker, patch, blade, 0.2f, 0.5f, 30);

            Assert.Greater(patch.DragDistance, 0.001f, "Skin bunches a few millimetres along a moving blade.");
        }

        [Test]
        public void ForcepsClosedOnTheSkinHoldIt_ClosedInTheAirHoldNothing()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            Transform tip = Spawn("ForcepsTip").transform;
            SkinForceps forceps = Spawn("Forceps").AddComponent<SkinForceps>();
            forceps.Bind(tip, null, null, patch, null);

            tip.position = patch.IncisionPoint(0.5f, 0.01f, 0.05f);
            forceps.SetPinch(true);
            Assert.IsFalse(forceps.IsHoldingSkin, "Squeezed in the air: nothing to hold.");
            forceps.SetPinch(false);

            tip.position = patch.IncisionPoint(0.5f, 0.01f, 0.001f);
            forceps.SetPinch(true);
            Assert.IsTrue(forceps.IsHoldingSkin, "Squeezed on the skin: it holds.");

            forceps.SetPinch(false);
            Assert.IsFalse(forceps.IsHoldingSkin);
        }

        [Test]
        public void HoldingTheEdgeWithForcepsMakesTheBiteQuicker()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            SutureWorker worker = Suture(patch, out Transform needle, out _);

            Transform tip = Spawn("ForcepsTip").transform;
            SkinForceps forceps = Spawn("Forceps").AddComponent<SkinForceps>();
            forceps.Bind(tip, null, null, patch, null);
            worker.BindForceps(forceps);

            tip.position = patch.IncisionPoint(0.9f, 0.012f, 0.001f);
            forceps.SetPinch(true);
            Assert.IsTrue(forceps.IsHoldingSkin, "test setup: the edge is held beside the first stitch");

            // 0.35 s alone; with the edge held, 0.2 s is enough.
            Hold(worker, needle, worker.MarkPosition(0, 0), 0.2f);
            Assert.IsTrue(worker.EdgeHeld || worker.Progress01 > 0f);
            Assert.AreEqual(0.1f, worker.Progress01, 1e-4f, "The first bite went in: half a stitch.");
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

        // ------------------------------------------------------------------ cautery

        private CauteryWorker Cautery(ChestSkinPatch patch, out Transform tip, out VRSurgery.Interaction.SurgicalInteractable pen,
            out List<Transform> bleeders)
        {
            tip = Spawn("PenTip").transform;
            pen = Spawn("Pen").AddComponent<VRSurgery.Interaction.SurgicalInteractable>();
            bleeders = new List<Transform>();
            List<GameObject> scorches = new List<GameObject>();
            for (int i = 0; i < 3; i++)
            {
                Transform b = Spawn("Bleeder" + i).transform;
                b.position = patch.IncisionPoint(0.3f + 0.2f * i, i % 2 == 0 ? -0.04f : 0.04f, -0.008f);
                bleeders.Add(b);
                scorches.Add(Spawn("Scorch" + i));
            }

            CauteryWorker worker = Spawn("Cautery").AddComponent<CauteryWorker>();
            worker.Bind(tip, pen, patch, bleeders, scorches, null);
            return worker;
        }

        [Test]
        public void BleedersOnlyShowOnceTheWoundIsOpen()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            CauteryWorker worker = Cautery(patch, out _, out _, out List<Transform> bleeders);

            worker.Tick(Step);
            Assert.AreEqual(0, worker.OpenBleeders);
            Assert.IsFalse(bleeders[0].gameObject.activeSelf);

            patch.Open();
            for (int i = 0; i < 120; i++) { patch.Tick(Step); }
            worker.Tick(Step);

            Assert.AreEqual(3, worker.OpenBleeders);
            Assert.IsTrue(bleeders[0].gameObject.activeSelf);
        }

        [Test]
        public void ThePenSealsABleederItRestsOnButOnlyInTheHand()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            patch.Open();
            for (int i = 0; i < 120; i++) { patch.Tick(Step); }

            CauteryWorker worker = Cautery(patch, out Transform tip, out VRSurgery.Interaction.SurgicalInteractable pen,
                out List<Transform> bleeders);

            int sealedCount = 0;
            worker.Sealed += _ => sealedCount++;

            tip.position = bleeders[1].position;
            for (int i = 0; i < 60; i++) { worker.Tick(Step); }
            Assert.AreEqual(0, sealedCount, "A pen lying in its holster seals nothing.");

            pen.OnGrabbed(null);
            for (int i = 0; i < 60; i++) { worker.Tick(Step); }

            Assert.AreEqual(1, sealedCount);
            Assert.AreEqual(2, worker.OpenBleeders);
            Assert.IsFalse(bleeders[1].gameObject.activeSelf, "The sealed bleeder stops bleeding.");
        }

        // ------------------------------------------------------------------ hearts

        [Test]
        public void TheNativeHeartBeatsUntilCardioplegiaArrestsIt()
        {
            TransplantProcedure procedure = Spawn("Procedure").AddComponent<TransplantProcedure>();
            procedure.Begin();

            Heartbeat heart = Spawn("NativeHeart").AddComponent<Heartbeat>();
            NativeHeartRhythm rhythm = Spawn("Rhythm").AddComponent<NativeHeartRhythm>();
            rhythm.Bind(procedure, heart);

            rhythm.Tick();
            Assert.IsTrue(heart.IsBeating, "The sick heart is still beating when the chest is opened.");

            procedure.CompleteStage(TransplantStage.OpenChest);
            procedure.Bypass.Attempt(BypassStep.Cannulate);
            procedure.Bypass.Attempt(BypassStep.ClampAorta);
            procedure.Bypass.Attempt(BypassStep.Cardioplegia);
            rhythm.Tick();

            Assert.IsFalse(heart.IsBeating, "Cardioplegia is what stops it.");
        }

        [Test]
        public void AnIrregularHeartStillBeatsAtRoughlyItsRate()
        {
            Heartbeat heart = Spawn("Heart").AddComponent<Heartbeat>();
            heart.Configure(90f, 0.04f, 0.2f);
            heart.StartBeating();

            int beats = 0;
            heart.Beat += () => beats++;
            for (int i = 0; i < 60 * 20; i++) { heart.Tick(Step); }

            // 90 bpm for 20 s is 30 beats; a 20% wander either way averages out.
            Assert.That(beats, Is.InRange(24, 36));
        }

        // ------------------------------------------------------------------ sternal saw

        [Test]
        public void TheSawOnlyCutsBoneWhileItIsHeld()
        {
            TransplantProcedure procedure = Spawn("Procedure").AddComponent<TransplantProcedure>();
            procedure.Begin();

            SternotomyController bone = Spawn("Sternum").AddComponent<SternotomyController>();
            Transform site = Spawn("Site").transform;
            Transform blade = Spawn("SawBlade").transform;
            blade.position = site.position;

            SternotomyWorker worker = Spawn("Worker").AddComponent<SternotomyWorker>();
            worker.Bind(blade, site, bone, procedure, 0.1f, 3f);

            VRSurgery.Interaction.SurgicalInteractable saw =
                Spawn("Saw").AddComponent<VRSurgery.Interaction.SurgicalInteractable>();
            worker.BindInstrument(saw, blade);

            for (int i = 0; i < 240; i++) { worker.Tick(Step); }
            Assert.AreEqual(0f, worker.Progress01, "A saw lying on the tray over the chest cuts nothing.");
            Assert.IsFalse(bone.IsMoving);

            saw.OnGrabbed(null);
            for (int i = 0; i < 200; i++) { worker.Tick(Step); }

            Assert.IsTrue(worker.UsesSaw);
            Assert.IsTrue(bone.IsMoving || bone.IsOpen, "Three seconds of saw on the bone opens the sternum.");
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
        public void AClosureOnTheMarksIsGradedPerfect()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            SutureWorker worker = Suture(patch, out Transform needle, out _);

            SutureGrade? graded = null;
            worker.Graded += g => graded = g;

            for (int k = 0; k < 5; k++)
            {
                Hold(worker, needle, worker.MarkPosition(k, 0), 0.5f);
                Hold(worker, needle, worker.MarkPosition(k, 1), 0.5f);
            }

            Assert.IsTrue(graded.HasValue, "Tying the last stitch grades the closure.");
            Assert.AreEqual(0, graded.Value.Misses);
            Assert.AreEqual(3, graded.Value.Stars);
        }

        [Test]
        public void ANeedlePushedThroughSkinAwayFromTheMarkIsAHole()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            SutureWorker worker = Suture(patch, out Transform needle, out _);

            Hold(worker, needle, patch.IncisionPoint(0.4f, 0.03f, -0.004f), 0.2f);
            Assert.AreEqual(1, worker.Misses, "Three centimetres from the mark, into the skin: one stray hole,");

            Hold(worker, needle, patch.IncisionPoint(0.4f, 0.03f, -0.006f), 0.2f);
            Assert.AreEqual(1, worker.Misses, "not one per frame while it stays in;");

            Hold(worker, needle, patch.IncisionPoint(0.4f, 0.03f, 0.02f), 0.1f);
            Hold(worker, needle, patch.IncisionPoint(0.3f, 0.03f, -0.004f), 0.2f);
            Assert.AreEqual(2, worker.Misses, "out and in again is another.");
        }

        [Test]
        public void TheNeedleRunningUnderTheSkinFromEntryToExitIsTheStitch()
        {
            ChestSkinPatch patch = FlatPatch(out _);
            SutureWorker worker = Suture(patch, out Transform needle, out _);

            Hold(worker, needle, worker.MarkPosition(0, 0), 0.5f);

            // Under the skin across the wound to the exit mark, the way a curved needle goes.
            Vector3 entry = worker.MarkPosition(0, 0), exit = worker.MarkPosition(0, 1);
            for (int i = 0; i <= 20; i++)
            {
                needle.position = Vector3.Lerp(entry, exit, i / 20f) + Vector3.down * 0.006f;
                worker.Tick(Step);
            }

            Hold(worker, needle, exit, 0.5f);

            Assert.AreEqual(1, worker.StitchesTied);
            Assert.AreEqual(0, worker.Misses, "That is the stitch, not a stray hole.");
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
