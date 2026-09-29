using System;
using UnityEngine;

namespace VRSurgery.Transplant
{
    /// <summary>
    /// The skin inside the drape window: two halves that meet on the sternal midline and part
    /// when the incision is opened.
    ///
    /// The body model is 30k triangles for a whole 2 m figure — about 4 cm per triangle over the
    /// chest — so neither a cut through it nor a hole in it can have a clean edge. The drapes
    /// already hide everything but a window over the sternum, which is exactly how a real chest is
    /// prepared, so the window gets its own fine surface instead: a height grid sampled from the
    /// body at build time, split down the midline, rebuilt here as two meshes. Parting them is a
    /// lateral shift that is widest at the middle of the incision and fades to nothing at its ends
    /// and under the drape, so the opening reads as an eye-shaped wound held open, not a hole.
    ///
    /// All positions live in this object's local space: X across the patient, Z along the body,
    /// Y up. The builder places the object on the midline with identity rotation.
    /// </summary>
    public class ChestSkinPatch : MonoBehaviour
    {
        [Header("Sampled surface (filled by the scene builder)")]
        [SerializeField, Min(2)] private int columnsPerHalf = 13;
        [SerializeField, Min(2)] private int rows = 33;
        [SerializeField] private float halfWidth = 0.10f;
        [SerializeField] private float halfLength = 0.16f;

        [Tooltip("Local surface height, row-major: rows x (2 * columnsPerHalf - 1), from -halfWidth to +halfWidth.")]
        [SerializeField] private float[] heights = new float[0];

        [Header("Incision")]
        [Tooltip("Where the incision starts and ends along the patch, as fractions of its length.")]
        [SerializeField, Range(0f, 1f)] private float incisionStart = 0.14f;
        [SerializeField, Range(0f, 1f)] private float incisionEnd = 0.86f;

        [Tooltip("How far each wound edge retracts at the middle of the incision, in metres.")]
        [SerializeField] private float retraction = 0.055f;

        [Tooltip("Depth of the visible wound wall, in metres.")]
        [SerializeField] private float woundDepth = 0.02f;

        [Header("Parts")]
        [SerializeField] private MeshFilter leftSkin;
        [SerializeField] private MeshFilter rightSkin;
        [SerializeField] private MeshFilter leftWound;
        [SerializeField] private MeshFilter rightWound;

        [Tooltip("Shown only while the wound is open: the cavity liner and anything under the skin.")]
        [SerializeField] private GameObject[] revealWhenOpen = new GameObject[0];

        [Header("Timing")]
        [SerializeField, Min(0.1f)] private float openSeconds = 1.2f;
        [SerializeField, Min(0.1f)] private float closeSeconds = 1.0f;

        [Header("Tissue response")]
        [Tooltip("How far each edge of a fresh skin cut springs back, in metres. Skin is under " +
                 "tension: a real incision gapes a couple of millimetres the moment it is made.")]
        [SerializeField, Min(0f)] private float gapeWidth = 0.0022f;

        [Tooltip("Seconds for a fresh cut to spring open, or for a stitch to draw it shut.")]
        [SerializeField, Min(0.01f)] private float gapeSeconds = 0.35f;

        [Tooltip("Radius of the dent a blade or needle pushes into the skin, in metres.")]
        [SerializeField, Min(0.002f)] private float pressRadius = 0.012f;

        [Tooltip("Deepest dent the skin takes before it gives way, in metres.")]
        [SerializeField, Min(0f)] private float maxPress = 0.006f;

        [Tooltip("Farthest the wound may lie from the drawn midline, in metres. The skin parts " +
                 "where the blade actually went, within this much: a hand that wandered leaves " +
                 "a wound that wanders, never one that crosses the stitch marks.")]
        [SerializeField, Min(0f)] private float maxWander = 0.004f;

        private Mesh _left, _right, _leftWall, _rightWall;
        private int _direction;
        private float _drawn = -1f;
        private bool _dirty = true;

