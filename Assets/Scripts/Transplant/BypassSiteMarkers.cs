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
    ///
    /// A small flat ring inside the chest was not enough to find: it sits among the organs and
    /// nothing on it says what it is. Over the active ring float an arrow pointing down at it
    /// and a label with the step's number and name, turned to face the visitor.
    /// </summary>
    public class BypassSiteMarkers : MonoBehaviour
    {
        [SerializeField] private TransplantProcedure procedure;
        [SerializeField] private BypassWorker worker;

        [Tooltip("One ring per bypass site, in the same order as the worker's sites.")]
        [SerializeField] private List<Renderer> rings = new List<Renderer>();

        [SerializeField] private Color color = new Color(1f, 0.82f, 0.25f, 1f);

        [SerializeField, Min(0.1f)] private float pulseHz = 1.2f;

        [Header("Arrow and label over the active ring")]
        [SerializeField] private Transform arrow;
        [SerializeField] private Transform labelRoot;
        [SerializeField] private TextMesh label;
        [SerializeField] private Transform viewer;
        [SerializeField] private float arrowHeight = 0.09f;
        [SerializeField] private float arrowBounce = 0.02f;
        [SerializeField] private float labelHeight = 0.2f;
        [SerializeField] private float spinDegreesPerSecond = 90f;

        private float _clock;
        private string _labelShown = string.Empty;

        /// <summary>Which rings are showing, for tests. Same order as the worker's sites.</summary>
        public bool IsShowing(int index) => index >= 0 && index < rings.Count && rings[index] != null && rings[index].enabled;

        /// <summary>True while the arrow and label are up over a ring.</summary>
        public bool IsBeaconShowing => arrow != null && arrow.gameObject.activeSelf;

        /// <summary>What the label says now, empty when it is hidden.</summary>
        public string LabelText => IsBeaconShowing ? _labelShown : string.Empty;

        private void Awake() => SetBeacon(false);

        private void Update() => Tick(Time.deltaTime);

        public void Tick(float deltaTime)
        {
            _clock += Mathf.Max(0f, deltaTime);

            IReadOnlyList<BypassSite> sites = worker != null ? worker.Sites : null;
            bool pumpStage = procedure != null &&
                             (procedure.Stage == TransplantStage.GoOnBypass || procedure.Stage == TransplantStage.Restart);
            BypassStep next = procedure != null ? procedure.Bypass.NextStep : BypassStep.NotStarted;

            float wave = 0.5f + 0.5f * Mathf.Sin(_clock * pulseHz * Mathf.PI * 2f);
            int active = -1;

            for (int i = 0; i < rings.Count; i++)
            {
                Renderer ring = rings[i];
                if (ring == null) { continue; }

                bool show = pumpStage && sites != null && i < sites.Count && Carries(sites[i], next);
                if (ring.enabled != show) { ring.enabled = show; }

                if (show && ring.sharedMaterial != null)
                {
                    // Never fades out: the old pulse went down to a tenth of the alpha and the ring
                    // vanished against the tissue for part of every beat.
                    Color c = color;
                    c.a = color.a * Mathf.Lerp(0.65f, 1f, wave);
                    ring.sharedMaterial.color = c;
                    ring.transform.localScale = Vector3.one * Mathf.Lerp(0.95f, 1.2f, wave);
                }

                if (show && active < 0 && sites[i].Point != null) { active = i; }
            }

            if (active < 0)
            {
                SetBeacon(false);
                return;
            }

            SetBeacon(true);
            Vector3 at = sites[active].Point.position;

            if (arrow != null)
            {
                arrow.position = at + Vector3.up * (arrowHeight + arrowBounce * wave);
                arrow.rotation = Quaternion.Euler(0f, _clock * spinDegreesPerSecond, 0f);
            }

            if (labelRoot != null)
            {
                labelRoot.position = at + Vector3.up * labelHeight;
                Transform eye = viewer != null ? viewer : (Camera.main != null ? Camera.main.transform : null);
                Vector3 away = eye != null ? labelRoot.position - eye.position : Vector3.forward;
                if (away.sqrMagnitude > 1e-6f) { labelRoot.rotation = Quaternion.LookRotation(away, Vector3.up); }
            }

            string text = LabelFor(next, procedure.Bypass.CompletedSteps + 1, procedure.Bypass.TotalSteps);
            if (label != null && text != _labelShown) { label.text = text; }
            _labelShown = text;
        }

        /// <summary>The step's number and plain name, and what to do about it.</summary>
        public static string LabelFor(BypassStep step, int number, int total) =>
            $"{number}/{total}  {StepName(step)}\nSegure a mão no anel";

        private static string StepName(BypassStep step)
        {
            switch (step)
            {
                case BypassStep.Cannulate: return "Conectar a máquina";
                case BypassStep.ClampAorta: return "Fechar o clampe";
                case BypassStep.Cardioplegia: return "Parar o coração";
                case BypassStep.Unclamp: return "Abrir o clampe";
                case BypassStep.DeAir: return "Tirar o ar";
                case BypassStep.Wean: return "Desligar a máquina";
                default: return "Próximo passo";
            }
        }

        private void SetBeacon(bool on)
        {
            if (arrow != null && arrow.gameObject.activeSelf != on) { arrow.gameObject.SetActive(on); }
            if (labelRoot != null && labelRoot.gameObject.activeSelf != on) { labelRoot.gameObject.SetActive(on); }
            if (!on) { _labelShown = string.Empty; }
        }

        /// <summary>Hooks up the arrow and the label. The viewer is the head; null uses the main camera.</summary>
        public void BindBeacon(Transform arrowObject, Transform labelObject, TextMesh labelText, Transform head = null)
        {
            arrow = arrowObject;
            labelRoot = labelObject;
            label = labelText;
            viewer = head;
            SetBeacon(false);
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
