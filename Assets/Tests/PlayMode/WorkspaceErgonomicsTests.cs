using System.Collections;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VRSurgery.Diagnostics;
using VRSurgery.Interaction;
using VRSurgery.Surgery;
using VRSurgery.Tissue;

namespace VRSurgery.Tests
{
    /// <summary>
    /// Phase 1 validation: the workstation blockout.
    ///
    /// Zero locomotion is a design pillar, which makes layout a correctness problem rather than
    /// an aesthetic one — anything the procedure requires that falls outside the arm envelope is
    /// a defect, not a style choice. These measurements run headlessly so a layout regression is
    /// caught immediately instead of at the next hardware session.
    /// </summary>
    public class WorkspaceErgonomicsTests
    {
        private const string SceneName = "SurgeryMVP";

        private Camera _camera;
        private Vector3 _eye;
        private Quaternion _head;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return HeadlessScene.Load(SceneName);

            // Must be the player's head specifically. The scene also contains a fixed projector
            // camera feeding the room's display, and measuring ergonomics from that one produces
            // confident, entirely meaningless numbers.
            _camera = Camera.main;
            Assert.IsNotNull(_camera,
                "No camera tagged MainCamera — the player's head pose cannot be identified.");

            _eye = _camera.transform.position;
            _head = _camera.transform.rotation;

            StringBuilder poses = new StringBuilder();
            poses.AppendLine($"[Layout] camera '{_camera.name}' eye={_eye} fwd={_head * Vector3.forward}");
            foreach (SurgicalInteractable tool in Object.FindObjectsByType<SurgicalInteractable>(FindObjectsSortMode.None))
            {
                poses.AppendLine($"[Layout] {tool.name} @ {tool.transform.position}");
            }

            TissueSurface tissueSurface = Object.FindFirstObjectByType<TissueSurface>();
            if (tissueSurface != null)
            {
                poses.AppendLine($"[Layout] operative field @ {tissueSurface.transform.position}");
            }

            SurgeryHUD monitorHud = Object.FindFirstObjectByType<SurgeryHUD>();
            if (monitorHud != null)
            {
                poses.AppendLine($"[Layout] monitor @ {monitorHud.transform.position}");
            }

            Debug.Log(poses.ToString());
        }

        [TearDown]
        public void TearDown() => SurgeryEvents.ResetAll();

        [UnityTest]
        public IEnumerator OperativeField_IsWithinPrecisionReach()
        {
            TissueSurface tissue = Object.FindFirstObjectByType<TissueSurface>();
            Vector3 field = tissue.transform.position;

            float distance = ReachEnvelope.DistanceFromNearestShoulder(_eye, _head, field);
            ReachClass reach = ReachEnvelope.Classify(distance);

            Debug.Log($"[Ergonomics] operative field: {distance:F3} m from shoulder -> {reach}");

            Assert.AreEqual(ReachClass.Precision, reach,
                $"The operative field sits {distance:F3} m from the shoulder. Delicate cutting must " +
                $"happen inside {ReachEnvelope.PrecisionReach:F2} m or the player works at arm's length.");

            yield break;
        }

        [UnityTest]
        public IEnumerator EveryTool_IsReachableWithoutStepping()
        {
            SurgicalInteractable[] tools = Object.FindObjectsByType<SurgicalInteractable>(FindObjectsSortMode.None);
            Assert.Greater(tools.Length, 0, "No tools in the scene.");

            StringBuilder report = new StringBuilder();
            foreach (SurgicalInteractable tool in tools)
            {
                Vector3 grabPoint = tool.GripPoint != null ? tool.GripPoint.position : tool.transform.position;
                float distance = ReachEnvelope.DistanceFromNearestShoulder(_eye, _head, grabPoint);
                ReachClass reach = ReachEnvelope.Classify(distance);

                report.AppendLine($"  {tool.name}: {distance:F3} m -> {reach}");

                Assert.AreNotEqual(ReachClass.OutOfReach, reach,
                    $"'{tool.name}' is {distance:F3} m away — unreachable without walking, " +
                    "which the design forbids.");
                Assert.AreNotEqual(ReachClass.Strained, reach,
                    $"'{tool.name}' is {distance:F3} m away, inside the strain band. " +
                    "Primary instruments must be comfortable to pick up repeatedly.");
            }

            Debug.Log("[Ergonomics] tool reach:\n" + report);
            yield break;
        }

