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

        /// <summary>
        /// Ticks when this run was filed. Breaks ties: the concept is explicit that on an equal
        /// time, whoever reached it first keeps the higher place — the visitor who set the bar
        /// should not be pushed down by someone who merely matched it.
        /// </summary>
        public long AchievedAtTicks;

        /// <summary>Whole milliseconds, which is the unit the stand's scoreboard shows.</summary>
        public int Milliseconds => Mathf.RoundToInt(Seconds * 1000f);

        public LeaderboardEntry(string name, float seconds, int score, long achievedAtTicks)
        {
            Name = name;
            Seconds = seconds;
            Score = score;
            AchievedAtTicks = achievedAtTicks;
        }

        /// <summary>"12.487s" — the stand reads to the millisecond, so ties are rare and real.</summary>
        public string TimeLabel => $"{Seconds:F3}s";
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
                result.Score,
                DateTime.UtcNow.Ticks));

            SortEntries();

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

        /// <summary>
        /// Fastest first; on an identical time, whoever got there first stays ahead. Compared on
        /// whole milliseconds rather than raw floats, so two runs the scoreboard displays as
        /// equal are actually treated as equal instead of being separated by float noise the
        /// audience cannot see.
        /// </summary>
        private void SortEntries()
        {
            _entries.Sort((a, b) =>
            {
                int byTime = a.Milliseconds.CompareTo(b.Milliseconds);
                return byTime != 0 ? byTime : a.AchievedAtTicks.CompareTo(b.AchievedAtTicks);
            });
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
                    SortEntries();
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
