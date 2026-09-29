using System;
using System.Collections.Generic;
using UnityEngine;
using VRSurgery.Feedback;
using VRSurgery.Interaction;

namespace VRSurgery.Transplant
{
    /// <summary>How the skin closure went, for the result card: on target, clean, and not slow.</summary>
    [Serializable]
    public struct SutureGrade
    {
        public int Score;
        public int Stars;
        public int Stitches;
        public float AccuracyMm;
        public int Misses;
        public float Seconds;

        public string Title => Stars >= 3 ? "SUTURA PERFEITA" : Stars == 2 ? "BOA SUTURA" : "SUTURA IRREGULAR";

        public string Detail
        {
            get
            {
                string text = $"{Stitches} pontos · {AccuracyMm:F0} mm do alvo · {Seconds:F0} s";
                if (Misses > 0) { text += $" · {Misses} furo{(Misses == 1 ? "" : "s")} fora"; }
                return text;
            }
        }

        /// <summary>
        /// Bites placed where they were marked, no needle pushed through skin anywhere else, and a
        /// pace that would not keep a patient on the table: what a preceptor looks at in a closure.
        /// </summary>
        public static SutureGrade From(int stitches, float accuracyMm, int misses, float seconds)
        {
            float score = 100f;
            score -= Mathf.Clamp((accuracyMm - 3f) * 4f, 0f, 30f);
            score -= Mathf.Min(30f, 10f * misses);
            score -= Mathf.Clamp(seconds - 30f, 0f, 25f);

            int rounded = Mathf.Clamp(Mathf.RoundToInt(score), 0, 100);
            return new SutureGrade
            {
                Score = rounded,
                Stars = rounded >= 85 ? 3 : rounded >= 60 ? 2 : 1,
                Stitches = stitches,
                AccuracyMm = accuracyMm,
                Misses = misses,
                Seconds = seconds,
            };
        }
    }

    /// <summary>
    /// Closing the skin with interrupted stitches: for each stitch the needle goes in on one
    /// side of the incision and comes out on the other, and a knotted loop is left behind.
    ///
    /// Each bite is a short hold with the needle tip on a blue mark — the same "hold, not tap"
    /// the rest of the operation uses — so the needle is not driven through by a hand that only
    /// brushed past. The two marks of the current stitch are the only ones shown, entry first,
    /// then exit, from the head of the incision toward the feet, which is the order the stitches
    /// are actually laid.
    ///
    /// It waits for the wound to be closed before it will take a bite: stitches are placed on
    /// skin edges brought together, not across an open chest.
    /// </summary>
    public class SutureWorker : MonoBehaviour, IWorkProgressSource
    {
        [Header("Parts")]
        [Tooltip("The needle's point.")]
        [SerializeField] private Transform needleTip;

        [Tooltip("The needle holder. Stitches only count while it is held.")]
        [SerializeField] private SurgicalInteractable needleHolder;

        [SerializeField] private ChestSkinPatch patch;
        [SerializeField] private TransplantProcedure procedure;

        [Header("Stitches")]
        [SerializeField, Range(2, 10)] private int stitches = 5;

        [Tooltip("Distance of entry and exit from the incision, in metres.")]
        [SerializeField, Min(0.003f)] private float bite = 0.009f;

        [Tooltip("How close the needle has to be to a mark, in metres.")]
        [SerializeField, Min(0.004f)] private float radius = 0.013f;

        [Tooltip("Seconds on a mark to pass the needle.")]
        [SerializeField, Min(0.05f)] private float holdSeconds = 0.35f;

        [Header("Look")]
        [Tooltip("Two marks per stitch: entry then exit.")]
        [SerializeField] private List<Renderer> marks = new List<Renderer>();

        [Tooltip("One tied stitch per stitch, hidden until it is made.")]
        [SerializeField] private List<GameObject> knots = new List<GameObject>();

        [Tooltip("The thread running from the last bite to the needle while a stitch is being made.")]
        [SerializeField] private LineRenderer thread;

        [SerializeField] private Color markColor = new Color(0.2f, 0.55f, 1f, 0.95f);
        [SerializeField, Min(0.1f)] private float pulseHz = 1.8f;

        private int _stitch;
        private int _phase;
        private float _held;
        private float _clock;

        private float _started = -1f;
        private float _accuracySum;
        private int _passes;
        private int _misses;
        private bool _poking;
        private readonly Vector3[] _threadPoints = new Vector3[8];