        private float[] _gapeTarget = new float[0];
        private float[] _gapeCurrent = new float[0];
        private float[] _wander = new float[0];

        private Vector3 _pressLocal;
        private float _pressTarget;
        private float _pressCurrent;
        private bool _pressedThisFrame;

        public float Openness01 { get; private set; }
        public bool IsOpen => Openness01 >= 1f;
        public bool IsClosed => Openness01 <= 0f && _direction == 0;
        public bool IsMoving => _direction != 0;

        public float IncisionStart => incisionStart;
        public float IncisionEnd => incisionEnd;

        /// <summary>Length of the incision itself, in metres.</summary>
        public float IncisionLength => 2f * halfLength * Mathf.Abs(incisionEnd - incisionStart);

        /// <summary>Goes up every time the meshes are rebuilt, so anything drawn on the skin knows to follow.</summary>
        public int Version { get; private set; }

        public event Action Opened;
        public event Action Closed;

        private void Awake()
        {
            EnsureMeshes();
            Openness01 = 0f;
            _direction = 0;
            Rebuild(0f);
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Advances opening, closing, the cut's gape and any dent. Stepped by hand in tests.</summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f) { return; }

            TickTissue(deltaTime);

            if (_direction == 0)
            {
                if (_dirty) { Rebuild(Openness01); }
                return;
            }

            float duration = _direction > 0 ? openSeconds : closeSeconds;
            Openness01 = Mathf.Clamp01(Openness01 + _direction * deltaTime / duration);
            Rebuild(Openness01);

            if (_direction > 0 && Openness01 >= 1f)
            {
                _direction = 0;
                Opened?.Invoke();
            }
            else if (_direction < 0 && Openness01 <= 0f)
            {
                _direction = 0;
                Closed?.Invoke();
            }
        }

        public void Open()
        {
            if (Openness01 >= 1f) { return; }
            _direction = 1;
        }

        public void Close()
        {
            if (Openness01 <= 0f) { return; }
            _direction = -1;
        }

        public void ResetClosed()
        {
            _direction = 0;
            Openness01 = 0f;
            EnsureGape();
            for (int i = 0; i < _gapeTarget.Length; i++) { _gapeTarget[i] = 0f; _gapeCurrent[i] = 0f; _wander[i] = 0f; }
            _pressTarget = 0f;
            _pressCurrent = 0f;
            _dirty = true;
            Rebuild(0f);
        }

        // ---------------------------------------------------------------- tissue response

        /// <summary>
        /// Marks a stretch of the incision as cut (it springs a little open) or sutured (it is
        /// drawn back shut). <paramref name="alongFrom"/> and <paramref name="alongTo"/> run 0..1
        /// over the incision.
        /// </summary>
        public void SetCut(float alongFrom, float alongTo, bool cut) => SetCut(alongFrom, alongTo, cut, 0f, 0f);

        /// <summary>
        /// As <see cref="SetCut(float, float, bool)"/>, with where the blade actually ran: the
        /// lateral offset from the midline at each end of the stretch, in metres. The wound lies
        /// there rather than on the drawn line. Ignored when stitching: a sutured wound stays
        /// where it was cut.
        /// </summary>
        public void SetCut(float alongFrom, float alongTo, bool cut, float lateralFrom, float lateralTo)
        {
            EnsureGape();
            float lo = Mathf.Min(alongFrom, alongTo), hi = Mathf.Max(alongFrom, alongTo);

            for (int r = 0; r < rows; r++)
            {
                float along = AlongIncision(r / (float)(rows - 1));
                if (along < lo - 1e-4f || along > hi + 1e-4f || along < 0f || along > 1f) { continue; }

                float target = cut ? gapeWidth * EndTaper(along) : 0f;
                if (!Mathf.Approximately(_gapeTarget[r], target)) { _gapeTarget[r] = target; _dirty = true; }

                if (!cut) { continue; }

                float k = hi - lo > 1e-5f ? Mathf.InverseLerp(alongFrom, alongTo, along) : 0.5f;
                float wander = Mathf.Clamp(Mathf.Lerp(lateralFrom, lateralTo, k), -maxWander, maxWander) * EndTaper(along);
                if (!Mathf.Approximately(_wander[r], wander)) { _wander[r] = wander; _dirty = true; }
            }
        }

