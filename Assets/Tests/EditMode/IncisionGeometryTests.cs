using NUnit.Framework;
using UnityEngine;
using VRSurgery.Surgery;
using VRSurgery.Tissue;

namespace VRSurgery.Tests
{
    /// <summary>
    /// Unit coverage for the cut maths. These run without a scene precisely because the
    /// tunnelling and depth rules are the parts most likely to break silently.
    /// </summary>
    public class IncisionGeometryTests
    {
        private static readonly Vector2 Extents = new Vector2(0.09f, 0.06f);
        private const float MaxPenetration = 0.02f;

        [Test]
        public void Sweep_EntirelyAboveSurface_ReportsNoContact()
        {
            IncisionGeometry.SweepResult result = IncisionGeometry.Sweep(
                new Vector3(0f, 0.05f, 0f), new Vector3(0.01f, 0.04f, 0f), Extents, MaxPenetration);

            Assert.IsFalse(result.InContact);
            Assert.IsFalse(result.CrossedSurface);
        }

        [Test]
        public void Sweep_CrossingSurface_ReportsContactAndEntryPoint()
        {
            IncisionGeometry.SweepResult result = IncisionGeometry.Sweep(
                new Vector3(0f, 0.01f, 0f), new Vector3(0f, -0.01f, 0f), Extents, MaxPenetration);

            Assert.IsTrue(result.InContact);
            Assert.IsTrue(result.CrossedSurface);
            Assert.AreEqual(0f, result.SurfacePoint.y, 1e-5f);
            Assert.AreEqual(0.01f, result.Penetration, 1e-5f);
            Assert.AreEqual(0.5f, result.Depth01, 1e-4f);
        }

        /// <summary>
        /// The reason cut detection is swept rather than sampled: a blade moving fast enough to
        /// jump the whole sheet in one physics step must still register.
        /// </summary>
        [Test]
        public void Sweep_FastMovementThroughSurface_StillDetectsContact()
        {
            // 1.2 m in a single step — far beyond any per-frame sample-based check.
            IncisionGeometry.SweepResult result = IncisionGeometry.Sweep(
                new Vector3(0f, 0.6f, 0f), new Vector3(0f, -0.6f, 0f), Extents, MaxPenetration);

            Assert.IsTrue(result.CrossedSurface, "Fast segment tunnelled straight through the tissue.");
            Assert.IsTrue(result.InContact);
            Assert.AreEqual(1f, result.Depth01, 1e-4f, "Penetration past the max should clamp to full depth.");
        }

        [Test]
        public void Sweep_OutsideSheetBounds_ReportsNoContact()
        {
            IncisionGeometry.SweepResult result = IncisionGeometry.Sweep(
                new Vector3(0.5f, 0.01f, 0f), new Vector3(0.5f, -0.01f, 0f), Extents, MaxPenetration);

            Assert.IsFalse(result.InContact, "A cut well outside the tissue must not register.");
        }

        [Test]
        public void Sweep_EndingAboveSurface_ReportsNoContact()
        {
            // Dipped in and came back out within one step: nothing to record this step.
            IncisionGeometry.SweepResult result = IncisionGeometry.Sweep(
                new Vector3(0f, -0.005f, 0f), new Vector3(0f, 0.005f, 0f), Extents, MaxPenetration);

            Assert.IsFalse(result.InContact);
        }

        [Test]
        public void EvaluateCutSpeed_BelowMinimum_IsZero()
        {
            Assert.AreEqual(0f, IncisionGeometry.EvaluateCutSpeed(0.005f, 0.02f, 1.2f), 1e-5f);
        }

        [Test]
        public void EvaluateCutSpeed_AboveMaximum_IsZero()
        {
            Assert.AreEqual(0f, IncisionGeometry.EvaluateCutSpeed(3f, 0.02f, 1.2f), 1e-5f);
        }

        [Test]
        public void EvaluateCutSpeed_AtDeliberateSurgicalSpeed_IsFullQuality()
        {
            // ~5 cm/s: a controlled stroke should not be scored as a poor one.
            Assert.AreEqual(1f, IncisionGeometry.EvaluateCutSpeed(0.05f, 0.02f, 1.2f), 1e-3f);
        }

        [Test]
        public void DistanceToPolyline_PointOffCentre_ReturnsPerpendicularDistance()
        {
            Vector3[] path = { new Vector3(-0.05f, 0f, 0f), new Vector3(0.05f, 0f, 0f) };
            float distance = IncisionGeometry.DistanceToPolyline(new Vector3(0f, 0f, 0.01f), path);

            Assert.AreEqual(0.01f, distance, 1e-5f);
        }

        [Test]
        public void DistanceToPolyline_EmptyPath_ReturnsMaxValue()
        {
            Assert.AreEqual(float.MaxValue, IncisionGeometry.DistanceToPolyline(Vector3.zero, new Vector3[0]));
        }
    }

