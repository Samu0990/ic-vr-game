using NUnit.Framework;
using VRSurgery.Transplant;

namespace VRSurgery.Tests
{
    /// <summary>
    /// The three levels, and the thing that must be true of all of them: the clinical rules do not
    /// bend.
    ///
    /// An easier level is a visitor who arrives later in the operation, not a patient the rules
    /// stopped applying to. The whole risk of a difficulty system like this is that Fácil is
    /// implemented by dropping a patient into a state the procedure could never have reached —
    /// clamped but not cannulated, arrested with no cardioplegia — so most of this file is about
    /// checking that the shortcut is a fast-forward and not a teleport.
    ///
    /// No GameObject here either: a level is a set of rules about an operation.
    /// </summary>
    public class DifficultyTests
    {
        [Test]
        public void EveryLevelStartsThePatientOnTheirOwnCirculation()
        {
            foreach (SurgicalDifficulty level in new[]
                     { SurgicalDifficulty.Facil, SurgicalDifficulty.Medio, SurgicalDifficulty.Dificil })
            {
                BypassProcedure bypass = new BypassProcedure();
                Assert.AreEqual(BypassStep.NotStarted, bypass.Step, $"{level} started mid-operation.");
            }
        }

        [Test]
        public void TheTeamsPreparationRunsThroughTheSameRules()
        {
            BypassPlan plan = BypassPlan.For(SurgicalDifficulty.Facil);
            BypassProcedure bypass = new BypassProcedure();

            BypassAttempt applied = bypass.ApplyTeamPreparation(plan.DoneByTeam);

            Assert.IsTrue(applied.Accepted, applied.Reason);
            Assert.IsTrue(bypass.IsArrested,
                "Fácil hands the visitor an arrested patient — reached properly, not assigned.");
            Assert.AreEqual(3, bypass.CompletedSteps,
                "Three steps were performed, not a state that was written in.");
        }

        [Test]
        public void APreparationThatSkipsAStepIsRefused()
        {
            BypassProcedure bypass = new BypassProcedure();

            // A level that tried to hand over a clamped but uncannulated patient.
            BypassAttempt applied = bypass.ApplyTeamPreparation(
                new[] { BypassStep.ClampAorta, BypassStep.Cardioplegia });

            Assert.IsFalse(applied.Accepted,
                "A shortcut must not be able to produce a patient the procedure could not.");
            StringAssert.Contains("Preparo da equipe inválido", applied.Reason);
            Assert.IsFalse(bypass.IsArrested);
        }

        [Test]
        public void NoLevelLetsTheTeamDoTheExitHalf()
        {
            foreach (SurgicalDifficulty level in new[]
                     { SurgicalDifficulty.Facil, SurgicalDifficulty.Medio, SurgicalDifficulty.Dificil })
            {
                BypassPlan plan = BypassPlan.For(level);
                foreach (BypassStep step in plan.DoneByTeam)
                {
                    bool isEntryHalf = step == BypassStep.Cannulate
                                    || step == BypassStep.ClampAorta
                                    || step == BypassStep.Cardioplegia;

                    Assert.IsTrue(isEntryHalf,
                        $"{level} has the team performing {step} — coming off bypass is the " +
                        "visitor's moment and no level may take it from them.");
                }
            }
        }

        [Test]
        public void EveryLevelPerformsAllSixStepsBetweenTeamAndVisitor()
        {
            foreach (SurgicalDifficulty level in new[]
                     { SurgicalDifficulty.Facil, SurgicalDifficulty.Medio, SurgicalDifficulty.Dificil })
            {
                BypassPlan plan = BypassPlan.For(level);

                int steps = plan.DoneByTeam.Count;
                foreach (BypassGesture gesture in plan.Gestures) { steps += gesture.Steps.Length; }

                Assert.AreEqual(6, steps,
                    $"{level} performs {steps} bypass steps. No level may leave one out — an " +
                    "easier operation is a shorter one for the visitor, not an incomplete one " +
                    "for the patient.");
            }
        }

