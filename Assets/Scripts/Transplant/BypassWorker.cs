using System;
using System.Collections.Generic;
using UnityEngine;
using VRSurgery.Interaction;

namespace VRSurgery.Transplant
{
    /// <summary>
    /// One place on the patient where a bypass step is performed — the cannulation site, the
    /// cross-clamp, the cardioplegia cannula.
    ///
    /// Geometry and nothing else. What the step means and whether it is allowed lives in
    /// BypassProcedure, which has no Unity in it; this only says where the surgeon has to put
    /// their hands.
    /// </summary>
    [Serializable]
    public class BypassSite
    {
        public BypassStep Step;
        public Transform Point;

        [Min(0.005f)] public float Radius = 0.03f;

        [Tooltip("Seconds of steady contact. Six steps at this pace is about the forty seconds " +
                 "bypass is allowed in a four-minute operation.")]
        [Min(0.1f)] public float Seconds = 6.5f;

        [NonSerialized] public float Held;

        public float Progress01 => Seconds <= 0f ? 0f : Mathf.Clamp01(Held / Seconds);
    }

    /// <summary>
    /// Carries the surgeon's hand to the bypass sites, on the same terms as the anastomoses: the
    /// instrument has to be held, inside the radius, for a while. Drifting off costs ground
    /// without losing it, because punishing a tremor punishes everyone at a stand.
    ///
    /// A refused step is reported rather than swallowed. The refusal carries the clinical reason,
    /// and the reason is the whole point of modelling bypass at all — a visitor who reaches for
    /// the cardioplegia before the clamp should learn why that does nothing, not watch a gesture
    /// fail silently.
    /// </summary>
    public class BypassWorker : MonoBehaviour
    {
        [SerializeField] private Transform tip;
        [SerializeField] private List<BypassSite> sites = new List<BypassSite>();
        [SerializeField] private TransplantProcedure procedure;

        [Tooltip("Require the instrument to be held. Off lets a bare tracked hand drive the sites.")]
        [SerializeField] private bool requireHeldInstrument = true;

        private SurgicalInteractable _interactable;

        /// <summary>The site being worked, if any. Drives the surgeon's prompt.</summary>
        public BypassSite ActiveSite { get; private set; }

        /// <summary>Last clinical refusal, for the room to show. Cleared when a step succeeds.</summary>
        public string LastRefusal { get; private set; } = string.Empty;

        public event Action<BypassStep> StepPerformed;
        public event Action<string> StepRefused;

        private void Awake()
        {
            _interactable = GetComponent<SurgicalInteractable>();
            if (tip == null) { tip = transform; }
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Advances whichever site the tip is inside. Stepped by hand in tests.</summary>
        public void Tick(float deltaTime)
        {
            ActiveSite = null;

            if (deltaTime <= 0f || tip == null || procedure == null) { return; }

            bool held = !requireHeldInstrument || _interactable == null || _interactable.IsHeld;
            if (!held) { return; }

            BypassStep expected = procedure.Bypass.NextStep;

            BypassSite best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < sites.Count; i++)
            {
                BypassSite site = sites[i];
                if (site?.Point == null) { continue; }

                float distance = Vector3.Distance(tip.position, site.Point.position);
                if (distance <= site.Radius && distance < bestDistance)
                {
                    bestDistance = distance;
                    best = site;
                }
            }

            // Everything the hand is not on decays, so leaving a half-done step and coming back
            // later does not bank progress indefinitely.
            for (int i = 0; i < sites.Count; i++)
            {
                if (sites[i] != null && sites[i] != best)
                {
                    sites[i].Held = Mathf.Max(0f, sites[i].Held - deltaTime);
                }
            }

            if (best == null) { return; }

            ActiveSite = best;

            // Working the wrong site is refused before any time is banked, so a visitor cannot
            // hold on the clamp for six seconds and only then be told it was the wrong move.
            if (best.Step != expected)
            {
                BypassAttempt preview = procedure.Bypass.Why(best.Step, procedure.IsImplantComplete);
                if (!preview.Accepted)
                {
                    Refuse(preview.Reason);
                    return;
                }
            }

            best.Held += deltaTime;
            if (best.Held < best.Seconds) { return; }

            BypassAttempt attempt = procedure.Bypass.Attempt(best.Step, procedure.IsImplantComplete);
            if (attempt.Accepted)
            {
                best.Held = 0f;
                LastRefusal = string.Empty;
                StepPerformed?.Invoke(best.Step);
            }
            else
            {
                best.Held = 0f;
                Refuse(attempt.Reason);
            }
        }

        private void Refuse(string reason)
        {
            if (string.IsNullOrEmpty(reason) || reason == LastRefusal) { return; }

            LastRefusal = reason;
            StepRefused?.Invoke(reason);
        }

        public void Bind(Transform workingTip, List<BypassSite> bypassSites, TransplantProcedure transplant)
        {
            tip = workingTip != null ? workingTip : transform;
            sites = bypassSites;
            procedure = transplant;
        }
    }
}
