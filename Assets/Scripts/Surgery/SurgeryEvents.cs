using System;
using VRSurgery.Interaction;
using VRSurgery.Tools;

namespace VRSurgery.Surgery
{
    /// <summary>
    /// Central event bus so tools/tissue/patient never reference UI, Audio, or Score
    /// directly. Systems that care subscribe here instead of being hard-wired together.
    /// </summary>
    public static class SurgeryEvents
    {
        public static event Action<SurgicalTool, IHandInteractor> OnToolGrabbed;
        public static event Action<SurgicalTool, IHandInteractor> OnToolReleased;

        public static event Action<SurgicalTool> OnIncisionStarted;
        public static event Action<SurgicalTool, float> OnIncisionProgressed;
        public static event Action<SurgicalTool> OnIncisionCompleted;

        public static event Action OnBleedingStarted;
        public static event Action OnBleedingStopped;

        public static event Action<string> OnObjectiveCompleted;
        public static event Action<string> OnObjectiveFailed;
        public static event Action OnSurgeryCompleted;

        public static event Action<ErrorSeverity, string> OnError;

        public static void RaiseToolGrabbed(SurgicalTool tool, IHandInteractor holder) => OnToolGrabbed?.Invoke(tool, holder);
        public static void RaiseToolReleased(SurgicalTool tool, IHandInteractor holder) => OnToolReleased?.Invoke(tool, holder);

        public static void RaiseIncisionStarted(SurgicalTool tool) => OnIncisionStarted?.Invoke(tool);
        public static void RaiseIncisionProgressed(SurgicalTool tool, float progress01) => OnIncisionProgressed?.Invoke(tool, progress01);
        public static void RaiseIncisionCompleted(SurgicalTool tool) => OnIncisionCompleted?.Invoke(tool);

        public static void RaiseBleedingStarted() => OnBleedingStarted?.Invoke();
        public static void RaiseBleedingStopped() => OnBleedingStopped?.Invoke();

        public static void RaiseObjectiveCompleted(string objectiveId) => OnObjectiveCompleted?.Invoke(objectiveId);
        public static void RaiseObjectiveFailed(string objectiveId) => OnObjectiveFailed?.Invoke(objectiveId);
        public static void RaiseSurgeryCompleted() => OnSurgeryCompleted?.Invoke();

        public static void RaiseError(ErrorSeverity severity, string message) => OnError?.Invoke(severity, message);

        /// <summary>
        /// Clears every subscription. Static events survive scene loads and (with domain reload
        /// disabled) survive leaving Play Mode, so a stale listener on a destroyed object would
        /// otherwise keep firing. Called on surgery reset and between tests.
        /// </summary>
        public static void ResetAll()
        {
            OnToolGrabbed = null;
            OnToolReleased = null;
            OnIncisionStarted = null;
            OnIncisionProgressed = null;
            OnIncisionCompleted = null;
            OnBleedingStarted = null;
            OnBleedingStopped = null;
            OnObjectiveCompleted = null;
            OnObjectiveFailed = null;
            OnSurgeryCompleted = null;
            OnError = null;
        }
    }
}
