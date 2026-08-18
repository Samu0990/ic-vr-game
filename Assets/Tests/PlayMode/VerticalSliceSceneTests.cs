using System.Collections;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VRSurgery.Diagnostics;
using VRSurgery.Haptics;
using VRSurgery.Surgery;
using VRSurgery.Tissue;
using VRSurgery.Tools;

namespace VRSurgery.Tests
{
    /// <summary>
    /// Verifies the shipped scene, not a rig assembled by the test. The milestone tests prove the
    /// mechanics work; these prove the scene the player actually loads is wired to use them —
    /// the failure mode where every system is correct and nothing is connected.
    /// </summary>
    public class VerticalSliceSceneTests
    {
        private const string SceneName = "VerticalSlice";

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return HeadlessScene.Load(SceneName);
        }

        [TearDown]
        public void TearDown()
        {
            SurgeryEvents.ResetAll();
        }

        [UnityTest]
        public IEnumerator Scene_ContainsCoreSurgicalSystems()
        {
            Assert.IsNotNull(Object.FindFirstObjectByType<TissueSurface>(), "No tissue surface in the scene.");
            Assert.IsNotNull(Object.FindFirstObjectByType<IncisionSystem>(), "No incision system in the scene.");
            Assert.IsNotNull(Object.FindFirstObjectByType<BleedingSystem>(), "No bleeding system in the scene.");
            Assert.IsNotNull(Object.FindFirstObjectByType<ScalpelTool>(), "No scalpel in the scene.");
            Assert.IsNotNull(Object.FindFirstObjectByType<ForcepsTool>(), "No forceps in the scene.");
            Assert.IsNotNull(Object.FindFirstObjectByType<SurgeryObjectiveSystem>(), "No objective system in the scene.");
            Assert.IsNotNull(Object.FindFirstObjectByType<SurgeryEvaluation>(), "No evaluation system in the scene.");
            Assert.IsNotNull(Object.FindFirstObjectByType<Camera>(), "No camera in the scene.");

            yield break;
        }

        [UnityTest]
        public IEnumerator Scene_ScalpelIsWiredForCutting()
        {
            ScalpelTool scalpel = Object.FindFirstObjectByType<ScalpelTool>();

            Assert.IsNotNull(scalpel.ToolDefinition, "Scalpel has no ToolDefinition assigned.");
            Assert.IsTrue(scalpel.HasCapability(ToolCapability.Cut), "Scalpel is not marked as able to cut.");
            Assert.IsNotNull(scalpel.BladeTip, "Scalpel has no BladeTip — cut detection would use the handle.");
            Assert.IsNotNull(scalpel.CuttingInteractor, "Scalpel has no CuttingInteractor.");

            // The tip must sit at the sharp end, not at the tool's origin.
            float tipOffset = Vector3.Distance(scalpel.transform.position, scalpel.BladeTip.transform.position);
            Assert.Greater(tipOffset, 0.02f, "BladeTip is effectively at the tool origin.");

            yield break;
        }

        [UnityTest]
        public IEnumerator Scene_TissueGuideAndObjectivesAreConfigured()
        {
            IncisionSystem incision = Object.FindFirstObjectByType<IncisionSystem>();
            Assert.IsNotNull(incision.Guide, "Incision system has no guide — deviation could never be scored.");
            Assert.Greater(incision.Guide.PathLength, 0.01f, "Guide path is degenerate.");
            Assert.Greater(incision.RequiredLength, 0.01f, "Required incision length is degenerate.");

            SurgeryObjectiveSystem objectives = Object.FindFirstObjectByType<SurgeryObjectiveSystem>();
            Assert.IsNotNull(objectives.SurgeryDefinition, "No SurgeryDefinition assigned in the scene.");
            Assert.Greater(objectives.Objectives.Count, 0, "The procedure has no objectives.");
            Assert.IsNotNull(objectives.ActiveObjective, "No objective became active on start.");

            yield break;
        }

