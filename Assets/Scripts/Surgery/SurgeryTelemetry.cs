using System.Collections.Generic;
using System.Text;
using UnityEngine;
using VRSurgery.Interaction;
using VRSurgery.Tools;

namespace VRSurgery.Surgery
{
    /// <summary>
    /// Timestamped local log of everything the surgery systems announce. This is the fastest
    /// way to find out why a mechanic "didn't work" when there is no way to watch the headset
    /// view and read the console at the same time.
    /// </summary>
    public class SurgeryTelemetry : MonoBehaviour
    {
        [SerializeField] private bool echoToConsole = true;
        [SerializeField, Min(16)] private int maxEntries = 512;

        private readonly List<string> _entries = new List<string>();
        private float _startTime;

        public IReadOnlyList<string> Entries => _entries;

        private void Awake()
        {
            _startTime = Time.time;
        }

        private void OnEnable()
        {
            SurgeryEvents.OnToolGrabbed += HandleToolGrabbed;
            SurgeryEvents.OnToolReleased += HandleToolReleased;
            SurgeryEvents.OnIncisionStarted += HandleIncisionStarted;
            SurgeryEvents.OnIncisionCompleted += HandleIncisionCompleted;
            SurgeryEvents.OnBleedingStarted += HandleBleedingStarted;
            SurgeryEvents.OnBleedingStopped += HandleBleedingStopped;
            SurgeryEvents.OnObjectiveCompleted += HandleObjectiveCompleted;
            SurgeryEvents.OnObjectiveFailed += HandleObjectiveFailed;
            SurgeryEvents.OnSurgeryCompleted += HandleSurgeryCompleted;
            SurgeryEvents.OnError += HandleError;
        }

        private void OnDisable()
        {
            SurgeryEvents.OnToolGrabbed -= HandleToolGrabbed;
            SurgeryEvents.OnToolReleased -= HandleToolReleased;
            SurgeryEvents.OnIncisionStarted -= HandleIncisionStarted;
            SurgeryEvents.OnIncisionCompleted -= HandleIncisionCompleted;
            SurgeryEvents.OnBleedingStarted -= HandleBleedingStarted;
            SurgeryEvents.OnBleedingStopped -= HandleBleedingStopped;
            SurgeryEvents.OnObjectiveCompleted -= HandleObjectiveCompleted;
            SurgeryEvents.OnObjectiveFailed -= HandleObjectiveFailed;
            SurgeryEvents.OnSurgeryCompleted -= HandleSurgeryCompleted;
            SurgeryEvents.OnError -= HandleError;
        }

        public void Log(string message)
        {
            float elapsed = Time.time - _startTime;
            int minutes = Mathf.FloorToInt(elapsed / 60f);
            float seconds = elapsed - minutes * 60f;
            string entry = $"[{minutes:00}:{seconds:00.00}] {message}";

            _entries.Add(entry);
            if (_entries.Count > maxEntries)
            {
                _entries.RemoveAt(0);
            }

            if (echoToConsole)
            {
                Debug.Log("[Telemetry] " + entry);
            }
        }

        public string Dump()
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < _entries.Count; i++)
            {
                builder.AppendLine(_entries[i]);
            }

            return builder.ToString();
        }

        public void Clear()
        {
            _entries.Clear();
            _startTime = Time.time;
        }

        private void HandleToolGrabbed(SurgicalTool tool, IHandInteractor holder) =>
            Log($"{ToolName(tool)} grabbed");

        private void HandleToolReleased(SurgicalTool tool, IHandInteractor holder) =>
            Log($"{ToolName(tool)} released");

        private void HandleIncisionStarted(SurgicalTool tool) =>
            Log($"Incision started with {ToolName(tool)}");

        private void HandleIncisionCompleted(SurgicalTool tool) =>
            Log("Incision completed");

        private void HandleBleedingStarted() => Log("Bleeding started");
        private void HandleBleedingStopped() => Log("Bleeding controlled");
        private void HandleObjectiveCompleted(string id) => Log($"Objective completed: {id}");
        private void HandleObjectiveFailed(string id) => Log($"Objective FAILED: {id}");
        private void HandleSurgeryCompleted() => Log("Surgery completed");
        private void HandleError(ErrorSeverity severity, string message) => Log($"{severity}: {message}");

        private static string ToolName(SurgicalTool tool) => tool != null ? tool.ToolId : "unknown tool";
    }
}
