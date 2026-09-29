using System;
using System.Collections.Generic;
using UnityEngine;
using VRSurgery.Feedback;
using VRSurgery.Interaction;
using VRSurgery.Surgery;

namespace VRSurgery.Transplant
{
    /// <summary>
    /// The first cut: the scalpel drawn down the sternal midline, over the purple line the team
    /// marked on the skin.
    ///
    /// The incision is judged where the blade actually is, not by a hold: the line is split into
    /// short bins and each bin is cut when the blade tip passes over it at skin depth and within
    /// a hand's-width of tolerance of the midline. A fast stroke fills the bins it skipped
    /// between two frames, so a confident surgeon is not punished for moving at a normal speed.
    /// A blade on the skin but off the line is an error, named on the monitor and felt in the hand.
    ///
    /// What gets drawn is the real record of the stroke — the red line and the beads of blood only
    /// appear on the stretches that were actually cut.
    /// </summary>
    public class SkinIncisionWorker : MonoBehaviour, IWorkProgressSource
    {
        [Header("Parts")]
        [Tooltip("The scalpel's cutting point.")]
        [SerializeField] private Transform bladeTip;

        [Tooltip("The scalpel. The incision only counts while it is held.")]
        [SerializeField] private SurgicalInteractable scalpel;

        [SerializeField] private ChestSkinPatch patch;
        [SerializeField] private TransplantProcedure procedure;

        [Header("Tolerances")]
        [SerializeField, Min(8)] private int bins = 36;

        [Tooltip("How far from the midline, in metres, the blade may run and still be on the line.")]
        [SerializeField, Min(0.002f)] private float lateralTolerance = 0.014f;

        [Tooltip("How far above the skin the tip may be and still count as touching it, in metres. " +
                 "Only tracking noise: a blade hovering over skin cuts nothing.")]
        [SerializeField, Min(0.001f)] private float above = 0.004f;

        [Tooltip("How far below the skin the tip may be and still be cutting, in metres.")]
        [SerializeField, Min(0.001f)] private float below = 0.03f;

        [Tooltip("Deeper than this is through the subcutaneous fat and onto the sternum: it still " +
                 "cuts, but it is an error and it bleeds more.")]
        [SerializeField, Min(0.002f)] private float tooDeep = 0.022f;

        [SerializeField, Min(0.5f)] private float deepCooldown = 3f;

        [Tooltip("Seconds for the beads of blood along a fresh cut to well up to full size.")]
        [SerializeField, Min(0.05f)] private float beadGrowSeconds = 1.4f;

        [Tooltip("Fraction of the line that has to be cut to count as a finished incision.")]
        [SerializeField, Range(0.5f, 1f)] private float completeFraction = 0.9f;

        [Tooltip("Largest gap between two frames' bins that is filled in as one stroke.")]
        [SerializeField, Min(1)] private int maxStrokeGap = 5;

        [Tooltip("Seconds off the line, on the skin, before it is called an error.")]
        [SerializeField, Min(0f)] private float deviationGrace = 0.3f;

        [SerializeField, Min(0.5f)] private float deviationCooldown = 2.5f;

        [Header("Look")]
        [SerializeField] private MeshFilter cutLine;
        [SerializeField] private Renderer guideLine;
        [SerializeField] private List<GameObject> bloodBeads = new List<GameObject>();

        [SerializeField] private float cutWidth = 0.0028f;

        private bool[] _cut = new bool[0];
        private int _lastBin = -1;
        private int _cutCount;
        private bool _announced;
        private float _offLine;
        private float _sinceDeviation = float.PositiveInfinity;
        private Mesh _lineMesh;
        private bool _lineDirty = true;

        private float[] _cutTime = new float[0];
        private bool[] _deep = new bool[0];
        private Vector3[] _beadScale;
        private float _clock;
        private float _sinceDeep = float.PositiveInfinity;

        public bool IsWorking { get; private set; }
        public bool IsComplete { get; private set; }
        public float Progress01 => bins <= 0 ? 0f : _cutCount / (float)bins;

        /// <summary>How deep the blade is this frame, 0 at the skin and 1 at too deep. Drives the hand's vibration.</summary>
        public float CutDepth01 { get; private set; }

        public int Bins => bins;
        public int CutCount => _cutCount;
        public bool IsBinCut(int bin) => bin >= 0 && bin < _cut.Length && _cut[bin];
        public bool IsBinDeep(int bin) => bin >= 0 && bin < _deep.Length && _deep[bin];

        /// <summary>Seconds since a stretch of the line was cut; infinite if it has not been.</summary>
        public float BinAge(int bin) =>
            IsBinCut(bin) && bin < _cutTime.Length ? _clock - _cutTime[bin] : float.PositiveInfinity;

        float IWorkProgressSource.WorkProgress01 => Mathf.Clamp01(Progress01 / completeFraction);
        Vector3 IWorkProgressSource.WorkPoint => bladeTip != null ? bladeTip.position : transform.position;
        string IWorkProgressSource.WorkLabel => "INCISÃO";
        bool IWorkProgressSource.IsAlarm => false;

