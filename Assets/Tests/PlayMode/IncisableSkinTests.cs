using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRSurgery.Diagnostics;
using VRSurgery.Surgery;
using VRSurgery.Tissue;
using VRSurgery.Tools;

namespace VRSurgery.Tests
{
    /// <summary>
    /// Covers the binary-state incision: that the blade tip actually reaches the region from a
    /// hand position the player can hold, and that touching it twice does not act twice.
    /// </summary>
    public class IncisableSkinTests
    {
        private const string SceneName = "VerticalSlice";

        private IncisableSkin _skin;
        private ScalpelTool _scalpel;
        private Vector3 _scalpelHome;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return HeadlessScene.Load(SceneName);

            _skin = Object.FindFirstObjectByType<IncisableSkin>();
            _scalpel = Object.FindFirstObjectByType<ScalpelTool>();
            Assert.IsNotNull(_skin, "No IncisableSkin on the surgical region.");
            Assert.IsNotNull(_scalpel, "No scalpel in the scene.");
            _scalpelHome = _scalpel.transform.position;
        }

        [TearDown]
        public void TearDown() => SurgeryEvents.ResetAll();

        /// <summary>Puts the blade tip at a world point, moving the whole tool as a rigid body.</summary>
        private void PlaceTipAt(Vector3 worldPoint)
        {
            Vector3 offset = _scalpel.transform.position - _scalpel.BladeTip.transform.position;
            _scalpel.transform.position = worldPoint + offset;
            Physics.SyncTransforms();
        }

        // ------------------------------------------------------------------ Phase 2: reach
        [UnityTest]
        public IEnumerator BladeTip_ReachesTheRegionFromAPrecisionGripPosition()
        {
            Camera cam = Camera.main;
            Vector3 eye = cam.transform.position;
            Quaternion head = cam.transform.rotation;

            IncisionSystem incision = Object.FindFirstObjectByType<IncisionSystem>();
            TissueSurface tissue = incision.Tissue;

            // Distance the grip trails behind the tip along the tool's own axis.
            float tipReach = Vector3.Distance(
                _scalpel.BladeTip.transform.position,
                _scalpel.GetComponent<VRSurgery.Interaction.SurgicalInteractable>().GripPoint.position);

            Assert.Greater(tipReach, 0.01f, "Blade tip and grip are effectively coincident.");

            // Sample the whole guided incision, not just its midpoint: the ends are further from
            // the shoulder than the centre and are what would fall out of reach first.
            Vector3[] targets =
            {
                tissue.TissueLocalToWorld(incision.Guide.Path[0]),
                tissue.transform.position,
                tissue.TissueLocalToWorld(incision.Guide.Path[incision.Guide.Path.Count - 1]),
            };

            string[] labels = { "guide start", "region centre", "guide end" };

            for (int i = 0; i < targets.Length; i++)
            {
                // Blade pointing down at the skin: the hand sits one blade-length above the target.
                Vector3 gripNeeded = targets[i] + Vector3.up * tipReach;
                float d = ReachEnvelope.DistanceFromNearestShoulder(eye, head, gripNeeded);
                ReachClass cls = ReachEnvelope.Classify(d);

                Debug.Log($"[Reach] {labels[i]}: target {targets[i]} -> grip must be at {gripNeeded}, " +
                          $"{d:F4} m from shoulder -> {cls}");

                Assert.AreEqual(ReachClass.Precision, cls,
                    $"To cut at the {labels[i]} the hand must hold the grip {d:F3} m from the " +
                    $"shoulder, which is {cls}, not Precision. Being above the skin is not the " +
                    "same as being reachable.");
            }

            Debug.Log($"[Reach] blade tip extends {tipReach:F4} m beyond the grip");
            yield break;
        }

        // ------------------------------------------------------------------ Phase 3: single fire
        [UnityTest]
        public IEnumerator Incision_FiresOnceOnly_OnRepeatedContact()
        {
            Assert.IsFalse(_skin.IsIncised, "Region started already incised.");
            Assert.AreEqual(0, _skin.IncisionCount);

            MeshRenderer surface = null;
            foreach (MeshRenderer r in _skin.GetComponentsInChildren<MeshRenderer>())
            {
                if (r.name == "TissueVisual")
                {
                    surface = r;
                }
            }

            Assert.IsNotNull(surface, "No TissueVisual renderer to observe.");
            Material intact = surface.sharedMaterial;

            string logPath = _skin.LogFilePath;
            int linesBefore = File.Exists(logPath) ? File.ReadAllLines(logPath).Length : 0;

            // --- first contact
            PlaceTipAt(_skin.transform.position);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.IsTrue(_skin.IsIncised, "Blade tip entered the region but no incision fired.");
            Assert.AreEqual(1, _skin.IncisionCount);
            Assert.AreNotSame(intact, surface.sharedMaterial, "Region material did not change.");
            Assert.IsNotEmpty(_skin.LastLogEntry ?? string.Empty, "No log entry was produced.");

            Material incisedMaterial = surface.sharedMaterial;
            string firstEntry = _skin.LastLogEntry;

            int linesAfterFirst = File.Exists(logPath) ? File.ReadAllLines(logPath).Length : 0;
            Assert.AreEqual(linesBefore + 1, linesAfterFirst,
                "The first incision should have appended exactly one line.");

            // --- withdraw and touch again
            PlaceTipAt(_scalpelHome);
            yield return new WaitForFixedUpdate();
            PlaceTipAt(_skin.transform.position);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.AreEqual(1, _skin.IncisionCount,
                $"Incision fired {_skin.IncisionCount} times. Re-entering the trigger must not " +
                "re-cut an already incised region.");
            Assert.AreSame(incisedMaterial, surface.sharedMaterial, "Feedback re-applied on second contact.");
            Assert.AreEqual(firstEntry, _skin.LastLogEntry, "A second log entry was produced.");

            int linesAfterSecond = File.Exists(logPath) ? File.ReadAllLines(logPath).Length : 0;
            Assert.AreEqual(linesAfterFirst, linesAfterSecond,
                "The log file grew on the second contact; the single-fire guard is not holding.");

            Debug.Log($"[Incision] count={_skin.IncisionCount} log='{_skin.LastLogEntry}' file={logPath}");
        }

        [UnityTest]
        public IEnumerator Incision_IgnoresUntaggedColliders()
        {
            // The scalpel's own grab collider overlaps the region on the way in and is untagged.
            // Only the tagged tip may cut, otherwise merely holding the tool near the patient
            // would open them up.
            GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            probe.transform.localScale = Vector3.one * 0.01f;
            Rigidbody body = probe.AddComponent<Rigidbody>();
            body.isKinematic = true;
            probe.transform.position = _skin.transform.position;
            Physics.SyncTransforms();

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.IsFalse(_skin.IsIncised, "An untagged collider incised the patient.");
            Assert.AreEqual(0, _skin.IncisionCount);

            Object.Destroy(probe);
        }

        [UnityTest]
        public IEnumerator ResetSkin_ReturnsTheRegionToIntact()
        {
            PlaceTipAt(_skin.transform.position);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(_skin.IsIncised, "Precondition failed: nothing to reset.");

            _skin.ResetSkin();

            Assert.IsFalse(_skin.IsIncised);
            Assert.AreEqual(0, _skin.IncisionCount);
        }
    }
}