        /// <summary>How far the wound lies from the drawn midline at a point along the incision, in metres.</summary>
        public float WanderAt(float along)
        {
            EnsureGape();
            float t = Mathf.Lerp(incisionStart, incisionEnd, Mathf.Clamp01(along));
            float f = t * (rows - 1);
            int r0 = Mathf.Clamp(Mathf.FloorToInt(f), 0, rows - 1);
            int r1 = Mathf.Min(r0 + 1, rows - 1);
            return Mathf.Lerp(WanderRow(r0), WanderRow(r1), f - r0);
        }

        /// <summary>A point on the wound itself: the midline shifted to where the blade ran.</summary>
        public Vector3 WoundPoint(float along, float lateral = 0f, float lift = 0.0012f) =>
            IncisionPoint(along, WanderAt(along) + lateral, lift);

        /// <summary>
        /// Where a mark made on the closed skin is now, after the skin gaped, dented or was
        /// retracted. <paramref name="restLateral"/> is metres from the midline and
        /// <paramref name="longitudinal"/> the 0..1 patch fraction at the moment it was made.
        /// </summary>
        public Vector3 SurfacePoint(float restLateral, float longitudinal, float lift)
        {
            EnsureGape();
            float side = restLateral < 0f ? -1f : 1f;
            float u = halfWidth > 0f ? Mathf.Clamp01(Mathf.Abs(restLateral) / halfWidth) : 0f;
            float f = Mathf.Clamp01(longitudinal) * (rows - 1);
            int r0 = Mathf.Clamp(Mathf.FloorToInt(f), 0, rows - 1);
            int r1 = Mathf.Min(r0 + 1, rows - 1);

            Vector3 a = Deformed(side, u, r0, _drawn < 0f ? Openness01 : _drawn);
            Vector3 b = Deformed(side, u, r1, _drawn < 0f ? Openness01 : _drawn);
            Vector3 local = Vector3.Lerp(a, b, f - r0);
            local.z = Mathf.Lerp(-halfLength, halfLength, Mathf.Clamp01(longitudinal));
            return transform.TransformPoint(local + new Vector3(0f, lift, 0f));
        }

        /// <summary>How far apart the cut edges stand at a point along the incision, in metres.</summary>
        public float GapeAt(float along)
        {
            EnsureGape();
            float t = Mathf.Lerp(incisionStart, incisionEnd, Mathf.Clamp01(along));
            int r = Mathf.Clamp(Mathf.RoundToInt(t * (rows - 1)), 0, rows - 1);
            return _gapeCurrent.Length > r ? _gapeCurrent[r] * 2f : 0f;
        }

        /// <summary>
        /// A tip pushing into the skin this frame. The skin dents under it up to its give, then the
        /// tip is through. Call every frame the tip is pressing; the dent relaxes when it stops.
        /// </summary>
        public void Press(Vector3 world, float depth)
        {
            _pressLocal = transform.InverseTransformPoint(world);
            _pressTarget = Mathf.Clamp(depth, 0f, maxPress);
            _pressedThisFrame = true;
        }

        /// <summary>Current dent depth, in metres. Exposed for tests.</summary>
        public float PressDepth => _pressCurrent;

