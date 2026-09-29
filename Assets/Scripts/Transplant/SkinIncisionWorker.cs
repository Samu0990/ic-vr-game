using System;
using System.Collections.Generic;
using UnityEngine;
using VRSurgery.Feedback;
using VRSurgery.Interaction;
using VRSurgery.Surgery;

namespace VRSurgery.Transplant
{
    /// <summary>
    /// How the incision went, for the pop-up over the chest and the log. Nothing here changes
    /// whether the operation can continue: a crooked incision still opens the chest.
    /// </summary>
    [Serializable]
    public struct IncisionGrade
    {
        public int Score;
        public int Stars;
        public int Strokes;
        public float DeviationMm;
        public bool TooDeep;
        public int Scratches;
        public bool NeededBladeHelp;

        public string Title => Stars >= 3 ? "INCISÃO PERFEITA" : Stars == 2 ? "BOA INCISÃO" : "INCISÃO IRREGULAR";

        public string Detail
        {
            get
            {
                string text = $"{Strokes} passada{(Strokes == 1 ? "" : "s")} · {DeviationMm:F0} mm da linha";
                if (TooDeep) { text += " · funda demais"; }
                if (Scratches > 0) { text += $" · {Scratches} arranhão{(Scratches == 1 ? "" : "ões")}"; }
                return text;
            }
        }

        /// <summary>
        /// A skin incision is judged the way a preceptor judges it: one confident stroke, on the
        /// line, through the skin and no deeper, and no nicks either side of it.
        /// </summary>
        public static IncisionGrade From(int strokes, float deviationMm, bool tooDeep, int scratches, bool neededHelp)
        {
            float score = 100f;
            score -= Mathf.Min(32f, 8f * Mathf.Max(0, strokes - 1));
            score -= Mathf.Clamp((deviationMm - 2f) * 5f, 0f, 30f);
            if (tooDeep) { score -= 15f; }
            score -= Mathf.Min(30f, 10f * scratches);
            if (neededHelp) { score -= 10f; }

            int rounded = Mathf.Clamp(Mathf.RoundToInt(score), 0, 100);
            return new IncisionGrade
            {
                Score = rounded,
                Stars = rounded >= 85 ? 3 : rounded >= 60 ? 2 : 1,
                Strokes = Mathf.Max(1, strokes),
                DeviationMm = deviationMm,
                TooDeep = tooDeep,
                Scratches = scratches,
                NeededBladeHelp = neededHelp,
            };
        }
    }

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
    /// A scalpel cuts with its edge while it is drawn. A blade resting on the skin only dents it,
    /// and one laid flat or pushed sideways scrapes. Those are the first things a surgery game
    /// teaches, so they are required here too — with adaptive help: a visitor who keeps trying
    /// with the blade the wrong way round is told how to hold it, and after a few seconds the
    /// rule relaxes for the rest of their turn rather than leaving them stuck at the first step.
    ///
    /// What gets drawn is the real record of the stroke: the wound lies where the blade went
    /// (within a few millimetres of the line), the red line and the beads of blood only appear
    /// on the stretches that were actually cut, the blade comes away bloody, and a nick off the
    /// line stays on the skin as a scratch.
    /// </summary>
    public class SkinIncisionWorker : MonoBehaviour, IWorkProgressSource
    {
        [Header("Parts")]
        [Tooltip("The scalpel's cutting point. Its local X axis is the blade's face normal: the " +
                 "blade lies in its local Y-Z plane.")]
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

        [Header("Technique")]
        [Tooltip("Slowest the blade can be drawn and still cut, in metres per second. Below this " +
                 "it is resting on the skin, which dents it and nothing more.")]
        [SerializeField, Min(0f)] private float minSliceSpeed = 0.02f;

        [Tooltip("The blade must stand on its edge and move along its own plane. Off in scenes " +
                 "whose scalpel model does not say which way its blade faces.")]
        [SerializeField] private bool requireEdge = true;

        [Tooltip("How far from ideal the blade may be, as the cosine of the angle between its face " +
                 "and the skin's normal (and between its face and the stroke). 0.75 allows about " +
                 "50 degrees of lean either way.")]
        [SerializeField, Range(0.3f, 0.99f)] private float edgeTolerance = 0.75f;

        [Tooltip("Adaptive help: seconds of trying on the line with the blade the wrong way round " +
                 "after which the edge rule is dropped for the rest of this visitor's turn.")]
        [SerializeField, Min(0.5f)] private float edgeHelpSeconds = 3f;

