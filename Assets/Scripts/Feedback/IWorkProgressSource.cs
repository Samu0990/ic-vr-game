using UnityEngine;

namespace VRSurgery.Feedback
{
    /// <summary>
    /// Anything the surgeon performs by holding still or by moving along a path, reported in the
    /// one shape the progress ring knows how to draw. Workers that predate this interface are
    /// polled directly by <see cref="WorkProgressIndicator"/> instead of being changed to fit it.
    /// </summary>
    public interface IWorkProgressSource
    {
        /// <summary>True while the hand or instrument is actively advancing this work.</summary>
        bool IsWorking { get; }

        /// <summary>0..1 toward finishing the current gesture.</summary>
        float WorkProgress01 { get; }

        /// <summary>Where the ring should be drawn, in world space.</summary>
        Vector3 WorkPoint { get; }

        /// <summary>Short pt-BR label, e.g. "INCISÃO".</summary>
        string WorkLabel { get; }

        /// <summary>True when the progress is an emergency being fixed (red ring) rather than a step.</summary>
        bool IsAlarm { get; }
    }
}
