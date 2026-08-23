using System.Collections.Generic;
using UnityEngine;
using VRSurgery.Interaction;
using VRSurgery.Surgery;
using VRSurgery.Tools;

namespace VRSurgery.Haptics
{
    public enum HapticEvent
    {
        Grab,
        Release,
        Impact,
        Incision,
        TissueContact,
        Warning,
        Success,
        Error,
    }

    /// <summary>
    /// Single place that decides what the controllers do. Everything is short pulses —
    /// sustained full-amplitude rumble is fatiguing and destroys the fine positional feel
    /// the rest of the game depends on.
    /// </summary>
    public class HapticManager : MonoBehaviour
    {
        [System.Serializable]
        private struct EventProfile
        {
            public HapticEvent hapticEvent;
            public HapticProfile profile;
        }

        [SerializeField] private List<EventProfile> profiles = new List<EventProfile>();
        [SerializeField] private bool enableHaptics = true;

        private readonly Dictionary<HapticEvent, HapticProfile> _lookup = new Dictionary<HapticEvent, HapticProfile>();
        private IHandInteractor _lastActiveHand;

        /// <summary>Pulses actually dispatched. Exposed so tests can assert feedback fired.</summary>
        public int DispatchedPulses { get; private set; }

        public HapticEvent? LastEvent { get; private set; }

        /// <summary>
        /// How many event profiles are actually assigned. Exposed so a test can catch a scene
        /// that shipped with its haptics unwired, which is otherwise invisible without hardware.
        /// </summary>
        public int RegisteredProfileCount => profiles != null ? profiles.Count : 0;

        private void Awake()
        {
            RebuildLookup();
        }

        private void RebuildLookup()
        {
            _lookup.Clear();
            for (int i = 0; i < profiles.Count; i++)
            {
                if (profiles[i].profile != null)
                {
                    _lookup[profiles[i].hapticEvent] = profiles[i].profile;
                }
            }
        }

        private void OnEnable()
        {
            SurgeryEvents.OnToolGrabbed += HandleToolGrabbed;
            SurgeryEvents.OnToolReleased += HandleToolReleased;
            SurgeryEvents.OnIncisionProgressed += HandleIncisionProgressed;
            SurgeryEvents.OnObjectiveCompleted += HandleObjectiveCompleted;
            SurgeryEvents.OnError += HandleError;
        }

        private void OnDisable()
        {
            SurgeryEvents.OnToolGrabbed -= HandleToolGrabbed;
            SurgeryEvents.OnToolReleased -= HandleToolReleased;
            SurgeryEvents.OnIncisionProgressed -= HandleIncisionProgressed;
            SurgeryEvents.OnObjectiveCompleted -= HandleObjectiveCompleted;
            SurgeryEvents.OnError -= HandleError;
        }

        /// <summary>
        /// Assigns the profile for an event.
        ///
        /// This writes the serialized list as well as the runtime lookup, so a registration made
        /// while building a scene is saved into that scene instead of evaporating. Writing only
        /// the lookup meant Awake later rebuilt from the (still empty) list and silently wiped
        /// every profile — leaving the game with no haptics at all.
        /// </summary>
        public void RegisterProfile(HapticEvent hapticEvent, HapticProfile profile)
        {
            if (profile == null)
            {
                return;
            }

            for (int i = 0; i < profiles.Count; i++)
            {
                if (profiles[i].hapticEvent == hapticEvent)
                {
                    EventProfile updated = profiles[i];
                    updated.profile = profile;
                    profiles[i] = updated;
                    _lookup[hapticEvent] = profile;
                    return;
                }
            }

            profiles.Add(new EventProfile { hapticEvent = hapticEvent, profile = profile });
            _lookup[hapticEvent] = profile;
        }

        public void Play(HapticEvent hapticEvent, IHandInteractor hand)
        {
            LastEvent = hapticEvent;

            if (!enableHaptics)
            {
                return;
            }

            IHandInteractor target = hand ?? _lastActiveHand;
            if (target == null)
            {
                return;
            }

            if (!_lookup.TryGetValue(hapticEvent, out HapticProfile profile) || profile == null)
            {
                return;
            }

            target.SendHapticFeedback(profile);
            DispatchedPulses++;
        }

        private void HandleToolGrabbed(SurgicalTool tool, IHandInteractor holder)
        {
            _lastActiveHand = holder;
            Play(HapticEvent.Grab, holder);
        }

        private void HandleToolReleased(SurgicalTool tool, IHandInteractor holder) =>
            Play(HapticEvent.Release, holder);

        private void HandleIncisionProgressed(SurgicalTool tool, float progress01) =>
            Play(HapticEvent.Incision, tool != null ? tool.Holder : null);

        private void HandleObjectiveCompleted(string objectiveId) =>
            Play(HapticEvent.Success, null);

        private void HandleError(ErrorSeverity severity, string message)
        {
            Play(severity == ErrorSeverity.Warning ? HapticEvent.Warning : HapticEvent.Error, null);
        }
    }
}