        [SerializeField, Min(0.5f)] private float edgeHintCooldown = 4f;

        [Header("Look")]
        [SerializeField] private MeshFilter cutLine;
        [SerializeField] private Renderer guideLine;
        [SerializeField] private List<GameObject> bloodBeads = new List<GameObject>();

        [Tooltip("Thin red nicks left on the skin wherever the blade cut off the line.")]
        [SerializeField] private MeshFilter scratchLine;

        [Tooltip("Blood on the blade, shown from the first cut until the next visitor.")]
        [SerializeField] private Renderer bladeBlood;

        [SerializeField] private float cutWidth = 0.0028f;
        [SerializeField] private float scratchWidth = 0.0007f;
        [SerializeField, Min(8)] private int maxScratchPoints = 160;

        private bool[] _cut = new bool[0];
        private int _lastBin = -1;
        private float _lastLateral;
        private int _cutCount;
        private bool _announced;
        private float _offLine;
        private float _sinceDeviation = float.PositiveInfinity;
        private Mesh _lineMesh;
        private bool _lineDirty = true;

        // Reused every rebuild: the marks follow the skin every frame it moves, and a Quest
        // should not collect garbage for it.
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<int> _triangles = new List<int>();

        private float[] _cutTime = new float[0];
        private bool[] _deep = new bool[0];
        private float[] _lateral = new float[0];
        private bool[] _byBlade = new bool[0];
        private Vector3[] _beadScale;
        private float[] _beadAlong;
        private float[] _beadLateral;
        private float[] _beadLift;
        private int _beadVersion = -1;
        private float _clock;
        private float _sinceDeep = float.PositiveInfinity;

        private Vector3 _previousTip;
        private bool _hasPreviousTip;
        private bool _inContact;
        private bool _strokeCounted;
        private int _contactStartBin = -1;
        private int _strokes;
        private bool _anyDeep;
        private float _misaligned;
        private float _sinceEdgeHint = float.PositiveInfinity;

        // Scratch points: x = rest lateral (m), y = patch longitudinal fraction, z = nick index.
        private readonly List<Vector3> _scratch = new List<Vector3>();
        private Vector3 _lastScratchWorld;
        private bool _scratching;
        private int _scratchCount;
        private Mesh _scratchMesh;
        private bool _scratchDirty = true;
        private int _scratchVersion = -1;

        public bool IsWorking { get; private set; }
        public bool IsComplete { get; private set; }
        public float Progress01 => bins <= 0 ? 0f : _cutCount / (float)bins;

        /// <summary>How deep the blade is this frame, 0 at the skin and 1 at too deep. Drives the hand's vibration.</summary>
        public float CutDepth01 { get; private set; }

        public int Bins => bins;
        public int CutCount => _cutCount;
        public bool IsBinCut(int bin) => bin >= 0 && bin < _cut.Length && _cut[bin];
        public bool IsBinDeep(int bin) => bin >= 0 && bin < _deep.Length && _deep[bin];

        /// <summary>Where the blade ran across a stretch of the line, in metres from the midline.</summary>
        public float LateralAt(int bin) => bin >= 0 && bin < _lateral.Length ? _lateral[bin] : 0f;

        /// <summary>Separate passes of the blade that cut along the line. One is the textbook incision.</summary>
        public int Strokes => _strokes;

        /// <summary>Nicks left on the skin off the line.</summary>
        public int Scratches => _scratchCount;

        /// <summary>True while the blade is on the line, in the skin, with its edge and stroke right.</summary>
        public bool BladeOnEdge { get; private set; }

        /// <summary>True once the adaptive help has dropped the edge rule for this visitor.</summary>
        public bool EdgeRuleRelaxed { get; private set; }

        public bool RequiresEdge => requireEdge;

        /// <summary>The finished incision's grade. Only meaningful once <see cref="IsComplete"/>.</summary>
        public IncisionGrade Grade { get; private set; }

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

        /// <summary>Raised when the blade is drawn on the line flat or sideways, with a hint on the monitor.</summary>
        public event Action BladeMisaligned;

        /// <summary>Raised once, as the incision is finished, with how it went.</summary>
        public event Action<IncisionGrade> Graded;

        private void Awake() => EnsureState();

