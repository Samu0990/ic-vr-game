using System;
using System.Collections.Generic;
using UnityEngine;
using VRSurgery.Feedback;
using VRSurgery.Interaction;

namespace VRSurgery.Transplant
{
    /// <summary>
    /// Opening the pericardium: the tough sac around the heart, the last layer between the
    /// sternotomy and the heart itself.
    ///
    /// Every account of the operation has it — "after the pericardium is opened, the aorta is
    /// cannulated" — and this one went straight from the saw to the pump, with the heart simply
    /// lying there bare. Now the open chest shows a glistening membrane with the heart beating
    /// under it, and the visitor opens it with the cautery pen along the dashed line down its
    /// middle. The two halves fold back to the wound edges and stay there: the pericardium is
    /// left open at the end of a transplant, so nothing here closes it again.
    ///
    /// The line is judged like the skin incision, in bins the tip has to pass over, so a quick
    /// confident stroke works and a hover does not. The membrane is a paraboloid cap over the
    /// heart; the tip counts when it is on that surface, within a finger's width of the line.
    /// </summary>
    public class PericardiumWorker : MonoBehaviour, IWorkProgressSource
    {
        [Header("Parts")]
        [SerializeField] private Transform tip;
        [SerializeField] private SurgicalInteractable pen;
        [SerializeField] private TransplantProcedure procedure;

        [Tooltip("The two halves, each pivoted on its outer hinge line (local Z along the body).")]
        [SerializeField] private Transform leftHalf;
        [SerializeField] private Transform rightHalf;

        [SerializeField] private Renderer guide;
        [SerializeField] private MeshFilter slit;
        [SerializeField] private ParticleSystem smoke;

        [Header("Membrane (world space, filled by the builder)")]
        [SerializeField] private Vector3 centre;
        [SerializeField] private float halfWidth = 0.05f;
        [SerializeField] private float halfLength = 0.07f;
        [SerializeField] private float top = 1f;
        [SerializeField] private float sag = 0.025f;

        [Tooltip("The part of the midline that has to be opened, as a fraction of the half length.")]
        [SerializeField, Range(0.3f, 1f)] private float lineReach = 0.75f;

        [Header("Tolerances")]
        [SerializeField, Min(4)] private int bins = 14;
        [SerializeField, Min(0.002f)] private float lateralTolerance = 0.013f;
        [SerializeField, Min(0.001f)] private float above = 0.008f;
        [SerializeField, Min(0.001f)] private float below = 0.02f;
        [SerializeField, Range(0.5f, 1f)] private float completeFraction = 0.85f;
        [SerializeField, Min(1)] private int maxStrokeGap = 4;

        [Header("Opening")]
        [SerializeField, Range(30f, 170f)] private float foldDegrees = 115f;
        [SerializeField, Min(0.1f)] private float foldSeconds = 1.1f;

        private bool[] _cut = new bool[0];
        private int _cutCount;
        private int _lastBin = -1;
        private float _open;
        private Quaternion _leftRest = Quaternion.identity;
        private Quaternion _rightRest = Quaternion.identity;
        private bool _restCaptured;
        private Mesh _slitMesh;
        private bool _slitDirty = true;
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<int> _triangles = new List<int>();

        public bool IsWorking { get; private set; }
        public bool IsComplete { get; private set; }
        public float Progress01 => bins <= 0 ? 0f : _cutCount / (float)bins;

        /// <summary>How far the halves have folded back, 0..1.</summary>
        public float Openness01 => _open;

        float IWorkProgressSource.WorkProgress01 => Mathf.Clamp01(Progress01 / completeFraction);
        Vector3 IWorkProgressSource.WorkPoint => tip != null ? tip.position : centre;
        string IWorkProgressSource.WorkLabel => "PERICÁRDIO";
        bool IWorkProgressSource.IsAlarm => false;

        /// <summary>Raised on each frame the pen advances the opening.</summary>
        public event Action Cutting;

        public event Action Completed;

        private void Awake()
        {
            EnsureState();
            CaptureRest();
        }

        private void EnsureState()
        {
            if (_cut == null || _cut.Length != bins) { _cut = new bool[bins]; }
            if (_slitMesh == null && slit != null)
            {
                _slitMesh = new Mesh { name = "PericardioCorte" };
                _slitMesh.MarkDynamic();
                slit.sharedMesh = _slitMesh;
            }
        }

        private void CaptureRest()
        {
            if (_restCaptured) { return; }
            _restCaptured = true;
            if (leftHalf != null) { _leftRest = leftHalf.localRotation; }
            if (rightHalf != null) { _rightRest = rightHalf.localRotation; }
        }

        private void Update() => Tick(Time.deltaTime);

        public void Tick(float deltaTime)
        {
            EnsureState();
            CaptureRest();
            IsWorking = false;

            if (deltaTime > 0f && IsComplete && _open < 1f)
            {
                _open = Mathf.Min(1f, _open + deltaTime / foldSeconds);
                Fold();
            }

            UpdateLook();

            if (deltaTime <= 0f || tip == null || IsComplete) { return; }
            if (procedure != null && procedure.Stage != TransplantStage.OpenPericardium) { _lastBin = -1; return; }
            if (pen != null && !pen.IsHeld) { _lastBin = -1; return; }

            Vector3 p = tip.position;
            float along = AlongOf(p);
            float lateral = p.x - centre.x;
            float depth = SurfaceY(p.x, p.z) - p.y;

            bool onLine = Mathf.Abs(lateral) <= lateralTolerance && depth >= -above && depth <= below &&
                          along >= -0.03f && along <= 1.03f;
            if (!onLine)
            {
                _lastBin = -1;
                return;
            }

            IsWorking = true;
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
                if (_cut[i]) { continue; }
                _cut[i] = true;
                _cutCount++;
                advanced = true;
            }