    public class IncisionGuideTests
    {
        private GameObject _host;
        private IncisionGuide _guide;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("Guide");
            _guide = _host.AddComponent<IncisionGuide>();
            _guide.Configure(new Vector3(-0.055f, 0f, 0f), new Vector3(0.055f, 0f, 0f));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_host);
        }

        [Test]
        public void PathLength_MatchesConfiguredSpan()
        {
            Assert.AreEqual(0.11f, _guide.PathLength, 1e-4f);
        }

        [Test]
        public void DeviationAt_PointOnPath_IsZero()
        {
            Assert.AreEqual(0f, _guide.DeviationAt(new Vector3(0.02f, 0f, 0f)), 1e-5f);
        }

        [Test]
        public void ScoreDeviation_WithinPerfectTolerance_ScoresFull()
        {
            Assert.AreEqual(1f, _guide.ScoreDeviation(0.001f), 1e-4f);
        }

        [Test]
        public void ScoreDeviation_BeyondFailureTolerance_ScoresZero()
        {
            Assert.AreEqual(0f, _guide.ScoreDeviation(0.2f), 1e-4f);
        }

        [Test]
        public void ScoreDeviation_IsMonotonicallyDecreasing()
        {
            float previous = 1.01f;
            for (float deviation = 0f; deviation <= 0.06f; deviation += 0.002f)
            {
                float score = _guide.ScoreDeviation(deviation);
                Assert.LessOrEqual(score, previous + 1e-4f,
                    $"Accuracy score increased as deviation grew (at {deviation}).");
                previous = score;
            }
        }

        [Test]
        public void IsOutsideValidArea_OnlyBeyondFailureTolerance()
        {
            Assert.IsFalse(_guide.IsOutsideValidArea(_guide.AcceptableTolerance));
            Assert.IsTrue(_guide.IsOutsideValidArea(_guide.FailureTolerance + 0.001f));
        }
    }

    public class SurgeryEvaluationTests
    {
        [Test]
        public void RankFor_MapsScoreBandsToRanks()
        {
            Assert.AreEqual(SurgeryRank.S, SurgeryEvaluation.RankFor(0.95f));
            Assert.AreEqual(SurgeryRank.A, SurgeryEvaluation.RankFor(0.80f));
            Assert.AreEqual(SurgeryRank.B, SurgeryEvaluation.RankFor(0.60f));
            Assert.AreEqual(SurgeryRank.C, SurgeryEvaluation.RankFor(0.20f));
        }
    }

    public class TissueSurfaceTests
    {
        private GameObject _host;
        private TissueSurface _tissue;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("Tissue");
            _tissue = _host.AddComponent<TissueSurface>();
            _tissue.Configure(new Vector2(0.09f, 0.06f), 0.02f);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_host);
        }

        [Test]
        public void ApplyIncision_ShallowCut_BecomesSuperficialNotBleeding()
        {
            _tissue.ApplyIncision(Vector3.zero, 0.2f);

            Assert.AreEqual(IncisionState.Superficial, _tissue.State);
        }

        [Test]
        public void ApplyIncision_DeepCut_ReachesBleedingState()
        {
            _tissue.ApplyIncision(Vector3.zero, 0.6f);

            Assert.AreEqual(IncisionState.Bleeding, _tissue.State);
        }

        [Test]
        public void ApplyIncision_PointsCloserThanSpacing_DoNotAccumulateLength()
        {
            _tissue.ApplyIncision(new Vector3(0f, 0f, 0f), 0.5f);
            _tissue.ApplyIncision(new Vector3(0.0005f, 0f, 0f), 0.5f);

            Assert.AreEqual(1, _tissue.IncisionPoints.Count,
                "Holding the blade still must not spam the incision polyline.");
            Assert.AreEqual(0f, _tissue.IncisionLength, 1e-5f);
        }

        [Test]
        public void ApplyIncision_AcrossSurface_AccumulatesLength()
        {
            for (int i = 0; i < 10; i++)
            {
                _tissue.ApplyIncision(new Vector3(-0.05f + i * 0.01f, 0f, 0f), 0.5f);
            }

            Assert.AreEqual(10, _tissue.IncisionPoints.Count);
            Assert.AreEqual(0.09f, _tissue.IncisionLength, 1e-3f);
        }

        [Test]
        public void ResetTissue_ClearsEverything()
        {
            _tissue.ApplyIncision(Vector3.zero, 0.9f);
            _tissue.ResetTissue();

            Assert.AreEqual(IncisionState.Intact, _tissue.State);
            Assert.AreEqual(0, _tissue.IncisionPoints.Count);
            Assert.AreEqual(0f, _tissue.PeakDepth01, 1e-5f);
        }
    }
}