        /// <summary>
        /// Regression guard: haptic profiles registered while generating the scene were
        /// previously discarded, leaving the built scene with no feedback whatsoever.
        /// </summary>
        [UnityTest]
        public IEnumerator Scene_HapticProfilesSurvivedSceneBuild()
        {
            HapticManager haptics = Object.FindFirstObjectByType<HapticManager>();

            Assert.IsNotNull(haptics, "No HapticManager in the scene.");
            Assert.Greater(haptics.RegisteredProfileCount, 0,
                "The scene shipped with no haptic profiles assigned.");

            // The same asset-creation bug also left per-tool profile references empty, which is
            // invisible in the editor until someone puts a headset on and feels nothing.
            ScalpelTool scalpel = Object.FindFirstObjectByType<ScalpelTool>();
            Assert.IsNotNull(scalpel.ToolDefinition.HapticProfile,
                "The scalpel's ToolDefinition has no haptic profile assigned.");

            yield break;
        }

        [UnityTest]
        public IEnumerator Scene_ProbeDrivenIncision_ProducesAWound()
        {
            SurgeryProbe probe = Object.FindFirstObjectByType<SurgeryProbe>();
            Assert.IsNotNull(probe, "No SurgeryProbe in the scene — headset-free validation is impossible.");

            TissueSurface tissue = Object.FindFirstObjectByType<TissueSurface>();
            WoundRenderer wound = Object.FindFirstObjectByType<WoundRenderer>();
            Assert.AreEqual(IncisionState.Intact, tissue.State, "Tissue did not start intact.");

            yield return probe.RunIncision();

            Assert.IsTrue(tissue.HasIncision, "The probe ran but no incision was recorded in the scene.");
            Assert.AreNotEqual(IncisionState.Intact, tissue.State, "Tissue state never changed in the scene.");
            Assert.Greater(tissue.IncisionLength, 0.05f,
                $"Incision in the scene is too short ({tissue.IncisionLength:F4} m).");

            Assert.IsNotNull(wound, "No WoundRenderer in the scene — the cut would be invisible.");
            Assert.Greater(wound.VertexCount, 0, "The wound mesh has no geometry; the cut is invisible.");

            Debug.Log($"[SceneTest] state={tissue.State} length={tissue.IncisionLength:F4}m " +
                      $"points={tissue.IncisionPoints.Count} woundVerts={wound.VertexCount}");
        }

        /// <summary>
        /// Replaces an earlier test that demanded an internal organ and a second camera. The organ
        /// was out of scope and is gone; the camera count was never the real requirement. What
        /// actually matters is that exactly one camera is identifiable as the player's head —
        /// an ambiguous MainCamera is how the ergonomics measurements once got taken from a
        /// fixed projector camera and produced confident nonsense.
        /// </summary>
        [UnityTest]
        public IEnumerator Scene_HasExactlyOneIdentifiablePlayerCamera()
        {
            Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            Assert.Greater(cameras.Length, 0, "Scene has no camera at all.");

            int tagged = 0;
            foreach (Camera c in cameras)
            {
                if (c.CompareTag("MainCamera"))
                {
                    tagged++;
                }
            }

            Assert.AreEqual(1, tagged,
                $"{tagged} cameras are tagged MainCamera. The player's head must be unambiguous.");

            Assert.IsNotNull(Camera.main, "Camera.main does not resolve.");

            XROrigin origin = Object.FindFirstObjectByType<XROrigin>();
            Assert.IsNotNull(origin, "No XR Origin in the scene.");
            Assert.AreSame(Camera.main, origin.Camera,
                "The MainCamera is not the XR Origin's camera — the player's head is somewhere else.");

            yield break;
        }

        [UnityTest]
        public IEnumerator Scene_HasNoInternalOrganLeftovers()
        {
            // The first surgery is spine-only. Organ geometry was removed as out of scope, and
            // one of its parts was protruding 7 mm through the skin while it was there.
            foreach (string stale in new[] { "InternalOrgan", "Organ_HeartBody", "Organ_HeartAtrium", "Organ_Vessel" })
            {
                Assert.IsNull(GameObject.Find(stale), $"Removed organ object '{stale}' is back in the scene.");
            }

            yield break;
        }
    }
}