        /// <summary>First contact of the blade with the line. Starts the visitor's clock.</summary>
        public event Action Started;

        /// <summary>Raised on each frame the blade advances the cut.</summary>
        public event Action Cutting;

        /// <summary>Raised when the blade runs on the skin off the line.</summary>
        public event Action Deviated;

        public event Action Completed;

        /// <summary>Raised when the blade goes through to the bone.</summary>
        public event Action DeepCut;

        private void Awake() => EnsureState();

        private void EnsureState()
        {
            if (_cut == null || _cut.Length != bins) { _cut = new bool[bins]; }
            if (_cutTime == null || _cutTime.Length != bins) { _cutTime = new float[bins]; }
            if (_deep == null || _deep.Length != bins) { _deep = new bool[bins]; }

            if (_beadScale == null || _beadScale.Length != bloodBeads.Count)
            {
                _beadScale = new Vector3[bloodBeads.Count];
                for (int i = 0; i < bloodBeads.Count; i++)
                {
                    if (bloodBeads[i] != null) { _beadScale[i] = bloodBeads[i].transform.localScale; }
                }
            }

            if (_lineMesh == null && cutLine != null)
            {
                _lineMesh = new Mesh { name = "IncisionLine" };
                _lineMesh.MarkDynamic();
                cutLine.sharedMesh = _lineMesh;
            }
        }

        private void Update() => Tick(Time.deltaTime);

        public void Tick(float deltaTime)
        {
            EnsureState();
            IsWorking = false;
            CutDepth01 = 0f;
            _sinceDeviation += Mathf.Max(0f, deltaTime);
            _sinceDeep += Mathf.Max(0f, deltaTime);
            _clock += Mathf.Max(0f, deltaTime);

            UpdateVisibility();

            if (deltaTime <= 0f || bladeTip == null || patch == null || IsComplete) { return; }
            if (procedure != null && procedure.Stage != TransplantStage.SkinIncision) { return; }
            if (scalpel != null && !scalpel.IsHeld) { _lastBin = -1; return; }

            Vector3 tip = bladeTip.position;
            float along = patch.AlongIncision(patch.LongitudinalOf(tip));
            float lateral = patch.LateralOf(tip);
            float surface = patch.SurfaceHeightUnder(tip);
            float depth = surface - tip.y;
            bool atSkin = depth >= -above && depth <= below;
            bool onLength = along >= -0.02f && along <= 1.02f;

            if (!atSkin || !onLength)
            {
                _lastBin = -1;
                _offLine = 0f;
                RefreshLine();
                return;
            }

            // Skin is elastic: it dents under the blade before it parts, on or off the line.
            if (depth > 0f && Mathf.Abs(lateral) < lateralTolerance * 4f) { patch.Press(tip, depth); }

            if (Mathf.Abs(lateral) > lateralTolerance)
            {
                _lastBin = -1;

                // Only a blade actually on this patch of skin counts as off the line; one resting
                // on the far side of the chest is simply somewhere else.
                if (Mathf.Abs(lateral) < lateralTolerance * 4f)
                {
                    _offLine += deltaTime;
                    if (_offLine >= deviationGrace && _sinceDeviation >= deviationCooldown)
                    {
                        _sinceDeviation = 0f;
                        _offLine = 0f;
                        SurgeryEvents.RaiseError(ErrorSeverity.MinorError,
                            "Incisão fora da linha média: siga a linha roxa sobre o esterno.");
                        Deviated?.Invoke();
                    }
                }

                RefreshLine();
                return;
            }

            _offLine = 0f;
            IsWorking = true;
            CutDepth01 = Mathf.Clamp01(depth / tooDeep);

            if (!_announced)
            {
                _announced = true;
                Started?.Invoke();
            }

            bool deep = depth > tooDeep;
            if (deep && _sinceDeep >= deepCooldown)
            {
                _sinceDeep = 0f;
                SurgeryEvents.RaiseError(ErrorSeverity.MinorError,
                    "Incisão profunda demais: a lâmina chegou ao esterno. Corte só a pele.");
                DeepCut?.Invoke();
            }

            int bin = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(along) * bins), 0, bins - 1);
            int from = bin, to = bin;
            if (_lastBin >= 0 && Mathf.Abs(bin - _lastBin) <= maxStrokeGap)
            {
                from = Mathf.Min(bin, _lastBin);
                to = Mathf.Max(bin, _lastBin);
            }

            bool advanced = false;
            for (int i = from; i <= to; i++)
            {
                if (_cut[i])
                {
                    // Going back over a stretch deeper than before still marks it as deep.
                    if (deep) { _deep[i] = true; }
                    continue;
                }

                MarkCut(i, deep);
                advanced = true;
            }

            _lastBin = bin;

            if (advanced)
            {
                _lineDirty = true;
                Cutting?.Invoke();
            }

            if (Progress01 >= completeFraction)
            {
                Finish();
            }

