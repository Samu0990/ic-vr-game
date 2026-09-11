using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRSurgery.Ports
{
    /// <summary>
    /// The run: three or four port sites, and the question of whether they are all dealt with
    /// before the clock runs out.
    ///
    /// It owns no timing of its own. The booth's session owns the clock and the states; this
    /// owns only what "finished" means, which is every site resolved — closed cleanly, or bled
    /// and then packed. A site that bled and was controlled still counts, it just cost the
    /// seconds that decide the scoreboard.
    /// </summary>
    public class PortProcedure : MonoBehaviour
    {
        [SerializeField] private List<InsertionPort> ports = new List<InsertionPort>();

        public IReadOnlyList<InsertionPort> Ports => ports;

        public int Total => ports.Count;

        public int ResolvedCount
        {
            get
            {
                int resolved = 0;
                for (int i = 0; i < ports.Count; i++)
                {
                    if (ports[i] != null && ports[i].IsResolved) { resolved++; }
                }

                return resolved;
            }
        }

        /// <summary>Sites currently bleeding and waiting for gauze.</summary>
        public int BleedingCount
        {
            get
            {
                int bleeding = 0;
                for (int i = 0; i < ports.Count; i++)
                {
                    if (ports[i] != null && ports[i].State == PortState.Bleeding) { bleeding++; }
                }

                return bleeding;
            }
        }

        public bool IsComplete => Total > 0 && ResolvedCount == Total;

        /// <summary>How many sites went in without catching a vessel. Feeds the result screen.</summary>
        public int CleanCount
        {
            get
            {
                int clean = 0;
                for (int i = 0; i < ports.Count; i++)
                {
                    if (ports[i] != null && ports[i].State == PortState.Closed) { clean++; }
                }

                return clean;
            }
        }

        public event Action<InsertionPort, PortState> PortChanged;
        public event Action ProcedureCompleted;

        private bool _announced;

        private void OnEnable()
        {
            for (int i = 0; i < ports.Count; i++)
            {
                if (ports[i] != null) { ports[i].StateChanged += HandlePortChanged; }
            }
        }

        private void OnDisable()
        {
            for (int i = 0; i < ports.Count; i++)
            {
                if (ports[i] != null) { ports[i].StateChanged -= HandlePortChanged; }
            }
        }

        /// <summary>The nearest site to a point, so a tool can tell which one it is acting on.</summary>
        public InsertionPort NearestTo(Vector3 worldPoint, float maxDistance)
        {
            InsertionPort best = null;
            float bestDistance = maxDistance;

            for (int i = 0; i < ports.Count; i++)
            {
                InsertionPort port = ports[i];
                if (port == null) { continue; }

                float distance = Vector3.Distance(port.transform.position, worldPoint);
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    best = port;
                }
            }

            return best;
        }

        public void ResetProcedure()
        {
            _announced = false;
            for (int i = 0; i < ports.Count; i++)
            {
                if (ports[i] != null) { ports[i].ResetPort(); }
            }
        }

        public void Bind(IEnumerable<InsertionPort> sites)
        {
            for (int i = 0; i < ports.Count; i++)
            {
                if (ports[i] != null) { ports[i].StateChanged -= HandlePortChanged; }
            }

            ports = new List<InsertionPort>(sites);

            if (isActiveAndEnabled)
            {
                for (int i = 0; i < ports.Count; i++)
                {
                    if (ports[i] != null) { ports[i].StateChanged += HandlePortChanged; }
                }
            }
        }

        private void HandlePortChanged(InsertionPort port, PortState state)
        {
            PortChanged?.Invoke(port, state);

            if (!_announced && IsComplete)
            {
                _announced = true;
                ProcedureCompleted?.Invoke();
            }
        }
    }
}
