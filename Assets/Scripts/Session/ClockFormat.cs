using UnityEngine;

namespace VRSurgery.Session
{
    /// <summary>
    /// One format for every clock a visitor can see: m:ss.mmm. The scoreboard ranks on the
    /// millisecond, so a clock that hides it hides the number people are comparing.
    /// </summary>
    public static class ClockFormat
    {
        // Reused so a label redrawn every frame costs one string, not one string plus boxing.
        private static readonly char[] Buffer = new char[9];

        /// <summary>
        /// Time left. Ceil, not round: a clock showing zero while the round is still winnable is a
        /// lie the audience can see.
        /// </summary>
        public static string Countdown(float seconds) =>
            Format(Mathf.CeilToInt(Mathf.Max(0f, seconds) * 1000f));

        /// <summary>Time spent, rounded to the nearest millisecond.</summary>
        public static string Elapsed(float seconds) =>
            Format(Mathf.RoundToInt(Mathf.Max(0f, seconds) * 1000f));

        private static string Format(int milliseconds)
        {
            int minutes = Mathf.Min(milliseconds / 60000, 99);
            int rest = milliseconds - minutes * 60000;
            int secs = rest / 1000;
            int ms = rest % 1000;

            int n = 0;
            if (minutes >= 10) { Buffer[n++] = (char)('0' + minutes / 10); }
            Buffer[n++] = (char)('0' + minutes % 10);
            Buffer[n++] = ':';
            Buffer[n++] = (char)('0' + secs / 10);
            Buffer[n++] = (char)('0' + secs % 10);
            Buffer[n++] = '.';
            Buffer[n++] = (char)('0' + ms / 100);
            Buffer[n++] = (char)('0' + ms / 10 % 10);
            Buffer[n++] = (char)('0' + ms % 10);
            return new string(Buffer, 0, n);
        }
    }
}
