using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRSurgery.Session
{
    /// <summary>One visitor's finished run, as the scoreboard remembers it.</summary>
    [Serializable]
    public struct LeaderboardEntry
    {
        public string Name;
        public float Seconds;
        public int Score;

        public LeaderboardEntry(string name, float seconds, int score)
        {
            Name = name;
            Seconds = seconds;
            Score = score;
        }
    }

    /// <summary>
    /// The day's best times. Ranked on seconds — the fastest control of the bleeding wins, which
    /// is the number the stand puts on the screen between visitors.
    ///
    /// Persisted so a crash or a battery swap mid-morning does not wipe the queue's results. It
    /// deliberately keeps only a handful of rows: a stand's screen cannot show more than that,
    /// and an all-time table would stop being beatable by lunchtime.
    /// </summary>
    public class Leaderboard : MonoBehaviour
    {
        private const string StorageKey = "VRSurgery.Leaderboard";

        [Tooltip("How many rows the scoreboard keeps and shows.")]
        [SerializeField, Min(1)] private int capacity = 5;

        [Tooltip("Clear the table when the stand opens, so each event day starts empty.")]
        [SerializeField] private bool clearOnStart = false;

        [Tooltip("Optional. When set, finished rounds are filed automatically.")]
        [SerializeField] private EventSessionController session;

        [Tooltip("Filed under this until the visitor can type a name. PENDING: the concept has " +
                 "them registering one, which needs a keyboard the headset can show.")]
        [SerializeField] private string pendingName = "Anônimo";

        private readonly List<LeaderboardEntry> _entries = new List<LeaderboardEntry>();

        public IReadOnlyList<LeaderboardEntry> Entries => _entries;

        public bool HasAny => _entries.Count > 0;

        /// <summary>The fastest run so far, or null while nobody has finished one.</summary>
        public LeaderboardEntry? Best => _entries.Count > 0 ? _entries[0] : (LeaderboardEntry?)null;

        public event Action Changed;

        private void Awake()
        {
            if (clearOnStart)
            {
                Clear();
                return;
            }

            Load();
        }

        private void OnEnable()
        {
            if (session != null)
            {
                session.RoundEnded += HandleRoundEnded;
            }
        }

        private void OnDisable()
        {
            if (session != null)
            {
                session.RoundEnded -= HandleRoundEnded;
            }
        }

        private void HandleRoundEnded(SessionResult result) => Submit(pendingName, result);

        public void Bind(EventSessionController controller)
        {
            if (session != null)
            {
                session.RoundEnded -= HandleRoundEnded;
            }

            session = controller;

            if (session != null && isActiveAndEnabled)
            {
                session.RoundEnded += HandleRoundEnded;
            }
        }

        /// <summary>
        /// Files a finished run. Only successes are ranked: a visitor who ran out of time has no
        /// time to rank, and putting failures on the board would bury the times worth beating.
        /// </summary>
        public bool Submit(string name, SessionResult result)
        {
            if (!result.Succeeded)
            {
                return false;
            }

            _entries.Add(new LeaderboardEntry(
                string.IsNullOrWhiteSpace(name) ? "Anônimo" : name.Trim(),
                result.ElapsedSeconds,
                result.Score));

            _entries.Sort((a, b) => a.Seconds.CompareTo(b.Seconds));

            if (_entries.Count > capacity)
            {
                _entries.RemoveRange(capacity, _entries.Count - capacity);
            }

            Save();
            Changed?.Invoke();

            // Whether the run actually made the table, which is what the result screen wants to
            // shout about — not merely that it was fast enough to finish.
            return _entries.Exists(e => Mathf.Approximately(e.Seconds, result.ElapsedSeconds));
        }

        public void Clear()
        {
            _entries.Clear();
            PlayerPrefs.DeleteKey(StorageKey);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        [Serializable]
        private class Payload
        {
            public List<LeaderboardEntry> entries = new List<LeaderboardEntry>();
        }

        private void Load()
        {
            _entries.Clear();

            string raw = PlayerPrefs.GetString(StorageKey, string.Empty);
            if (string.IsNullOrEmpty(raw))
            {
                return;
            }

            try
            {
                Payload payload = JsonUtility.FromJson<Payload>(raw);
                if (payload?.entries != null)
                {
                    _entries.AddRange(payload.entries);
                    _entries.Sort((a, b) => a.Seconds.CompareTo(b.Seconds));
                }
            }
            catch (Exception exception)
            {
                // A corrupt table must not take the stand down mid-event; an empty board is a
                // recoverable disappointment, an exception loop on load is not.
                Debug.LogWarning($"[Leaderboard] Stored table unreadable, starting empty: {exception.Message}");
                _entries.Clear();
            }
        }

        private void Save()
        {
            Payload payload = new Payload();
            payload.entries.AddRange(_entries);
            PlayerPrefs.SetString(StorageKey, JsonUtility.ToJson(payload));
            PlayerPrefs.Save();
        }
    }
}
