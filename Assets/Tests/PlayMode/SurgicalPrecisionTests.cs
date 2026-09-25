using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRSurgery.Surgery;
using VRSurgery.Tissue;

namespace VRSurgery.Tests
{
    /// <summary>
    /// The accuracy the procedure demands, pinned down.
    ///
    /// These exist because every number here was once loose enough to be meaningless: the failure
    /// band was wider than half the incision, and pressure counted anywhere on a 14x10 cm patch.
    /// Both passed every test in the suite. Precision that nothing asserts is precision that drifts
    /// back out the next time someone tunes a feel problem.
    /// </summary>
    public class SurgicalPrecisionTests
    {
        private GameObject _host;
        private TissueSurface _tissue;
        private IncisionGuide _guide;

        /// <summary>The guided incision the scene builds: 9 cm along local X.</summary>
        private const float GuideHalfLength = 0.045f;

        [SetUp]
        public void SetUp()
        {
            SurgeryEvents.ResetAll();

            _host = new GameObject("Region");
            _host.SetActive(false);
            _tissue = _host.AddComponent<TissueSurface>();
            _tissue.Configure(new Vector2(0.07f, 0.05f), 0.02f);

            GameObject guideHost = new GameObject("Guide");
            guideHost.transform.SetParent(_host.transform, false);
            _guide = guideHost.AddComponent<IncisionGuide>();
            _host.SetActive(true);

            _guide.Configure(new Vector3(-GuideHalfLength, 0f, 0f), new Vector3(GuideHalfLength, 0f, 0f));
            _guide.SetTolerances(0.0015f, 0.004f, 0.010f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) { Object.DestroyImmediate(_host); }
            SurgeryEvents.ResetAll();
        }

        [Test]
        public void TheFailureBandIsSmallAgainstTheIncisionItself()
        {
            // The regression this guards: a failure tolerance of 5 cm on a 9 cm path, which let a
            // cut run nearly perpendicular to the guide and still pass.
            Assert.Less(_guide.FailureTolerance, GuideHalfLength * 0.25f,
                "A cut is only guided if missing is possible. The failure band must stay small " +
                "relative to the length of the line being followed.");
        }

        [Test]
        public void ACleanLineScoresAndAWanderingOneDoesNot()
        {
            // 1 mm off the guided line: within a surgeon's hand.
            Assert.AreEqual(1f, _guide.ScoreDeviation(_guide.DeviationAt(new Vector3(0f, 0f, 0.001f))), 0.001f);

            // 6 mm off: past acceptable, still scoring something.
            float sloppy = _guide.ScoreDeviation(_guide.DeviationAt(new Vector3(0f, 0f, 0.006f)));
            Assert.Greater(sloppy, 0f);
            Assert.Less(sloppy, 0.6f);

            // 1.5 cm off: nowhere near the line.
            Assert.AreEqual(0f, _guide.ScoreDeviation(_guide.DeviationAt(new Vector3(0f, 0f, 0.015f))), 0.001f,
                "Fifteen millimetres from a guided incision is a miss, not a rough attempt.");
        }

        [Test]
        public void PressingAwayFromTheWoundIsNotPressingOnIt()
        {
            // A cut down the middle of the guide.
            _tissue.ApplyIncision(new Vector3(-0.02f, 0f, 0f), 0.9f);
            _tissue.ApplyIncision(new Vector3(0.02f, 0f, 0f), 0.9f);

            float onWound = _tissue.DistanceToIncision(_tissue.TissueLocalToWorld(new Vector3(0f, 0f, 0f)));
            Assert.Less(onWound, 0.001f, "A point on the cut line is on the wound.");

            // Still inside the 14x10 cm tissue collider, but 4 cm from the incision. This is the
            // case that used to control the bleeding just as well as pressing on it.
            float acrossTheField = _tissue.DistanceToIncision(_tissue.TissueLocalToWorld(new Vector3(0f, 0f, 0.04f)));
            Assert.Greater(acrossTheField, 0.03f,
                "Touching the operative field somewhere else is not applying pressure to the wound.");
        }

        [Test]
        public void AnUntouchedPatientHasNoWoundToPressOn()
        {
            Assert.IsTrue(float.IsPositiveInfinity(_tissue.DistanceToIncision(Vector3.zero)),
                "With no incision recorded there is nothing to be near; returning zero would make " +
                "an intact patient read as a hit.");
        }

        [UnityTest]
        public IEnumerator BleedingGrowsSmoothlyInsteadOfJumping()
        {
            GameObject bleedHost = new GameObject("Bleeder");
            bleedHost.SetActive(false);
            TissueSurface tissue = bleedHost.AddComponent<TissueSurface>();
            tissue.Configure(new Vector2(0.07f, 0.05f), 0.02f);
            bleedHost.AddComponent<BleedingSystem>();
            bleedHost.SetActive(true);

            BleedingSystem bleeding = bleedHost.GetComponent<BleedingSystem>();

            // Walk the blade deeper in small steps, letting the system's own Update run between
            // them, and collect the intensity at each one.
            float previous = -1f;
            int distinctValues = 0;
            for (float depth = 0.55f; depth <= 1.0001f; depth += 0.05f)
            {
                tissue.ApplyIncision(new Vector3(depth * 0.02f, 0f, 0f), depth);
                yield return null;

                if (!Mathf.Approximately(bleeding.Intensity01, previous))
                {
                    distinctValues++;
                    previous = bleeding.Intensity01;
                }
            }

            // The old model produced exactly three values across this entire sweep.
            Assert.Greater(distinctValues, 4,
                "The concept asks for a bar that grows. Three hardcoded steps make it pop between " +
                "three positions instead.");

            Object.DestroyImmediate(bleedHost);
        }

        [UnityTest]
        public IEnumerator LeavingTheWoundAloneMakesItWorse()
        {
            GameObject bleedHost = new GameObject("Bleeder");
            bleedHost.SetActive(false);
            TissueSurface tissue = bleedHost.AddComponent<TissueSurface>();
            tissue.Configure(new Vector2(0.07f, 0.05f), 0.02f);
            bleedHost.AddComponent<BleedingSystem>();
            bleedHost.SetActive(true);

            BleedingSystem bleeding = bleedHost.GetComponent<BleedingSystem>();

            tissue.ApplyIncision(new Vector3(-0.01f, 0f, 0f), 0.75f);
            tissue.ApplyIncision(new Vector3(0.01f, 0f, 0f), 0.75f);
            yield return null;

            float atFirst = bleeding.Intensity01;

            // Hesitate. Depth has not changed, so under the old model nothing would.
            for (int i = 0; i < 30; i++)
            {
                yield return null;
            }

            Assert.Greater(bleeding.UncontrolledSeconds, 0f);
            Assert.Greater(bleeding.Intensity01, atFirst,
                "A wound left unanswered has to get worse, or the clock is the only thing in the " +
                "room applying any pressure.");

            Object.DestroyImmediate(bleedHost);
        }
    }
}
