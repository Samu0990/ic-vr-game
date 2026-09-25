using System;
using UnityEngine;
using VRSurgery.Tools;

namespace VRSurgery.Ports
{
    /// <summary>
    /// The gauze. Packed onto a bleeding site to hold it.
    ///
    /// It has to be held against the site rather than dropped near it: a pad that stops bleeding
    /// the instant it touches turns the recovery from a mistake into a formality, and the concept
    /// wants the cost of a bad entry to be felt in seconds.
    /// </summary>
    public class GauzeTool : SurgicalTool
    {
        [SerializeField] private Transform padCentre;
        [SerializeField] private PortProcedure procedure;

        [Tooltip("How near a bleeding site the pad has to sit to be packing it, in metres.")]
        [SerializeField, Min(0.005f)] private float packRadius = 0.03f;

        [Tooltip("Seconds the pad must be held on the site before the bleeding is controlled.")]
        [SerializeField, Min(0.1f)] private float secondsToControl = 1.2f;

        /// <summary>Progress against the current site, 0..1. Drives the audience's readout.</summary>
        public float PackProgress01 { get; private set; }

        public InsertionPort PackingPort { get; private set; }

        public event Action<InsertionPort> PortControlled;

        private float _held;

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Stepped by hand in tests, by the frame in play.</summary>
        public void Tick(float deltaTime)
        {
            if (procedure == null || padCentre == null)
            {
                Release();
                return;
            }

            InsertionPort near = procedure.NearestTo(padCentre.position, packRadius);
            if (near == null || near.State != PortState.Bleeding)
            {
                Release();
                return;
            }

            // Moving to a different site restarts the clock: pressure is only pressure if it
            // stays in one place.
            if (near != PackingPort)
            {
                PackingPort = near;
                _held = 0f;
            }

            _held += deltaTime;
            PackProgress01 = Mathf.Clamp01(_held / secondsToControl);

            if (_held >= secondsToControl)
            {
                InsertionPort controlled = near;
                if (controlled.ApplyGauze())
                {
                    PortControlled?.Invoke(controlled);
                }

                Release();
            }
        }

        private void Release()
        {
            PackingPort = null;
            PackProgress01 = 0f;
            _held = 0f;
        }

        public void Bind(PortProcedure portProcedure, Transform pad)
        {
            procedure = portProcedure;
            padCentre = pad;
        }
    }
}
