using System;
using UnityEngine;

namespace VRSurgery.Feedback
{
    /// <summary>
    /// The clock on the theatre wall, showing the real time of day. A small thing, but a room with
    /// a stopped clock reads as a set; one that ticks reads as a place.
    /// </summary>
    public class WallClock : MonoBehaviour
    {
        [SerializeField] private Transform hourHand;
        [SerializeField] private Transform minuteHand;
        [SerializeField] private Transform secondHand;

        private void Update() => Show(DateTime.Now);

        /// <summary>Hands rotate about the clock's local Z. The face is seen from local -Z (like a Unity quad), from where a negative angle turns clockwise.</summary>
        public void Show(DateTime time)
        {
            float seconds = time.Second;
            float minutes = time.Minute + seconds / 60f;
            float hours = (time.Hour % 12) + minutes / 60f;

            if (hourHand != null) { hourHand.localRotation = Quaternion.Euler(0f, 0f, -hours * 30f); }
            if (minuteHand != null) { minuteHand.localRotation = Quaternion.Euler(0f, 0f, -minutes * 6f); }
            if (secondHand != null) { secondHand.localRotation = Quaternion.Euler(0f, 0f, -seconds * 6f); }
        }

        public void Bind(Transform hour, Transform minute, Transform second)
        {
            hourHand = hour;
            minuteHand = minute;
            secondHand = second;
        }
    }
}