        private void EnsureState()
        {
            if (_cut == null || _cut.Length != bins) { _cut = new bool[bins]; }
            if (_cutTime == null || _cutTime.Length != bins) { _cutTime = new float[bins]; }
            if (_deep == null || _deep.Length != bins) { _deep = new bool[bins]; }
            if (_lateral == null || _lateral.Length != bins) { _lateral = new float[bins]; }
            if (_byBlade == null || _byBlade.Length != bins) { _byBlade = new bool[bins]; }

            if (_beadScale == null || _beadScale.Length != bloodBeads.Count)
            {
                int n = bloodBeads.Count;
                _beadScale = new Vector3[n];
                _beadAlong = new float[n];
                _beadLateral = new float[n];
                _beadLift = new float[n];
                _beadVersion = -1;
                for (int i = 0; i < n; i++)
                {
                    if (bloodBeads[i] == null) { continue; }
                    Transform bead = bloodBeads[i].transform;
                    _beadScale[i] = bead.localScale;
                    _beadAlong[i] = patch != null ? patch.AlongIncision(patch.LongitudinalOf(bead.position)) : 0f;
                    _beadLateral[i] = patch != null ? patch.LateralOf(bead.position) : 0f;
                    _beadLift[i] = patch != null ? bead.position.y - patch.SurfaceHeightUnder(bead.position) : 0f;
                }
            }

            if (_lineMesh == null && cutLine != null)
            {
                _lineMesh = new Mesh { name = "IncisionLine" };
                _lineMesh.MarkDynamic();
                cutLine.sharedMesh = _lineMesh;
            }

            if (_scratchMesh == null && scratchLine != null)
            {
                _scratchMesh = new Mesh { name = "IncisionScratches" };
                _scratchMesh.MarkDynamic();
                scratchLine.sharedMesh = _scratchMesh;
            }
        }

        private void Update() => Tick(Time.deltaTime);

