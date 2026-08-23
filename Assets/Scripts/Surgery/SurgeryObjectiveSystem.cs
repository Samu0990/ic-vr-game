using System;
using System.Collections.Generic;
using UnityEngine;
using VRSurgery.Tools;

namespace VRSurgery.Surgery
{
    /// <summary>
    /// Owns the procedure's flow. Objectives advance strictly in order, and the rest of the
    /// game learns about it through SurgeryEvents — no objective logic is allowed to leak into
    /// tool or tissue scripts.
    /// </summary>
    public class SurgeryObjectiveSystem : MonoBehaviour
    {
        [SerializeField] private SurgeryDefinition surgeryDefinition;
        [SerializeField] private bool autoStart = true;

        private readonly List<ObjectiveRuntime> _objectives = new List<ObjectiveRuntime>();

        public sealed class ObjectiveRuntime
        {
            public ObjectiveDefinition Definition { get; }
            public ObjectiveState State { get; internal set; }

            public string Id => Definition.ObjectiveId;
            public string Description => Definition.Description;

            internal ObjectiveRuntime(ObjectiveDefinition definition, ObjectiveState state)
            {
                Definition = definition;
                State = state;
            }
        }

        public IReadOnlyList<ObjectiveRuntime> Objectives => _objectives;
        public SurgeryDefinition SurgeryDefinition => surgeryDefinition;

        public ObjectiveRuntime ActiveObjective { get; private set; }
        public bool IsSurgeryComplete { get; private set; }

        /// <summary>Seconds elapsed since the procedure started.</summary>
        public float ElapsedSeconds { get; private set; }

        public event Action<ObjectiveRuntime> ObjectiveActivated;
        public event Action<ObjectiveRuntime> ObjectiveCompleted;

        private bool _running;

        private void OnEnable()
        {
            SurgeryEvents.OnToolGrabbed += HandleToolGrabbed;
            SurgeryEvents.OnToolReleased += HandleToolReleased;
            SurgeryEvents.OnIncisionCompleted += HandleIncisionCompleted;
            SurgeryEvents.OnBleedingStopped += HandleBleedingStopped;
        }

        private void OnDisable()
        {
            SurgeryEvents.OnToolGrabbed -= HandleToolGrabbed;
            SurgeryEvents.OnToolReleased -= HandleToolReleased;
            SurgeryEvents.OnIncisionCompleted -= HandleIncisionCompleted;
            SurgeryEvents.OnBleedingStopped -= HandleBleedingStopped;
        }

        private void Start()
        {
            if (autoStart)
            {
                StartSurgery();
            }
        }

        private void Update()
        {
            if (_running && !IsSurgeryComplete)
            {
                ElapsedSeconds += Time.deltaTime;
            }
        }

        public void SetSurgeryDefinition(SurgeryDefinition definition)
        {
            surgeryDefinition = definition;
        }

        public void StartSurgery()
        {
            _objectives.Clear();
            ActiveObjective = null;
            IsSurgeryComplete = false;
            ElapsedSeconds = 0f;
            _running = true;

            if (surgeryDefinition == null)
            {
                Debug.LogWarning("[SurgeryObjectiveSystem] No SurgeryDefinition assigned; nothing to run.", this);
                return;
            }

            IReadOnlyList<ObjectiveDefinition> definitions = surgeryDefinition.Objectives;
            for (int i = 0; i < definitions.Count; i++)
            {
                if (definitions[i] == null)
                {
                    continue;
                }

                _objectives.Add(new ObjectiveRuntime(
                    definitions[i],
                    _objectives.Count == 0 ? ObjectiveState.Available : ObjectiveState.Locked));
            }

            AdvanceToNextObjective();
        }

        private void AdvanceToNextObjective()
        {
            for (int i = 0; i < _objectives.Count; i++)
            {
                ObjectiveRuntime objective = _objectives[i];
                if (objective.State is ObjectiveState.Completed or ObjectiveState.Skipped or ObjectiveState.Failed)
                {
                    continue;
                }

                objective.State = ObjectiveState.Active;
                ActiveObjective = objective;
                ObjectiveActivated?.Invoke(objective);
                return;
            }

            ActiveObjective = null;
            if (!IsSurgeryComplete)
            {
                IsSurgeryComplete = true;
                _running = false;
                SurgeryEvents.RaiseSurgeryCompleted();
            }
        }

        private void TryComplete(ObjectiveTrigger trigger, ToolType? toolType)
        {
            ObjectiveRuntime objective = ActiveObjective;
            if (objective == null || objective.State != ObjectiveState.Active)
            {
                return;
            }

            if (objective.Definition.Trigger != trigger)
            {
                return;
            }

            if (objective.Definition.ToolTypeMatters
                && toolType.HasValue
                && objective.Definition.RequiredTool != toolType.Value)
            {
                // Right action, wrong instrument: a real mistake, but not a lost procedure.
                SurgeryEvents.RaiseError(ErrorSeverity.MajorError,
                    $"Wrong instrument for '{objective.Id}': expected {objective.Definition.RequiredTool}, got {toolType.Value}.");
                return;
            }

            CompleteObjective(objective);
        }

        private void CompleteObjective(ObjectiveRuntime objective)
        {
            objective.State = ObjectiveState.Completed;
            ObjectiveCompleted?.Invoke(objective);
            SurgeryEvents.RaiseObjectiveCompleted(objective.Id);
            AdvanceToNextObjective();
        }

        /// <summary>Completes the active objective for triggers the systems cannot infer on their own.</summary>
        public void CompleteActiveObjectiveManually()
        {
            if (ActiveObjective != null && ActiveObjective.Definition.Trigger == ObjectiveTrigger.Manual)
            {
                CompleteObjective(ActiveObjective);
            }
        }

        public void FailActiveObjective(string reason)
        {
            ObjectiveRuntime objective = ActiveObjective;
            if (objective == null)
            {
                return;
            }

            objective.State = ObjectiveState.Failed;
            SurgeryEvents.RaiseObjectiveFailed(objective.Id);
            SurgeryEvents.RaiseError(ErrorSeverity.MajorError, reason);
            AdvanceToNextObjective();
        }

        private void HandleToolGrabbed(SurgicalTool tool, Interaction.IHandInteractor holder) =>
            TryComplete(ObjectiveTrigger.GrabTool, tool != null ? (ToolType?)tool.ToolType : null);

        private void HandleToolReleased(SurgicalTool tool, Interaction.IHandInteractor holder) =>
            TryComplete(ObjectiveTrigger.ReleaseTool, tool != null ? (ToolType?)tool.ToolType : null);

        private void HandleIncisionCompleted(SurgicalTool tool) =>
            TryComplete(ObjectiveTrigger.CompleteIncision, tool != null ? (ToolType?)tool.ToolType : null);

        private void HandleBleedingStopped() =>
            TryComplete(ObjectiveTrigger.ControlBleeding, null);

        public int CompletedCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _objectives.Count; i++)
                {
                    if (_objectives[i].State == ObjectiveState.Completed)
                    {
                        count++;
                    }
                }

                return count;
            }
        }
    }
}
