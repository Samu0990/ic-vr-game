using NUnit.Framework;
using UnityEngine;
using VRSurgery.Transplant;

namespace VRSurgery.Tests
{
    /// <summary>
    /// The order of a heart transplant, which is the part of it this experience is teaching.
    ///
    /// The sequence is not decoration: you cannot lift a heart out of a closed chest, and a heart
    /// sitting in place with nothing connected is not a transplant. These check that the
    /// operation cannot be performed out of order or skipped forward by an instrument that fires
    /// twice — which at a stand, with a first-timer waving a controller, it will.
    /// </summary>
    public class TransplantTests
    {
        private GameObject _host;
        private TransplantProcedure _procedure;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("Transplant");
            _procedure = _host.AddComponent<TransplantProcedure>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) { Object.DestroyImmediate(_host); }
        }

        /// <summary>
        /// Onto the pump. Every stage past the sternotomy is now behind it, because a patient
        /// whose heart is about to come out has to be on bypass first.
        /// </summary>
        private void GoOnPump()
        {
            _procedure.Bypass.Attempt(BypassStep.Cannulate);
            _procedure.Bypass.Attempt(BypassStep.ClampAorta);
            _procedure.Bypass.Attempt(BypassStep.Cardioplegia);
            _procedure.CompleteStage(TransplantStage.GoOnBypass);
        }

        /// <summary>Off the pump, which is what the restart stage now consists of.</summary>
        private void ComeOffPump()
        {
            _procedure.Bypass.Attempt(BypassStep.Unclamp, true);
            _procedure.Bypass.Attempt(BypassStep.DeAir, true);
            _procedure.Bypass.Attempt(BypassStep.Wean, true);
        }

        [Test]
        public void NothingHappensBeforeTheOperationStarts()
        {
            Assert.AreEqual(TransplantStage.Idle, _procedure.Stage);
            Assert.AreEqual(0f, _procedure.Progress01);
            Assert.IsFalse(_procedure.CompleteStage(TransplantStage.OpenChest),
                "A stage cannot be finished before the operation has begun.");
        }

        [Test]
        public void TheOperationRunsInOrder()
        {
            _procedure.Begin();
            Assert.AreEqual(TransplantStage.OpenChest, _procedure.Stage);

            Assert.IsTrue(_procedure.CompleteStage(TransplantStage.OpenChest));
            Assert.AreEqual(TransplantStage.GoOnBypass, _procedure.Stage,
                "The pump comes before the heart does.");

            GoOnPump();
            Assert.AreEqual(TransplantStage.RemoveNativeHeart, _procedure.Stage);

            Assert.IsTrue(_procedure.CompleteStage(TransplantStage.RemoveNativeHeart));
            Assert.AreEqual(TransplantStage.PlaceDonorHeart, _procedure.Stage);

            Assert.IsTrue(_procedure.CompleteStage(TransplantStage.PlaceDonorHeart));
            Assert.AreEqual(TransplantStage.ConnectVessels, _procedure.Stage);
        }

        [Test]
        public void StagesCannotBeSkipped()
        {
            _procedure.Begin();

            // Reaching for the donor heart while the chest is still shut.
            Assert.IsFalse(_procedure.CompleteStage(TransplantStage.PlaceDonorHeart));
            Assert.AreEqual(TransplantStage.OpenChest, _procedure.Stage,
                "Skipping ahead must leave the operation exactly where it was.");
        }

        [Test]
        public void AStageFiringTwiceDoesNotAdvanceTwice()
        {
            _procedure.Begin();
            Assert.IsTrue(_procedure.CompleteStage(TransplantStage.OpenChest));

            // The saw reports again — a controller held down, or a second collision.
            Assert.IsFalse(_procedure.CompleteStage(TransplantStage.OpenChest));
            Assert.AreEqual(TransplantStage.GoOnBypass, _procedure.Stage);
        }

        [Test]
        public void EveryVesselHasToBeConnected()
        {
            _procedure.Begin();
            _procedure.CompleteStage(TransplantStage.OpenChest);
            GoOnPump();
            _procedure.CompleteStage(TransplantStage.RemoveNativeHeart);
            _procedure.CompleteStage(TransplantStage.PlaceDonorHeart);

            Assert.AreEqual(TransplantStage.ConnectVessels, _procedure.Stage);

            for (int i = 1; i < _procedure.VesselCount; i++)
            {
                Assert.IsTrue(_procedure.ConnectVessel());
                Assert.AreEqual(TransplantStage.ConnectVessels, _procedure.Stage,
                    "The implant is not finished while a vessel is still open.");
            }

            Assert.IsTrue(_procedure.ConnectVessel());
            Assert.AreEqual(TransplantStage.Restart, _procedure.Stage);
            Assert.IsFalse(_procedure.ConnectVessel(), "There is no sixth vessel.");
        }

        [Test]
        public void TheOperationFinishesOnceAndReportsIt()
        {
            int completions = 0;
            _procedure.ProcedureCompleted += () => completions++;

            _procedure.Begin();
            _procedure.CompleteStage(TransplantStage.OpenChest);
            GoOnPump();
            _procedure.CompleteStage(TransplantStage.RemoveNativeHeart);
            _procedure.CompleteStage(TransplantStage.PlaceDonorHeart);
            for (int i = 0; i < _procedure.VesselCount; i++) { _procedure.ConnectVessel(); }
            ComeOffPump();
            _procedure.CompleteStage(TransplantStage.Restart);

            Assert.IsTrue(_procedure.IsComplete);
            Assert.AreEqual(1f, _procedure.Progress01);
            Assert.AreEqual(1, completions);
        }

        [Test]
        public void ProgressRisesThroughTheImplantRatherThanJumping()
        {
            _procedure.Begin();
            _procedure.CompleteStage(TransplantStage.OpenChest);
            GoOnPump();
            _procedure.CompleteStage(TransplantStage.RemoveNativeHeart);
            _procedure.CompleteStage(TransplantStage.PlaceDonorHeart);

            float before = _procedure.Progress01;
            _procedure.ConnectVessel();

            Assert.Greater(_procedure.Progress01, before,
                "Each vessel has to move the audience's progress bar, or the longest stage of " +
                "the operation reads as the screen having frozen.");
        }

        [Test]
        public void ANewVisitorGetsAClosedChest()
        {
            _procedure.Begin();
            _procedure.CompleteStage(TransplantStage.OpenChest);
            GoOnPump();
            _procedure.CompleteStage(TransplantStage.RemoveNativeHeart);

            _procedure.ResetProcedure();

            Assert.AreEqual(TransplantStage.Idle, _procedure.Stage);
            Assert.AreEqual(0, _procedure.VesselsConnected);
        }
    }
}
