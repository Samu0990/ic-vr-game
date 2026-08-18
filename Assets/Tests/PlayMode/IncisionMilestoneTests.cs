using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRSurgery.Data;
using VRSurgery.Diagnostics;
using VRSurgery.Haptics;
using VRSurgery.Interaction;
using VRSurgery.Surgery;
using VRSurgery.Tissue;
using VRSurgery.Tools;

namespace VRSurgery.Tests
{
    /// <summary>
    /// The project's first production milestone, executed for real in Play Mode:
    /// grab the scalpel -> touch the tissue -> make an incision -> the wound reacts ->
    /// feedback fires -> the step completes.
    ///
    /// The rig is assembled in code rather than loaded from the scene so a failure here points
    /// at the mechanics rather than at scene wiring; the scene itself is covered separately.
    /// </summary>
    public class IncisionMilestoneTests
    {
        private GameObject _root;
        private TissueSurface _tissue;
        private IncisionSystem _incisionSystem;
        private BleedingSystem _bleeding;
        private ScalpelTool _scalpel;
        private SurgeryProbe _probe;
        private ProbeHand _hand;
        private SurgeryObjectiveSystem _objectives;
        private SurgeryEvaluation _evaluation;
        private HapticManager _haptics;

        private readonly System.Collections.Generic.List<string> _completedObjectives =
            new System.Collections.Generic.List<string>();

        private bool _incisionStarted;
        private bool _incisionCompleted;
        private bool _bleedingStarted;
        private bool _surgeryCompleted;

        [SetUp]
        public void SetUp()
        {
            // Static events outlive scenes and Play Mode sessions; a leftover listener from a
            // previous test would fire into destroyed objects.
            SurgeryEvents.ResetAll();

            _completedObjectives.Clear();
            _incisionStarted = false;
            _incisionCompleted = false;
            _bleedingStarted = false;
            _surgeryCompleted = false;

            _root = new GameObject("TestRig");

            BuildTissue();
            BuildScalpel();
            BuildSystems();
            BuildProbe();

            SurgeryEvents.OnIncisionStarted += _ => _incisionStarted = true;
            SurgeryEvents.OnIncisionCompleted += _ => _incisionCompleted = true;
            SurgeryEvents.OnBleedingStarted += () => _bleedingStarted = true;
            SurgeryEvents.OnObjectiveCompleted += id => _completedObjectives.Add(id);
            SurgeryEvents.OnSurgeryCompleted += () => _surgeryCompleted = true;
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.Destroy(_root);
            }

            SurgeryEvents.ResetAll();
        }

        private void BuildTissue()
        {
            GameObject tissueObject = new GameObject("Tissue");
            tissueObject.transform.SetParent(_root.transform, false);
            tissueObject.transform.position = new Vector3(0f, 1f, 0.5f);

            // The guide must exist as a child before IncisionSystem awakes, since that is how
            // the system resolves it when nothing was wired in the inspector.
            GameObject guideObject = new GameObject("Guide");
            guideObject.transform.SetParent(tissueObject.transform, false);
            IncisionGuide guide = guideObject.AddComponent<IncisionGuide>();
            guide.Configure(new Vector3(-0.055f, 0f, 0f), new Vector3(0.055f, 0f, 0f));

            _tissue = tissueObject.AddComponent<TissueSurface>();
            _tissue.Configure(new Vector2(0.09f, 0.06f), 0.02f);

            _incisionSystem = tissueObject.AddComponent<IncisionSystem>();
            _bleeding = tissueObject.AddComponent<BleedingSystem>();
        }

