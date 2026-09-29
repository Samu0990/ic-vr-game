using System;
using System.Collections.Generic;
using UnityEngine;
using VRSurgery.Feedback;
using VRSurgery.Interaction;

namespace VRSurgery.Transplant
{
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

        [SerializeField] private Color markColor = new Color(0.2f, 0.55f, 1f, 0.95f);
        [SerializeField, Min(0.1f)] private float pulseHz = 1.8f;

        private int _stitch;
        private int _phase;
        private float _held;
        private float _clock;

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

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Entry (phase 0) or exit (phase 1) mark of stitch <paramref name="index"/>, in world space.</summary>
        public Vector3 MarkPosition(int index, int phase)
        {
            if (patch == null) { return transform.position; }

            // Head to feet: the first stitch sits near the top of the incision.
            float along = stitches <= 1 ? 0.5f : Mathf.Lerp(0.9f, 0.1f, index / (float)(stitches - 1));

            // Entry on the surgeon's side, exit on the far side.
            float lateral = phase == 0 ? bite : -bite;
            return patch.IncisionPoint(along, lateral, 0.0015f);
        }

        private Vector3 CurrentTarget() => MarkPosition(Mathf.Min(_stitch, stitches - 1), _phase);

        public void Tick(float deltaTime)
        {
            IsWorking = false;
            _clock += Mathf.Max(0f, deltaTime);

            bool active = !IsComplete && patch != null &&
                          (procedure == null || procedure.Stage == TransplantStage.CloseSkin) &&
                          patch.IsClosed;

            UpdateMarks(active);

            if (!active || deltaTime <= 0f || needleTip == null) { return; }
            if (needleHolder != null && !needleHolder.IsHeld) { _held = 0f; return; }

            if (Vector3.Distance(needleTip.position, CurrentTarget()) > radius)
            {
                _held = Mathf.Max(0f, _held - deltaTime);
                return;
            }

            IsWorking = true;
            _held += deltaTime;
            if (_held < holdSeconds) { return; }

            _held = 0f;
            NeedlePassed?.Invoke();

            if (_phase == 0)
            {
                _phase = 1;
                return;
            }

            _phase = 0;
            if (_stitch < knots.Count && knots[_stitch] != null) { knots[_stitch].SetActive(true); }
            _stitch++;
            Tied?.Invoke(_stitch);

            if (_stitch >= stitches)
            {
                IsComplete = true;
                UpdateMarks(false);
                Completed?.Invoke();
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

        public void ResetSuture()
        {
            _stitch = 0;
            _phase = 0;
            _held = 0f;
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
    }
}
