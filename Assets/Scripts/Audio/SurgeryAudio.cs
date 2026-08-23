using System.Collections.Generic;
using UnityEngine;
using VRSurgery.Interaction;
using VRSurgery.Surgery;
using VRSurgery.Tools;

namespace VRSurgery.Audio
{
    public enum SurgerySound
    {
        ToolGrab,
        ToolRelease,
        TissueContact,
        Incision,
        BleedingStart,
        BleedingControlled,
        ObjectiveComplete,
        Error,
        SurgeryComplete,
    }

    /// <summary>
    /// Spatial audio for the procedure. Listens to the event bus rather than being called by
    /// tools, so no tool ever holds an AudioSource reference.
    /// </summary>
    public class SurgeryAudio : MonoBehaviour
    {
        [System.Serializable]
        private struct SoundEntry
        {
            public SurgerySound sound;
            public AudioClip clip;
            [Range(0f, 1f)] public float volume;
        }

        [SerializeField] private List<SoundEntry> clips = new List<SoundEntry>();
        [SerializeField] private AudioSource source;

        [Tooltip("Minimum seconds between repeats of the same sound, to stop incision ticks machine-gunning.")]
        [SerializeField] private float repeatCooldown = 0.06f;

        private readonly Dictionary<SurgerySound, SoundEntry> _lookup = new Dictionary<SurgerySound, SoundEntry>();
        private readonly Dictionary<SurgerySound, float> _lastPlayed = new Dictionary<SurgerySound, float>();

        /// <summary>Number of play requests accepted. Exposed for tests and the debug overlay.</summary>
        public int PlayCount { get; private set; }

        public SurgerySound? LastSound { get; private set; }

        private void Awake()
        {
            if (source == null)
            {
                source = GetComponent<AudioSource>();
            }

            _lookup.Clear();
            for (int i = 0; i < clips.Count; i++)
            {
                _lookup[clips[i].sound] = clips[i];
            }
        }

        private void OnEnable()
        {
            SurgeryEvents.OnToolGrabbed += HandleToolGrabbed;
            SurgeryEvents.OnToolReleased += HandleToolReleased;
            SurgeryEvents.OnIncisionStarted += HandleIncisionStarted;
            SurgeryEvents.OnIncisionProgressed += HandleIncisionProgressed;
            SurgeryEvents.OnBleedingStarted += HandleBleedingStarted;
            SurgeryEvents.OnBleedingStopped += HandleBleedingStopped;
            SurgeryEvents.OnObjectiveCompleted += HandleObjectiveCompleted;
            SurgeryEvents.OnSurgeryCompleted += HandleSurgeryCompleted;
            SurgeryEvents.OnError += HandleError;
        }

        private void OnDisable()
        {
            SurgeryEvents.OnToolGrabbed -= HandleToolGrabbed;
            SurgeryEvents.OnToolReleased -= HandleToolReleased;
            SurgeryEvents.OnIncisionStarted -= HandleIncisionStarted;
            SurgeryEvents.OnIncisionProgressed -= HandleIncisionProgressed;
            SurgeryEvents.OnBleedingStarted -= HandleBleedingStarted;
            SurgeryEvents.OnBleedingStopped -= HandleBleedingStopped;
            SurgeryEvents.OnObjectiveCompleted -= HandleObjectiveCompleted;
            SurgeryEvents.OnSurgeryCompleted -= HandleSurgeryCompleted;
            SurgeryEvents.OnError -= HandleError;
        }

        public void Play(SurgerySound sound)
        {
            if (_lastPlayed.TryGetValue(sound, out float last) && Time.time - last < repeatCooldown)
            {
                return;
            }

            _lastPlayed[sound] = Time.time;
            LastSound = sound;
            PlayCount++;

            if (source == null || !_lookup.TryGetValue(sound, out SoundEntry entry) || entry.clip == null)
            {
                // No clip authored yet: the event still counts as fired so the wiring is testable
                // and the missing asset shows up as silence rather than a null reference.
                return;
            }

            source.PlayOneShot(entry.clip, entry.volume <= 0f ? 1f : entry.volume);
        }

        private void HandleToolGrabbed(SurgicalTool tool, IHandInteractor holder) => Play(SurgerySound.ToolGrab);
        private void HandleToolReleased(SurgicalTool tool, IHandInteractor holder) => Play(SurgerySound.ToolRelease);
        private void HandleIncisionStarted(SurgicalTool tool) => Play(SurgerySound.TissueContact);
        private void HandleIncisionProgressed(SurgicalTool tool, float progress01) => Play(SurgerySound.Incision);
        private void HandleBleedingStarted() => Play(SurgerySound.BleedingStart);
        private void HandleBleedingStopped() => Play(SurgerySound.BleedingControlled);
        private void HandleObjectiveCompleted(string id) => Play(SurgerySound.ObjectiveComplete);
        private void HandleSurgeryCompleted() => Play(SurgerySound.SurgeryComplete);
        private void HandleError(ErrorSeverity severity, string message) => Play(SurgerySound.Error);
    }
}
