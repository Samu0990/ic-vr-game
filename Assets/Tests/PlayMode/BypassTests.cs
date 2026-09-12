using NUnit.Framework;
using VRSurgery.Transplant;

namespace VRSurgery.Tests
{
    /// <summary>
    /// Cardiopulmonary bypass: the order, and what each wrong move would do to the patient.
    ///
    /// No GameObject anywhere in this file. The rules are the clinical content of the experience,
    /// and they are worth being able to check without a scene, a rig or a frame.
    /// </summary>
    public class BypassTests
    {
        private BypassProcedure _bypass;

        [SetUp]
        public void SetUp() => _bypass = new BypassProcedure();

        private void GoOnPump()
        {
            _bypass.Attempt(BypassStep.Cannulate);
            _bypass.Attempt(BypassStep.ClampAorta);
            _bypass.Attempt(BypassStep.Cardioplegia);
        }

        [Test]
        public void ThePatientStartsOnTheirOwnCirculation()
        {
            Assert.AreEqual(BypassStep.NotStarted, _bypass.Step);
            Assert.IsFalse(_bypass.IsOnPump);
            Assert.IsFalse(_bypass.IsArrested);
            Assert.AreEqual(BypassStep.Cannulate, _bypass.NextStep);
        }

        [Test]
        public void ClampingBeforeCannulatingStopsTheCirculation()
        {
            BypassAttempt attempt = _bypass.Attempt(BypassStep.ClampAorta);

            Assert.IsFalse(attempt.Accepted);
            StringAssert.Contains("sem estar em bomba", attempt.Reason);
            Assert.AreEqual(BypassStep.NotStarted, _bypass.Step,
                "A refused step must leave the patient exactly where they were.");
        }

        [Test]
        public void CardioplegiaBeforeTheClampWashesOut()
        {
            _bypass.Attempt(BypassStep.Cannulate);

            BypassAttempt attempt = _bypass.Attempt(BypassStep.Cardioplegia);

            Assert.IsFalse(attempt.Accepted);
            StringAssert.Contains("clampeada", attempt.Reason);
            Assert.IsFalse(_bypass.IsArrested, "The heart must not stop without cardioplegia.");
        }

        [Test]
        public void SkippingTheClampEntirelyIsRefused()
        {
            _bypass.Attempt(BypassStep.Cannulate);

            // Straight for the arrest, leaving the clamp out.
            Assert.IsFalse(_bypass.Attempt(BypassStep.Cardioplegia).Accepted);
            Assert.AreEqual(BypassStep.ClampAorta, _bypass.NextStep,
                "The clamp is still what comes next; a refusal does not advance anything.");
        }

        [Test]
        public void GoingOnPumpArrestsTheHeart()
        {
            int arrests = 0;
            _bypass.Arrested += () => arrests++;

            GoOnPump();

            Assert.IsTrue(_bypass.IsArrested);
            Assert.IsTrue(_bypass.IsOnPump);
            Assert.AreEqual(1, arrests, "The arrest is announced once.");
        }

        [Test]
        public void TheImplantHasToBeFinishedBeforeTheClampComesOff()
        {
            GoOnPump();

            BypassAttempt early = _bypass.Attempt(BypassStep.Unclamp, implantComplete: false);
            Assert.IsFalse(early.Accepted);
            StringAssert.Contains("anastomoses", early.Reason);

            Assert.IsTrue(_bypass.Attempt(BypassStep.Unclamp, implantComplete: true).Accepted);
        }

        [Test]
        public void WeaningBeforeDeAiringSendsAirToTheBrain()
        {
            GoOnPump();
            _bypass.Attempt(BypassStep.Unclamp, implantComplete: true);

            BypassAttempt attempt = _bypass.Attempt(BypassStep.Wean, implantComplete: true);

            Assert.IsFalse(attempt.Accepted);
            StringAssert.Contains("êmbolo gasoso", attempt.Reason);
            Assert.IsFalse(_bypass.IsOff, "The patient is still on the pump.");
        }

        [Test]
        public void DeAiringUnderTheClampDoesNothing()
        {
            GoOnPump();

            BypassAttempt attempt = _bypass.Attempt(BypassStep.DeAir, implantComplete: true);

            Assert.IsFalse(attempt.Accepted);
            StringAssert.Contains("clampeada", attempt.Reason);
        }

        [Test]
        public void TheWholeSequenceEndsOffPump()
        {
            int weanings = 0;
            _bypass.WeanedOff += () => weanings++;

            GoOnPump();
            Assert.IsTrue(_bypass.Attempt(BypassStep.Unclamp, implantComplete: true).Accepted);
            Assert.IsTrue(_bypass.Attempt(BypassStep.DeAir, implantComplete: true).Accepted);
            Assert.IsTrue(_bypass.Attempt(BypassStep.Wean, implantComplete: true).Accepted);

            Assert.IsTrue(_bypass.IsOff);
            Assert.IsFalse(_bypass.IsOnPump);
            Assert.AreEqual(_bypass.TotalSteps, _bypass.CompletedSteps);
            Assert.AreEqual(1, weanings);
        }

        [Test]
        public void NothingHappensAfterComingOffPump()
        {
            GoOnPump();
            _bypass.Attempt(BypassStep.Unclamp, implantComplete: true);
            _bypass.Attempt(BypassStep.DeAir, implantComplete: true);
            _bypass.Attempt(BypassStep.Wean, implantComplete: true);

            BypassAttempt again = _bypass.Attempt(BypassStep.Cannulate, implantComplete: true);
            Assert.IsFalse(again.Accepted);
            Assert.IsTrue(_bypass.IsOff);
        }

        [Test]
        public void EveryRefusalExplainsItself()
        {
            // A refusal without a reason is a button that did nothing, and the reason is the
            // entire clinical content of this stage.
            foreach (BypassStep step in new[]
                     { BypassStep.ClampAorta, BypassStep.Cardioplegia, BypassStep.Unclamp,
                       BypassStep.DeAir, BypassStep.Wean })
            {
                BypassAttempt attempt = new BypassProcedure().Attempt(step);
                Assert.IsFalse(attempt.Accepted, $"{step} should be refused first thing.");
                Assert.IsNotEmpty(attempt.Reason, $"{step} was refused without saying why.");
            }
        }

        [Test]
        public void AResetPutsTheNextPatientBackOnTheirOwnCirculation()
        {
            GoOnPump();
            _bypass.Reset();

            Assert.AreEqual(BypassStep.NotStarted, _bypass.Step);
            Assert.AreEqual(0, _bypass.CompletedSteps);
            Assert.IsFalse(_bypass.IsOnPump);
        }
    }
}