        private void TickTissue(float deltaTime)
        {
            EnsureGape();

            float gapeStep = gapeWidth * deltaTime / gapeSeconds;
            for (int i = 0; i < _gapeCurrent.Length; i++)
            {
                float next = Mathf.MoveTowards(_gapeCurrent[i], _gapeTarget[i], gapeStep);
                if (!Mathf.Approximately(next, _gapeCurrent[i])) { _gapeCurrent[i] = next; _dirty = true; }
            }

            float pressGoal = _pressedThisFrame ? _pressTarget : 0f;
            _pressedThisFrame = false;

            // Skin gives quickly under a tip and comes back a little slower, as it does.
            float rate = pressGoal > _pressCurrent ? 0.08f : 0.03f;
            float pressed = Mathf.MoveTowards(_pressCurrent, pressGoal, rate * deltaTime);
            if (!Mathf.Approximately(pressed, _pressCurrent)) { _pressCurrent = pressed; _dirty = true; }
        }

        private void EnsureGape()
        {
            if (_gapeTarget == null || _gapeTarget.Length != rows) { _gapeTarget = new float[rows]; }
            if (_gapeCurrent == null || _gapeCurrent.Length != rows) { _gapeCurrent = new float[rows]; }
            if (_wander == null || _wander.Length != rows) { _wander = new float[rows]; }
        }

        /// <summary>A cut gapes least at its two ends, where the skin either side is uncut.</summary>
        private static float EndTaper(float along) => Mathf.Clamp01(Mathf.Sin(Mathf.Clamp01(along) * Mathf.PI) * 1.6f);

        private float Dent(float x, float z)
        {
            if (_pressCurrent <= 0f) { return 0f; }
            float dx = x - _pressLocal.x, dz = z - _pressLocal.z;
            float sigma = pressRadius * 0.5f;
            return _pressCurrent * Mathf.Exp(-(dx * dx + dz * dz) / (2f * sigma * sigma));
        }

        /// <summary>Longitudinal fraction (0 = feet end, 1 = head end) of a world point.</summary>
        public float LongitudinalOf(Vector3 world)
        {
            Vector3 local = transform.InverseTransformPoint(world);
            return Mathf.InverseLerp(-halfLength, halfLength, local.z);
        }

        /// <summary>Signed lateral distance of a world point from the midline, in metres.</summary>
        public float LateralOf(Vector3 world) => transform.InverseTransformPoint(world).x;

        /// <summary>Closed-skin height under a world point, in world Y.</summary>
        public float SurfaceHeightUnder(Vector3 world)
        {
            Vector3 local = transform.InverseTransformPoint(world);
            return transform.TransformPoint(new Vector3(local.x, Height(local.x, local.z), local.z)).y;
        }

        /// <summary>
        /// A point on the closed skin. <paramref name="along"/> runs 0..1 over the incision (feet
        /// to head), <paramref name="lateral"/> is metres from the midline, <paramref name="lift"/>
        /// metres above the surface.
        /// </summary>
        public Vector3 IncisionPoint(float along, float lateral = 0f, float lift = 0.0012f)
        {
            float t = Mathf.Lerp(incisionStart, incisionEnd, Mathf.Clamp01(along));
            float z = Mathf.Lerp(-halfLength, halfLength, t);
            return transform.TransformPoint(new Vector3(lateral, Height(lateral, z) + lift, z));
        }

        /// <summary>Along-incision fraction of a longitudinal patch fraction, unclamped.</summary>
        public float AlongIncision(float longitudinal) =>
            Mathf.Approximately(incisionEnd, incisionStart)
                ? 0f
                : (longitudinal - incisionStart) / (incisionEnd - incisionStart);

        // ---------------------------------------------------------------- geometry

        private int FullColumns => columnsPerHalf * 2 - 1;