            _lastBin = bin;

            if (advanced)
            {
                _slitDirty = true;
                if (smoke != null) { smoke.transform.position = p; smoke.Emit(2); }
                Cutting?.Invoke();
            }

            if (Progress01 >= completeFraction) { Finish(); }
        }

        private void Finish()
        {
            for (int i = 0; i < bins; i++)
            {
                if (!_cut[i]) { _cut[i] = true; _cutCount++; }
            }

            IsComplete = true;
            _slitDirty = true;
            Completed?.Invoke();
            if (procedure != null) { procedure.CompleteStage(TransplantStage.OpenPericardium); }
        }

        /// <summary>0 at the feet end of the line, 1 at the head end.</summary>
        private float AlongOf(Vector3 world)
        {
            float reach = halfLength * lineReach;
            return reach <= 0f ? 0f : Mathf.InverseLerp(centre.z - reach, centre.z + reach, world.z);
        }

        /// <summary>Height of the closed membrane over a point.</summary>
        public float SurfaceY(float x, float z)
        {
            float dx = halfWidth > 0f ? (x - centre.x) / halfWidth : 0f;
            float dz = halfLength > 0f ? (z - centre.z) / halfLength : 0f;
            return top - sag * (dx * dx + dz * dz);
        }

        /// <summary>A point on the line to be opened, <paramref name="along"/> 0..1, lifted off the membrane.</summary>
        public Vector3 LinePoint(float along, float lift = 0f)
        {
            float reach = halfLength * lineReach;
            float z = Mathf.Lerp(centre.z - reach, centre.z + reach, Mathf.Clamp01(along));
            return new Vector3(centre.x, SurfaceY(centre.x, z) + lift, z);
        }

        private void Fold()
        {
            float angle = foldDegrees * Mathf.SmoothStep(0f, 1f, _open);

            // Each half swings up and over its outer hinge, away from the midline.
            if (leftHalf != null) { leftHalf.localRotation = _leftRest * Quaternion.Euler(0f, 0f, angle); }
            if (rightHalf != null) { rightHalf.localRotation = _rightRest * Quaternion.Euler(0f, 0f, -angle); }
        }

        private void UpdateLook()
        {
            if (guide != null)
            {
                bool show = !IsComplete && procedure != null && procedure.Stage == TransplantStage.OpenPericardium;
                if (guide.enabled != show) { guide.enabled = show; }
            }

            if (slit != null)
            {
                MeshRenderer renderer = slit.GetComponent<MeshRenderer>();
                bool show = _cutCount > 0 && _open < 0.05f;
                if (renderer != null && renderer.enabled != show) { renderer.enabled = show; }
            }

            if (!_slitDirty || _slitMesh == null) { return; }
            _slitDirty = false;

            _vertices.Clear();
            _triangles.Clear();
            Transform space = slit.transform;
            const float width = 0.0016f;

            for (int i = 0; i < bins; i++)
            {
                if (!_cut[i]) { continue; }
                Vector3 a = LinePoint(i / (float)bins, 0.0008f);
                Vector3 b = LinePoint((i + 1) / (float)bins, 0.0008f);
                int s = _vertices.Count;
                _vertices.Add(space.InverseTransformPoint(a + Vector3.left * width));
                _vertices.Add(space.InverseTransformPoint(a + Vector3.right * width));
                _vertices.Add(space.InverseTransformPoint(b + Vector3.left * width));
                _vertices.Add(space.InverseTransformPoint(b + Vector3.right * width));
                _triangles.Add(s); _triangles.Add(s + 2); _triangles.Add(s + 1);
                _triangles.Add(s + 1); _triangles.Add(s + 2); _triangles.Add(s + 3);
            }

            _slitMesh.Clear();
            _slitMesh.SetVertices(_vertices);
            _slitMesh.SetTriangles(_triangles, 0);
            _slitMesh.RecalculateNormals();
            _slitMesh.RecalculateBounds();
        }

        public void ResetPericardium()
        {
            EnsureState();
            CaptureRest();
            for (int i = 0; i < _cut.Length; i++) { _cut[i] = false; }
            _cutCount = 0;
            _lastBin = -1;
            _open = 0f;
            IsComplete = false;
            IsWorking = false;
            _slitDirty = true;
            Fold();
            UpdateLook();
        }

        public void Bind(Transform penTip, SurgicalInteractable cauteryPen, TransplantProcedure transplant,
            Transform left, Transform right, Renderer guideLine, MeshFilter slitLine, ParticleSystem smokePuffs)
        {
            tip = penTip;
            pen = cauteryPen;
            procedure = transplant;
            leftHalf = left;
            rightHalf = right;
            guide = guideLine;
            slit = slitLine;
            smoke = smokePuffs;
            _slitMesh = null;
            _restCaptured = false;
            _cut = new bool[bins];
        }

        /// <summary>The membrane's shape, in world space: a cap over the heart.</summary>
        public void Shape(Vector3 capCentre, float width, float length, float capTop, float capSag)
        {
            centre = capCentre;
            halfWidth = width;
            halfLength = length;
            top = capTop;
            sag = capSag;
        }
    }
}
