using System;
using UnityEngine;

namespace VRSurgery.Ports
{
    public enum PortState
    {
        /// <summary>Nothing has been put here yet.</summary>
        Pending,

        /// <summary>The trocar went in clear of the vessels. Nothing more to do.</summary>
        Closed,

        /// <summary>The trocar caught a vessel in the abdominal wall. Needs gauze.</summary>
        Bleeding,

        /// <summary>Gauze is holding. Counts as resolved, but it cost time.</summary>
        Controlled,
    }

    /// <summary>
    /// One port site on the abdominal wall: the decision the whole game is built around.
    ///
    /// A site is not pass/fail by position alone. It carries a safe zone and a vessel zone that
    /// overlap, which is the point the procedure is teaching — under transillumination the
    /// surgeon sees vessel shadows through the skin and picks a spot, and the two regions are not
    /// cleanly separated. Aiming at the centre of the safe zone is always right; the interesting
    /// part is the band where the two meet.
    ///
    /// Geometry is local to this transform, on its XZ plane, so a site can be laid anywhere on
    /// the body and still reason in its own millimetres.
    /// </summary>
    public class InsertionPort : MonoBehaviour
    {
        [Header("Zones (metres, local XZ)")]
        [Tooltip("Radius of the region clear of vessels. Entering inside this closes the port.")]
        [SerializeField, Min(0.001f)] private float safeRadius = 0.018f;

        [Tooltip("Centre of the vessel, offset from this transform. The overlap between this and " +
                 "the safe zone is the part the player has to read.")]
        [SerializeField] private Vector2 vesselOffset = new Vector2(0.014f, 0f);

        [Tooltip("Radius of the vessel's danger region.")]
        [SerializeField, Min(0.001f)] private float vesselRadius = 0.012f;

        [Header("Identity")]
        [SerializeField] private string portId = "port";

        public string PortId => string.IsNullOrEmpty(portId) ? name : portId;
        public float SafeRadius => safeRadius;
        public float VesselRadius => vesselRadius;
        public Vector2 VesselOffset => vesselOffset;

        public PortState State { get; private set; } = PortState.Pending;

        /// <summary>Where the trocar actually went in, in local space. Read by the scoring.</summary>
        public Vector3 EntryPointLocal { get; private set; }

        /// <summary>True once this site needs nothing further, however it got there.</summary>
        public bool IsResolved => State == PortState.Closed || State == PortState.Controlled;

        public event Action<InsertionPort, PortState> StateChanged;

        /// <summary>
        /// Drives the insertion. Returns the state the site landed in.
        ///
        /// Order matters: the vessel wins ties. A point inside both zones has a vessel under it —
        /// the safe zone is only safe where the vessel is not, and scoring it the other way round
        /// would teach exactly the wrong lesson.
        /// </summary>
        public PortState Insert(Vector3 worldPoint)
        {
            if (State != PortState.Pending)
            {
                return State;
            }

            Vector3 local = transform.InverseTransformPoint(worldPoint);
            EntryPointLocal = local;

            Vector2 flat = new Vector2(local.x, local.z);

            if (Vector2.Distance(flat, vesselOffset) <= vesselRadius)
            {
                SetState(PortState.Bleeding);
                return State;
            }

            if (flat.magnitude <= safeRadius)
            {
                SetState(PortState.Closed);
                return State;
            }

            // Off the site entirely. Missing the marked area is its own kind of wrong, and it
            // bleeds: the abdominal wall outside a planned port is not safer, just unplanned.
            SetState(PortState.Bleeding);
            return State;
        }

        /// <summary>Gauze packed onto a bleeding site. Does nothing to a site that is not bleeding.</summary>
        public bool ApplyGauze()
        {
            if (State != PortState.Bleeding)
            {
                return false;
            }

            SetState(PortState.Controlled);
            return true;
        }

        public void ResetPort()
        {
            EntryPointLocal = Vector3.zero;
            SetState(PortState.Pending);
        }

        /// <summary>Distance from a world point to this site's centre, on the wall's plane.</summary>
        public float PlanarDistanceTo(Vector3 worldPoint)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            return new Vector2(local.x, local.z).magnitude;
        }

        public void Configure(string id, float safe, Vector2 vessel, float vesselR)
        {
            portId = id;
            safeRadius = Mathf.Max(0.001f, safe);
            vesselOffset = vessel;
            vesselRadius = Mathf.Max(0.001f, vesselR);
        }

        private void SetState(PortState next)
        {
            if (next == State)
            {
                return;
            }

            State = next;
            StateChanged?.Invoke(this, State);
        }
    }
}