        [Test]
        public void TheOrderIsTheSameAtEveryLevel()
        {
            BypassStep[] canonical =
            {
                BypassStep.Cannulate, BypassStep.ClampAorta, BypassStep.Cardioplegia,
                BypassStep.Unclamp, BypassStep.DeAir, BypassStep.Wean,
            };

            foreach (SurgicalDifficulty level in new[]
                     { SurgicalDifficulty.Facil, SurgicalDifficulty.Medio, SurgicalDifficulty.Dificil })
            {
                BypassPlan plan = BypassPlan.For(level);

                System.Collections.Generic.List<BypassStep> actual =
                    new System.Collections.Generic.List<BypassStep>(plan.DoneByTeam);
                foreach (BypassGesture gesture in plan.Gestures) { actual.AddRange(gesture.Steps); }

                CollectionAssert.AreEqual(canonical, actual,
                    $"{level} performs the steps in a different order. Collapsing gestures may " +
                    "shorten the operation; it may not rearrange it.");
            }
        }

        [Test]
        public void AChainedGestureStillPassesEveryCheck()
        {
            // Médio does the whole entry in one hold. Running its steps must still go through the
            // same validation, one at a time.
            BypassPlan plan = BypassPlan.For(SurgicalDifficulty.Medio);
            BypassGesture entry = plan.Gestures[0];
            BypassProcedure bypass = new BypassProcedure();

            foreach (BypassStep step in entry.Steps)
            {
                Assert.IsTrue(bypass.Attempt(step).Accepted, $"{step} was refused inside the chain.");
            }

            Assert.IsTrue(bypass.IsArrested);
        }

        [Test]
        public void WeaningEarlyIsRefusedAtEveryLevel()
        {
            // The rule that matters most, checked from each level's own starting point.
            foreach (SurgicalDifficulty level in new[]
                     { SurgicalDifficulty.Facil, SurgicalDifficulty.Medio, SurgicalDifficulty.Dificil })
            {
                BypassPlan plan = BypassPlan.For(level);
                BypassProcedure bypass = new BypassProcedure();
                bypass.ApplyTeamPreparation(plan.DoneByTeam);

                // Get to arrested however this level gets there.
                if (!bypass.IsArrested)
                {
                    bypass.Attempt(BypassStep.Cannulate);
                    bypass.Attempt(BypassStep.ClampAorta);
                    bypass.Attempt(BypassStep.Cardioplegia);
                }

                bypass.Attempt(BypassStep.Unclamp, implantComplete: true);

                BypassAttempt early = bypass.Attempt(BypassStep.Wean, implantComplete: true);
                Assert.IsFalse(early.Accepted, $"{level} let the patient off pump full of air.");
                StringAssert.Contains("êmbolo gasoso", early.Reason);
            }
        }

        [Test]
        public void RoundLengthComesFromTheLevel()
        {
            Assert.AreEqual(90f, BypassPlan.For(SurgicalDifficulty.Facil).RoundSeconds);
            Assert.AreEqual(150f, BypassPlan.For(SurgicalDifficulty.Medio).RoundSeconds);
            Assert.AreEqual(240f, BypassPlan.For(SurgicalDifficulty.Dificil).RoundSeconds);

            // Each level's holds have to fit inside its own round with room for the surgery.
            foreach (SurgicalDifficulty level in new[]
                     { SurgicalDifficulty.Facil, SurgicalDifficulty.Medio, SurgicalDifficulty.Dificil })
            {
                BypassPlan plan = BypassPlan.For(level);
                Assert.Less(plan.GestureSeconds, plan.RoundSeconds * 0.35f,
                    $"{level} spends {plan.GestureSeconds:F0}s of a {plan.RoundSeconds:F0}s round " +
                    "on bypass alone, which turns the pump into the game.");
            }
        }

        [Test]
        public void EasierLevelsAskForLessTime()
        {
            float easy = BypassPlan.For(SurgicalDifficulty.Facil).GestureSeconds;
            float medium = BypassPlan.For(SurgicalDifficulty.Medio).GestureSeconds;
            float hard = BypassPlan.For(SurgicalDifficulty.Dificil).GestureSeconds;

            Assert.Less(easy, medium);
            Assert.Less(medium, hard);
        }

        [Test]
        public void EveryLevelExplainsWhatWasDoneBeforeTheVisitorArrived()
        {
            foreach (SurgicalDifficulty level in new[]
                     { SurgicalDifficulty.Facil, SurgicalDifficulty.Medio, SurgicalDifficulty.Dificil })
            {
                BypassPlan plan = BypassPlan.For(level);
                Assert.IsNotEmpty(plan.Briefing,
                    $"{level} drops the visitor into an operation already in progress without " +
                    "saying so.");
            }
        }
    }
}