            RefreshLine();
        }

        /// <summary>One stretch of the line cut: remembered, sprung open, left to bleed.</summary>
        private void MarkCut(int bin, bool deep)
        {
            _cut[bin] = true;
            _cutTime[bin] = _clock;
            _deep[bin] = deep;
            _cutCount++;
            if (patch != null) { patch.SetCut(bin / (float)bins, (bin + 1) / (float)bins, true); }
        }

        private void Finish()
        {
            for (int i = 0; i < bins; i++)
            {
                if (!_cut[i]) { MarkCut(i, false); }
            }

            IsComplete = true;
            _lineDirty = true;
            RefreshLine();

            Completed?.Invoke();
            if (procedure != null) { procedure.CompleteStage(TransplantStage.SkinIncision); }
            if (patch != null) { patch.Open(); }
        }

        /// <summary>The purple guide shows until the cut is made; the cut shows until the wound opens.</summary>
        private void UpdateVisibility()
        {
            bool open = patch != null && patch.Openness01 > 0.02f;

            if (guideLine != null)
            {
                bool guide = !IsComplete && !open;
                if (guideLine.enabled != guide) { guideLine.enabled = guide; }
            }

            if (cutLine != null)
            {
                MeshRenderer renderer = cutLine.GetComponent<MeshRenderer>();
                bool line = _cutCount > 0 && !open;
                if (renderer != null && renderer.enabled != line) { renderer.enabled = line; }
            }

            for (int i = 0; i < bloodBeads.Count; i++)
            {
                GameObject bead = bloodBeads[i];
                if (bead == null) { continue; }

                int bin = bloodBeads.Count <= 0 ? 0 : Mathf.Min(bins - 1, i * bins / bloodBeads.Count);
                bool show = !open && _cut.Length > bin && _cut[bin];
                if (bead.activeSelf != show) { bead.SetActive(show); }

                if (show && _beadScale != null && i < _beadScale.Length)
                {
                    // Blood wells up along a fresh cut over a second or so rather than appearing,
                    // and a cut that went too deep bleeds more.
                    float grow = Mathf.SmoothStep(0.15f, 1f, Mathf.Clamp01(BinAge(bin) / beadGrowSeconds));
                    float size = grow * (IsBinDeep(bin) ? 1.6f : 1f);
                    bead.transform.localScale = _beadScale[i] * size;
                }
            }
        }

        private void RefreshLine()
        {
            if (!_lineDirty || _lineMesh == null || patch == null) { return; }
            _lineDirty = false;

            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            Transform space = cutLine.transform;

            for (int i = 0; i < bins; i++)
            {
                if (!_cut[i]) { continue; }

                float a = i / (float)bins;
                float b = (i + 1) / (float)bins;

                // Narrower at the two ends of the whole incision, like a real skin cut.
                float wa = cutWidth * Taper(a);
                float wb = cutWidth * Taper(b);

                // Just under the skin: the floor of the slit, seen between the gaping edges,
                // rather than a stripe painted on top of them.
                const float floor = -0.0015f;
                int start = vertices.Count;
                vertices.Add(space.InverseTransformPoint(patch.IncisionPoint(a, -wa, floor)));
                vertices.Add(space.InverseTransformPoint(patch.IncisionPoint(a, wa, floor)));
                vertices.Add(space.InverseTransformPoint(patch.IncisionPoint(b, -wb, floor)));
                vertices.Add(space.InverseTransformPoint(patch.IncisionPoint(b, wb, floor)));

                triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
                triangles.Add(start + 1); triangles.Add(start + 2); triangles.Add(start + 3);
            }

            _lineMesh.Clear();
            _lineMesh.SetVertices(vertices);
            _lineMesh.SetTriangles(triangles, 0);
            _lineMesh.RecalculateNormals();
            _lineMesh.RecalculateBounds();
        }

        private static float Taper(float along) => 0.35f + 0.65f * Mathf.Sin(Mathf.Clamp01(along) * Mathf.PI);

        public void ResetIncision()
        {
            EnsureState();
            for (int i = 0; i < _cut.Length; i++) { _cut[i] = false; _deep[i] = false; _cutTime[i] = 0f; }
            _cutCount = 0;
            _sinceDeep = float.PositiveInfinity;
            CutDepth01 = 0f;
            _lastBin = -1;
            _announced = false;
            _offLine = 0f;
            IsComplete = false;
            IsWorking = false;
            _lineDirty = true;
            RefreshLine();
            UpdateVisibility();
        }

        public void Bind(Transform blade, SurgicalInteractable tool, ChestSkinPatch skin,
            TransplantProcedure transplant, MeshFilter line, Renderer guide, IEnumerable<GameObject> beads)
        {
            bladeTip = blade;
            scalpel = tool;
            patch = skin;
            procedure = transplant;
            cutLine = line;
            guideLine = guide;
            bloodBeads = beads != null ? new List<GameObject>(beads) : new List<GameObject>();
            _cut = new bool[bins];
            _cutTime = new float[bins];
            _deep = new bool[bins];
            _beadScale = null;
            _lineMesh = null;
        }
    }
}