        public void Tick(float deltaTime)
        {
            EnsureState();
            IsWorking = false;
            BladeOnEdge = false;
            CutDepth01 = 0f;
            _sinceDeviation += Mathf.Max(0f, deltaTime);
            _sinceDeep += Mathf.Max(0f, deltaTime);
            _sinceEdgeHint += Mathf.Max(0f, deltaTime);
            _clock += Mathf.Max(0f, deltaTime);

            UpdateVisibility();

            if (deltaTime <= 0f || bladeTip == null || patch == null)
            {
                RefreshMarks();
                return;
            }

            // Tracked every frame, held or not, so the first frame on the skin has a real speed.
            Vector3 tip = bladeTip.position;
            Vector3 velocity = _hasPreviousTip ? (tip - _previousTip) / deltaTime : Vector3.zero;
            _previousTip = tip;
            _hasPreviousTip = true;

            if (IsComplete
                || (procedure != null && procedure.Stage != TransplantStage.SkinIncision)
                || (scalpel != null && !scalpel.IsHeld))
            {
                EndContact();
                RefreshMarks();
                return;
            }

            float longitudinal = patch.LongitudinalOf(tip);
            float along = patch.AlongIncision(longitudinal);
            float lateral = patch.LateralOf(tip);
            float surface = patch.SurfaceHeightUnder(tip);
            float depth = surface - tip.y;
            bool atSkin = depth >= -above && depth <= below;
            bool onLength = along >= -0.02f && along <= 1.02f;

            if (!atSkin || !onLength)
            {
                EndContact();
                _offLine = 0f;
                RefreshMarks();
                return;
            }

            // Only motion along the skin slices. A blade pushed straight down is a stab: it dents
            // the skin and, deep enough, is an error, but it does not draw a line.
            Vector3 up = patch.transform.up;
            Vector3 slide = velocity - Vector3.Dot(velocity, up) * up;
            float speed = slide.magnitude;
            bool moving = speed >= minSliceSpeed;

            // Skin is elastic: it dents under the blade before it parts, on or off the line.
            // Drawn through it, the skin is dragged a little along with the blade.
            if (depth > 0f && Mathf.Abs(lateral) < lateralTolerance * 4f) { patch.Press(tip, depth, slide); }

            if (Mathf.Abs(lateral) > lateralTolerance)
            {
                _lastBin = -1;
                _inContact = false;

                // Only a blade actually on this patch of skin counts as off the line; one resting
                // on the far side of the chest is simply somewhere else.
                if (Mathf.Abs(lateral) < lateralTolerance * 4f)
                {
                    // A blade drawn through the skin off the line leaves a nick where it went.
                    if (depth > 0.0005f && moving) { AddScratch(tip, lateral, longitudinal); }
                    else { _scratching = false; }

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
                else
                {
                    _scratching = false;
                }

                RefreshMarks();
                return;
            }

            _scratching = false;
            _offLine = 0f;
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
            if (!_inContact)
            {
                _inContact = true;
                _strokeCounted = false;
                _contactStartBin = bin;
            }

            // A resting blade dents the skin and nothing more. Where it rests is remembered, so the
            // stroke that starts from there cuts from there.
            if (!moving)
            {
                if (_lastBin < 0) { _lastBin = bin; _lastLateral = lateral; }
                RefreshMarks();
                return;
            }

            if (!EdgeAligned(slide, speed))
            {
                // Scraping: nothing is cut, and nothing it passed over is cut afterwards either.
                _lastBin = -1;
                _misaligned += deltaTime;

                if (_sinceEdgeHint >= edgeHintCooldown)
                {
                    _sinceEdgeHint = 0f;
                    SurgeryEvents.RaiseError(ErrorSeverity.Warning,
                        "Aponte o bisturi ao longo da linha roxa, lâmina em pé, e puxe: ele corta com o fio, não com a lateral.");
                    BladeMisaligned?.Invoke();
                }

                // Adaptive help: the point of the booth is the transplant, not the grip. Someone who
                // has tried for a few seconds gets the scalpel working however they hold it.
                if (_misaligned >= edgeHelpSeconds) { EdgeRuleRelaxed = true; }

                RefreshMarks();
                return;
            }

            IsWorking = true;
            BladeOnEdge = true;

            int from = bin, to = bin;
            bool bridged = _lastBin >= 0 && _lastBin != bin && Mathf.Abs(bin - _lastBin) <= maxStrokeGap;
            if (bridged)
            {
                from = Mathf.Min(bin, _lastBin);
                to = Mathf.Max(bin, _lastBin);
            }

            bool advanced = false;
            for (int i = from; i <= to; i++)
            {
                // Bins skipped between two frames lie on the straight line between the two samples.
                float lateralHere = bridged
                    ? Mathf.Lerp(_lastLateral, lateral, (i - _lastBin) / (float)(bin - _lastBin))
                    : lateral;

                if (_cut[i])
                {
                    // Going back over a stretch deeper than before still marks it as deep.
                    if (deep) { _deep[i] = true; _anyDeep = true; }
                    continue;
                }

                MarkCut(i, deep, lateralHere, true);
                advanced = true;
            }

            if (!_strokeCounted && (advanced || bin != _contactStartBin))
            {
                _strokeCounted = true;
                _strokes++;
            }

            _lastBin = bin;
            _lastLateral = lateral;

            if (advanced)
            {
                _lineDirty = true;
                Cutting?.Invoke();
            }

            if (Progress01 >= completeFraction)
            {
                Finish();
            }

            RefreshMarks();
        }

        /// <summary>
        /// Edge on the skin and drawn along its own plane. The blade's face normal is the tip's
        /// local X; laid flat it points at the sky, pushed sideways it points along the stroke.
        /// </summary>
        private bool EdgeAligned(Vector3 velocity, float speed)
        {
            if (!requireEdge || EdgeRuleRelaxed) { return true; }

            Vector3 face = bladeTip.right;
            if (Mathf.Abs(Vector3.Dot(face, patch.transform.up)) > edgeTolerance) { return false; }
            if (speed > 1e-4f && Mathf.Abs(Vector3.Dot(velocity / speed, face)) > edgeTolerance) { return false; }
            return true;
        }

        private void EndContact()
        {
            _lastBin = -1;
            _inContact = false;
            _scratching = false;
        }

        /// <summary>One stretch of the line cut: remembered, sprung open where the blade went, left to bleed.</summary>
        private void MarkCut(int bin, bool deep, float lateral, bool byBlade)
        {
            _cut[bin] = true;
            _cutTime[bin] = _clock;
            _deep[bin] = deep;
            _lateral[bin] = lateral;
            _byBlade[bin] = byBlade;
            _anyDeep |= deep;
            _cutCount++;
            if (patch != null)
            {
                patch.SetCut(bin / (float)bins, (bin + 1) / (float)bins, true, lateral, lateral);
            }
        }

        private void AddScratch(Vector3 tip, float lateral, float longitudinal)
        {
            if (!_scratching)
            {
                if (_scratch.Count + 2 > maxScratchPoints) { return; }
                _scratching = true;
                _scratchCount++;
            }
            else if ((tip - _lastScratchWorld).sqrMagnitude < 0.003f * 0.003f || _scratch.Count >= maxScratchPoints)
            {
                return;
            }

            _lastScratchWorld = tip;
            _scratch.Add(new Vector3(lateral, longitudinal, _scratchCount));
            _scratchDirty = true;
        }

        private void Finish()
        {
            // The last few percent are what the ring's "done" promised; they take the line of the
            // nearest stretch the blade did cut, so the wound does not kink back to the midline.
            for (int i = 0; i < bins; i++)
            {
                if (_cut[i]) { continue; }
                MarkCut(i, false, NearestCutLateral(i), false);
            }

            Grade = IncisionGrade.From(_strokes, DeviationMm(), _anyDeep, _scratchCount, EdgeRuleRelaxed);

            IsComplete = true;
            _lineDirty = true;
            RefreshMarks();

            Debug.Log($"[Transplante] incisão: {Grade.Title} ({Grade.Score}/100, {Grade.Stars}★) — {Grade.Detail}");
            Completed?.Invoke();
            Graded?.Invoke(Grade);
            if (procedure != null) { procedure.CompleteStage(TransplantStage.SkinIncision); }
            if (patch != null) { patch.Open(); }
        }

        private float NearestCutLateral(int bin)
        {
            for (int d = 1; d < bins; d++)
            {
                if (bin - d >= 0 && _cut[bin - d] && _byBlade[bin - d]) { return _lateral[bin - d]; }
                if (bin + d < bins && _cut[bin + d] && _byBlade[bin + d]) { return _lateral[bin + d]; }
            }

            return 0f;
        }

        /// <summary>Root-mean-square distance of the blade from the line over what it cut, in millimetres.</summary>
        private float DeviationMm()
        {
            float sum = 0f;
            int n = 0;
            for (int i = 0; i < bins; i++)
            {
                if (!_cut[i] || !_byBlade[i]) { continue; }
                sum += _lateral[i] * _lateral[i];
                n++;
            }

            return n == 0 ? 0f : Mathf.Sqrt(sum / n) * 1000f;
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

            if (scratchLine != null)
            {
                MeshRenderer renderer = scratchLine.GetComponent<MeshRenderer>();
                bool show = _scratch.Count > 1;
                if (renderer != null && renderer.enabled != show) { renderer.enabled = show; }
            }

            if (bladeBlood != null)
            {
                bool bloody = _cutCount > 0;
                if (bladeBlood.enabled != bloody) { bladeBlood.enabled = bloody; }
            }

            bool follow = patch != null && patch.Version != _beadVersion;
            if (follow) { _beadVersion = patch.Version; }

            for (int i = 0; i < bloodBeads.Count; i++)
            {
                GameObject bead = bloodBeads[i];
                if (bead == null) { continue; }

                int bin = bloodBeads.Count <= 0 ? 0 : Mathf.Min(bins - 1, i * bins / bloodBeads.Count);
                bool show = !open && _cut.Length > bin && _cut[bin];
                if (bead.activeSelf != show) { bead.SetActive(show); follow = true; }

                if (!show) { continue; }

                // The beads sit in the wound, which is wherever the blade put it.
                if (follow && _beadAlong != null && i < _beadAlong.Length)
                {
                    bead.transform.position = patch.WoundPoint(_beadAlong[i], _beadLateral[i], _beadLift[i]);
                }

                if (_beadScale != null && i < _beadScale.Length)
                {
                    // Blood wells up along a fresh cut over a second or so rather than appearing,
                    // and a cut that went too deep bleeds more.
                    float grow = Mathf.SmoothStep(0.15f, 1f, Mathf.Clamp01(BinAge(bin) / beadGrowSeconds));
                    float size = grow * (IsBinDeep(bin) ? 1.6f : 1f);
                    bead.transform.localScale = _beadScale[i] * size;
                }
            }
        }

        private void RefreshMarks()
        {
            if (patch == null) { return; }

            // The skin moves under the nicks as it gapes, dents and is retracted; they move with
            // it. The cut line only changes as it is cut, so it is left alone until then.
            if (_scratch.Count > 1 && patch.Version != _scratchVersion)
            {
                _scratchVersion = patch.Version;
                _scratchDirty = true;
            }

            RefreshLine();
            RefreshScratches();
        }

        private void RefreshLine()
        {
            if (!_lineDirty || _lineMesh == null || patch == null) { return; }
            _lineDirty = false;

            List<Vector3> vertices = _vertices;
            List<int> triangles = _triangles;
            vertices.Clear();
            triangles.Clear();
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
                // rather than a stripe painted on top of them — and along the wound, not the guide.
                const float floor = -0.0015f;
                int start = vertices.Count;
                vertices.Add(space.InverseTransformPoint(patch.WoundPoint(a, -wa, floor)));
                vertices.Add(space.InverseTransformPoint(patch.WoundPoint(a, wa, floor)));
                vertices.Add(space.InverseTransformPoint(patch.WoundPoint(b, -wb, floor)));
                vertices.Add(space.InverseTransformPoint(patch.WoundPoint(b, wb, floor)));

                triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
                triangles.Add(start + 1); triangles.Add(start + 2); triangles.Add(start + 3);
            }

            _lineMesh.Clear();
            _lineMesh.SetVertices(vertices);
            _lineMesh.SetTriangles(triangles, 0);
            _lineMesh.RecalculateNormals();
            _lineMesh.RecalculateBounds();
        }

