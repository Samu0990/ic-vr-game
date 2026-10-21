using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRSurgery.Feedback;
using VRSurgery.Transplant;

namespace VRSurgery.Tests
{
    /// <summary>
    /// The card that tells the visitor what to do, in front of them, at each step.
    /// Driven by hand with fixed steps; the viewer is a bare transform standing in for the head.
    /// </summary>
    public class StageInstructionModalTests
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
        }

        private GameObject Spawn(string name)
        {
            GameObject go = new GameObject(name);
            _spawned.Add(go);
            return go;
        }

        private TransplantProcedure Procedure()
        {
            TransplantProcedure procedure = Spawn("Procedure").AddComponent<TransplantProcedure>();
            procedure.SetSkinStages(true);
            return procedure;
        }

        private StageInstructionModal Modal(TransplantProcedure procedure, Transform viewer, out Transform body)
        {
            return Modal(procedure, viewer, out body, out _);
        }

        private StageInstructionModal Modal(TransplantProcedure procedure, Transform viewer, out Transform body,
            out Transform plate)
        {
            GameObject root = Spawn("Modal");
            body = new GameObject("Corpo").transform;
            body.SetParent(root.transform, false);
            plate = new GameObject("Placa").transform;
            plate.SetParent(body, false);
            plate.localScale = new Vector3(0.4f, 0.2f, 0.002f);
            StageInstructionModal modal = root.AddComponent<StageInstructionModal>();
            modal.Bind(body, plate, null, null, null, procedure, null, viewer);
            return modal;
        }

        private Transform Viewer(Vector3 position, float yaw, float pitch)
        {
            Transform head = Spawn("Head").transform;
            head.SetPositionAndRotation(position, Quaternion.Euler(pitch, yaw, 0f));
            return head;
        }

        private static void Run(StageInstructionModal modal, float seconds)
        {
            for (float t = 0f; t < seconds; t += Step) { modal.Tick(Step); }
        }

        [Test]
        public void ShowsTheInstructionOfTheStepThatBegins()
        {
            TransplantProcedure procedure = Procedure();
            StageInstructionModal modal = Modal(procedure, Viewer(new Vector3(0f, 1.6f, 0f), 0f, 0f), out Transform body);

            procedure.Begin();
            modal.Tick(Step);

            Assert.IsTrue(modal.IsShowing);
            Assert.IsTrue(body.gameObject.activeSelf);
            Assert.AreEqual(procedure.CurrentInstruction, modal.ShownText,
                "The card says what the monitor says; it owns no wording of its own.");
        }

        [Test]
        public void GoesAwayByItselfAndStaysAway()
        {
            TransplantProcedure procedure = Procedure();
            StageInstructionModal modal = Modal(procedure, Viewer(new Vector3(0f, 1.6f, 0f), 0f, 0f), out Transform body);
            procedure.Begin();

            Run(modal, 8f);

            Assert.IsFalse(modal.IsShowing);
            Assert.IsFalse(body.gameObject.activeSelf);
            Assert.AreEqual(string.Empty, modal.ShownText);
        }

        [Test]
        public void ComesBackWhenTheStepChanges()
        {
            TransplantProcedure procedure = Procedure();
            StageInstructionModal modal = Modal(procedure, Viewer(new Vector3(0f, 1.6f, 0f), 0f, 0f), out _);
            procedure.Begin();
            Run(modal, 8f);

            procedure.CompleteStage(TransplantStage.SkinIncision);
            modal.Tick(Step);

            Assert.IsTrue(modal.IsShowing);
            Assert.AreEqual(TransplantStage.OpenChest, procedure.Stage);
            Assert.AreEqual(procedure.CurrentInstruction, modal.ShownText);
        }

        [Test]
        public void SitsInFrontOfWhereTheVisitorIsLookingAndFacesThem()
        {
            TransplantProcedure procedure = Procedure();
            // Turned 90° to the right and looking down at the table: the card must still be level
            // in front of the face, not on the floor.
            Transform head = Viewer(new Vector3(1f, 1.6f, 2f), 90f, 40f);
            StageInstructionModal modal = Modal(procedure, head, out _);

            procedure.Begin();
            modal.Tick(Step);

            Vector3 offset = modal.transform.position - head.position;
            Assert.AreEqual(1.1f, new Vector2(offset.x, offset.z).magnitude, 0.01f);
            Assert.Greater(offset.x, 1f, "In the direction the head is turned (+X).");
            Assert.Less(offset.y, 0f, "A little below the eyes,");
            Assert.Greater(offset.y, -0.2f, "never on the floor.");
            Assert.Greater(Vector3.Dot(modal.transform.forward, Vector3.right), 0.99f,
                "+Z points away from the viewer, so the readable side faces them.");
        }

        [Test]
        public void StaysWhereItAppearedWhenTheHeadMoves()
        {
            TransplantProcedure procedure = Procedure();
            Transform head = Viewer(new Vector3(0f, 1.6f, 0f), 0f, 0f);
            StageInstructionModal modal = Modal(procedure, head, out _);
            procedure.Begin();
            modal.Tick(Step);
            Vector3 placed = modal.transform.position;

            head.SetPositionAndRotation(new Vector3(0.5f, 1.5f, 0.3f), Quaternion.Euler(0f, 120f, 0f));
            Run(modal, 1f);

            Assert.AreEqual(placed, modal.transform.position,
                "A panel that follows the head is the one thing this card must not be.");
        }

        [Test]
        public void NothingIsShownBeforeTheOperationStartsOrAfterItEnds()
        {
            TransplantProcedure procedure = Procedure();
            StageInstructionModal modal = Modal(procedure, Viewer(new Vector3(0f, 1.6f, 0f), 0f, 0f), out _);

            modal.Tick(Step);
            Assert.IsFalse(modal.IsShowing, "Idle: nothing to say yet.");

            procedure.Begin();
            modal.Tick(Step);
            Assert.IsTrue(modal.IsShowing);

            procedure.ResetProcedure();
            modal.Tick(Step);
            Assert.IsFalse(modal.IsShowing, "The patient reset for the next visitor takes the card with it.");
        }

        [Test]
        public void WrapBreaksOnWordsWithinTheWidth()
        {
            string text = "Pegue o bisturi e corte a pele sobre a linha roxa";
            string[] lines = StageInstructionModal.WrapToWidth(text, 0.4f, s => s.Length * 0.02f);

            Assert.Greater(lines.Length, 1);
            foreach (string line in lines)
            {
                Assert.LessOrEqual(line.Length * 0.02f, 0.4f, line);
            }

            Assert.AreEqual(text, string.Join(" ", lines));
            Assert.AreEqual(0, StageInstructionModal.WrapToWidth(null, 0.4f, s => s.Length * 0.02f).Length);
        }

        [Test]
        public void EveryInstructionFitsInsideItsSolidPlate()
        {
            TransplantProcedure procedure = Procedure();
            StageInstructionModal modal = Modal(procedure, Viewer(new Vector3(0f, 1.6f, 0f), 0f, 0f),
                out _, out Transform plate);

            procedure.Begin();
            modal.Tick(Step);

            Assert.AreEqual(modal.PlateSize.x, plate.localScale.x, 1e-5f, "The plate takes the size the layout asks for.");
            Assert.AreEqual(modal.PlateSize.y, plate.localScale.y, 1e-5f);
            Assert.LessOrEqual(modal.WidestLine + 0.04f, modal.PlateSize.x,
                "The first instruction used to run off the edges of the card.");

            procedure.CompleteStage(TransplantStage.SkinIncision);
            modal.Tick(Step);
            Assert.LessOrEqual(modal.WidestLine + 0.04f, modal.PlateSize.x);
        }

        [Test]
        public void ALongerInstructionGetsATallerPlate()
        {
            TransplantProcedure procedure = Procedure();
            StageInstructionModal modal = Modal(procedure, Viewer(new Vector3(0f, 1.6f, 0f), 0f, 0f), out _);

            procedure.Begin();
            modal.Tick(Step);
            float first = modal.PlateSize.y;

            procedure.CompleteStage(TransplantStage.SkinIncision);
            modal.Tick(Step);

            Assert.Greater(modal.PlateSize.y, first,
                "The sternum instruction is the longer sentence and needs more lines.");
        }
    }
}
