using System.Collections.Generic;
using UnityEngine;
using VRSurgery.Transplant;

namespace VRSurgery.Feedback
{
    /// <summary>
    /// A ring that fills in the air above whatever the surgeon is working on, with a one-word
    /// label under it.
    ///
    /// Every gesture in this operation is "hold still here for a few seconds", and until now none
    /// of them showed that anything was happening: a first-timer held the sternum for three
    /// seconds with nothing changing, decided it had not worked and let go, which reset the hold
    /// and confirmed the wrong conclusion. The ring is anchored in the world beside the work
    /// rather than stuck to the face, because a head-locked overlay is the classic way to make
    /// people queasy in a headset.
    ///
    /// It polls the workers rather than subscribing to them, the same way the monitor does, so it
    /// can never disagree with what the workers actually believe.
    /// </summary>
    public class WorkProgressIndicator : MonoBehaviour
    {
        [Header("Sources")]
        [SerializeField] private SternotomyWorker sternotomy;
        [SerializeField] private BypassWorker bypass;
        [SerializeField] private AnastomosisWorker anastomosis;

        [Tooltip("Anything else implementing IWorkProgressSource: the incision, the suture.")]
        [SerializeField] private List<MonoBehaviour> extraSources = new List<MonoBehaviour>();

        [Header("Look")]
        [SerializeField] private MeshFilter fillFilter;
        [SerializeField] private MeshRenderer fillRenderer;
        [SerializeField] private MeshRenderer backRenderer;
        [SerializeField] private TextMesh label;

        [Tooltip("Metres above the work point. Clears the skin over a site inside the chest.")]
        [SerializeField] private float lift = 0.085f;

        [SerializeField] private float radius = 0.035f;
        [SerializeField] private float thickness = 0.008f;

        [SerializeField] private Color workingColor = new Color(0.25f, 0.85f, 1f, 0.95f);
        [SerializeField] private Color alarmColor = new Color(1f, 0.2f, 0.15f, 0.95f);
        [SerializeField] private Color doneColor = new Color(0.35f, 1f, 0.45f, 0.95f);

        [Tooltip("How long the ring lingers, full and green, after a gesture completes.")]
        [SerializeField, Min(0f)] private float doneHoldSeconds = 0.6f;

        private Mesh _fillMesh;
        private float _drawnProgress = -1f;
        private float _lastProgress;
        private Vector3 _lastPoint;
        private string _lastLabel = string.Empty;
        private float _doneElapsed = float.PositiveInfinity;
        private Camera _camera;

        /// <summary>True while a gesture is being advanced this frame.</summary>
        public bool IsActive { get; private set; }

        /// <summary>Progress of the gesture being shown, 0..1.</summary>
        public float Progress01 { get; private set; }

        /// <summary>True when the shown work is an emergency (a leak being pressed).</summary>
        public bool IsAlarm { get; private set; }

        /// <summary>Raised once each time a shown gesture reaches the end.</summary>
        public event System.Action Completed;

        private void Awake()
        {
            _fillMesh = new Mesh { name = "ProgressFill" };
            _fillMesh.MarkDynamic();
            if (fillFilter != null) { fillFilter.sharedMesh = _fillMesh; }
            SetVisible(false);
        }

        private void LateUpdate() => Tick(Time.deltaTime);

        /// <summary>Resolves what is being worked and redraws. Stepped by hand in tests.</summary>
        public void Tick(float deltaTime)
        {
            bool working = TryRead(out float progress, out Vector3 point, out string text, out bool alarm);

            if (working)
            {
                // A gesture that was nearly done and now reads empty just finished: its worker
                // reset the hold. Show it full and green for a moment instead of vanishing.
                if (IsActive && _lastProgress > 0.97f && progress < 0.15f && _lastLabel == text)
                {
                    BeginDone();
                }
                else
                {
                    _doneElapsed = float.PositiveInfinity;
                    Show(progress, point, text, alarm ? alarmColor : workingColor);
                }

                _lastProgress = progress;
                _lastPoint = point;
                _lastLabel = text;
                IsAlarm = alarm;
            }
            else if (IsActive && _lastProgress > 0.97f && !IsAlarmPending())
            {
                BeginDone();
            }

            IsActive = working;
            Progress01 = working ? progress : 0f;

            if (_doneElapsed < doneHoldSeconds)
            {
                _doneElapsed += Mathf.Max(0f, deltaTime);
                Show(1f, _lastPoint, "OK", doneColor);
                if (_doneElapsed >= doneHoldSeconds) { _lastProgress = 0f; }
                return;
            }

            if (!working)
            {
                SetVisible(false);
                _lastProgress = 0f;
            }
        }

        private bool IsAlarmPending() => IsAlarm && _lastProgress < 0.999f;

        private void BeginDone()
        {
            _doneElapsed = 0f;
            _lastProgress = 0f;
            Completed?.Invoke();
        }