        private void RefreshScratches()
        {
            if (!_scratchDirty || _scratchMesh == null || patch == null) { return; }
            _scratchDirty = false;

            List<Vector3> vertices = _vertices;
            List<int> triangles = _triangles;
            vertices.Clear();
            triangles.Clear();
            Transform space = scratchLine.transform;
            Vector3 up = patch.transform.up;

            for (int i = 1; i < _scratch.Count; i++)
            {
                Vector3 p = _scratch[i - 1], q = _scratch[i];
                if (!Mathf.Approximately(p.z, q.z)) { continue; }

                Vector3 a = patch.SurfacePoint(p.x, p.y, 0.0005f);
                Vector3 b = patch.SurfacePoint(q.x, q.y, 0.0005f);
                Vector3 across = Vector3.Cross(b - a, up);
                if (across.sqrMagnitude < 1e-10f) { continue; }
                across = across.normalized * (scratchWidth * 0.5f);

                int s = vertices.Count;
                vertices.Add(space.InverseTransformPoint(a - across));
                vertices.Add(space.InverseTransformPoint(a + across));
                vertices.Add(space.InverseTransformPoint(b - across));
                vertices.Add(space.InverseTransformPoint(b + across));
                triangles.Add(s); triangles.Add(s + 2); triangles.Add(s + 1);
                triangles.Add(s + 1); triangles.Add(s + 2); triangles.Add(s + 3);
            }

            _scratchMesh.Clear();
            _scratchMesh.SetVertices(vertices);
            _scratchMesh.SetTriangles(triangles, 0);
            _scratchMesh.RecalculateNormals();
            _scratchMesh.RecalculateBounds();
        }

