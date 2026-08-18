using UnityEngine;

namespace VRSurgery.Surgery
{
    /// <summary>
    /// Drives the in-room objective monitor. Deliberately minimal and diegetic: the text lives
    /// on a screen in the operating room, never floating in front of the player's eyes.
    /// </summary>
    public class SurgeryHUD : MonoBehaviour
    {
        [SerializeField] private SurgeryObjectiveSystem objectiveSystem;
        [SerializeField] private TextMesh objectiveText;

        public string CurrentText { get; private set; } = string.Empty;

        private void OnEnable()
        {
            if (objectiveSystem != null)
            {
                objectiveSystem.ObjectiveActivated += HandleObjectiveChanged;
                objectiveSystem.ObjectiveCompleted += HandleObjectiveChanged;
            }

            SurgeryEvents.OnSurgeryCompleted += HandleSurgeryCompleted;
            Refresh();
        }

        private void OnDisable()
        {
            if (objectiveSystem != null)
            {
                objectiveSystem.ObjectiveActivated -= HandleObjectiveChanged;
                objectiveSystem.ObjectiveCompleted -= HandleObjectiveChanged;
            }

            SurgeryEvents.OnSurgeryCompleted -= HandleSurgeryCompleted;
        }

        public void Bind(SurgeryObjectiveSystem system, TextMesh text)
        {
            objectiveSystem = system;
            objectiveText = text;
        }

        private void HandleObjectiveChanged(SurgeryObjectiveSystem.ObjectiveRuntime objective) => Refresh();

        private void HandleSurgeryCompleted() => SetText("PROCEDURE COMPLETE");

        private void Refresh()
        {
            if (objectiveSystem == null)
            {
                SetText("STANDBY");
                return;
            }

            SurgeryObjectiveSystem.ObjectiveRuntime active = objectiveSystem.ActiveObjective;
            if (active == null)
            {
                SetText(objectiveSystem.IsSurgeryComplete ? "PROCEDURE COMPLETE" : "STANDBY");
                return;
            }

            int step = objectiveSystem.CompletedCount + 1;
            int total = objectiveSystem.Objectives.Count;
            SetText($"STEP {step}/{total}\n\n{active.Description}");
        }

        private void SetText(string value)
        {
            CurrentText = value;
            if (objectiveText != null)
            {
                objectiveText.text = value;
            }
        }
    }
}
