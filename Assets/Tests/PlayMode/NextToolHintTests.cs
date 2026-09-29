using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRSurgery.Feedback;
using VRSurgery.Interaction;
using VRSurgery.Surgery;
using VRSurgery.Transplant;

namespace VRSurgery.Tests
{
    /// <summary>The arrow points at the instrument each step needs, and goes once it is in hand.</summary>
    public class NextToolHintTests
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

        private SurgicalInteractable Tool(string name, Vector3 at)
        {
            GameObject go = new GameObject(name);
            _spawned.Add(go);
            go.transform.position = at;
            return go.AddComponent<SurgicalInteractable>();
        }

        [Test]
        public void TheArrowFollowsTheOperationFromScalpelToNeedle()
        {
            GameObject host = new GameObject("Procedure");
            _spawned.Add(host);
            TransplantProcedure procedure = host.AddComponent<TransplantProcedure>();
            procedure.SetSkinStages(true);
            procedure.SetPericardiumStage(true);
            procedure.Begin();

            SurgicalInteractable scalpel = Tool("Scalpel", new Vector3(0f, 1f, 0f));
            SurgicalInteractable pen = Tool("Pen", new Vector3(0.2f, 1f, 0f));
            SurgicalInteractable saw = Tool("Saw", new Vector3(0.4f, 1f, 0f));
            SurgicalInteractable holder = Tool("Holder", new Vector3(0.6f, 1f, 0f));

            GameObject arrow = new GameObject("Arrow");
            _spawned.Add(arrow);
            NextToolHint hint = host.AddComponent<NextToolHint>();
            hint.Bind(procedure, arrow.transform, scalpel, pen, saw, holder, null, null, null);

            hint.Tick(0.1f);
            Assert.AreEqual(scalpel, hint.Target, "The operation starts with the scalpel,");
            Assert.IsTrue(arrow.activeSelf);
            Assert.AreEqual(0f, arrow.transform.position.x, 1e-4f, "and the arrow is over it.");
            Assert.Greater(arrow.transform.position.y, 1.05f);

            scalpel.OnGrabbed(null);
            hint.Tick(0.1f);
            Assert.IsFalse(arrow.activeSelf, "In the hand, there is nothing left to point at.");
            scalpel.OnReleased();

            procedure.CompleteStage(TransplantStage.SkinIncision);
            hint.Tick(0.1f);
            Assert.AreEqual(saw, hint.Target, "No bleeders known: the sternum is next.");

            procedure.CompleteStage(TransplantStage.OpenChest);
            hint.Tick(0.1f);
            Assert.AreEqual(pen, hint.Target, "The pericardium is opened with the pen.");

            procedure.CompleteStage(TransplantStage.OpenPericardium);
            hint.Tick(0.1f);
            Assert.IsNull(hint.Target, "The pump is worked with the hands: no instrument.");
            Assert.IsFalse(arrow.activeSelf);
        }
    }
}