        private static float Taper(float along) => 0.35f + 0.65f * Mathf.Sin(Mathf.Clamp01(along) * Mathf.PI);

        public void ResetIncision()
        {
            EnsureState();
            for (int i = 0; i < _cut.Length; i++)
            {
                _cut[i] = false; _deep[i] = false; _cutTime[i] = 0f; _lateral[i] = 0f; _byBlade[i] = false;
            }

            _cutCount = 0;
            _sinceDeep = float.PositiveInfinity;
            _sinceEdgeHint = float.PositiveInfinity;
            CutDepth01 = 0f;
            EndContact();
            _announced = false;
            _offLine = 0f;
            _strokes = 0;
            _anyDeep = false;
            _misaligned = 0f;
            EdgeRuleRelaxed = false;
            Grade = default;
            _scratch.Clear();
            _scratchCount = 0;
            _scratchDirty = true;
            IsComplete = false;
            IsWorking = false;
            BladeOnEdge = false;
            _lineDirty = true;
            _beadVersion = -1;
            RefreshMarks();
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
            _lateral = new float[bins];
            _byBlade = new bool[bins];
            _beadScale = null;
            _lineMesh = null;
        }

        /// <summary>The scratches and the bloody blade. Optional: a scene without them still cuts.</summary>
        public void BindMarks(MeshFilter scratches, Renderer blood)
        {
            scratchLine = scratches;
            bladeBlood = blood;
            _scratchMesh = null;
            if (bladeBlood != null) { bladeBlood.enabled = false; }
        }

        /// <summary>
        /// Whether the blade's orientation matters. The builder turns it off when it could not
        /// work out which way the scalpel model's blade faces, rather than guess and reject a
        /// correctly held scalpel.
        /// </summary>
        public void SetEdgeRequirement(bool required) => requireEdge = required;
    }
}
