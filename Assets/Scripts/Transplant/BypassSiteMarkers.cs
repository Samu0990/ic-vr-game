using System.Collections.Generic;
using UnityEngine;

namespace VRSurgery.Transplant
{
    /// <summary>
    /// Shows the bypass site the pump is waiting on, and only that one.
    ///
    /// The four cannulation and clamp sites were bare transforms with a 2.8cm radius and nothing
    /// drawn: finding them meant sweeping a hand around the heart until something happened. All
    /// of them lit at once would be no better — the whole lesson of this stage is the order — so
    /// each site's ring appears when its step is next, pulses so it reads as "here", and hides
    /// again once the pump has moved on.
    /// </summary>
    public class BypassSiteMarkers : MonoBehaviour
    {
        [SerializeField] private TransplantProcedure procedure;
        [SerializeField] private BypassWorker worker;

        [Tooltip("One ring per bypass site, in the same order as the worker's sites.")]
        [SerializeField] private List<Renderer> rings = new List<Renderer>();

        [SerializeField] private Color color = new Color(1f, 0.82f, 0.25f, 0.8f);

        [SerializeField, Min(0.1f)] private float pulseHz = 1.2f;

        private float _clock;

        /// <summary>Which rings are showing, for tests. Same order as the worker's sites.</summary>
        public bool IsShowing(int index) => index >= 0 && index < rings.Count && rings[index] != null && rings[index].enabled;

        private void Update() => Tick(Time.deltaTime);

        public void Tick(float deltaTime)
        {
            _clock += Mathf.Max(0f, deltaTime);

            IReadOnlyList<BypassSite> sites = worker != null ? worker.Sites : null;
            bool pumpStage = procedure != null &&
                             (procedure.Stage == TransplantStage.GoOnBypass || procedure.Stage == TransplantStage.Restart);
            BypassStep next = procedure != null ? procedure.Bypass.NextStep : BypassStep.NotStarted;

            float pulse = 0.55f + 0.45f * Mathf.Sin(_clock * pulseHz * Mathf.PI * 2f);

            for (int i = 0; i < rings.Count; i++)
            {
                Renderer ring = rings[i];
                if (ring == null) { continue; }

                bool show = pumpStage && sites != null && i < sites.Count && Carries(sites[i], next);
                if (ring.enabled != show) { ring.enabled = show; }

                if (show && ring.sharedMaterial != null)
                {
                    Color c = color;
                    c.a = color.a * pulse;
                    ring.sharedMaterial.color = c;
                    ring.transform.localScale = Vector3.one * (0.9f + 0.2f * pulse);
                }
            }
        }

        private static bool Carries(BypassSite site, BypassStep step)
        {
            if (site == null) { return false; }
            if (site.Step == step) { return true; }
            if (site.Chain == null) { return false; }

            for (int i = 0; i < site.Chain.Length; i++)
            {
                if (site.Chain[i] == step) { return true; }
            }

            return false;
        }

        public void Bind(TransplantProcedure transplant, BypassWorker bypassWorker, IEnumerable<Renderer> siteRings)
        {
            procedure = transplant;
            worker = bypassWorker;
            rings = new List<Renderer>(siteRings);
        }
    }
}