        private void BuildScalpel()
        {
            GameObject scalpelObject = new GameObject("Scalpel");
            scalpelObject.transform.SetParent(_root.transform, false);
            scalpelObject.transform.position = new Vector3(-0.3f, 1.1f, 0.3f);

            GameObject tipObject = new GameObject("BladeTip");
            tipObject.transform.SetParent(scalpelObject.transform, false);
            tipObject.transform.localPosition = new Vector3(0f, 0f, 0.086f);
            tipObject.AddComponent<BladeTip>();

            // ScalpelTool pulls in SurgicalInteractable, which pulls in Rigidbody.
            _scalpel = scalpelObject.AddComponent<ScalpelTool>();

            Rigidbody body = scalpelObject.GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            HapticProfile haptic = HapticProfile.Create(HapticIntensity.Medium, 0.4f, 0.06f);
            ToolDefinition definition = ToolDefinition.Create(
                "scalpel", "Scalpel", ToolType.Scalpel, ToolCapability.Cut, haptic);

            _scalpel.SetToolDefinition(definition);

            SurgicalInteractable interactable = scalpelObject.GetComponent<SurgicalInteractable>();
            SetPrivateField(interactable, "toolDefinition", definition);

            scalpelObject.AddComponent<CuttingInteractor>();
        }

        private void BuildSystems()
        {
            // Built inactive so the surgery definition is in place before Start runs.
            GameObject systems = new GameObject("Systems");
            systems.SetActive(false);
            systems.transform.SetParent(_root.transform, false);

            _objectives = systems.AddComponent<SurgeryObjectiveSystem>();
            _objectives.SetSurgeryDefinition(SurgeryDefinition.Create("test-slice", "Test Incision", new[]
            {
                ObjectiveDefinition.Create("select-instrument", "Pick up the scalpel.",
                    ObjectiveTrigger.GrabTool, ToolType.Scalpel),
                ObjectiveDefinition.Create("perform-incision", "Make the incision.",
                    ObjectiveTrigger.CompleteIncision, ToolType.Scalpel),
                ObjectiveDefinition.Create("control-bleeding", "Control the bleeding.",
                    ObjectiveTrigger.ControlBleeding, ToolType.Forceps, false),
            }));

            _evaluation = systems.AddComponent<SurgeryEvaluation>();
            _evaluation.Bind(_objectives, _incisionSystem, _bleeding);

            _haptics = systems.AddComponent<HapticManager>();
            _haptics.RegisterProfile(HapticEvent.Grab, HapticProfile.Create(HapticIntensity.Medium, 0.4f, 0.06f));
            _haptics.RegisterProfile(HapticEvent.Incision, HapticProfile.Create(HapticIntensity.Soft, 0.18f, 0.03f));

            systems.AddComponent<SurgeryTelemetry>();

            systems.SetActive(true);
        }

        private void BuildProbe()
        {
            GameObject probeObject = new GameObject("Probe");
            probeObject.SetActive(false);
            probeObject.transform.SetParent(_root.transform, false);

            _hand = probeObject.AddComponent<ProbeHand>();
            _probe = probeObject.AddComponent<SurgeryProbe>();
            _probe.RunOnStart = false;
            _probe.Bind(_scalpel, _incisionSystem);

            probeObject.SetActive(true);
        }