        public bool IsWorking { get; private set; }
        public bool IsComplete { get; private set; }
        public int StitchesTied => _stitch;
        public int StitchCount => stitches;

        /// <summary>0 before the first bite, 1 when every stitch is tied.</summary>
        public float Progress01 => stitches <= 0 ? 0f : (_stitch + _phase * 0.5f) / stitches;

        float IWorkProgressSource.WorkProgress01 => Mathf.Clamp01((_phase + _held / holdSeconds) / 2f);
        Vector3 IWorkProgressSource.WorkPoint => CurrentTarget();
        string IWorkProgressSource.WorkLabel => $"PONTO {Mathf.Min(_stitch + 1, stitches)}/{stitches}";
        bool IWorkProgressSource.IsAlarm => false;

        /// <summary>The needle passed through one side.</summary>
        public event Action NeedlePassed;

        /// <summary>A stitch was tied. Carries how many are done.</summary>
        public event Action<int> Tied;

        public event Action Completed;

        /// <summary>Raised once, as the last stitch is tied, with how the closure went.</summary>
        public event Action<SutureGrade> Graded;

        /// <summary>The finished closure's grade. Only meaningful once <see cref="IsComplete"/>.</summary>
        public SutureGrade Grade { get; private set; }

        /// <summary>Times the needle went through skin away from the mark being aimed at.</summary>
        public int Misses => _misses;

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Entry (phase 0) or exit (phase 1) mark of stitch <paramref name="index"/>, in world space.</summary>
        public Vector3 MarkPosition(int index, int phase)
        {
            if (patch == null) { return transform.position; }

            // Entry on the surgeon's side, exit on the far side.
            float lateral = phase == 0 ? bite : -bite;
            return patch.IncisionPoint(StitchAlong(index), lateral, 0.0015f);
        }

        /// <summary>Head to feet: the first stitch sits near the top of the incision.</summary>
        private float StitchAlong(int index) =>
            stitches <= 1 ? 0.5f : Mathf.Lerp(0.9f, 0.1f, index / (float)(stitches - 1));

        /// <summary>Half the stretch of incision one stitch holds shut, with the ends covered too.</summary>
        private float StitchReach => stitches <= 1 ? 0.5f : 0.4f / (stitches - 1) + 0.1f / stitches;

        private Vector3 CurrentTarget() => MarkPosition(Mathf.Min(_stitch, stitches - 1), _phase);

        public void Tick(float deltaTime)
        {
            IsWorking = false;
            _clock += Mathf.Max(0f, deltaTime);

            bool active = !IsComplete && patch != null &&
                          (procedure == null || procedure.Stage == TransplantStage.CloseSkin) &&
                          patch.IsClosed;

            UpdateMarks(active);
            UpdateThread(active);

            if (!active || deltaTime <= 0f || needleTip == null) { return; }
            if (needleHolder != null && !needleHolder.IsHeld) { _held = 0f; _poking = false; return; }

            float distance = Vector3.Distance(needleTip.position, CurrentTarget());
            bool inSkin = patch.SurfaceHeightUnder(needleTip.position) - needleTip.position.y > 0.001f;

            if (distance > radius)
            {
                // A needle pushed into the skin somewhere other than the entry mark is a hole
                // where none was wanted. Only while looking for the entry: between the bite in
                // and the bite out the needle is meant to be under the skin. And only a fresh
                // push: the needle still in the skin from the last bite has to come out first.
                if (_phase == 0 && inSkin && !_poking && NearWound(needleTip.position)) { _misses++; }
                _poking = inSkin;
                _held = Mathf.Max(0f, _held - deltaTime);
                return;
            }

            if (_started < 0f) { _started = _clock; }

            IsWorking = true;
            _held += deltaTime;

            // The needle point dents the skin as it is pushed, then the skin gives.
            float surface = patch.SurfaceHeightUnder(needleTip.position);
            float depth = surface - needleTip.position.y;
            if (depth > -0.003f) { patch.Press(needleTip.position, Mathf.Max(0.0015f, depth)); }

            if (_held < holdSeconds) { return; }

            _held = 0f;
            _accuracySum += distance;
            _passes++;
            _poking = true;
            NeedlePassed?.Invoke();

            if (_phase == 0)
            {
                _phase = 1;
                return;
            }

            _phase = 0;
            if (_stitch < knots.Count && knots[_stitch] != null) { knots[_stitch].SetActive(true); }

            // Tying the stitch draws the gaping edges together over its share of the incision.
            patch.SetCut(StitchAlong(_stitch) - StitchReach, StitchAlong(_stitch) + StitchReach, false);
            _stitch++;
            Tied?.Invoke(_stitch);

            if (_stitch >= stitches)
            {
                IsComplete = true;
                UpdateMarks(false);
                UpdateThread(false);
                Grade = SutureGrade.From(stitches, _passes > 0 ? _accuracySum / _passes * 1000f : 0f, _misses,
                    _started < 0f ? 0f : _clock - _started);
                Debug.Log($"[Transplante] sutura: {Grade.Title} ({Grade.Score}/100) — {Grade.Detail}");
                Completed?.Invoke();
                Graded?.Invoke(Grade);
                if (procedure != null) { procedure.CompleteStage(TransplantStage.CloseSkin); }
            }
        }