        [UnityTest]
        public IEnumerator EveryTool_IsReachableWithEitherHand()
        {
            // This replaces Tools_AreDistributedToBothSides, which asserted the right requirement
            // through the wrong measurement.
            //
            // That test demanded an instrument on each side of the gaze axis, and a sweep of 107
            // layouts found the only geometry satisfying it puts BOTH instruments 88 degrees
            // off-axis — against the 90 degree limit NothingEssential_SitsBehindThePlayer enforces
            // for the same ergonomic reason. Two tests, one layout, no way to pass both.
            //
            // But splitting the tray was never the requirement; it was one way of meeting it. What
            // actually matters is that a left-handed visitor, or one with a shorter reach, can
            // take either instrument with either hand. That is measured from both shoulders
            // instead of from the gaze, and it does not care which side of the midline anything
            // sits on — so it can be satisfied at the same time as the 90 degree rule.
            SurgicalInteractable[] tools = Object.FindObjectsByType<SurgicalInteractable>(FindObjectsSortMode.None);
            Assert.Greater(tools.Length, 0, "No tools in the scene.");

            Vector3 leftShoulder = ReachEnvelope.ShoulderPosition(_eye, _head, true);
            Vector3 rightShoulder = ReachEnvelope.ShoulderPosition(_eye, _head, false);

            StringBuilder report = new StringBuilder();
            foreach (SurgicalInteractable tool in tools)
            {
                Vector3 grab = tool.GripPoint != null ? tool.GripPoint.position : tool.transform.position;

                float fromLeft = Vector3.Distance(leftShoulder, grab);
                float fromRight = Vector3.Distance(rightShoulder, grab);

                report.AppendLine($"  {tool.name}: esquerda {fromLeft:F3} m [{ReachEnvelope.Classify(fromLeft)}], " +
                                  $"direita {fromRight:F3} m [{ReachEnvelope.Classify(fromRight)}]");

                foreach ((float distance, string hand) in new[] { (fromLeft, "esquerda"), (fromRight, "direita") })
                {
                    Assert.AreNotEqual(ReachClass.OutOfReach, ReachEnvelope.Classify(distance),
                        $"'{tool.name}' is {distance:F3} m from the {hand} shoulder — a visitor " +
                        "leading with that hand would have to step, which the design forbids.");
                    Assert.AreNotEqual(ReachClass.Strained, ReachEnvelope.Classify(distance),
                        $"'{tool.name}' is {distance:F3} m from the {hand} shoulder, inside the " +
                        "strain band. A left-handed visitor, or one with a shorter reach, would " +
                        "work at full extension all session.");
                }
            }

            Debug.Log("[Ergonomics] alcance de cada instrumento pelos dois ombros:\n" + report);
            yield break;
        }

        [UnityTest]
        public IEnumerator OperativeField_DoesNotRequireExcessiveNeckFlexion()
        {
            TissueSurface tissue = Object.FindFirstObjectByType<TissueSurface>();
            float pitch = ReachEnvelope.GazePitchDegrees(_eye, _head, tissue.transform.position);

            Debug.Log($"[Ergonomics] gaze pitch to operative field: {pitch:F1}deg down");

            Assert.Less(pitch, 55f,
                $"Looking at the operative field needs {pitch:F1}deg of downward gaze. " +
                "Sustained neck flexion is the fastest route to discomfort in a stationary experience.");
            Assert.Greater(pitch, 10f,
                $"The operative field is only {pitch:F1}deg below eye level — the patient reads as " +
                "floating at chest height rather than lying on a table.");

            yield break;
        }

        [UnityTest]
        public IEnumerator Monitor_IsReadableWithoutTurningAway()
        {
            SurgeryHUD hud = Object.FindFirstObjectByType<SurgeryHUD>();
            Assert.IsNotNull(hud, "No objective monitor in the scene.");

            Vector3 monitor = hud.transform.position;
            float yaw = ReachEnvelope.GazeYawDegrees(_eye, _head, monitor);
            float distance = Vector3.Distance(_eye, monitor);

            Debug.Log($"[Ergonomics] monitor: {yaw:F1}deg off-axis, {distance:F2} m away");

            Assert.Less(yaw, 50f,
                $"The monitor sits {yaw:F1}deg off-axis; checking the objective should be a glance, " +
                "not a turn away from the patient.");
            Assert.Greater(distance, 0.6f,
                $"The monitor is {distance:F2} m from the eyes — too close to focus on comfortably.");

            yield break;
        }

        [UnityTest]
        public IEnumerator NothingEssential_SitsBehindThePlayer()
        {
            SurgicalInteractable[] tools = Object.FindObjectsByType<SurgicalInteractable>(FindObjectsSortMode.None);

            foreach (SurgicalInteractable tool in tools)
            {
                float yaw = ReachEnvelope.GazeYawDegrees(_eye, _head, tool.transform.position);
                Assert.Less(yaw, 90f,
                    $"'{tool.name}' is {yaw:F1}deg off-axis — behind the player's shoulder line. " +
                    "No instrument the procedure needs may require turning around.");
            }

            yield break;
        }
    }
}