        /// <summary>Bilinear sample of the stored surface, in local space.</summary>
        public float Height(float x, float z)
        {
            if (heights == null || heights.Length < rows * FullColumns) { return 0f; }

            float fx = Mathf.InverseLerp(-halfWidth, halfWidth, x) * (FullColumns - 1);
            float fz = Mathf.InverseLerp(-halfLength, halfLength, z) * (rows - 1);

            int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, FullColumns - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt(fz), 0, rows - 1);
            int x1 = Mathf.Min(x0 + 1, FullColumns - 1);
            int z1 = Mathf.Min(z0 + 1, rows - 1);
            float tx = Mathf.Clamp01(fx - x0);
            float tz = Mathf.Clamp01(fz - z0);

            float a = Mathf.Lerp(heights[z0 * FullColumns + x0], heights[z0 * FullColumns + x1], tx);
            float b = Mathf.Lerp(heights[z1 * FullColumns + x0], heights[z1 * FullColumns + x1], tx);
            return Mathf.Lerp(a, b, tz);
        }

        /// <summary>0 outside the incision, 1 at its middle: how much each row is allowed to part.</summary>
        private float Profile(float t)
        {
            if (t <= incisionStart || t >= incisionEnd) { return 0f; }
            float s = (t - incisionStart) / (incisionEnd - incisionStart);
            return Mathf.Pow(Mathf.Sin(s * Mathf.PI), 0.65f);
        }

        private void EnsureMeshes()
        {
            _left = Ensure(leftSkin, ref _left, "ChestSkin_Left");
            _right = Ensure(rightSkin, ref _right, "ChestSkin_Right");
            _leftWall = Ensure(leftWound, ref _leftWall, "Wound_Left");
            _rightWall = Ensure(rightWound, ref _rightWall, "Wound_Right");
        }

        private static Mesh Ensure(MeshFilter filter, ref Mesh mesh, string name)
        {
            if (filter == null) { return null; }
            if (mesh == null)
            {
                mesh = new Mesh { name = name };
                mesh.MarkDynamic();
            }

            filter.sharedMesh = mesh;
            return mesh;
        }

        /// <summary>Rebuilds both halves and both wound walls for an opening amount.</summary>
        public void Rebuild(float amount)
        {
            if (_left == null && leftSkin != null) { EnsureMeshes(); }
            if (!_dirty && Mathf.Approximately(amount, _drawn) && _left != null && _left.vertexCount > 0) { return; }
            _drawn = amount;
            _dirty = false;
            Version++;
            EnsureGape();

            BuildHalf(_left, -1f, amount);
            BuildHalf(_right, 1f, amount);
            BuildWall(_leftWall, -1f, amount);
            BuildWall(_rightWall, 1f, amount);

            bool open = amount > 0.001f;
            bool gaping = false;
            for (int i = 0; i < _gapeCurrent.Length && !gaping; i++) { gaping = _gapeCurrent[i] > 1e-5f; }

            // The wound wall shows for a cut that only gapes, too: that red line in the slit is
            // what a fresh incision looks like before anyone retracts it.
            if (leftWound != null) { SetRendered(leftWound, open || gaping); }
            if (rightWound != null) { SetRendered(rightWound, open || gaping); }

            for (int i = 0; i < revealWhenOpen.Length; i++)
            {
                if (revealWhenOpen[i] != null && revealWhenOpen[i].activeSelf != open)
                {
                    revealWhenOpen[i].SetActive(open);
                }
            }
        }

