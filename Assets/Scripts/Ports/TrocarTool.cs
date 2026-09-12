using System;
using UnityEngine;
using VRSurgery.Tools;

namespace VRSurgery.Ports
{
    /// <summary>
    /// The trocar. Its whole job is to decide, once per site, where the tip was when it went
    /// through the wall.
    ///
    /// Insertion is committed on depth, not on touch: the tip has to actually travel into the
    /// wall past a threshold, so brushing across the abdomen does not fire off three ports in a
    /// row. That also makes the gesture read like the real one — it is a push, not a tap.
    /// </summary>
    public class TrocarTool : SurgicalTool
    {
        [Header("Tip")]
        [SerializeField] private Transform tip;

        [Header("Insertion")]
        [Tooltip("How far the tip must travel into the wall before the port is committed, in metres.")]
        [SerializeField, Min(0.001f)] private float insertionDepth = 0.012f;

        [Tooltip("How near a site the tip has to be for that site to be the one being entered.")]
        [SerializeField, Min(0.005f)] private float siteSearchRadius = 0.06f;

        [SerializeField] private PortProcedure procedure;

        /// <summary>The site the tip is currently over, if any.</summary>
        public InsertionPort HoveredPort { get; private set; }

        /// <summary>How far into the wall the tip has travelled at the hovered site, 0..1.</summary>
        public float Penetration01 { get; private set; }

        public Transform Tip => tip;

        public event Action<InsertionPort, PortState> PortEntered;

        private InsertionPort _committing;

        private void Update()
        {
            if (tip == null || procedure == null)
            {
                HoveredPort = null;
                Penetration01 = 0f;
                return;
            }

            InsertionPort near = procedure.NearestTo(tip.position, siteSearchRadius);
            HoveredPort = near;

            if (near == null || near.State != PortState.Pending)
            {
                Penetration01 = 0f;
                _committing = null;
                return;
            }

            // Depth is measured along the site's own normal, so a wall that is not axis-aligned
            // still reads a straight push as a straight push.
            Vector3 local = near.transform.InverseTransformPoint(tip.position);
            float depth = -local.y;

            Penetration01 = Mathf.Clamp01(depth / insertionDepth);

            if (depth >= insertionDepth && _committing != near)
            {
                _committing = near;
                PortState result = near.Insert(tip.position);
                PortEntered?.Invoke(near, result);
            }
            else if (depth <= 0f)
            {
                _committing = null;
            }
        }

        public void Bind(PortProcedure portProcedure, Transform trocarTip)
        {
            procedure = portProcedure;
            tip = trocarTip;
        }
    }
}