        [UnityTest]
        public IEnumerator Milestone_GrabScalpelCutTissue_ProducesWoundFeedbackAndObjectives()
        {
            yield return null;

            Assert.AreEqual("select-instrument", _objectives.ActiveObjective.Id,
                "The procedure should open on the instrument-selection step.");

            yield return _probe.RunIncision();

            // --- the tool was actually picked up ---
            Assert.IsTrue(_scalpel.IsHeld, "Scalpel was never held.");
            CollectionAssert.Contains(_completedObjectives, "select-instrument");

            // --- the blade actually cut ---
            Assert.IsTrue(_incisionStarted, "OnIncisionStarted never fired — the blade never registered contact.");
            Assert.Greater(_tissue.IncisionPoints.Count, 5,
                "Too few incision points recorded; the sweep is not tracking along the tissue.");
            Assert.Greater(_tissue.IncisionLength, 0.05f,
                $"Incision far too short ({_tissue.IncisionLength:F4} m).");

            // --- the wound reacted ---
            Assert.AreNotEqual(IncisionState.Intact, _tissue.State, "Tissue state never changed.");
            Assert.IsTrue(_tissue.State == IncisionState.Bleeding || _tissue.State == IncisionState.Open,
                $"Expected an open or bleeding wound, got {_tissue.State}.");

            // --- the incision completed and the objective advanced ---
            Assert.IsTrue(_incisionCompleted, "OnIncisionCompleted never fired.");
            CollectionAssert.Contains(_completedObjectives, "perform-incision");

            // --- bleeding began as a consequence ---
            Assert.IsTrue(_bleedingStarted, "A wound this deep should have started bleeding.");
            Assert.AreNotEqual(BleedingState.None, _bleeding.State);

            // --- feedback fired ---
            Assert.Greater(_hand.HapticPulseCount, 0, "No haptic feedback was ever dispatched.");

            // --- the player controls the bleeding, finishing the procedure ---
            _bleeding.ControlBleeding();
            yield return null;

            Assert.AreEqual(BleedingState.Controlled, _bleeding.State);
            CollectionAssert.Contains(_completedObjectives, "control-bleeding");
            Assert.IsTrue(_surgeryCompleted, "Surgery never reported completion.");
            Assert.IsTrue(_evaluation.HasScore, "No score was produced at the end of the procedure.");

            SurgeryScore score = _evaluation.LastScore;
            Assert.Greater(score.Accuracy, 0.5f,
                $"A cut driven straight down the guide should score well; got {score.Accuracy:F2}.");
            Assert.GreaterOrEqual(score.Overall, 0f);
            Assert.LessOrEqual(score.Overall, 1f);

            Debug.Log($"[MilestoneTest] {score} | length={_tissue.IncisionLength:F4}m " +
                      $"points={_tissue.IncisionPoints.Count} avgDev={_incisionSystem.AverageDeviation * 1000f:F2}mm");
        }

        [UnityTest]
        public IEnumerator Scalpel_NotHeld_DoesNotCutTissue()
        {
            yield return null;

            CuttingInteractor cutter = _scalpel.GetComponent<CuttingInteractor>();
            cutter.BindTissue(_incisionSystem);

            // Drag the blade straight through the tissue without ever picking it up.
            for (int i = 0; i < 30; i++)
            {
                Vector3 local = new Vector3(-0.06f + i * 0.004f, -0.01f, 0f);
                MoveScalpelTipTo(local);
                yield return new WaitForFixedUpdate();
            }

            Assert.AreEqual(IncisionState.Intact, _tissue.State,
                "An unheld scalpel must not cut — otherwise a tool resting on the patient wounds them.");
            Assert.IsFalse(_tissue.HasIncision);
        }

        [UnityTest]
        public IEnumerator Reset_AfterIncision_ReturnsTissueToIntact()
        {
            yield return null;
            yield return _probe.RunIncision();

            Assert.IsTrue(_tissue.HasIncision, "Precondition failed: no incision to reset.");

            _incisionSystem.ResetSession();
            yield return null;

            Assert.AreEqual(IncisionState.Intact, _tissue.State);
            Assert.AreEqual(0, _tissue.IncisionPoints.Count);
            Assert.AreEqual(BleedingState.None, _bleeding.State);
            Assert.IsFalse(_incisionSystem.IsCompleted);
        }

        private void MoveScalpelTipTo(Vector3 tissueLocal)
        {
            BladeTip tip = _scalpel.BladeTip;
            Vector3 worldTarget = _tissue.TissueLocalToWorld(tissueLocal);
            Vector3 offset = _scalpel.transform.position - tip.transform.position;
            _scalpel.transform.position = worldTarget + offset;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            System.Reflection.FieldInfo field = target.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            field?.SetValue(target, value);
        }
    }
}