        private void UpdateMarks(bool active)
        {
            float pulse = 0.6f + 0.4f * Mathf.Sin(_clock * pulseHz * Mathf.PI * 2f);

            for (int i = 0; i < marks.Count; i++)
            {
                Renderer mark = marks[i];
                if (mark == null) { continue; }

                int index = i / 2;
                int phase = i % 2;
                bool current = active && index == _stitch;
                bool show = current && phase >= _phase;
                if (mark.enabled != show) { mark.enabled = show; }

                if (show && mark.sharedMaterial != null)
                {
                    // The mark to aim at now pulses; the one after it waits, dim.
                    Color c = markColor;
                    c.a *= phase == _phase ? pulse : 0.35f;
                    mark.sharedMaterial.color = c;
                }
            }
        }

        /// <summary>On the skin around the incision, where a stray needle would leave a mark that matters.</summary>
        private bool NearWound(Vector3 point)
        {
            float along = patch.AlongIncision(patch.LongitudinalOf(point));
            return Mathf.Abs(patch.LateralOf(point)) < 0.05f && along > -0.15f && along < 1.15f;
        }

        /// <summary>
        /// Between the bite in and the bite out, the thread runs from where the needle went in to
        /// the needle, sagging a little: the stitch being made is visibly one piece of thread.
        /// </summary>
        private void UpdateThread(bool active)
        {
            if (thread == null) { return; }

            bool show = active && _phase == 1 && needleTip != null;
            if (thread.enabled != show) { thread.enabled = show; }
            if (!show) { return; }

            Vector3 from = MarkPosition(Mathf.Min(_stitch, stitches - 1), 0);
            Vector3 to = needleTip.position;
            float sag = Mathf.Min(0.02f, Vector3.Distance(from, to) * 0.25f);
            for (int i = 0; i < _threadPoints.Length; i++)
            {
                float t = i / (float)(_threadPoints.Length - 1);
                _threadPoints[i] = Vector3.Lerp(from, to, t) + Vector3.down * (sag * 4f * t * (1f - t));
            }

            if (thread.positionCount != _threadPoints.Length) { thread.positionCount = _threadPoints.Length; }
            thread.SetPositions(_threadPoints);
        }

        public void ResetSuture()
        {
            _stitch = 0;
            _phase = 0;
            _held = 0f;
            _started = -1f;
            _accuracySum = 0f;
            _passes = 0;
            _misses = 0;
            _poking = false;
            Grade = default;
            if (thread != null) { thread.enabled = false; }
            IsComplete = false;
            IsWorking = false;

            for (int i = 0; i < knots.Count; i++)
            {
                if (knots[i] != null) { knots[i].SetActive(false); }
            }

            UpdateMarks(false);
        }

        public void Bind(Transform tip, SurgicalInteractable holder, ChestSkinPatch skin,
            TransplantProcedure transplant, int stitchCount, IEnumerable<Renderer> stitchMarks,
            IEnumerable<GameObject> tiedStitches)
        {
            needleTip = tip;
            needleHolder = holder;
            patch = skin;
            procedure = transplant;
            stitches = Mathf.Clamp(stitchCount, 2, 10);
            marks = stitchMarks != null ? new List<Renderer>(stitchMarks) : new List<Renderer>();
            knots = tiedStitches != null ? new List<GameObject>(tiedStitches) : new List<GameObject>();
        }

        /// <summary>Distance of the marks from the incision. The builder places marks and knots from it.</summary>
        public float BiteDistance => bite;

        /// <summary>The thread shown while a stitch is being made. Optional.</summary>
        public void BindThread(LineRenderer line)
        {
            thread = line;
            if (thread != null)
            {
                thread.useWorldSpace = true;
                thread.enabled = false;
            }
        }
    }
}
