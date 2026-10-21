using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRSurgery.Transplant;

namespace VRSurgery.Tests
{
    /// <summary>
    /// The ring on the pump site, and the arrow and label floating over it.
    /// Driven by hand with fixed steps; the viewer is a bare transform standing in for the head.
    /// </summary>
    public class BypassSiteMarkersTests
    {
        private const float Step = 1f / 60f;

        private readonly List<GameObject> _spawned = new List<GameObject>();

        private TransplantProcedure _procedure;
        private BypassSiteMarkers _markers;
        private Transform _arrow;
        private Transform _labelRoot;
        private Transform _head;
        private Transform _first;
        private Transform _second;

        [SetUp]
        public void SetUp()
        {
            _procedure = Spawn("Procedure").AddComponent<TransplantProcedure>();
            _procedure.SetSkinStages(true);

            _first = Spawn("SiteA").transform;
            _first.position = new Vector3(0f, 1f, 0f);
            _second = Spawn("SiteB").transform;
            _second.position = new Vector3(0.1f, 1f, 0.05f);

            BypassWorker worker = Spawn("Worker").AddComponent<BypassWorker>();
            worker.Bind(null, new List<BypassSite>
            {
                new BypassSite { Step = BypassStep.Cannulate, Point = _first },
                new BypassSite { Step = BypassStep.ClampAorta, Point = _second },
            }, _procedure);

            Renderer ringA = Ring(_first);
            Renderer ringB = Ring(_second);

            _arrow = Spawn("Arrow").transform;
            _labelRoot = Spawn("Label").transform;
            TextMesh text = _labelRoot.gameObject.AddComponent<TextMesh>();
            _head = Spawn("Head").transform;
            _head.position = new Vector3(0f, 1.6f, -0.7f);

            _markers = Spawn("Markers").AddComponent<BypassSiteMarkers>();
            _markers.Bind(_procedure, worker, new[] { ringA, ringB });
            _markers.BindBeacon(_arrow, _labelRoot, text, _head);
        }

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

        private Renderer Ring(Transform site)
        {
            GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _spawned.Add(ring);
            ring.transform.SetParent(site, false);
            ring.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            return ring.GetComponent<Renderer>();
        }

        private void ToThePump()
        {
            _procedure.Begin();
            _procedure.CompleteStage(TransplantStage.SkinIncision);
            _procedure.CompleteStage(TransplantStage.OpenChest);
            Assert.AreEqual(TransplantStage.GoOnBypass, _procedure.Stage, "test setup: on the pump");
        }

        [Test]
        public void NothingShowsBeforeThePump()
        {
            _procedure.Begin();
            _markers.Tick(Step);

            Assert.IsFalse(_markers.IsShowing(0));
            Assert.IsFalse(_markers.IsBeaconShowing);
            Assert.IsEmpty(_markers.LabelText);
        }

        [Test]
        public void TheArrowAndLabelFloatOverTheActiveRing()
        {
            ToThePump();
            _markers.Tick(Step);

            Assert.IsTrue(_markers.IsShowing(0));
            Assert.IsFalse(_markers.IsShowing(1), "Only the step that is next.");
            Assert.IsTrue(_markers.IsBeaconShowing);
            Assert.Greater(_arrow.position.y, _first.position.y, "The arrow points down from above the ring.");
            Assert.Greater(_labelRoot.position.y, _arrow.position.y, "The label sits above the arrow.");
            Assert.AreEqual(_first.position.x, _arrow.position.x, 1e-4f);
        }

        [Test]
        public void TheLabelNamesTheStepAndItsNumber()
        {
            ToThePump();
            _markers.Tick(Step);

            StringAssert.Contains("1/6", _markers.LabelText);
            StringAssert.Contains("Conectar a máquina", _markers.LabelText);
            StringAssert.Contains("Segure a mão", _markers.LabelText);
        }

        [Test]
        public void TheLabelTurnsItsReadableSideToTheViewer()
        {
            ToThePump();
            _markers.Tick(Step);

            // The card's +Z points away from the viewer.
            Vector3 away = (_labelRoot.position - _head.position).normalized;
            Assert.Greater(Vector3.Dot(_labelRoot.forward, away), 0.99f);
        }

        [Test]
        public void TheBeaconMovesOnWhenTheStepIsDone()
        {
            ToThePump();
            _markers.Tick(Step);

            Assert.IsTrue(_procedure.Bypass.Attempt(BypassStep.Cannulate).Accepted);
            _markers.Tick(Step);

            Assert.IsFalse(_markers.IsShowing(0));
            Assert.IsTrue(_markers.IsShowing(1));
            Assert.AreEqual(_second.position.x, _arrow.position.x, 1e-4f);
            StringAssert.Contains("2/6", _markers.LabelText);
            StringAssert.Contains("Fechar o clampe", _markers.LabelText);
        }

        [Test]
        public void TheRingNeverFadesAwayInItsPulse()
        {
            ToThePump();
            Renderer ring = _first.GetChild(0).GetComponent<Renderer>();

            float lowest = float.MaxValue;
            for (int i = 0; i < 180; i++)
            {
                _markers.Tick(Step);
                lowest = Mathf.Min(lowest, ring.sharedMaterial.color.a);
            }

            Assert.GreaterOrEqual(lowest, 0.6f, "A ring that drops to a tenth of its alpha is lost against the tissue.");
        }

        [Test]
        public void EveryStepHasAPlainName()
        {
            foreach (BypassStep step in new[]
                     {
                         BypassStep.Cannulate, BypassStep.ClampAorta, BypassStep.Cardioplegia,
                         BypassStep.Unclamp, BypassStep.DeAir, BypassStep.Wean,
                     })
            {
                StringAssert.DoesNotContain("Próximo passo", BypassSiteMarkers.LabelFor(step, 1, 6), step.ToString());
            }
        }
    }
}
