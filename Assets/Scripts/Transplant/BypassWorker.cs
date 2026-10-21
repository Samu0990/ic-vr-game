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

        [Tooltip("Steps this one hold performs, in order. More than one on the easier levels, " +
                 "where several steps collapse into a single gesture.")]
        //
        // Serialized, and that is the whole point. The scene is generated in the Editor and
        // saved; a [NonSerialized] chain is rebuilt at build time and then thrown away when the
        // file is written. Opening the saved scene brought the site back with an empty chain, so
        // the collapsed Médio gesture — cannulate, cross-clamp, cardioplegia in one hold —
        // degraded to its single Step, cardioplegia. The procedure was still waiting on
        // cannulation, refused it, and kept refusing it: the operation could not be finished at
        // all from the saved scene, only from one freshly built in the Editor.
        public BypassStep[] Chain = Array.Empty<BypassStep>();

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
    /// <summary>
    /// Something outside the pump that can hold a bypass step back — a heart in fibrillation
    /// cannot be de-aired and weaned. Returns the clinical reason, or null to let it through.
    /// </summary>
    public interface IBypassGate
    {
        string Blocks(BypassStep step);
    }

    public class BypassWorker : MonoBehaviour
    {
        [SerializeField] private Transform tip;
        [SerializeField] private List<BypassSite> sites = new List<BypassSite>();
        [SerializeField] private TransplantProcedure procedure;

        [Tooltip("Require the instrument to be held. Off lets a bare tracked hand drive the sites.")]
        [SerializeField] private bool requireHeldInstrument = true;

        [Tooltip("Components implementing IBypassGate, asked before each step.")]
        [SerializeField] private List<MonoBehaviour> gates = new List<MonoBehaviour>();

        private SurgicalInteractable _interactable;

        /// <summary>The site being worked, if any. Drives the surgeon's prompt.</summary>
        /// <summary>
        /// Where the step the operation is waiting for has to be performed, or null when the
        /// pump is done. Reads the gesture's own chain, so on the easier levels it points at the
        /// one hold that performs three steps rather than at a site the player never touches.
        /// </summary>
        public Transform NextPoint
        {
            get
            {
                if (procedure == null || sites == null) { return null; }

                BypassStep expected = procedure.Bypass.NextStep;
                for (int i = 0; i < sites.Count; i++)
                {
                    BypassSite site = sites[i];
                    if (site?.Point == null) { continue; }

                    BypassStep first = site.Chain != null && site.Chain.Length > 0
                        ? site.Chain[0]
                        : site.Step;

                    if (first == expected) { return site.Point; }
                }

                return null;
            }
        }

        public BypassSite ActiveSite { get; private set; }

        /// <summary>Last clinical refusal, for the room to show. Cleared when a step succeeds.</summary>
        public string LastRefusal { get; private set; } = string.Empty;

        /// <summary>Every site this worker can drive, in build order. Read by the markers and the progress ring.</summary>
        public IReadOnlyList<BypassSite> Sites => sites;

        /// <summary>How fast holding counts, 1 normally. Raised by the pace assist.</summary>
        public float SpeedMultiplier { get; set; } = 1f;

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

            // The patient can hold a step back too, before any time is banked on it.
            string blocked = GateReason(best.Chain != null && best.Chain.Length > 0 ? best.Chain[0] : best.Step);
            if (!string.IsNullOrEmpty(blocked))
            {
                best.Held = 0f;
                Refuse(blocked);
                return;
            }

            // A collapsed gesture is named/positioned by its final step, but it must be admitted
            // by the first step it performs. Medium's "enter bypass" gesture, for example, is
            // anchored at cardioplegia while the procedure is still waiting for cannulation.
            // Comparing the anchor here made that valid gesture look out of order forever.
            BypassStep firstStep = best.Chain != null && best.Chain.Length > 0
                ? best.Chain[0]
                : best.Step;

            // Working the wrong site is refused before any time is banked, so a visitor cannot
            // hold on the clamp for six seconds and only then be told it was the wrong move.
            if (firstStep != expected)
            {
                BypassAttempt preview = procedure.Bypass.Why(firstStep, procedure.IsImplantComplete);
                if (!preview.Accepted)
                {
                    Refuse(preview.Reason);
                    return;
                }
            }

            best.Held += deltaTime * Mathf.Max(0.1f, SpeedMultiplier);
            if (best.Held < best.Seconds) { return; }

            // A gesture may carry several steps on the easier levels. They still go through
            // Attempt one at a time and in order, so a collapsed gesture cannot get a patient past
            // a rule that the six-gesture version would have caught.
            BypassStep[] chain = best.Chain != null && best.Chain.Length > 0
                ? best.Chain
                : new[] { best.Step };

            best.Held = 0f;

            foreach (BypassStep step in chain)
            {
                BypassAttempt attempt = procedure.Bypass.Attempt(step, procedure.IsImplantComplete);
                if (!attempt.Accepted)
                {
                    Refuse(attempt.Reason);
                    return;
                }

                LastRefusal = string.Empty;
                StepPerformed?.Invoke(step);
            }

            // The pump owns the safety checks, but the hand gesture owns completion. Without
            // this join the monitor moved on to "retire o clampe" while the operation remained
            // stuck in GoOnBypass, so the native heart could never become removable.
            if (procedure.Stage == TransplantStage.GoOnBypass && procedure.Bypass.IsArrested)
            {
                procedure.CompleteStage(TransplantStage.GoOnBypass);
            }
            else if (procedure.Stage == TransplantStage.Restart && procedure.Bypass.IsOff)
            {
                procedure.CompleteStage(TransplantStage.Restart);
            }
        }

        private string GateReason(BypassStep step)
        {
            for (int i = 0; i < gates.Count; i++)
            {
                if (gates[i] is IBypassGate gate && gates[i].isActiveAndEnabled)
                {
                    string reason = gate.Blocks(step);
                    if (!string.IsNullOrEmpty(reason)) { return reason; }
                }
            }

            return null;
        }

        /// <summary>Adds a gate that may hold a step back. Used by the scene builder.</summary>
        public void AddGate(MonoBehaviour gate)
        {
            if (gate is IBypassGate && !gates.Contains(gate)) { gates.Add(gate); }
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
