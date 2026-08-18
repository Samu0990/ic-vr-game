using System.Collections.Generic;
using UnityEngine;

namespace VRSurgery.Tissue
{
    /// <summary>
    /// Builds the visible wound as a mesh strip along the recorded incision polyline.
    /// This is the MVP cut strategy from the spec (mesh strip rather than true mesh surgery):
    /// it is cheap, controllable, and cannot fail catastrophically the way live retriangulation
    /// can — while leaving the door open to replace only this component later.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class WoundRenderer : MonoBehaviour
    {
        [SerializeField] private TissueSurface tissue;

        [Header("Appearance")]
        [Tooltip("Wound width in metres at full depth.")]
        [SerializeField] private float maxWidth = 0.006f;
        [SerializeField] private float minWidth = 0.0012f;
        [Tooltip("Lift above the tissue plane to avoid z-fighting, in metres.")]
        [SerializeField] private float surfaceOffset = 0.0004f;

        private Mesh _mesh;
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<int> _triangles = new List<int>();
        private readonly List<Color> _colors = new List<Color>();

        private static readonly Color ShallowColor = new Color(0.62f, 0.14f, 0.13f);
        private static readonly Color DeepColor = new Color(0.24f, 0.03f, 0.04f);

        private void Awake()
        {
            if (tissue == null)
            {
                tissue = GetComponentInParent<TissueSurface>();
            }

            _mesh = new Mesh { name = "IncisionWound" };
            _mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = _mesh;
        }

        private void OnEnable()
        {
            if (tissue != null)
            {
                tissue.IncisionPointAdded += HandleIncisionPointAdded;
                tissue.StateChanged += HandleStateChanged;
            }
        }

        private void OnDisable()
        {
            if (tissue != null)
            {
                tissue.IncisionPointAdded -= HandleIncisionPointAdded;
                tissue.StateChanged -= HandleStateChanged;
            }
        }

        private void OnDestroy()
        {
            if (_mesh != null)
            {
                Destroy(_mesh);
            }
        }

        private void HandleIncisionPointAdded(TissueSurface source, Vector3 localPoint, float depth01)
        {
            Rebuild();
        }

        private void HandleStateChanged(TissueSurface source, IncisionState state)
        {
            if (state == IncisionState.Intact)
            {
                Rebuild();
            }
        }

        /// <summary>
        /// Regenerates the whole strip. The polyline is short (tens of points for a full
        /// incision), so rebuilding is cheaper and far less bug-prone than incremental patching.
        /// </summary>
        public void Rebuild()
        {
            if (_mesh == null || tissue == null)
            {
                return;
            }

            _vertices.Clear();
            _triangles.Clear();
            _colors.Clear();

            IReadOnlyList<Vector3> points = tissue.IncisionPoints;
            IReadOnlyList<float> depths = tissue.IncisionDepths;

            if (points.Count >= 2)
            {
                for (int i = 0; i < points.Count; i++)
                {
                    Vector3 forward = GetTangent(points, i);
                    Vector3 side = Vector3.Cross(Vector3.up, forward).normalized;
                    if (side.sqrMagnitude < 0.5f)
                    {
                        side = Vector3.right;
                    }

                    float depth = depths[i];
                    float halfWidth = Mathf.Lerp(minWidth, maxWidth, depth) * 0.5f;
                    Vector3 center = points[i];
                    center.y = surfaceOffset;

                    _vertices.Add(center - side * halfWidth);
                    _vertices.Add(center + side * halfWidth);

                    Color color = Color.Lerp(ShallowColor, DeepColor, depth);
                    _colors.Add(color);
                    _colors.Add(color);
                }

                for (int i = 0; i < points.Count - 1; i++)
                {
                    int baseIndex = i * 2;
                    _triangles.Add(baseIndex);
                    _triangles.Add(baseIndex + 2);
                    _triangles.Add(baseIndex + 1);

                    _triangles.Add(baseIndex + 1);
                    _triangles.Add(baseIndex + 2);
                    _triangles.Add(baseIndex + 3);
                }
            }

            _mesh.Clear();
            if (_vertices.Count > 0)
            {
                _mesh.SetVertices(_vertices);
                _mesh.SetTriangles(_triangles, 0);
                _mesh.SetColors(_colors);
                _mesh.RecalculateNormals();
                _mesh.RecalculateBounds();
            }
        }

        private static Vector3 GetTangent(IReadOnlyList<Vector3> points, int index)
        {
            Vector3 tangent;
            if (index == 0)
            {
                tangent = points[1] - points[0];
            }
            else if (index == points.Count - 1)
            {
                tangent = points[index] - points[index - 1];
            }
            else
            {
                tangent = points[index + 1] - points[index - 1];
            }

            tangent.y = 0f;
            return tangent.sqrMagnitude < 1e-8f ? Vector3.forward : tangent.normalized;
        }

        /// <summary>Vertex count of the generated wound mesh, exposed for tests and debug overlays.</summary>
        public int VertexCount => _mesh != null ? _mesh.vertexCount : 0;
    }
}
