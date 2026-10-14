using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRSurgery.Transplant;

namespace VRSurgery.Tests
{
    /// <summary>Regressions that only appear after the authored transplant scene is serialized.</summary>
    public class BypassSceneRegressionTests
    {
        [Test]
        public void ChainedGestureSurvivesUnitySerialization()
        {
            BypassSite source = new BypassSite
            {
                Step = BypassStep.Cardioplegia,
                Chain = new[]
                {
                    BypassStep.Cannulate,
                    BypassStep.ClampAorta,
                    BypassStep.Cardioplegia,
                },
            };

            BypassSite restored = JsonUtility.FromJson<BypassSite>(JsonUtility.ToJson(source));

            CollectionAssert.AreEqual(source.Chain, restored.Chain,
                "A cena precisa conservar todas as ações do gesto encadeado.");
        }

        [Test]
        public void LegacyMediumSceneRestoresItsPlanAndAdvancesPastThePump()
        {
            GameObject procedureObject = new GameObject("Procedure");
            GameObject workerObject = new GameObject("Worker");
            GameObject tipObject = new GameObject("Tip");
            List<GameObject> points = new List<GameObject>();

            try
            {
                TransplantProcedure procedure = procedureObject.AddComponent<TransplantProcedure>();
                BypassPlan medium = BypassPlan.For(SurgicalDifficulty.Medio);
                List<BypassSite> sites = new List<BypassSite>();

                foreach (BypassGesture gesture in medium.Gestures)
                {
                    GameObject point = new GameObject("Bypass_" + gesture.Anchor);
                    points.Add(point);
                    sites.Add(new BypassSite
                    {
                        Step = gesture.Anchor,
                        Point = point.transform,
                        Radius = 0.03f,
                        Seconds = gesture.Seconds,
                        // Deliberately missing: this is how the current scene was saved before
                        // Chain became a serialized field.
                        Chain = null,
                    });
                }

                BypassWorker worker = workerObject.AddComponent<BypassWorker>();
                worker.Bind(tipObject.transform, sites, procedure);

                Assert.AreEqual(SurgicalDifficulty.Medio, procedure.Plan.Difficulty,
                    "The four authored medium sites must not reload as the hard plan.");
                CollectionAssert.AreEqual(medium.Gestures[0].Steps, sites[0].Chain,
                    "The entry site must recover cannulation, clamp and cardioplegia in order.");

                procedure.Begin();
                Assert.IsTrue(procedure.CompleteStage(TransplantStage.OpenChest));
                Assert.AreEqual(TransplantStage.GoOnBypass, procedure.Stage);

                tipObject.transform.position = sites[0].Point.position;
                worker.Tick(sites[0].Seconds + 0.1f);

                Assert.IsTrue(procedure.Bypass.IsArrested);
                Assert.AreEqual(TransplantStage.RemoveNativeHeart, procedure.Stage,
                    "Completing the pump entry must release the surgery to the next stage.");
            }
            finally
            {
                foreach (GameObject point in points) { Object.DestroyImmediate(point); }
                Object.DestroyImmediate(tipObject);
                Object.DestroyImmediate(workerObject);
                Object.DestroyImmediate(procedureObject);
            }
        }
    }
}
