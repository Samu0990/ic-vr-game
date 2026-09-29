using System.Collections.Generic;
using UnityEngine;
using VRSurgery.Interaction;

namespace VRSurgery.Transplant
{
    /// <summary>
    /// Carries the join from an instrument tip to whichever vessel site it is on.
    ///
    /// Separate from VesselAnastomosis on purpose: the site owns what it takes to be joined, this
    /// owns where the surgeon's hand is. That split is what lets the sites be tested with no rig
    /// in the scene, and what will let a needle holder, a stapler or a bare fingertip drive the
    /// same sites later without any of them knowing about each other.
    ///
    /// Only works while the instrument is actually held. An instrument lying on the tray inside a
    /// site's radius would otherwise sew the vessel by itself. A bare hand is the exception the
    /// class was always written for: there is nothing to put down, so there is nothing to hold.
    /// </summary>
    public class AnastomosisWorker : MonoBehaviour
    {
        [Tooltip("The working end. Defaults to this transform.")]
        [SerializeField] private Transform tip;

        [Tooltip("Require the instrument to be held. Off lets a bare tracked hand drive the sites.")]
        [SerializeField] private bool requireHeldInstrument = true;

        [Tooltip("Sites this instrument can join. Filled by the scene builder.")]
        [SerializeField] private List<VesselAnastomosis> sites = new List<VesselAnastomosis>();

        [Tooltip("Only joins while the procedure is at the vessel stage, so a visitor cannot sew " +
                 "a heart that is not in the chest yet.")]
        [SerializeField] private TransplantProcedure procedure;

        [Header("Needle holder (optional)")]
        [Tooltip("The needle's point. When set, the needle holder sews vessels too.")]
        [SerializeField] private Transform needleTip;
        [SerializeField] private SurgicalInteractable needleHolder;

        [Tooltip("How much faster a vessel is sewn with the needle than with the bare hand. The " +
                 "real instrument is rewarded, and it steadies the hand as it does in life.")]
        [SerializeField, Min(1f)] private float needleBonus = 1.5f;

        private SurgicalInteractable _interactable;

        /// <summary>True while the join in progress is being sewn with the needle holder.</summary>
        public bool SewingWithNeedle { get; private set; }

        /// <summary>The site currently being worked, if any. Drives the surgeon's prompt.</summary>
        public VesselAnastomosis ActiveSite { get; private set; }

        /// <summary>The sites this worker can join, in build order. Read by the feedback layer.</summary>
        public IReadOnlyList<VesselAnastomosis> Sites => sites;

        /// <summary>
        /// A site that is leaking right now, whether or not the hand is on it. The prompt has to
        /// keep naming the leak after the hand leaves it — that is exactly when the visitor
        /// thinks the join is finished and needs telling that it is not.
        /// </summary>
        public VesselAnastomosis BleedingSite
        {
            get
            {
                if (ActiveSite != null && ActiveSite.IsBleeding) { return ActiveSite; }

                for (int i = 0; i < sites.Count; i++)
                {
                    if (sites[i] != null && sites[i].IsBleeding) { return sites[i]; }
                }

                return null;
            }
        }

        private void Awake()
        {
            _interactable = GetComponent<SurgicalInteractable>();
            if (tip == null) { tip = transform; }
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Advances whatever join the tip is inside. Stepped by hand in tests.</summary>
        public void Tick(float deltaTime)
        {
            ActiveSite = null;
            SewingWithNeedle = false;

            if (deltaTime <= 0f) { return; }
            if (procedure != null && procedure.Stage != TransplantStage.ConnectVessels) { return; }

            // The needle first: if it is held on a site, that is the stitch being made.
            if (needleTip != null && needleHolder != null && needleHolder.IsHeld)
            {
                VesselAnastomosis sewn = Nearest(needleTip.position);
                if (sewn != null)
                {
                    sewn.Work(needleTip.position, deltaTime * needleBonus);
                    ActiveSite = sewn;
                    SewingWithNeedle = true;
                    return;
                }
            }

            if (tip == null) { return; }
            if (requireHeldInstrument && _interactable != null && !_interactable.IsHeld) { return; }

            VesselAnastomosis best = Nearest(tip.position);
            if (best == null) { return; }

            best.Work(tip.position, deltaTime);
            ActiveSite = best;
        }

        /// <summary>Nearest unjoined site the point is inside. Nearest rather than first: the sites overlap on a heart.</summary>
        private VesselAnastomosis Nearest(Vector3 point)
        {
            VesselAnastomosis best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < sites.Count; i++)
            {
                VesselAnastomosis site = sites[i];
                if (site == null || site.IsJoined) { continue; }

                float distance = Vector3.Distance(point, site.transform.position);
                if (distance <= site.Radius && distance < bestDistance)
                {
                    bestDistance = distance;
                    best = site;
                }
            }

            return best;
        }

        /// <summary>Lets the needle holder sew vessels too, faster than the bare hand.</summary>
        public void BindNeedle(Transform needle, SurgicalInteractable holder, float bonus = 1.5f)
        {
            needleTip = needle;
            needleHolder = holder;
            needleBonus = Mathf.Max(1f, bonus);
        }

        public void Bind(Transform workingTip, IEnumerable<VesselAnastomosis> vesselSites,
            TransplantProcedure transplant)
        {
            tip = workingTip != null ? workingTip : transform;
            sites = new List<VesselAnastomosis>(vesselSites);
            procedure = transplant;
        }
    }
}