        private static void SetRendered(MeshFilter filter, bool on)
        {
            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.enabled != on) { renderer.enabled = on; }
        }

        private float GapeRow(int row) =>
            _gapeCurrent != null && row >= 0 && row < _gapeCurrent.Length ? _gapeCurrent[row] : 0f;

        /// <summary>Wound offset of a row, softened with its neighbours so a shaky hand gives a smooth edge.</summary>
        private float WanderRow(int row)
        {
            if (_wander == null || _wander.Length == 0) { return 0f; }
            float Raw(int r) => _wander[Mathf.Clamp(r, 0, _wander.Length - 1)];
            return 0.25f * Raw(row - 1) + 0.5f * Raw(row) + 0.25f * Raw(row + 1);
        }

        private Vector3 Deformed(float side, float u, int row, float amount)
        {
            float t = row / (float)(rows - 1);
            float z = Mathf.Lerp(-halfLength, halfLength, t);
            float g = Profile(t) * amount;
            float falloff = Mathf.Pow(1f - u, 1.6f);

            // Tension pulls a cut edge back only near the cut; a few centimetres out the skin is
            // where it always was.
            float gape = GapeRow(row) * Mathf.Pow(1f - u, 3f);

            // Both edges move with the wound's line, so the two halves still meet exactly where
            // the blade went; the shift fades out across the skin so nothing under the drape moves.
            float wander = WanderRow(row) * Mathf.Pow(1f - u, 3f);
            float x = side * (u * halfWidth + retraction * g * falloff + gape) + wander;

            // The cut edge rolls a little into the wound as it is pulled back, which is what
            // makes a retracted incision read as thick skin rather than a sheet of paper.
            float curl = woundDepth * 0.35f * g * Mathf.Pow(1f - u, 4f)
                       + gape * 0.5f * Mathf.Pow(1f - u, 6f);
            return new Vector3(x, Height(x, z) - curl - Dent(x, z), z);
        }

        // Reused between rebuilds. The patch is rebuilt every frame a blade or needle presses on
        // it, and new arrays each time were tens of kilobytes of garbage per frame on the Quest.
        private Vector3[] _halfVertices = new Vector3[0];
        private Vector2[] _halfUvs = new Vector2[0];
        private int[] _leftTriangles = new int[0];
        private int[] _rightTriangles = new int[0];
        private Vector3[] _wallVertices = new Vector3[0];
        private Vector2[] _wallUvs = new Vector2[0];
        private int[] _wallTriangles = new int[0];

        /// <summary>Uploads a rebuild; the triangles only when the mesh is new or resized.</summary>
        private static void Upload(Mesh mesh, Vector3[] vertices, Vector2[] uvs, int[] triangles)
        {
            if (mesh.vertexCount != vertices.Length)
            {
                mesh.Clear();
                mesh.vertices = vertices;
                mesh.uv = uvs;
                mesh.triangles = triangles;
            }
            else
            {
                mesh.vertices = vertices;
                mesh.uv = uvs;
            }

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private void BuildHalf(Mesh mesh, float side, float amount)
        {
            if (mesh == null) { return; }

            int cols = columnsPerHalf;
            if (_halfVertices.Length != cols * rows)
            {
                _halfVertices = new Vector3[cols * rows];
                _halfUvs = new Vector2[cols * rows];
            }

            Vector3[] vertices = _halfVertices;
            Vector2[] uvs = _halfUvs;

            for (int r = 0; r < rows; r++)
            {
                float t = r / (float)(rows - 1);
                for (int c = 0; c < cols; c++)
                {
                    float u = c / (float)(cols - 1);
                    vertices[r * cols + c] = Deformed(side, u, r, amount);
                    uvs[r * cols + c] = new Vector2(0.5f + side * u * 0.5f, t);
                }
            }

            int count = (rows - 1) * (cols - 1) * 6;
            int[] triangles = side > 0f ? _rightTriangles : _leftTriangles;
            if (triangles.Length != count)
            {
                triangles = new int[count];
                int k = 0;
                for (int r = 0; r < rows - 1; r++)
                {
                    for (int c = 0; c < cols - 1; c++)
                    {
                        int a = r * cols + c, b = a + 1, d = a + cols, e = d + 1;

                        // Winding chosen per side so both halves face up (+Y).
                        if (side > 0f)
                        {
                            triangles[k++] = a; triangles[k++] = d; triangles[k++] = b;
                            triangles[k++] = b; triangles[k++] = d; triangles[k++] = e;
                        }
                        else
                        {
                            triangles[k++] = a; triangles[k++] = b; triangles[k++] = d;
                            triangles[k++] = b; triangles[k++] = e; triangles[k++] = d;
                        }
                    }
                }

                if (side > 0f) { _rightTriangles = triangles; } else { _leftTriangles = triangles; }
            }

            Upload(mesh, vertices, uvs, triangles);
        }

        private void BuildWall(Mesh mesh, float side, float amount)
        {
            if (mesh == null) { return; }

            if (_wallVertices.Length != rows * 2)
            {
                _wallVertices = new Vector3[rows * 2];
                _wallUvs = new Vector2[rows * 2];
            }

            Vector3[] vertices = _wallVertices;
            Vector2[] uvs = _wallUvs;

            for (int r = 0; r < rows; r++)
            {
                float t = r / (float)(rows - 1);
                Vector3 top = Deformed(side, 0f, r, amount);
                float g = Profile(t) * amount;
                float gape = gapeWidth > 0f ? GapeRow(r) / gapeWidth : 0f;

                // The wall leans back under the skin it was cut from, and is as deep as the
                // wound is open: at the ends of the incision it closes to nothing. A cut that is
                // only gaping shows the top of its wall, the red line in the slit.
                float depth = woundDepth * Mathf.Clamp01(Mathf.Max(g * 3f, gape * 0.45f));
                Vector3 bottom = top + new Vector3(side * 0.006f * g - side * 0.5f * GapeRow(r), -depth, 0f);

                vertices[r * 2] = top;
                vertices[r * 2 + 1] = bottom;

                // U is depth into the chest wall, so a shallow fresh cut shows skin and the top of
                // the fat, and only a retracted wound shows every layer down to the muscle.
                uvs[r * 2] = new Vector2(0f, t);
                uvs[r * 2 + 1] = new Vector2(woundDepth > 0f ? Mathf.Clamp01(depth / woundDepth) : 1f, t);
            }

            if (_wallTriangles.Length != (rows - 1) * 12)
            {
                _wallTriangles = new int[(rows - 1) * 12];
                int k = 0;
                for (int r = 0; r < rows - 1; r++)
                {
                    int a = r * 2, b = a + 1, c = a + 2, d = a + 3;

                    // Both windings: the wall is seen from across the wound and from above.
                    _wallTriangles[k++] = a; _wallTriangles[k++] = c; _wallTriangles[k++] = b;
                    _wallTriangles[k++] = b; _wallTriangles[k++] = c; _wallTriangles[k++] = d;
                    _wallTriangles[k++] = a; _wallTriangles[k++] = b; _wallTriangles[k++] = c;
                    _wallTriangles[k++] = b; _wallTriangles[k++] = d; _wallTriangles[k++] = c;
                }
            }

            Upload(mesh, vertices, uvs, _wallTriangles);
        }

        /// <summary>Stores the sampled surface and the parts. Called by the scene builder.</summary>
        public void Bind(int halfColumns, int rowCount, float width, float length, float[] sampledHeights,
            MeshFilter left, MeshFilter right, MeshFilter leftWall, MeshFilter rightWall, GameObject[] reveal)
        {
            columnsPerHalf = Mathf.Max(2, halfColumns);
            rows = Mathf.Max(2, rowCount);
            halfWidth = width;
            halfLength = length;
            heights = sampledHeights ?? new float[0];
            leftSkin = left;
            rightSkin = right;
            leftWound = leftWall;
            rightWound = rightWall;
            revealWhenOpen = reveal ?? new GameObject[0];

            _left = _right = _leftWall = _rightWall = null;
            _drawn = -1f;
            _dirty = true;
            _gapeTarget = new float[0];
            _gapeCurrent = new float[0];
            _wander = new float[0];
            _halfVertices = new Vector3[0];
            _leftTriangles = new int[0];
            _rightTriangles = new int[0];
            _wallVertices = new Vector3[0];
            _wallTriangles = new int[0];
            EnsureMeshes();
            Rebuild(0f);
        }
    }
}