        private bool TryRead(out float progress, out Vector3 point, out string text, out bool alarm)
        {
            progress = 0f;
            point = Vector3.zero;
            text = string.Empty;
            alarm = false;

            for (int i = 0; i < extraSources.Count; i++)
            {
                if (extraSources[i] is IWorkProgressSource source && extraSources[i].isActiveAndEnabled && source.IsWorking)
                {
                    progress = source.WorkProgress01;
                    point = source.WorkPoint;
                    text = source.WorkLabel;
                    alarm = source.IsAlarm;
                    return true;
                }
            }

            if (anastomosis != null && anastomosis.ActiveSite != null)
            {
                VesselAnastomosis site = anastomosis.ActiveSite;
                point = site.transform.position;

                if (site.IsBleeding)
                {
                    progress = site.BleedingControl01;
                    text = "ESTANCANDO";
                    alarm = true;
                }
                else
                {
                    progress = site.Progress01;
                    text = "SUTURANDO " + site.DisplayName.ToUpperInvariant();
                }

                return true;
            }

            if (bypass != null && bypass.ActiveSite != null && bypass.ActiveSite.Point != null &&
                bypass.ActiveSite.Held > 0f)
            {
                progress = bypass.ActiveSite.Progress01;
                point = bypass.ActiveSite.Point.position;
                text = "CEC";
                return true;
            }

            if (sternotomy != null && sternotomy.IsWorking && sternotomy.Site != null)
            {
                progress = sternotomy.Progress01;
                point = sternotomy.Site.position;
                text = "ABRINDO O ESTERNO";
                return true;
            }

            return false;
        }

        private void Show(float progress, Vector3 point, string text, Color colour)
        {
            SetVisible(true);

            transform.position = point + Vector3.up * lift;

            if (_camera == null) { _camera = Camera.main; }
            if (_camera != null)
            {
                Vector3 away = transform.position - _camera.transform.position;
                if (away.sqrMagnitude > 1e-6f) { transform.rotation = Quaternion.LookRotation(away, Vector3.up); }
            }

            if (Mathf.Abs(progress - _drawnProgress) > 0.005f)
            {
                BuildArc(_fillMesh, radius - thickness, radius, Mathf.Clamp01(progress));
                _drawnProgress = progress;
            }

            if (fillRenderer != null && fillRenderer.sharedMaterial != null)
            {
                fillRenderer.sharedMaterial.color = colour;
            }

            if (label != null)
            {
                string shown = text == "OK" ? "OK" : $"{text}  {Mathf.RoundToInt(progress * 100f)}%";
                if (label.text != shown) { label.text = shown; }
                label.color = colour;
            }
        }

        private void SetVisible(bool visible)
        {
            if (fillRenderer != null) { fillRenderer.enabled = visible; }
            if (backRenderer != null) { backRenderer.enabled = visible; }
            if (label != null)
            {
                MeshRenderer text = label.GetComponent<MeshRenderer>();
                if (text != null) { text.enabled = visible; }
            }
        }

        /// <summary>A flat arc in the local XY plane, clockwise from twelve o'clock, seen from both sides.</summary>
        public static void BuildArc(Mesh mesh, float inner, float outer, float fraction)
        {
            const int fullSegments = 48;
            int segments = Mathf.Max(1, Mathf.CeilToInt(fullSegments * fraction));
            float sweep = Mathf.PI * 2f * fraction;

            Vector3[] vertices = new Vector3[(segments + 1) * 2];
            int[] triangles = new int[segments * 12];

            for (int i = 0; i <= segments; i++)
            {
                float angle = Mathf.PI * 0.5f - sweep * (i / (float)segments);
                float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
                vertices[i * 2] = new Vector3(cos * inner, sin * inner, 0f);
                vertices[i * 2 + 1] = new Vector3(cos * outer, sin * outer, 0f);
            }

            int t = 0;
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2, b = i * 2 + 1, c = i * 2 + 2, d = i * 2 + 3;
                triangles[t++] = a; triangles[t++] = b; triangles[t++] = c;
                triangles[t++] = c; triangles[t++] = b; triangles[t++] = d;
                triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                triangles[t++] = c; triangles[t++] = d; triangles[t++] = b;
            }

            mesh.Clear();
            if (fraction <= 0f) { return; }
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
        }

        public void Bind(SternotomyWorker sternotomyWorker, BypassWorker bypassWorker,
            AnastomosisWorker anastomosisWorker, MeshFilter fill, MeshRenderer fillRend,
            MeshRenderer back, TextMesh text)
        {
            sternotomy = sternotomyWorker;
            bypass = bypassWorker;
            anastomosis = anastomosisWorker;
            fillFilter = fill;
            fillRenderer = fillRend;
            backRenderer = back;
            label = text;
        }

        /// <summary>Adds a newer worker (incision, suture) without changing this component's contract.</summary>
        public void AddSource(MonoBehaviour source)
        {
            if (source is IWorkProgressSource && !extraSources.Contains(source)) { extraSources.Add(source); }
        }
    }
}
