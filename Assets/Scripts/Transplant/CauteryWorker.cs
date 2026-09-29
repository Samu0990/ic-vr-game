using System;
using System.Collections.Generic;
using UnityEngine;
using VRSurgery.Feedback;
using VRSurgery.Interaction;

namespace VRSurgery.Transplant
{
    /// <summary>
    /// Electrocautery: the pen that seals the small vessels bleeding from the edges of the skin
    /// incision, with the puff of smoke and the sizzle every open operation has.
    ///
    /// When the wound is retracted open a few bleeders appear on its edges, pulsing. Resting the
    /// pen's tip on one for a moment seals it: smoke rises from the tip, the bleeder stops and a
    /// small scorch is left where it was. It is not a stage the operation waits on — a surgeon
    /// who leaves them simply works in a bloodier field — but it is the first thing the team does
    /// after the incision, and the one gesture every visitor recognises from television.
    /// </summary>
    public class CauteryWorker : MonoBehaviour, IWorkProgressSource
    {
        [SerializeField] private Transform penTip;
        [SerializeField] private SurgicalInteractable pen;
        [SerializeField] private ChestSkinPatch patch;

        [Tooltip("Bleeding points on the wound edges, shown while the wound is open.")]
        [SerializeField] private List<Transform> bleeders = new List<Transform>();

        [Tooltip("Scorch marks, one per bleeder, shown once it is sealed.")]
        [SerializeField] private List<GameObject> scorches = new List<GameObject>();

        [SerializeField] private ParticleSystem smoke;

        [SerializeField, Min(0.003f)] private float radius = 0.012f;
        [SerializeField, Min(0.05f)] private float holdSeconds = 0.4f;
        [SerializeField, Min(0.1f)] private float pulseHz = 1.4f;

        private bool[] _sealed = new bool[0];
        private Vector3[] _bleederScale = new Vector3[0];
        private float _held;
        private int _target = -1;
        private float _clock;
        private float _sinceWisp;

        public bool IsWorking { get; private set; }

        /// <summary>Bleeders still open.</summary>
        public int OpenBleeders
        {
            get
            {
                if (!ShowingBleeders) { return 0; }
                int open = 0;
                for (int i = 0; i < _sealed.Length; i++) { if (!_sealed[i]) { open++; } }
                return open;
            }
        }

        private bool ShowingBleeders => patch != null && patch.Openness01 > 0.9f;

        float IWorkProgressSource.WorkProgress01 => Mathf.Clamp01(_held / holdSeconds);
        Vector3 IWorkProgressSource.WorkPoint => penTip != null ? penTip.position : transform.position;
        string IWorkProgressSource.WorkLabel => "CAUTERIZANDO";
        bool IWorkProgressSource.IsAlarm => false;

        /// <summary>A bleeder sealed. Carries which one.</summary>
        public event Action<int> Sealed;

        /// <summary>Raised on every frame the pen is burning tissue: sound and hand buzz follow it.</summary>
        public event Action Burning;

        private void Awake() => EnsureState();

        private void EnsureState()
        {
            if (_sealed == null || _sealed.Length != bleeders.Count) { _sealed = new bool[bleeders.Count]; }
            if (_bleederScale == null || _bleederScale.Length != bleeders.Count)
            {
                _bleederScale = new Vector3[bleeders.Count];
                for (int i = 0; i < bleeders.Count; i++)
                {
                    if (bleeders[i] != null) { _bleederScale[i] = bleeders[i].localScale; }
                }
            }
        }

        private void Update() => Tick(Time.deltaTime);

        public void Tick(float deltaTime)
        {
            EnsureState();
            IsWorking = false;
            _clock += Mathf.Max(0f, deltaTime);
            _sinceWisp += Mathf.Max(0f, deltaTime);

            bool showing = ShowingBleeders;
            float pulse = 0.8f + 0.2f * Mathf.Sin(_clock * pulseHz * Mathf.PI * 2f);

            for (int i = 0; i < bleeders.Count; i++)
            {
                Transform b = bleeders[i];
                if (b == null) { continue; }

                bool open = showing && !_sealed[i];
                if (b.gameObject.activeSelf != open) { b.gameObject.SetActive(open); }
                if (open) { b.localScale = _bleederScale[i] * pulse; }

                if (i < scorches.Count && scorches[i] != null)
                {
                    bool scorch = showing && _sealed[i];
                    if (scorches[i].activeSelf != scorch) { scorches[i].SetActive(scorch); }
                }
            }

            if (!showing || deltaTime <= 0f || penTip == null) { _held = 0f; return; }
            if (pen != null && !pen.IsHeld) { _held = 0f; return; }

            int nearest = -1;
            float best = radius;
            for (int i = 0; i < bleeders.Count; i++)
            {
                if (_sealed[i] || bleeders[i] == null) { continue; }
                float d = Vector3.Distance(penTip.position, bleeders[i].position);
                if (d <= best) { best = d; nearest = i; }
            }

            if (nearest != _target) { _target = nearest; _held = 0f; }
            if (nearest < 0) { return; }

            IsWorking = true;
            _held += deltaTime;
            Burning?.Invoke();

            // A wisp of smoke the whole time the tip is burning, a puff when the vessel seals.
            if (_sinceWisp >= 0.06f)
            {
                _sinceWisp = 0f;
                EmitSmoke(2);
            }

            if (_held < holdSeconds) { return; }

            _sealed[nearest] = true;
            _held = 0f;
            _target = -1;
            EmitSmoke(18);
            Sealed?.Invoke(nearest);
        }

        private void EmitSmoke(int count)
        {
            if (smoke == null || penTip == null) { return; }
            smoke.transform.position = penTip.position;
            smoke.Emit(count);
        }

        public void ResetCautery()
        {
            EnsureState();
            for (int i = 0; i < _sealed.Length; i++) { _sealed[i] = false; }
            _held = 0f;
            _target = -1;
        }

        public void Bind(Transform tip, SurgicalInteractable cauteryPen, ChestSkinPatch skin,
            IEnumerable<Transform> bleedingPoints, IEnumerable<GameObject> scorchMarks, ParticleSystem smokePuff)
        {
            penTip = tip;
            pen = cauteryPen;
            patch = skin;
            bleeders = new List<Transform>(bleedingPoints);
            scorches = new List<GameObject>(scorchMarks);
            smoke = smokePuff;
            _sealed = new bool[bleeders.Count];
            _bleederScale = null;
        }
    }
}
