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

        private Mesh _left, _right, _leftWall, _rightWall;
        private int _direction;
        private float _drawn = -1f;

        public float Openness01 { get; private set; }
        public bool IsOpen => Openness01 >= 1f;
        public bool IsClosed => Openness01 <= 0f && _direction == 0;
        public bool IsMoving => _direction != 0;

        public float IncisionStart => incisionStart;
        public float IncisionEnd => incisionEnd;

        /// <summary>Length of the incision itself, in metres.</summary>
        public float IncisionLength => 2f * halfLength * Mathf.Abs(incisionEnd - incisionStart);

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

        /// <summary>Advances the opening or closing. Stepped by hand in tests.</summary>
        public void Tick(float deltaTime)
        {
            if (_direction == 0 || deltaTime <= 0f) { return; }

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
            Rebuild(0f);
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
            if (Mathf.Approximately(amount, _drawn) && _left != null && _left.vertexCount > 0) { return; }
            _drawn = amount;

            BuildHalf(_left, -1f, amount);
            BuildHalf(_right, 1f, amount);
            BuildWall(_leftWall, -1f, amount);
            BuildWall(_rightWall, 1f, amount);

            bool open = amount > 0.001f;
            if (leftWound != null) { SetRendered(leftWound, open); }
            if (rightWound != null) { SetRendered(rightWound, open); }

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

        private Vector3 Deformed(float side, float u, float t, float amount)
        {
            float z = Mathf.Lerp(-halfLength, halfLength, t);
            float g = Profile(t) * amount;
            float falloff = Mathf.Pow(1f - u, 1.6f);
            float x = side * (u * halfWidth + retraction * g * falloff);

            // The cut edge rolls a little into the wound as it is pulled back, which is what
            // makes a retracted incision read as thick skin rather than a sheet of paper.
            float curl = woundDepth * 0.35f * g * Mathf.Pow(1f - u, 4f);
            return new Vector3(x, Height(x, z) - curl, z);
        }

        private void BuildHalf(Mesh mesh, float side, float amount)
        {
            if (mesh == null) { return; }

            int cols = columnsPerHalf;
            Vector3[] vertices = new Vector3[cols * rows];
            Vector2[] uvs = new Vector2[cols * rows];

            for (int r = 0; r < rows; r++)
            {
                float t = r / (float)(rows - 1);
                for (int c = 0; c < cols; c++)
                {
                    float u = c / (float)(cols - 1);
                    vertices[r * cols + c] = Deformed(side, u, t, amount);
                    uvs[r * cols + c] = new Vector2(0.5f + side * u * 0.5f, t);
                }
            }

            int[] triangles = new int[(rows - 1) * (cols - 1) * 6];
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

            mesh.Clear();
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private void BuildWall(Mesh mesh, float side, float amount)
        {
            if (mesh == null) { return; }

            Vector3[] vertices = new Vector3[rows * 2];
            Vector2[] uvs = new Vector2[rows * 2];

            for (int r = 0; r < rows; r++)
            {
                float t = r / (float)(rows - 1);
                Vector3 top = Deformed(side, 0f, t, amount);
                float g = Profile(t) * amount;

                // The wall leans back under the skin it was cut from, and is as deep as the
                // wound is open: at the ends of the incision it closes to nothing.
                Vector3 bottom = top + new Vector3(side * 0.006f * g, -woundDepth * Mathf.Clamp01(g * 3f), 0f);

                vertices[r * 2] = top;
                vertices[r * 2 + 1] = bottom;
                uvs[r * 2] = new Vector2(0f, t);
                uvs[r * 2 + 1] = new Vector2(1f, t);
            }

            int[] triangles = new int[(rows - 1) * 12];
            int k = 0;
            for (int r = 0; r < rows - 1; r++)
            {
                int a = r * 2, b = a + 1, c = a + 2, d = a + 3;

                // Both windings: the wall is seen from across the wound and from above.
                triangles[k++] = a; triangles[k++] = c; triangles[k++] = b;
                triangles[k++] = b; triangles[k++] = c; triangles[k++] = d;
                triangles[k++] = a; triangles[k++] = b; triangles[k++] = c;
                triangles[k++] = b; triangles[k++] = d; triangles[k++] = c;
            }

            mesh.Clear();
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
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
            EnsureMeshes();
            Rebuild(0f);
        }
    }
}
