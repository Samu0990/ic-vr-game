using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRSurgery.Interaction;
using VRSurgery.Surgery;
using VRSurgery.Transplant;

namespace VRSurgery.Tests
{
    /// <summary>
    /// The way out of the chest. A heart comes out between the halves of the sternum, never
    /// through a rib, and nothing in the scene stops an organ from being carried through bone —
    /// so the gate is the only thing that does. These build the gate and a heart by hand, so the
    /// rule is checked on any machine and not only against the generated scene.
    /// </summary>
    public class ChestExitGateTests
    {
        private GameObject _host;
        private SternotomyController _sternotomy;
        private ChestExitGate _gate;
        private GrabbableOrgan _organ;
        private SurgicalInteractable _interactable;
        private TransplantProcedure _procedure;
        private Vector3 _seat;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("Host");
            _procedure = _host.AddComponent<TransplantProcedure>();
            _procedure.SetSkinStages(false);
            _procedure.Begin();

            GameObject sternum = new GameObject("Sternum");
            sternum.transform.SetParent(_host.transform, false);
            _sternotomy = sternum.AddComponent<SternotomyController>();

            GameObject gate = new GameObject("Gate");
            gate.transform.SetParent(_host.transform, false);
            gate.transform.position = new Vector3(0f, 0.3f, 0f);
            _gate = gate.AddComponent<ChestExitGate>();
            _gate.Bind(_sternotomy, 0.05f, 0.1f);

            _seat = new Vector3(0f, 0f, 0f);
            GameObject seat = new GameObject("Seat");
            seat.transform.SetParent(_host.transform, false);
            seat.transform.position = _seat;

            GameObject heart = new GameObject("Heart");
            heart.transform.SetParent(_host.transform, false);
            heart.transform.position = _seat;
            _organ = heart.AddComponent<GrabbableOrgan>();
            _interactable = heart.GetComponent<SurgicalInteractable>();
            heart.GetComponent<Rigidbody>().isKinematic = true;
            _organ.Bind(OrganRole.Native, seat.transform, _procedure, _gate);

            // The native heart is only judged once the operation reaches its stage.
            _procedure.CompleteStage(TransplantStage.OpenChest);
            _procedure.Bypass.Attempt(BypassStep.Cannulate);
            _procedure.Bypass.Attempt(BypassStep.ClampAorta);
            _procedure.Bypass.Attempt(BypassStep.Cardioplegia);
            _procedure.CompleteStage(TransplantStage.GoOnBypass);
            Assert.AreEqual(TransplantStage.RemoveNativeHeart, _procedure.Stage);
        }

        [TearDown]
        public void TearDown()
        {
            SurgeryEvents.ResetAll();
            if (_host != null) { Object.DestroyImmediate(_host); }
        }

        private void OpenChest()
        {
            _sternotomy.Open();
            _sternotomy.Tick(10f);
        }

        private IEnumerator Move(Vector3 position)
        {
            _organ.transform.position = position;
            yield return null;
        }

        [Test]
        public void AClosedChestHasNoOpening()
        {
            Assert.IsFalse(_gate.Admits(_gate.transform.position));

            OpenChest();

            Assert.IsTrue(_gate.Admits(_gate.transform.position));
            Assert.IsFalse(_gate.Admits(_gate.transform.position + new Vector3(0.2f, 0f, 0f)),
                "Beside the opening is rib, not opening.");
            Assert.IsFalse(_gate.Admits(_gate.transform.position + new Vector3(0f, 0f, 0.3f)),
                "Past the end of the sternum is not opening either.");
        }

        [UnityTest]
        public IEnumerator LiftedThroughTheOpeningTheHeartIsExplanted()
        {
            OpenChest();
            _interactable.OnGrabbed(null);
            yield return null;

            yield return Move(new Vector3(0.01f, 0.4f, 0.02f));
            yield return Move(new Vector3(0.6f, 0.4f, 0f));

            _interactable.OnReleased();
            yield return null;

            Assert.IsTrue(_organ.Satisfied);
            Assert.AreEqual(TransplantStage.PlaceDonorHeart, _procedure.Stage);
        }

        [UnityTest]
        public IEnumerator CarriedThroughTheRibsItIsSentBackAndTheStageStays()
        {
            OpenChest();
            _interactable.OnGrabbed(null);
            yield return null;

            yield return Move(new Vector3(0.3f, 0.5f, 0f));
            yield return Move(new Vector3(0.6f, 0.4f, 0f));

            _interactable.OnReleased();
            yield return null;
            yield return null;
            yield return null;

            Assert.IsFalse(_organ.Satisfied);
            Assert.AreEqual(TransplantStage.RemoveNativeHeart, _procedure.Stage);
            Assert.AreEqual(0f, Vector3.Distance(_organ.transform.position, _seat), 0.001f,
                "Barred by the ribs, the heart returns to its seat.");
        }

        [UnityTest]
        public IEnumerator HitTheRibsAndTheRoomSaysWhy()
        {
            OpenChest();
            string heard = null;
            SurgeryEvents.OnError += (severity, message) => heard = message;

            _interactable.OnGrabbed(null);
            yield return null;
            yield return Move(new Vector3(0.3f, 0.5f, 0f));

            Assert.AreEqual(ChestExitGate.BlockedMessage, heard);
        }

        [UnityTest]
        public IEnumerator GoingBackInAndOutThroughTheOpeningClearsAnEarlierRibHit()
        {
            OpenChest();
            _interactable.OnGrabbed(null);
            yield return null;

            yield return Move(new Vector3(0.3f, 0.5f, 0f));
            yield return Move(new Vector3(0.3f, -0.1f, 0f));
            yield return Move(new Vector3(0f, 0.4f, 0f));
            yield return Move(new Vector3(0.6f, 0.4f, 0f));

            _interactable.OnReleased();
            yield return null;

            Assert.IsTrue(_organ.Satisfied,
                "A surgeon who tries the ribs, backs off and takes the heart out properly has done it right.");
        }

        [UnityTest]
        public IEnumerator WithoutAGateTheOldDistanceRuleStillApplies()
        {
            _organ.Bind(OrganRole.Native, new GameObject("Seat2").transform, _procedure, null);
            _interactable.OnGrabbed(null);
            yield return null;

            yield return Move(new Vector3(0.6f, 0.1f, 0f));
            _interactable.OnReleased();
            yield return null;

            Assert.IsTrue(_organ.Satisfied);
        }
    }
}
