using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using VRSurgery.Data;
using VRSurgery.Feedback;
using VRSurgery.Interaction;
using VRSurgery.Session;
using VRSurgery.Surgery;
using VRSurgery.Tools;
using VRSurgery.Transplant;
using VRSurgery.VR;

namespace VRSurgery.EditorTools
{
    /// <summary>
    /// The operating theatre around the transplant, and the two stages that bracket it: the
    /// scalpel incision that opens the skin and the suture that closes it.
    ///
    /// Everything here is built from primitives and generated meshes, placed from the same
    /// measured anchors the rest of the builder uses — the table at the origin, the patient along
    /// +Z, the surgeon on +X — so a re-exported model moves the room with it.
    /// </summary>
    public static partial class TransplanteSceneBuilder
    {
        // ------------------------------------------------------------------ room constants

        private const float RoomMinX = -2.8f;
        private const float RoomMaxX = 2.8f;
        private const float RoomMinZ = -2.4f;
        private const float RoomMaxZ = 3.2f;
        private const float RoomHeight = 3.0f;
        private const float TableHalfWidth = 0.30f;

        private const string GeneratedMeshFolder = "Assets/Models/Patient/Generated";
        private const string GeneratedTextureFolder = "Assets/Textures/Generated";
        private const string WindowSkinAsset = GeneratedMeshFolder + "/PATIENT_BodySkin_Janela.asset";

        private const string ScalpelFbx = "Assets/Models/Tools/SCALPEL_FromGLB.fbx";
        private const string ScalpelAlbedo = "Assets/Models/Tools/BISTURI_Image_0.png";
        private const string ScalpelNormal = "Assets/Models/Tools/BISTURI_Image_2.png";
        private const string ScalpelMetallic = "Assets/Models/Tools/BISTURI_MetallicSmoothness.png";

        /// <summary>The body's upper surface, sampled before any of it is cut away.</summary>
        private static SurfaceField _surface;

        /// <summary>The patient's bounds, kept for the drapes and the ether screen.</summary>
        private static Bounds _bodyBounds;

        /// <summary>The operative window over the sternum for this build.</summary>
        private static ChestWindow _window;

        private struct ChestWindow
        {
            public Vector3 Center;
            public float HalfWidth;
            public float HalfLength;
            public float IncisionHalfLength;
            public float NeckZ;
        }

        /// <summary>
        /// Highest point of the patient's skin over each cell of the table, from the original mesh.
        /// Drapes rest on it and the chest patch is sampled from it.
        /// </summary>
        private sealed class SurfaceField
        {
            private readonly float _minX, _minZ, _cell;
            private readonly int _nx, _nz;
            private readonly float[] _top;

            public SurfaceField(float minX, float minZ, float cell, int nx, int nz)
            {
                _minX = minX; _minZ = minZ; _cell = cell; _nx = nx; _nz = nz;
                _top = new float[nx * nz];
                for (int i = 0; i < _top.Length; i++) { _top[i] = float.NegativeInfinity; }
            }

            public void Raise(int ix, int iz, float y)
            {
                if (ix < 0 || iz < 0 || ix >= _nx || iz >= _nz) { return; }
                int i = iz * _nx + ix;
                if (y > _top[i]) { _top[i] = y; }
            }

            public int CellX(float x) => Mathf.FloorToInt((x - _minX) / _cell);
            public int CellZ(float z) => Mathf.FloorToInt((z - _minZ) / _cell);
            public float CentreX(int ix) => _minX + (ix + 0.5f) * _cell;
            public float CentreZ(int iz) => _minZ + (iz + 0.5f) * _cell;

            /// <summary>Averaged top over a small neighbourhood; negative infinity where there is no body.</summary>
            public float Top(float x, float z, int radius = 1)
            {
                int cx = CellX(x), cz = CellZ(z);
                float sum = 0f;
                int count = 0;

                for (int dz = -radius; dz <= radius; dz++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int ix = cx + dx, iz = cz + dz;
                        if (ix < 0 || iz < 0 || ix >= _nx || iz >= _nz) { continue; }
                        float y = _top[iz * _nx + ix];
                        if (float.IsNegativeInfinity(y)) { continue; }
                        sum += y;
                        count++;
                    }
                }

                return count == 0 ? float.NegativeInfinity : sum / count;
            }
        }

        // ------------------------------------------------------------------ sampling

        /// <summary>Rasterises every skin triangle from above and keeps the highest point per 1 cm cell.</summary>
        private static SurfaceField SampleSurface(GameObject skin)
        {
            MeshFilter filter = skin.GetComponentInChildren<MeshFilter>();
            Bounds bounds = WorldBounds(skin);
            const float cell = 0.01f;

            int nx = Mathf.CeilToInt(bounds.size.x / cell) + 3;
            int nz = Mathf.CeilToInt(bounds.size.z / cell) + 3;
            SurfaceField field = new SurfaceField(bounds.min.x - cell, bounds.min.z - cell, cell, nx, nz);

            if (filter == null || filter.sharedMesh == null)
            {
                Debug.LogError("[Transplante] pele sem malha: campos e janela não terão superfície.");
                return field;
            }

            Vector3[] local = filter.sharedMesh.vertices;
            int[] triangles = filter.sharedMesh.triangles;
            Vector3[] world = new Vector3[local.Length];
            for (int i = 0; i < local.Length; i++) { world[i] = filter.transform.TransformPoint(local[i]); }

            for (int t = 0; t < triangles.Length; t += 3)
            {
                Vector3 a = world[triangles[t]], b = world[triangles[t + 1]], c = world[triangles[t + 2]];

                int x0 = field.CellX(Mathf.Min(a.x, Mathf.Min(b.x, c.x)));
                int x1 = field.CellX(Mathf.Max(a.x, Mathf.Max(b.x, c.x)));
                int z0 = field.CellZ(Mathf.Min(a.z, Mathf.Min(b.z, c.z)));
                int z1 = field.CellZ(Mathf.Max(a.z, Mathf.Max(b.z, c.z)));

                float det = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(det) < 1e-9f)
                {
                    // Seen edge-on from above: it still bounds the surface at its own vertices.
                    field.Raise(field.CellX(a.x), field.CellZ(a.z), a.y);
                    field.Raise(field.CellX(b.x), field.CellZ(b.z), b.y);
                    field.Raise(field.CellX(c.x), field.CellZ(c.z), c.y);
                    continue;
                }

                for (int iz = z0; iz <= z1; iz++)
                {
                    float pz = field.CentreZ(iz);
                    for (int ix = x0; ix <= x1; ix++)
                    {
                        float px = field.CentreX(ix);
                        float l1 = ((b.z - c.z) * (px - c.x) + (c.x - b.x) * (pz - c.z)) / det;
                        float l2 = ((c.z - a.z) * (px - c.x) + (a.x - c.x) * (pz - c.z)) / det;
                        float l3 = 1f - l1 - l2;
                        if (l1 < -0.02f || l2 < -0.02f || l3 < -0.02f) { continue; }

                        field.Raise(ix, iz, l1 * a.y + l2 * b.y + l3 * c.y);
                    }
                }
            }

            Debug.Log($"[Transplante] superfície do corpo amostrada: {nx}x{nz} células de {cell * 100f:F0} cm");
            return field;
        }

        /// <summary>Where the operative window goes: centred on the sternum, as long as the bone plus a margin.</summary>
        private static ChestWindow WindowFor(Vector3 thorax, GameObject sternum)
        {
            Bounds bone = WorldBounds(sternum);
            float incisionHalf = Mathf.Clamp(bone.size.z * 0.5f + 0.01f, 0.08f, 0.15f);
            float stature = _bodyBounds.size.z;

            ChestWindow window = new ChestWindow
            {
                Center = new Vector3(thorax.x, 0f, bone.center.z),
                HalfWidth = 0.075f,
                HalfLength = incisionHalf + 0.03f,
                IncisionHalfLength = incisionHalf,
            };

            window.NeckZ = Mathf.Max(window.Center.z + window.HalfLength + 0.07f,
                _bodyBounds.min.z + stature * 0.86f);

            Debug.Log($"[Transplante] janela operatória em z={window.Center.z:F3}, " +
                      $"{window.HalfWidth * 200f:F0} x {window.HalfLength * 200f:F0} cm, " +
                      $"incisão de {incisionHalf * 200f:F0} cm, campo de cabeça em z={window.NeckZ:F2}");
            return window;
        }

        private static float SkinTopAt(float x, float z)
        {
            float y = _surface != null ? _surface.Top(x, z, 2) : float.NegativeInfinity;
            return float.IsNegativeInfinity(y) ? TableTopY + 0.2f : y;
        }

        // ------------------------------------------------------------------ materials

        private static readonly Dictionary<string, Material> Palette = new Dictionary<string, Material>();

        private static Material Paint(string key, Color colour, float metallic = 0f, float smoothness = 0.35f)
        {
            if (Palette.TryGetValue(key, out Material existing) && existing != null) { return existing; }
            Material m = MakeMaterial(colour, metallic, smoothness);
            m.name = "OR_" + key;

            // Materials that are a real surface get its texture and relief, tinted by the colour
            // asked for. Everything else stays a clean painted finish, which is what equipment is.
            switch (key)
            {
                case "steel":
                case "steelBright":
                case "cabinet":
                case "plate":
                    Dress(m, _steel, new Vector2(2f, 2f), 0.4f);
                    break;
                case "drapeBlue":
                    m.color = Color.white;
                    Dress(m, _fabric, new Vector2(3f, 3f), 0.6f);
                    break;
                case "floor":
                    m.color = Color.white;
                    Dress(m, _floor, Vector2.one, 0.5f);
                    break;
                case "ceiling":
                    Dress(m, _ceiling, new Vector2(8f, 8f), 0.5f);
                    break;
            }

            Palette[key] = m;
            return m;
        }

        private static Material DoubleSided(Material m)
        {
            m.SetFloat("_Cull", 0f);
            m.doubleSidedGI = true;
            return m;
        }

        private static Material Glass(Color colour)
        {
            Material m = MakeMaterial(colour, 0f, 0.9f);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = 3000;
            return m;
        }

        private static Material Glow(Color colour)
        {
            Material m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = colour };
            return m;
        }

        // ------------------------------------------------------------------ primitives

        private static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 position,
            Vector3 scale, Material material, Quaternion? rotation = null, bool collider = false, bool shadows = true)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation ?? Quaternion.identity;
            go.transform.localScale = scale;

            if (!collider) { Object.DestroyImmediate(go.GetComponent<Collider>()); }

            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            if (!shadows) { renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
            return go;
        }

        /// <summary>Boxes thinner than this stay sharp cubes: plates, films, ticks, labels.</summary>
        private const float RoundedBoxMinSize = 0.015f;

        private static readonly Dictionary<Vector3Int, Mesh> RoundedBoxes = new Dictionary<Vector3Int, Mesh>();

        /// <summary>
        /// A box. Anything thick enough gets rounded edges with smooth shading — the soft, chunky
        /// look of Job Simulator's props instead of the sharp primitive cubes that made the room
        /// read as a test level. Thin plates stay plain cubes, where a bevel would be invisible.
        /// </summary>
        private static GameObject Box(string name, Transform parent, Vector3 position, Vector3 size, Material m,
            Quaternion? rotation = null, bool shadows = true)
        {
            float thinnest = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
            if (thinnest < RoundedBoxMinSize)
            {
                return Part(PrimitiveType.Cube, name, parent, position, size, m, rotation, false, shadows);
            }

            GameObject go = MeshPart(name, parent, RoundedBox(size, Mathf.Min(thinnest * 0.25f, 0.03f)), m, shadows);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation ?? Quaternion.identity;
            return go;
        }

        /// <summary>
        /// A box of the given size with rounded edges: each face a 4 x 4 grid whose outer ring is
        /// pulled onto a sphere of <paramref name="radius"/> around the inner box, with normals
        /// taken from that sphere. One segment per edge, smooth-shaded, so 108 triangles instead
        /// of 12 — cheap enough for the Quest, round enough to stop reading as a primitive.
        /// Shared between boxes of the same size (to the millimetre).
        /// </summary>
        private static Mesh RoundedBox(Vector3 size, float radius)
        {
            Vector3Int key = new Vector3Int(Mathf.RoundToInt(size.x * 1000f), Mathf.RoundToInt(size.y * 1000f),
                Mathf.RoundToInt(size.z * 1000f));
            if (RoundedBoxes.TryGetValue(key, out Mesh cached) && cached != null) { return cached; }

            Vector3 h = size * 0.5f;
            Vector3 inner = new Vector3(h.x - radius, h.y - radius, h.z - radius);
            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> triangles = new List<int>();

            // Each face: its outward normal and two in-plane axes with Cross(u, v) == normal, so
            // every triangle faces out.
            (Vector3 n, Vector3 u, Vector3 v)[] faces =
            {
                (Vector3.right, Vector3.up, Vector3.forward), (Vector3.left, Vector3.forward, Vector3.up),
                (Vector3.up, Vector3.forward, Vector3.right), (Vector3.down, Vector3.right, Vector3.forward),
                (Vector3.forward, Vector3.right, Vector3.up), (Vector3.back, Vector3.up, Vector3.right),
            };

            foreach ((Vector3 n, Vector3 u, Vector3 v) in faces)
            {
                float hn = Mathf.Abs(Vector3.Dot(h, n)), hu = Mathf.Abs(Vector3.Dot(h, u)), hv = Mathf.Abs(Vector3.Dot(h, v));
                float[] cu = { -hu, -hu + radius, hu - radius, hu };
                float[] cv = { -hv, -hv + radius, hv - radius, hv };
                int start = vertices.Count;

                for (int j = 0; j < 4; j++)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        Vector3 p = n * hn + u * cu[i] + v * cv[j];
                        Vector3 core = new Vector3(Mathf.Clamp(p.x, -inner.x, inner.x), Mathf.Clamp(p.y, -inner.y, inner.y),
                            Mathf.Clamp(p.z, -inner.z, inner.z));
                        Vector3 normal = (p - core).normalized;
                        vertices.Add(core + normal * radius);
                        normals.Add(normal);
                        uvs.Add(new Vector2((cu[i] + hu) / (2f * hu), (cv[j] + hv) / (2f * hv)));
                    }
                }

                for (int j = 0; j < 3; j++)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        int a = start + j * 4 + i, b = a + 1, c = a + 4, d = c + 1;
                        triangles.Add(a); triangles.Add(b); triangles.Add(c);
                        triangles.Add(b); triangles.Add(d); triangles.Add(c);
                    }
                }
            }

            Mesh mesh = new Mesh { name = $"CaixaArredondada_{key.x}x{key.y}x{key.z}" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            RoundedBoxes[key] = mesh;
            return mesh;
        }

        /// <summary>A cylinder of given radius and full length, along the parent's local Y.</summary>
        private static GameObject Rod(string name, Transform parent, Vector3 position, float radius, float length,
            Material m, Quaternion? rotation = null, bool shadows = true) =>
            Part(PrimitiveType.Cylinder, name, parent, position, new Vector3(radius * 2f, length * 0.5f, radius * 2f),
                m, rotation, false, shadows);

        private static GameObject Ball(string name, Transform parent, Vector3 position, Vector3 size, Material m) =>
            Part(PrimitiveType.Sphere, name, parent, position, size, m, null, false, false);

        private static GameObject MeshPart(string name, Transform parent, Mesh mesh, Material m, bool shadows = false)
        {
            GameObject go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = m;
            if (!shadows) { renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
            return go;
        }

        /// <summary>A tube swept along a smooth path through the given world points.</summary>
        private static Mesh TubeMesh(IList<Vector3> points, float radius, int sides = 10, int stepsPerSpan = 8)
        {
            List<Vector3> path = new List<Vector3>();
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector3 p0 = points[Mathf.Max(0, i - 1)], p1 = points[i], p2 = points[i + 1];
                Vector3 p3 = points[Mathf.Min(points.Count - 1, i + 2)];
                for (int s = 0; s < stepsPerSpan; s++)
                {
                    float t = s / (float)stepsPerSpan;
                    float t2 = t * t, t3 = t2 * t;
                    path.Add(0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                                     (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
            }

            path.Add(points[points.Count - 1]);

            Vector3[] vertices = new Vector3[path.Count * sides];
            Vector3[] normals = new Vector3[vertices.Length];
            int[] triangles = new int[(path.Count - 1) * sides * 6];

            Vector3 up = Vector3.up;
            for (int i = 0; i < path.Count; i++)
            {
                Vector3 forward = (i < path.Count - 1 ? path[i + 1] - path[i] : path[i] - path[i - 1]).normalized;
                if (Mathf.Abs(Vector3.Dot(forward, up)) > 0.95f) { up = Vector3.right; }
                Vector3 side = Vector3.Cross(up, forward).normalized;
                Vector3 normalUp = Vector3.Cross(forward, side).normalized;

                for (int s = 0; s < sides; s++)
                {
                    float angle = s / (float)sides * Mathf.PI * 2f;
                    Vector3 n = side * Mathf.Cos(angle) + normalUp * Mathf.Sin(angle);
                    vertices[i * sides + s] = path[i] + n * radius;
                    normals[i * sides + s] = n;
                }
            }

            int k = 0;
            for (int i = 0; i < path.Count - 1; i++)
            {
                for (int s = 0; s < sides; s++)
                {
                    int a = i * sides + s, b = i * sides + (s + 1) % sides;
                    int c = (i + 1) * sides + s, d = (i + 1) * sides + (s + 1) % sides;
                    triangles[k++] = a; triangles[k++] = c; triangles[k++] = b;
                    triangles[k++] = b; triangles[k++] = c; triangles[k++] = d;
                }
            }

            Mesh mesh = new Mesh { name = "Tube" };
            if (vertices.Length > 65000) { mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; }
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static GameObject Tube(string name, Transform parent, IList<Vector3> worldPoints, float radius, Material m)
        {
            GameObject go = MeshPart(name, parent, TubeMesh(worldPoints, radius), m);
            // Points were given in world space; the tube lives there too.
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            if (go.transform.parent != null)
            {
                Transform p = go.transform.parent;
                go.transform.SetParent(null, true);
                go.transform.SetParent(p, true);
            }

            return go;
        }

        /// <summary>Creates a folder through the AssetDatabase, so CreateAsset can write into it at once.</summary>
        private static void EnsureAssetFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) { return; }
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        private static Texture2D SaveTexture(Texture2D texture, string name)
        {
            EnsureAssetFolder(GeneratedTextureFolder);
            string path = $"{GeneratedTextureFolder}/{name}.asset";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) != null) { AssetDatabase.DeleteAsset(path); }
            AssetDatabase.CreateAsset(texture, path);
            return texture;
        }

        private static Texture2D XrayTexture()
        {
            const int w = 256, h = 320;
            Texture2D texture = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "OR_ChestXray" };

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float u = (x - w * 0.5f) / (w * 0.5f);
                    float v = (y - h * 0.45f) / (h * 0.5f);

                    // Thorax: a bright rounded cage on a dark field.
                    float cage = Mathf.Clamp01(1.2f - Mathf.Sqrt(u * u * 1.1f + v * v * 0.8f) * 1.3f);
                    // Lungs read dark inside it, either side of the bright mediastinum.
                    float lungs = Mathf.Clamp01(1f - Mathf.Sqrt((Mathf.Abs(u) - 0.42f) * (Mathf.Abs(u) - 0.42f) * 6f + (v - 0.05f) * (v - 0.05f) * 2.2f));
                    float spine = Mathf.Clamp01(1f - Mathf.Abs(u) * 9f);
                    // Ribs: arcs.
                    float ribs = Mathf.Abs(u) > 0.12f
                        ? Mathf.Clamp01(1f - Mathf.Abs(Mathf.Sin((v + u * u * 0.35f) * 22f)) * 3f) * cage
                        : 0f;
                    // Heart shadow, left of the midline (right of the film).
                    float heart = Mathf.Clamp01(1f - Mathf.Sqrt((u - 0.12f) * (u - 0.12f) * 5f + (v + 0.28f) * (v + 0.28f) * 7f));

                    float value = 0.05f + cage * 0.35f - lungs * 0.28f + spine * 0.35f + ribs * 0.35f + heart * 0.45f;
                    value = Mathf.Clamp01(value);
                    texture.SetPixel(x, y, new Color(value, value, value * 1.05f, 1f));
                }
            }

            texture.Apply(true);
            return SaveTexture(texture, "OR_ChestXray");
        }

        // ------------------------------------------------------------------ the room

        /// <summary>
        /// Walls, floor, ceiling and everything a theatre has in it that the visitor does not touch.
        /// The template's grey room stays for its colliders but stops drawing.
        /// </summary>
        private static void BuildTheatre(Vector3 thorax)
        {
            HideTemplateRoom();

            GameObject room = new GameObject("CentroCirurgico");
            Transform r = room.transform;

            BuildShell(r);
            BuildCeilingFixtures(r, thorax);
            BuildSurgicalLights(r, thorax);
            BuildWallDetails(r);
            BuildAnaesthesiaStation(r, thorax);
            BuildBypassMachine(r, thorax);
            BuildBackTable(r, thorax);
            BuildIvPole(r, thorax);
            BuildSmallFurniture(r, thorax);
            BuildTheatreEquipment(r, thorax);
            BuildTransplantDecor(r, thorax);

            // Ready-made models dropped into Assets/Models/Sala take the place of generated ones.
            ApplyModelSlots(r);

            AudioSource hum = room.AddComponent<AudioSource>();
            hum.playOnAwake = false;
            room.AddComponent<AmbientLoop>();

            foreach (Renderer renderer in room.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.GetComponent<LineRenderer>() != null || renderer is SkinnedMeshRenderer) { continue; }
                if (renderer.GetComponentInParent<WallClock>() != null) { continue; }
                if (renderer.GetComponentInParent<VitalSignsMonitor>() != null) { continue; }

                // A TextMesh builds its mesh at runtime; static batching would bake it empty.
                if (renderer.GetComponent<TextMesh>() != null) { continue; }
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, StaticEditorFlags.BatchingStatic);
            }

            Debug.Log($"[Transplante] centro cirúrgico montado: {room.GetComponentsInChildren<Renderer>(true).Length} peças");
        }

        private static void HideTemplateRoom()
        {
            int hidden = 0;
            foreach (string name in new[] { "Environment", "Grid" })
            {
                GameObject go = GameObject.Find(name);
                if (go == null) { continue; }
                foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.enabled = false;
                    hidden++;
                }
            }

            Debug.Log($"[Transplante] sala do template escondida ({hidden} renderer(s)); colisores mantidos");
        }

        private static void BuildShell(Transform r)
        {
            float width = RoomMaxX - RoomMinX;
            float depth = RoomMaxZ - RoomMinZ;
            Vector3 centre = new Vector3((RoomMinX + RoomMaxX) * 0.5f, 0f, (RoomMinZ + RoomMaxZ) * 0.5f);

            // Floor: seamless epoxy, the one surface in the room that is not tiled. It keeps its
            // collider so a dropped heart lands on it instead of falling out of the world.
            Material floor = Paint("floor", new Color(0.34f, 0.43f, 0.42f), 0f, 0.55f);
            // One texture repeat per metre and a half of floor.
            floor.mainTextureScale = new Vector2(width / 1.5f, depth / 1.5f);
            GameObject slab = Part(PrimitiveType.Cube, "Floor", r, centre + new Vector3(0f, -0.05f, 0f),
                new Vector3(width, 0.1f, depth), floor, null, true, false);
            slab.GetComponent<MeshRenderer>().receiveShadows = true;

            // Glazed tile, 15 cm, with sunken grout: four tiles per texture repeat.
            Color wallTint = new Color(0.86f, 0.95f, 0.95f);

            Material WallMat(string key, float w, float h)
            {
                Material m = MakeMaterial(wallTint, 0f, 0.7f);
                m.name = "OR_Wall_" + key;
                return Dress(m, _wallTile, new Vector2(w / 0.6f, h / 0.6f), 0.8f);
            }

            const float t = 0.1f;
            float h2 = RoomHeight * 0.5f;
            Box("Wall_Far", r, new Vector3(RoomMinX - t * 0.5f, h2, centre.z), new Vector3(t, RoomHeight, depth),
                WallMat("far", depth, RoomHeight), null, false);
            Box("Wall_Back", r, new Vector3(RoomMaxX + t * 0.5f, h2, centre.z), new Vector3(t, RoomHeight, depth),
                WallMat("back", depth, RoomHeight), null, false);
            Box("Wall_Head", r, new Vector3(centre.x, h2, RoomMaxZ + t * 0.5f), new Vector3(width, RoomHeight, t),
                WallMat("head", width, RoomHeight), null, false);
            Box("Wall_Feet", r, new Vector3(centre.x, h2, RoomMinZ - t * 0.5f), new Vector3(width, RoomHeight, t),
                WallMat("feet", width, RoomHeight), null, false);

            // Stainless kick rail along every wall: trolleys hit walls, theatres protect them.
            Material steel = Paint("steel", new Color(0.78f, 0.8f, 0.82f), 0.9f, 0.7f);
            Box("Rail_Far", r, new Vector3(RoomMinX + 0.01f, 0.35f, centre.z), new Vector3(0.03f, 0.12f, depth), steel, null, false);
            Box("Rail_Back", r, new Vector3(RoomMaxX - 0.01f, 0.35f, centre.z), new Vector3(0.03f, 0.12f, depth), steel, null, false);
            Box("Rail_Head", r, new Vector3(centre.x, 0.35f, RoomMaxZ - 0.01f), new Vector3(width, 0.12f, 0.03f), steel, null, false);

            Material ceiling = Paint("ceiling", new Color(0.93f, 0.94f, 0.95f), 0f, 0.2f);
            Box("Ceiling", r, new Vector3(centre.x, RoomHeight + 0.05f, centre.z), new Vector3(width, 0.1f, depth), ceiling, null, false);

            // Coved skirting where floor meets wall: the continuous floor turns up the wall so there
            // is no corner to collect dirt. Reads instantly as "hospital".
            Material cove = Paint("cove", new Color(0.30f, 0.38f, 0.37f), 0f, 0.5f);
            Box("Cove_Far", r, new Vector3(RoomMinX + 0.02f, 0.06f, centre.z), new Vector3(0.04f, 0.12f, depth), cove, null, false);
            Box("Cove_Back", r, new Vector3(RoomMaxX - 0.02f, 0.06f, centre.z), new Vector3(0.04f, 0.12f, depth), cove, null, false);
            Box("Cove_Head", r, new Vector3(centre.x, 0.06f, RoomMaxZ - 0.02f), new Vector3(width, 0.12f, 0.04f), cove, null, false);
            Box("Cove_Feet", r, new Vector3(centre.x, 0.06f, RoomMinZ + 0.02f), new Vector3(width, 0.12f, 0.04f), cove, null, false);
        }

        private static void BuildCeilingFixtures(Transform r, Vector3 thorax)
        {
            // Laminar-flow plenum over the table: the big perforated panel every modern theatre
            // has above the patient, pushing filtered air straight down onto the field.
            Material plenum = Paint("plenum", new Color(0.86f, 0.88f, 0.9f), 0.3f, 0.5f);
            Box("LaminarFlow", r, new Vector3(0f, RoomHeight - 0.03f, thorax.z - 0.1f), new Vector3(2.4f, 0.06f, 2.8f), plenum, null, false);
            Material frame = Paint("steel", new Color(0.78f, 0.8f, 0.82f), 0.9f, 0.7f);
            for (int i = -2; i <= 2; i++)
            {
                Box("Plenum_Rib_" + i, r, new Vector3(i * 0.5f, RoomHeight - 0.065f, thorax.z - 0.1f),
                    new Vector3(0.02f, 0.02f, 2.8f), frame, null, false);
            }

            // Recessed light panels around it. Unlit, so they glow without paying for a light each.
            Material panel = Glow(new Color(1f, 0.99f, 0.95f));
            Vector3[] spots =
            {
                new Vector3(-1.9f, 0f, -1.4f), new Vector3(1.9f, 0f, -1.4f),
                new Vector3(-1.9f, 0f, 0.9f), new Vector3(1.9f, 0f, 0.9f),
                new Vector3(-1.9f, 0f, 2.5f), new Vector3(1.9f, 0f, 2.5f),
                new Vector3(0f, 0f, 2.6f), new Vector3(0f, 0f, -1.9f),
            };

            for (int i = 0; i < spots.Length; i++)
            {
                Box("CeilingLight_" + i, r, spots[i] + new Vector3(0f, RoomHeight - 0.01f, 0f),
                    new Vector3(0.6f, 0.02f, 1.2f), panel, null, false);
            }
        }

        private static void BuildSurgicalLights(Transform r, Vector3 thorax)
        {
            int operatorLayer = LayerMask.NameToLayer(OperatorLayer);

            GameObject lights = new GameObject("SurgicalLights");
            lights.transform.SetParent(r, false);
            Transform l = lights.transform;

            Material white = Paint("lampShell", new Color(0.92f, 0.93f, 0.95f), 0.1f, 0.6f);
            Material steel = Paint("steel", new Color(0.78f, 0.8f, 0.82f), 0.9f, 0.7f);
            Material lens = Glow(new Color(1f, 0.98f, 0.9f));

            Vector3 mount = new Vector3(0.15f, RoomHeight, thorax.z - 0.15f);
            Rod("Mount", l, mount + new Vector3(0f, -0.2f, 0f), 0.06f, 0.4f, white, null, false);

            (Vector3 head, float tilt, float yaw)[] heads =
            {
                (new Vector3(-0.12f, 2.12f, thorax.z + 0.05f), 16f, 90f),
                (new Vector3(0.55f, 2.22f, thorax.z - 0.55f), 24f, -120f),
            };

            for (int i = 0; i < heads.Length; i++)
            {
                Vector3 head = heads[i].head;
                Vector3 elbow = new Vector3(Mathf.Lerp(mount.x, head.x, 0.5f), RoomHeight - 0.45f,
                    Mathf.Lerp(mount.z, head.z, 0.5f));

                Tube($"Arm_{i}_A", l, new[] { mount + new Vector3(0f, -0.4f, 0f), elbow }, 0.025f, white);
                Tube($"Arm_{i}_B", l, new[] { elbow, head + new Vector3(0f, 0.12f, 0f) }, 0.02f, white);
                Rod($"Yoke_{i}", l, head + new Vector3(0f, 0.09f, 0f), 0.018f, 0.08f, steel, null, false);

                GameObject lamp = new GameObject($"LampHead_{i}");
                lamp.transform.SetParent(l, false);
                lamp.transform.position = head;
                lamp.transform.rotation = Quaternion.Euler(heads[i].tilt, heads[i].yaw, 0f);

                Part(PrimitiveType.Sphere, "Dome", lamp.transform, Vector3.zero, new Vector3(0.62f, 0.14f, 0.62f), white, null, false, false);
                Part(PrimitiveType.Cylinder, "Lens", lamp.transform, new Vector3(0f, -0.045f, 0f),
                    new Vector3(0.5f, 0.004f, 0.5f), lens, null, false, false);
                // Sterile handle in the middle of the head: the only part the surgeon touches.
                Rod("SterileHandle", lamp.transform, new Vector3(0f, -0.1f, 0f), 0.014f, 0.1f,
                    Paint("handle", new Color(0.2f, 0.45f, 0.7f), 0f, 0.4f), null, false);
            }

            // The spot light the lighting pass already hung over the chest moves into the lamp,
            // so the pool of light on the field comes from something visible.
            GameObject spot = GameObject.Find("SurgicalLamp");
            if (spot != null)
            {
                Vector3 from = heads[0].head + new Vector3(0f, -0.06f, 0f);
                spot.transform.SetParent(l, true);
                spot.transform.position = from;
                spot.transform.rotation = Quaternion.LookRotation(thorax - from, Vector3.forward);
                Light light = spot.GetComponent<Light>();
                if (light != null)
                {
                    light.range = 3.5f;
                    light.spotAngle = 50f;
                    light.innerSpotAngle = 28f;
                    light.intensity = 7f;
                }
            }

            // Nothing hanging over the table may reach the projector, which looks straight down
            // onto the mannequin through exactly where the lamp heads are.
            if (operatorLayer >= 0)
            {
                foreach (Transform t in lights.GetComponentsInChildren<Transform>(true))
                {
                    t.gameObject.layer = operatorLayer;
                }
            }
        }

        private static void BuildWallDetails(Transform r)
        {
            Quaternion facingIn = Quaternion.LookRotation(Vector3.left); // local +Z -> world -X; the face (-Z) looks at +X
            float wall = RoomMinX + 0.005f;

            // Clock on the far wall, straight in the surgeon's line of sight.
            GameObject clock = new GameObject("WallClock");
            clock.transform.SetParent(r, false);
            clock.transform.position = new Vector3(wall + 0.03f, 2.35f, -0.1f);
            clock.transform.rotation = facingIn;

            Material rim = Paint("clockRim", new Color(0.15f, 0.15f, 0.17f), 0.3f, 0.5f);
            Material face = Paint("clockFace", new Color(0.97f, 0.97f, 0.95f), 0f, 0.3f);
            Material ink = Paint("ink", new Color(0.08f, 0.08f, 0.1f), 0f, 0.2f);
            Material red = Paint("red", new Color(0.8f, 0.1f, 0.08f), 0f, 0.3f);

            Part(PrimitiveType.Cylinder, "Rim", clock.transform, Vector3.zero, new Vector3(0.34f, 0.02f, 0.34f), rim,
                Quaternion.Euler(90f, 0f, 0f), false, false);
            Part(PrimitiveType.Cylinder, "Face", clock.transform, new Vector3(0f, 0f, -0.021f), new Vector3(0.31f, 0.002f, 0.31f), face,
                Quaternion.Euler(90f, 0f, 0f), false, false);

            for (int i = 0; i < 12; i++)
            {
                float angle = i * 30f * Mathf.Deg2Rad;
                Vector3 p = new Vector3(Mathf.Sin(angle), Mathf.Cos(angle), 0f) * 0.135f + new Vector3(0f, 0f, -0.024f);
                Box("Tick_" + i, clock.transform, p, i % 3 == 0 ? new Vector3(0.008f, 0.025f, 0.002f) : new Vector3(0.004f, 0.014f, 0.002f),
                    ink, Quaternion.Euler(0f, 0f, -i * 30f), false);
            }

            Transform Hand(string name, float length, float width, float z, Material m)
            {
                GameObject pivot = new GameObject(name);
                pivot.transform.SetParent(clock.transform, false);
                pivot.transform.localPosition = new Vector3(0f, 0f, z);
                Box("Blade", pivot.transform, new Vector3(0f, length * 0.4f, 0f), new Vector3(width, length, 0.002f), m, null, false);
                return pivot.transform;
            }

            clock.AddComponent<WallClock>().Bind(
                Hand("Hour", 0.08f, 0.009f, -0.026f, ink),
                Hand("Minute", 0.12f, 0.006f, -0.028f, ink),
                Hand("Second", 0.13f, 0.002f, -0.030f, red));

            // X-ray viewer: a lightbox with a chest film, the image of a hospital.
            GameObject viewer = new GameObject("XrayViewer");
            viewer.transform.SetParent(r, false);
            viewer.transform.position = new Vector3(wall + 0.03f, 1.6f, -0.75f);
            viewer.transform.rotation = facingIn;
            Box("Frame", viewer.transform, Vector3.zero, new Vector3(0.95f, 0.55f, 0.05f), Paint("frame", new Color(0.85f, 0.86f, 0.88f), 0.2f, 0.4f), null, false);
            Material film = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            film.SetTexture("_BaseMap", XrayTexture());
            film.color = new Color(0.95f, 0.97f, 1f);
            for (int i = 0; i < 2; i++)
            {
                GameObject q = Part(PrimitiveType.Quad, "Film_" + i, viewer.transform, new Vector3(-0.22f + i * 0.44f, 0f, -0.027f),
                    new Vector3(0.4f, 0.48f, 1f), film, null, false, false);
                q.transform.localRotation = Quaternion.identity;
            }

            // Medical gas outlets: oxygen, compressed air, vacuum, each in its colour.
            GameObject gas = new GameObject("GasOutlets");
            gas.transform.SetParent(r, false);
            gas.transform.position = new Vector3(wall + 0.02f, 1.45f, 1.35f);
            gas.transform.rotation = facingIn;
            Box("Plate", gas.transform, Vector3.zero, new Vector3(0.42f, 0.14f, 0.02f), Paint("plate", new Color(0.9f, 0.9f, 0.9f), 0.3f, 0.5f), null, false);
            Color[] gases = { new Color(0.1f, 0.6f, 0.25f), new Color(0.95f, 0.8f, 0.1f), new Color(0.55f, 0.55f, 0.58f) };
            for (int i = 0; i < gases.Length; i++)
            {
                Part(PrimitiveType.Cylinder, "Outlet_" + i, gas.transform, new Vector3(-0.13f + i * 0.13f, 0f, -0.015f),
                    new Vector3(0.07f, 0.01f, 0.07f), Paint("gas" + i, gases[i], 0.2f, 0.5f), Quaternion.Euler(90f, 0f, 0f), false, false);
            }

            // Glass-fronted supply cabinets on the far wall.
            GameObject cabinet = new GameObject("SupplyCabinet");
            cabinet.transform.SetParent(r, false);
            cabinet.transform.position = new Vector3(wall + 0.22f, 0f, 2.0f);
            Material steelWhite = Paint("cabinet", new Color(0.88f, 0.89f, 0.9f), 0.4f, 0.5f);
            Box("Body", cabinet.transform, new Vector3(0f, 1.05f, 0f), new Vector3(0.42f, 2.1f, 1.4f), steelWhite, null, false);
            Box("Glass", cabinet.transform, new Vector3(0.215f, 1.3f, 0f), new Vector3(0.01f, 1.4f, 1.3f), Glass(new Color(0.7f, 0.85f, 0.9f, 0.25f)), null, false);
            Material[] boxes =
            {
                Paint("box1", new Color(0.3f, 0.55f, 0.85f)), Paint("box2", new Color(0.95f, 0.95f, 0.95f)),
                Paint("box3", new Color(0.85f, 0.35f, 0.3f)), Paint("box4", new Color(0.4f, 0.75f, 0.45f)),
            };
            for (int shelf = 0; shelf < 4; shelf++)
            {
                float y = 0.75f + shelf * 0.33f;
                Box("Shelf_" + shelf, cabinet.transform, new Vector3(0.02f, y, 0f), new Vector3(0.36f, 0.015f, 1.34f), steelWhite, null, false);
                for (int b = 0; b < 5; b++)
                {
                    float z = -0.5f + b * 0.25f;
                    float bh = 0.1f + 0.08f * ((shelf + b) % 3);
                    Box($"Supply_{shelf}_{b}", cabinet.transform, new Vector3(0.02f, y + bh * 0.5f + 0.008f, z),
                        new Vector3(0.24f, bh, 0.18f), boxes[(shelf * 2 + b) % boxes.Length], null, false);
                }
            }

            // Double doors with round windows on the feet wall, and the room sign above.
            GameObject doors = new GameObject("Doors");
            doors.transform.SetParent(r, false);
            doors.transform.position = new Vector3(-0.2f, 0f, RoomMinZ + 0.03f);
            Material doorMat = Paint("door", new Color(0.62f, 0.72f, 0.74f), 0.3f, 0.5f);
            for (int i = 0; i < 2; i++)
            {
                float x = -0.45f + i * 0.9f;
                Box("Leaf_" + i, doors.transform, new Vector3(x, 1.05f, 0f), new Vector3(0.88f, 2.1f, 0.05f), doorMat, null, false);
                Part(PrimitiveType.Cylinder, "Porthole_" + i, doors.transform, new Vector3(x, 1.5f, 0.02f),
                    new Vector3(0.3f, 0.01f, 0.3f), Glass(new Color(0.75f, 0.85f, 0.9f, 0.5f)), Quaternion.Euler(90f, 0f, 0f), false, false);
                Box("PushPlate_" + i, doors.transform, new Vector3(x + (i == 0 ? 0.3f : -0.3f), 1.1f, 0.03f), new Vector3(0.1f, 0.3f, 0.01f), steel(), null, false);
            }

            GameObject sign = new GameObject("RoomSign");
            sign.transform.SetParent(r, false);
            sign.transform.position = new Vector3(-0.2f, 2.45f, RoomMinZ + 0.06f);
            sign.transform.rotation = Quaternion.LookRotation(Vector3.back);
            Box("Plate", sign.transform, Vector3.zero, new Vector3(0.7f, 0.18f, 0.02f), Paint("signRed", new Color(0.7f, 0.08f, 0.08f)), null, false);
            TextMesh text = BuildScreenText(sign.transform, "Text", new Vector3(0f, 0f, -0.012f), 0.06f);
            text.text = "SALA 3 — CIRURGIA CARDÍACA";
            text.color = Color.white;
            text.transform.localRotation = Quaternion.identity;

            // Scrub window on the wall behind the surgeon: through it, the scrub sink.
            GameObject scrub = new GameObject("ScrubWindow");
            scrub.transform.SetParent(r, false);
            scrub.transform.position = new Vector3(RoomMaxX - 0.02f, 1.55f, -0.6f);
            Box("Frame", scrub.transform, Vector3.zero, new Vector3(0.05f, 0.8f, 1.5f), steel(), null, false);
            Box("Pane", scrub.transform, new Vector3(-0.03f, 0f, 0f), new Vector3(0.01f, 0.7f, 1.4f), Glow(new Color(0.78f, 0.86f, 0.9f)), null, false);

            GameObject sink = new GameObject("ScrubSink");
            sink.transform.SetParent(r, false);
            sink.transform.position = new Vector3(RoomMaxX - 0.35f, 0f, 1.7f);
            Box("Basin", sink.transform, new Vector3(0f, 0.9f, 0f), new Vector3(0.55f, 0.25f, 1.2f), steel(), null, false);
            Box("Pedestal", sink.transform, new Vector3(0.1f, 0.4f, 0f), new Vector3(0.3f, 0.8f, 1.1f), steel(), null, false);
            for (int i = 0; i < 2; i++)
            {
                Tube("Tap_" + i, sink.transform, new[]
                {
                    sink.transform.position + new Vector3(0.24f, 1.05f, -0.3f + i * 0.6f),
                    sink.transform.position + new Vector3(0.2f, 1.3f, -0.3f + i * 0.6f),
                    sink.transform.position + new Vector3(0.02f, 1.28f, -0.3f + i * 0.6f),
                }, 0.012f, steel());
            }

            Material steel() => Paint("steel", new Color(0.78f, 0.8f, 0.82f), 0.9f, 0.7f);
        }

        private static void BuildAnaesthesiaStation(Transform r, Vector3 thorax)
        {
            GameObject station = new GameObject("Anestesia");
            station.transform.SetParent(r, false);
            Transform s = station.transform;
            Vector3 origin = new Vector3(-0.35f, 0f, _bodyBounds.max.z + 0.55f);
            s.position = origin;

            Material casing = Paint("casing", new Color(0.9f, 0.91f, 0.92f), 0.1f, 0.45f);
            Material dark = Paint("dark", new Color(0.12f, 0.13f, 0.15f), 0.2f, 0.4f);
            Material steel = Paint("steel", new Color(0.78f, 0.8f, 0.82f), 0.9f, 0.7f);

            Box("Cart", s, new Vector3(0f, 0.5f, 0f), new Vector3(0.75f, 0.9f, 0.6f), casing);
            Box("Worktop", s, new Vector3(0f, 0.97f, 0f), new Vector3(0.8f, 0.04f, 0.65f), dark);
            Box("Tower", s, new Vector3(0f, 1.3f, 0.18f), new Vector3(0.7f, 0.62f, 0.22f), casing);
            Box("TowerScreen", s, new Vector3(0f, 1.38f, 0.065f), new Vector3(0.42f, 0.28f, 0.01f), Glow(new Color(0.05f, 0.25f, 0.35f)), null, false);

            // Vaporisers: the coloured canisters every anaesthesia machine carries.
            Box("Vaporizer_Sevo", s, new Vector3(0.28f, 1.12f, -0.05f), new Vector3(0.1f, 0.2f, 0.12f), Paint("sevo", new Color(0.95f, 0.8f, 0.1f)));
            Box("Vaporizer_Iso", s, new Vector3(0.40f, 1.12f, -0.05f), new Vector3(0.1f, 0.2f, 0.12f), Paint("iso", new Color(0.55f, 0.25f, 0.6f)));

            // Ventilator bellows in its clear housing.
            Part(PrimitiveType.Cylinder, "BellowsHousing", s, new Vector3(-0.25f, 1.15f, -0.1f), new Vector3(0.16f, 0.14f, 0.16f),
                Glass(new Color(0.8f, 0.9f, 0.95f, 0.35f)), null, false, false);
            for (int i = 0; i < 5; i++)
            {
                Part(PrimitiveType.Cylinder, "Bellows_" + i, s, new Vector3(-0.25f, 1.05f + i * 0.04f, -0.1f),
                    new Vector3(0.13f - (i % 2) * 0.02f, 0.012f, 0.13f - (i % 2) * 0.02f), Paint("bellows", new Color(0.3f, 0.55f, 0.85f)), null, false, false);
            }

            // Gas cylinders on the back.
            Rod("O2Cylinder", s, new Vector3(-0.3f, 0.55f, 0.36f), 0.055f, 0.8f, Paint("gasO2", new Color(0.1f, 0.55f, 0.25f), 0.3f, 0.5f));
            Rod("N2OCylinder", s, new Vector3(0.3f, 0.55f, 0.36f), 0.055f, 0.8f, Paint("gasN2O", new Color(0.15f, 0.3f, 0.7f), 0.3f, 0.5f));

            // Breathing circuit: corrugated limbs from the machine to the patient's airway.
            Vector3 airway = new Vector3(_bodyBounds.center.x, TableTopY + 0.22f, _bodyBounds.max.z - 0.12f);
            Material circuit = Paint("circuit", new Color(0.55f, 0.75f, 0.9f), 0f, 0.3f);
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -0.05f : 0.05f;
                Tube("Circuit_" + i, s, new[]
                {
                    origin + new Vector3(-0.1f + side, 1.02f, -0.3f),
                    origin + new Vector3(-0.05f + side, 1.25f, -0.45f),
                    airway + new Vector3(side, 0.08f, 0.12f),
                    airway,
                }, 0.011f, circuit);
            }

            // Arm and the vitals monitor, turned to the surgeon.
            Rod("MonitorPole", s, new Vector3(-0.3f, 1.55f, 0.2f), 0.02f, 0.5f, steel);
        }

        /// <summary>
        /// What a heart-transplant room has that any operating room does not, and the paperwork
        /// every Brazilian theatre has on its walls: the organ's transport cooler, the WHO safe
        /// surgery checklist, the whiteboard with the counts and the ischaemia time, the
        /// transoesophageal echo, the heater-cooler and cell saver beside the pump, and the
        /// sponge counter. All static and low-poly, sharing materials, so they batch on the Quest.
        /// </summary>
        private static void BuildTransplantDecor(Transform r, Vector3 thorax)
        {
            Material white = Paint("casing", new Color(0.9f, 0.91f, 0.92f), 0.1f, 0.45f);
            Material dark = Paint("dark", new Color(0.12f, 0.13f, 0.15f), 0.2f, 0.4f);
            Material steel = Paint("steel", new Color(0.78f, 0.8f, 0.82f), 0.9f, 0.7f);
            Material plastic = Glass(new Color(0.85f, 0.92f, 0.98f, 0.35f));
            Material screen = Glow(new Color(0.03f, 0.05f, 0.06f));
            Material arterial = Paint("bloodArterial", new Color(0.62f, 0.04f, 0.05f), 0f, 0.85f);

            // ---- the organ's transport cooler, lid open, ice inside, beside the back table
            {
                GameObject cooler = new GameObject("CaixaTermicaOrgao");
                cooler.transform.SetParent(r, false);
                cooler.transform.position = new Vector3(1.6f, 0f, thorax.z - 1.05f);
                // Front (local -Z) turned to the surgeon at the table, lid hinged at the back.
                cooler.transform.rotation = Quaternion.LookRotation(new Vector3(1.15f, 0f, -1.05f), Vector3.up);
                Transform c = cooler.transform;

                Material blue = Paint("coolerBlue", new Color(0.13f, 0.32f, 0.68f), 0f, 0.35f);
                Material ice = Glass(new Color(0.85f, 0.95f, 1f, 0.7f));
                Box("Corpo", c, new Vector3(0f, 0.19f, 0f), new Vector3(0.5f, 0.36f, 0.36f), blue);
                Box("Borda", c, new Vector3(0f, 0.375f, 0f), new Vector3(0.52f, 0.02f, 0.38f), white);
                Box("Interior", c, new Vector3(0f, 0.37f, 0f), new Vector3(0.44f, 0.012f, 0.3f), white, null, false);
                Box("Tampa", c, new Vector3(0f, 0.55f, 0.2f), new Vector3(0.52f, 0.36f, 0.05f), blue, Quaternion.Euler(-12f, 0f, 0f));

                System.Random random = new System.Random(3);
                for (int i = 0; i < 9; i++)
                {
                    Vector3 at = new Vector3(-0.18f + (float)random.NextDouble() * 0.36f, 0.385f, -0.11f + (float)random.NextDouble() * 0.22f);
                    Box("Gelo_" + i, c, at, Vector3.one * (0.035f + (float)random.NextDouble() * 0.02f), ice,
                        Quaternion.Euler(0f, (float)random.NextDouble() * 90f, 0f), false);
                }

                for (int side = -1; side <= 1; side += 2)
                {
                    Box("Alca_" + side, c, new Vector3(side * 0.265f, 0.3f, 0f), new Vector3(0.02f, 0.03f, 0.16f), dark);
                }

                Box("Etiqueta", c, new Vector3(0f, 0.2f, -0.182f), new Vector3(0.36f, 0.16f, 0.004f), white, null, false);
                Box("Faixa", c, new Vector3(0f, 0.265f, -0.185f), new Vector3(0.36f, 0.03f, 0.002f),
                    Paint("signRed", new Color(0.7f, 0.08f, 0.08f)), null, false);
                TextMesh label = BuildScreenText(c, "Rotulo", new Vector3(0f, 0.19f, -0.186f), 0.026f);
                label.transform.localRotation = Quaternion.identity;
                label.text = "ÓRGÃO HUMANO\nPARA TRANSPLANTE";
                label.color = new Color(0.65f, 0.05f, 0.05f);
            }

            // ---- WHO safe surgery checklist, on the wall by the doors
            {
                GameObject board = new GameObject("CirurgiaSegura");
                board.transform.SetParent(r, false);
                board.transform.position = new Vector3(1.6f, 1.5f, RoomMinZ + 0.02f);
                board.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
                Transform b = board.transform;

                Box("Placa", b, Vector3.zero, new Vector3(0.8f, 1.0f, 0.015f), white, null, false);
                Box("Cabecalho", b, new Vector3(0f, 0.44f, -0.009f), new Vector3(0.8f, 0.12f, 0.004f),
                    Paint("checklistGreen", new Color(0.1f, 0.5f, 0.3f)), null, false);

                TextMesh head = BuildScreenText(b, "Titulo", new Vector3(0f, 0.44f, -0.013f), 0.04f);
                head.text = "CIRURGIA SEGURA\nLista de verificação — OMS";
                head.color = Color.white;

                TextMesh body = BuildScreenText(b, "Itens", new Vector3(0f, -0.06f, -0.01f), 0.022f);
                body.anchor = TextAnchor.MiddleCenter;
                body.alignment = TextAlignment.Left;
                body.color = new Color(0.12f, 0.14f, 0.16f);
                body.text =
                    "ANTES DA INDUÇÃO\n" +
                    "[x] Identidade, sítio e procedimento\n" +
                    "[x] Consentimento assinado\n" +
                    "[x] Oxímetro no paciente\n" +
                    "[x] Reserva de sangue\n\n" +
                    "ANTES DA INCISÃO (TIME-OUT)\n" +
                    "[x] Equipe se apresentou\n" +
                    "[x] Antibiótico nos últimos 60 min\n" +
                    "[x] Órgão conferido: tipo sanguíneo\n\n" +
                    "ANTES DE SAIR DA SALA\n" +
                    "[ ] Contagem de compressas e agulhas\n" +
                    "[ ] Peças identificadas";
                foreach (TextMesh t in new[] { head, body }) { t.transform.localRotation = Quaternion.identity; }
            }

            // ---- whiteboard with the counts and the clock that matters most here, above the crash cart
            {
                GameObject board = new GameObject("QuadroBranco");
                board.transform.SetParent(r, false);
                board.transform.position = new Vector3(RoomMinX + 0.02f, 1.85f, -1.85f);
                board.transform.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
                Transform b = board.transform;

                Box("Moldura", b, Vector3.zero, new Vector3(1.0f, 0.62f, 0.02f), steel, null, false);
                Box("Quadro", b, new Vector3(0f, 0f, -0.011f), new Vector3(0.96f, 0.58f, 0.004f),
                    Paint("whiteboard", new Color(0.97f, 0.97f, 0.96f), 0f, 0.8f), null, false);
                Box("Bandeja", b, new Vector3(0f, -0.32f, -0.03f), new Vector3(0.9f, 0.015f, 0.05f), steel, null, false);
                Rod("Marcador_Azul", b, new Vector3(-0.2f, -0.305f, -0.035f), 0.008f, 0.12f,
                    Paint("markerBlue", new Color(0.1f, 0.2f, 0.7f)), Quaternion.Euler(0f, 0f, 90f), false);
                Rod("Marcador_Vermelho", b, new Vector3(-0.05f, -0.305f, -0.035f), 0.008f, 0.12f,
                    Paint("signRed", new Color(0.7f, 0.08f, 0.08f)), Quaternion.Euler(0f, 0f, 90f), false);

                TextMesh text = BuildScreenText(b, "Anotacoes", new Vector3(0f, 0.02f, -0.015f), 0.034f);
                text.transform.localRotation = Quaternion.identity;
                text.alignment = TextAlignment.Left;
                text.color = new Color(0.1f, 0.2f, 0.65f);
                text.text =
                    "SALA 3 — TRANSPLANTE CARDÍACO\n" +
                    "Receptor: 54 a  ·  sangue A+\n" +
                    "Isquemia fria do enxerto: 02h10\n" +
                    "Compressas 10/10   Agulhas 5/5\n" +
                    "Instrumental conferido";
            }

            // ---- transoesophageal echo at the head, screen turned to the surgeon
            {
                GameObject tee = new GameObject("EcoTransesofagico");
                tee.transform.SetParent(r, false);
                tee.transform.position = new Vector3(-1.0f, 0f, _bodyBounds.max.z + 1.25f);
                // Screen (local -Z) toward the surgeon's side of the table.
                tee.transform.rotation = Quaternion.LookRotation(new Vector3(-1f, 0f, 1.2f).normalized, Vector3.up);
                Transform e = tee.transform;

                Box("Base", e, new Vector3(0f, 0.05f, 0f), new Vector3(0.5f, 0.06f, 0.5f), dark);
                Rod("Coluna", e, new Vector3(0f, 0.5f, 0f), 0.04f, 0.85f, white);
                Box("Console", e, new Vector3(0f, 0.95f, -0.05f), new Vector3(0.5f, 0.06f, 0.35f), white);
                Box("Teclado", e, new Vector3(0f, 0.985f, -0.08f), new Vector3(0.4f, 0.01f, 0.2f), dark, null, false);
                Box("Monitor", e, new Vector3(0f, 1.3f, 0.12f), new Vector3(0.46f, 0.34f, 0.04f), dark);
                Box("Tela", e, new Vector3(0f, 1.3f, 0.099f), new Vector3(0.42f, 0.3f, 0.002f), screen, null, false);

                // The echo's sector: a grey fan with the heart's chambers as darker blots.
                GameObject fan = MeshPart("Setor", e, FanMesh(0.13f, 70f, 16), Glow(new Color(0.42f, 0.42f, 0.42f)));
                fan.transform.localPosition = new Vector3(0f, 1.43f, 0.097f);
                fan.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);
                Material chamber = Glow(new Color(0.08f, 0.08f, 0.08f));
                Part(PrimitiveType.Sphere, "CamaraE", e, new Vector3(-0.02f, 1.35f, 0.096f), new Vector3(0.035f, 0.05f, 0.001f), chamber, null, false, false);
                Part(PrimitiveType.Sphere, "CamaraD", e, new Vector3(0.025f, 1.33f, 0.096f), new Vector3(0.03f, 0.04f, 0.001f), chamber, null, false, false);
                Tube("Sonda", e, new[]
                {
                    tee.transform.TransformPoint(new Vector3(0.2f, 0.95f, 0.2f)),
                    tee.transform.TransformPoint(new Vector3(0.45f, 0.8f, 0.4f)),
                    new Vector3(0.05f, TableTopY + 0.3f, _bodyBounds.max.z - 0.12f),
                }, 0.004f, dark);
            }

            // ---- beside the pump: the heater-cooler for the oxygenator and the cell saver
            {
                GameObject heater = new GameObject("TermorreguladorCEC");
                heater.transform.SetParent(r, false);
                heater.transform.position = new Vector3(-1.95f, 0f, thorax.z - 1.1f);
                Transform h = heater.transform;
                Box("Corpo", h, new Vector3(0f, 0.45f, 0f), new Vector3(0.45f, 0.85f, 0.55f), white);
                Box("Painel", h, new Vector3(0.226f, 0.72f, 0f), new Vector3(0.004f, 0.14f, 0.3f), screen, null, false);
                Box("Visor", h, new Vector3(0.23f, 0.72f, -0.06f), new Vector3(0.002f, 0.05f, 0.1f), Glow(new Color(0.2f, 0.9f, 0.4f)), null, false);
                Vector3 oxygenator = new Vector3(-1.4f, 1.0f, thorax.z - 0.42f);
                Tube("Mangueira_Azul", h, new[] { h.position + new Vector3(0.2f, 0.5f, 0.15f), h.position + new Vector3(0.45f, 0.35f, 0.4f), oxygenator + new Vector3(-0.04f, -0.05f, 0f) },
                    0.009f, Paint("hoseBlue", new Color(0.2f, 0.4f, 0.8f), 0f, 0.5f));
                Tube("Mangueira_Vermelha", h, new[] { h.position + new Vector3(0.2f, 0.55f, 0.2f), h.position + new Vector3(0.45f, 0.45f, 0.45f), oxygenator + new Vector3(-0.04f, 0.05f, 0f) },
                    0.009f, Paint("hoseRed", new Color(0.8f, 0.2f, 0.18f), 0f, 0.5f));

                GameObject saver = new GameObject("RecuperadorCelular");
                saver.transform.SetParent(r, false);
                saver.transform.position = new Vector3(-1.95f, 0f, thorax.z - 0.1f);
                Transform v = saver.transform;
                Box("Corpo", v, new Vector3(0f, 0.45f, 0f), new Vector3(0.45f, 0.9f, 0.45f), white);
                Rod("Centrifuga", v, new Vector3(0f, 0.92f, 0.05f), 0.09f, 0.04f, plastic, null, false);
                Box("Tela", v, new Vector3(0.226f, 0.75f, -0.1f), new Vector3(0.004f, 0.12f, 0.16f), screen, null, false);
                Rod("Haste", v, new Vector3(-0.15f, 1.25f, -0.15f), 0.012f, 0.7f, steel);
                Box("BolsaSangue", v, new Vector3(-0.15f, 1.45f, -0.1f), new Vector3(0.1f, 0.16f, 0.03f), arterial);
            }

            // ---- sponge counter: hanging clear pockets, the used ones red, so the count is visible
            {
                GameObject rack = new GameObject("PortaCompressas");
                rack.transform.SetParent(r, false);
                rack.transform.position = new Vector3(0.55f, 0f, thorax.z - 1.55f);
                rack.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
                Transform k = rack.transform;
                Rod("Poste", k, new Vector3(0f, 0.65f, 0f), 0.012f, 1.3f, steel);
                Box("Base", k, new Vector3(0f, 0.02f, 0f), new Vector3(0.4f, 0.03f, 0.4f), steel);
                Box("Travessa", k, new Vector3(0f, 1.28f, 0f), new Vector3(0.5f, 0.015f, 0.015f), steel);
                Box("Folha", k, new Vector3(0f, 1.0f, -0.01f), new Vector3(0.46f, 0.52f, 0.004f), plastic, null, false);
                Material used = Paint("spongeUsed", new Color(0.6f, 0.1f, 0.12f), 0f, 0.5f);
                Material clean = Paint("spongeClean", new Color(0.95f, 0.95f, 0.93f), 0f, 0.2f);
                for (int i = 0; i < 10; i++)
                {
                    int col = i % 2, row = i / 2;
                    Box("Compressa_" + i, k, new Vector3(-0.11f + col * 0.22f, 1.2f - row * 0.1f, -0.012f),
                        new Vector3(0.18f, 0.08f, 0.006f), i < 4 ? used : clean, null, false);
                }
            }

            Debug.Log("[Transplante] decoração do transplante: caixa térmica do órgão, cirurgia segura, quadro, eco, " +
                      "termorregulador, recuperador celular, porta-compressas");
        }

        /// <summary>A flat circular sector pointing down from its apex, facing -Z: the echo's image.</summary>
        private static Mesh FanMesh(float radius, float degrees, int segments)
        {
            List<Vector3> vertices = new List<Vector3> { Vector3.zero };
            List<int> triangles = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = Mathf.Lerp(-degrees * 0.5f, degrees * 0.5f, i / (float)segments) * Mathf.Deg2Rad;
                vertices.Add(new Vector3(Mathf.Sin(a) * radius, Mathf.Cos(a) * radius, 0f));
                if (i == 0) { continue; }
                triangles.Add(0); triangles.Add(i); triangles.Add(i + 1);
            }

            Mesh mesh = new Mesh { name = "SetorEco" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void BuildBypassMachine(Transform r, Vector3 thorax)
        {
            GameObject machine = new GameObject("MaquinaCEC");
            machine.transform.SetParent(r, false);
            Transform m = machine.transform;
            Vector3 origin = new Vector3(-1.2f, 0f, thorax.z - 0.8f);
            m.position = origin;

            Material casing = Paint("casing", new Color(0.9f, 0.91f, 0.92f), 0.1f, 0.45f);
            Material dark = Paint("dark", new Color(0.12f, 0.13f, 0.15f), 0.2f, 0.4f);
            Material steel = Paint("steel", new Color(0.78f, 0.8f, 0.82f), 0.9f, 0.7f);
            Material lid = Glass(new Color(0.8f, 0.9f, 0.95f, 0.3f));
            Material arterial = Paint("bloodArterial", new Color(0.62f, 0.04f, 0.05f), 0f, 0.85f);
            Material venous = Paint("bloodVenous", new Color(0.32f, 0.03f, 0.07f), 0f, 0.85f);

            Box("Base", m, new Vector3(0f, 0.42f, 0f), new Vector3(0.55f, 0.7f, 1.2f), casing);
            Box("Deck", m, new Vector3(0f, 0.79f, 0f), new Vector3(0.58f, 0.04f, 1.25f), dark);
            for (int w = 0; w < 4; w++)
            {
                Rod("Wheel_" + w, m, new Vector3(w < 2 ? -0.22f : 0.22f, 0.05f, w % 2 == 0 ? -0.5f : 0.5f), 0.05f, 0.04f, dark,
                    Quaternion.Euler(0f, 0f, 90f));
            }

            // Five roller pump heads along the deck: arterial, two suckers, vent, cardioplegia.
            for (int i = 0; i < 5; i++)
            {
                Vector3 p = new Vector3(0.02f, 0.85f, -0.48f + i * 0.24f);
                Rod("PumpHead_" + i, m, p, 0.09f, 0.08f, steel);
                Rod("PumpLid_" + i, m, p + new Vector3(0f, 0.05f, 0f), 0.092f, 0.02f, lid, null, false);
                Box("Roller_" + i, m, p + new Vector3(0f, 0.045f, 0f), new Vector3(0.15f, 0.015f, 0.025f), dark, Quaternion.Euler(0f, i * 37f, 0f));
            }

            // Mast with the oxygenator and the venous reservoir, full of blood.
            Rod("Mast", m, new Vector3(-0.2f, 1.3f, 0.5f), 0.025f, 1.0f, steel);
            Rod("Reservoir", m, new Vector3(-0.2f, 1.35f, 0.38f), 0.07f, 0.28f, lid, null, false);
            Rod("ReservoirBlood", m, new Vector3(-0.2f, 1.3f, 0.38f), 0.062f, 0.16f, venous, null, false);
            Rod("Oxygenator", m, new Vector3(-0.2f, 1.05f, 0.38f), 0.06f, 0.2f, Glass(new Color(0.9f, 0.4f, 0.4f, 0.55f)), null, false);

            // Pump console, facing the surgeon across the table.
            GameObject console = new GameObject("Console");
            console.transform.SetParent(m, false);
            console.transform.localPosition = new Vector3(-0.1f, 1.5f, -0.35f);
            console.transform.rotation = Quaternion.LookRotation(new Vector3(1f, 0f, 0.3f).normalized);
            Box("Body", console.transform, Vector3.zero, new Vector3(0.36f, 0.26f, 0.05f), dark);
            Box("Screen", console.transform, new Vector3(0f, 0f, 0.026f), new Vector3(0.32f, 0.2f, 0.002f), Glow(new Color(0.1f, 0.3f, 0.25f)), null, false);
            TextMesh readout = BuildScreenText(console.transform, "Readout", new Vector3(0f, 0.02f, 0.03f), 0.028f);
            readout.text = "FLUXO 4,8 L/min\nT 32 °C";
            readout.color = new Color(0.4f, 1f, 0.6f);
            Rod("ConsolePole", m, new Vector3(-0.1f, 1.12f, -0.35f), 0.018f, 0.6f, steel);

            // The lines: arterial out to the aorta, venous back from the atrium. They cross to the
            // table and lie on the drape beside the operative window, where the cannulas go in.
            Vector3 window = _window.Center + new Vector3(-_window.HalfWidth - 0.03f, 0f, 0f);

            Tube("ArterialLine", m, new[]
            {
                origin + new Vector3(0.1f, 0.92f, -0.48f),
                origin + new Vector3(0.45f, 1.18f, 0.1f),
                new Vector3(-0.42f, TableTopY + 0.12f, window.z + 0.05f),
                ArterialLineEnd(),
            }, 0.0065f, arterial);

            Tube("VenousLine", m, new[]
            {
                origin + new Vector3(0.1f, 0.92f, 0.46f),
                origin + new Vector3(0.45f, 1.1f, 0.6f),
                new Vector3(-0.44f, TableTopY + 0.1f, window.z - 0.02f),
                VenousLineEnd(),
            }, 0.0085f, venous);
        }

        /// <summary>Where the arterial line from the pump lies on the drape, beside the window.</summary>
        private static Vector3 ArterialLineEnd()
        {
            Vector3 window = _window.Center + new Vector3(-_window.HalfWidth - 0.03f, 0f, 0f);
            return new Vector3(window.x, SkinTopAt(window.x + 0.02f, window.z) + 0.03f, window.z + 0.08f);
        }

        /// <summary>Where the venous line back to the pump lies on the drape.</summary>
        private static Vector3 VenousLineEnd()
        {
            Vector3 window = _window.Center + new Vector3(-_window.HalfWidth - 0.03f, 0f, 0f);
            return new Vector3(window.x - 0.005f, SkinTopAt(window.x + 0.02f, window.z) + 0.025f, window.z - 0.02f);
        }

        /// <summary>
        /// The cannulas from the pump lines into the chest, the cardioplegia/vent line to the aortic
        /// root and the aortic cross-clamp, all hidden until the pump step that puts them there.
        /// </summary>
        private static void BuildBypassHardware(GameObject systems, Vector3 thorax, TransplantProcedure procedure)
        {
            GameObject root = new GameObject("CanulasEClampe");
            root.transform.SetParent(systems.transform, true);
            Transform r = root.transform;

            Material arterial = Paint("bloodArterial", new Color(0.62f, 0.04f, 0.05f), 0f, 0.85f);
            Material venous = Paint("bloodVenous", new Color(0.32f, 0.03f, 0.07f), 0f, 0.85f);
            Material clear = Paint("cannulaTip", new Color(0.85f, 0.88f, 0.9f), 0f, 0.9f);
            Material steel = Paint("steelBright", new Color(0.82f, 0.84f, 0.86f), 0.95f, 0.8f);

            Vector3 aorta = thorax + new Vector3(-0.004f, 0.04f, 0.085f);
            Vector3 svc = thorax + new Vector3(0.028f, 0.03f, 0.085f);
            Vector3 ivc = thorax + new Vector3(0.04f, 0.012f, -0.04f);
            Vector3 aorticRoot = thorax + BypassAnatomy[BypassStep.Cardioplegia];
            Vector3 clampAt = thorax + BypassAnatomy[BypassStep.ClampAorta];
            // The tubing arcs over the wound edge a few centimetres above the skin.
            float above = SkinTopAt(thorax.x, thorax.z) + 0.05f;

            List<Renderer> cannulas = new List<Renderer>();
            Vector3 art = ArterialLineEnd();
            cannulas.Add(Tube("CanulaAortica", r, new[]
            {
                art, art + new Vector3(0.03f, 0.03f, 0f), new Vector3(aorta.x - 0.02f, above, aorta.z + 0.01f),
                aorta + new Vector3(0f, 0.02f, 0f), aorta,
            }, 0.004f, arterial).GetComponent<Renderer>());

            // Bicaval: one cannula in each cava, joined by a Y to the single venous line.
            Vector3 ven = VenousLineEnd();
            Vector3 y = new Vector3(ven.x + 0.06f, above - 0.01f, ven.z);
            cannulas.Add(Tube("LinhaVenosaY", r, new[] { ven, ven + new Vector3(0.03f, 0.03f, 0f), y }, 0.0055f, venous)
                .GetComponent<Renderer>());
            cannulas.Add(Tube("CanulaCavaSuperior", r, new[]
            {
                y, new Vector3(svc.x - 0.01f, above + 0.01f, svc.z), svc + new Vector3(0f, 0.02f, 0f), svc,
            }, 0.0045f, venous).GetComponent<Renderer>());
            cannulas.Add(Tube("CanulaCavaInferior", r, new[]
            {
                y, new Vector3(ivc.x - 0.01f, above + 0.005f, ivc.z + 0.02f), ivc + new Vector3(0f, 0.02f, 0f), ivc,
            }, 0.0045f, venous).GetComponent<Renderer>());

            foreach (Vector3 tip in new[] { aorta, svc, ivc })
            {
                // The purse-string tourniquet snugged down where each cannula goes in.
                cannulas.Add(Rod("Torniquete", r, tip + new Vector3(0f, 0.012f, 0f), 0.006f, 0.02f, clear).GetComponent<Renderer>());
            }

            // Cardioplegia into the aortic root; the same needle vents the air out at the end.
            Vector3 lineStart = new Vector3(_window.Center.x - _window.HalfWidth - 0.05f,
                SkinTopAt(_window.Center.x - _window.HalfWidth - 0.05f, _window.Center.z + 0.12f) + 0.03f,
                _window.Center.z + 0.12f);
            Renderer rootLine = Tube("LinhaCardioplegia", r, new[]
            {
                lineStart, new Vector3(aorticRoot.x - 0.02f, above + 0.02f, aorticRoot.z + 0.02f),
                aorticRoot + new Vector3(0f, 0.015f, 0f), aorticRoot,
            }, 0.002f, Paint("cardioplegia", new Color(0.75f, 0.2f, 0.18f), 0f, 0.8f)).GetComponent<Renderer>();

            // The cross-clamp: jaws across the aorta, shanks and finger rings standing out of the
            // chest away from the surgeon so they do not block the work.
            List<Renderer> clamp = new List<Renderer>();
            clamp.Add(Box("ClampeMandibula", r, Vector3.zero, new Vector3(0.05f, 0.004f, 0.006f), steel).GetComponent<Renderer>());
            clamp[0].transform.position = clampAt;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 hinge = clampAt + new Vector3(-0.027f, 0.004f, side * 0.003f);
                Vector3 top = clampAt + new Vector3(-0.075f, 0.13f, side * 0.012f);
                clamp.Add(Tube("ClampeHaste_" + side, r, new[] { hinge, hinge + new Vector3(-0.02f, 0.05f, 0f), top }, 0.0022f, steel)
                    .GetComponent<Renderer>());

                GameObject ring = MeshPart("ClampeArgola_" + side, r, MakeRing(0.008f, 0.012f, 18), steel, true);
                ring.transform.position = top + new Vector3(-0.004f, 0.011f, 0f);
                ring.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
                clamp.Add(ring.GetComponent<Renderer>());
            }

            root.AddComponent<BypassHardware>().Bind(procedure, cannulas.ToArray(), new[] { rootLine }, clamp.ToArray());
            Debug.Log($"[Transplante] {cannulas.Count} peça(s) de canulação, linha de cardioplegia e clampe aórtico em {clampAt}");
        }

        private static void BuildBackTable(Transform r, Vector3 thorax)
        {
            GameObject table = new GameObject("MesaAuxiliar");
            table.transform.SetParent(r, false);
            Transform t = table.transform;
            t.position = new Vector3(1.55f, 0f, thorax.z - 0.1f);

            Material steel = Paint("steel", new Color(0.78f, 0.8f, 0.82f), 0.9f, 0.7f);
            Material drape = DoubleSided(Paint("drapeBlue", new Color(0.16f, 0.38f, 0.58f), 0f, 0.12f));

            Box("Top", t, new Vector3(0f, 0.9f, 0f), new Vector3(0.7f, 0.03f, 1.5f), steel);
            for (int i = 0; i < 4; i++)
            {
                Rod("Leg_" + i, t, new Vector3(i < 2 ? -0.3f : 0.3f, 0.45f, i % 2 == 0 ? -0.7f : 0.7f), 0.015f, 0.9f, steel);
            }

            Box("Drape", t, new Vector3(0f, 0.92f, 0f), new Vector3(0.9f, 0.01f, 1.7f), drape);
            Box("DrapeFallA", t, new Vector3(-0.45f, 0.72f, 0f), new Vector3(0.01f, 0.4f, 1.7f), drape);
            Box("DrapeFallB", t, new Vector3(0.45f, 0.72f, 0f), new Vector3(0.01f, 0.4f, 1.7f), drape);

            // Basins, a gown pack, rows of instruments, gauze.
            Rod("Basin_A", t, new Vector3(0.05f, 0.95f, -0.5f), 0.14f, 0.07f, steel);
            Rod("Basin_B", t, new Vector3(0.05f, 0.95f, -0.15f), 0.1f, 0.05f, steel);
            Box("GownPack", t, new Vector3(0.1f, 0.96f, 0.45f), new Vector3(0.3f, 0.07f, 0.35f), Paint("gown", new Color(0.3f, 0.55f, 0.72f)));
            Material gauze = Paint("gauze", new Color(0.97f, 0.97f, 0.95f), 0f, 0.1f);
            for (int i = 0; i < 3; i++)
            {
                Box("Gauze_" + i, t, new Vector3(-0.22f, 0.94f + i * 0.012f, 0.15f + i * 0.01f), new Vector3(0.1f, 0.01f, 0.1f), gauze);
            }

            for (int i = 0; i < 9; i++)
            {
                Box("Instrument_" + i, t, new Vector3(-0.22f + (i % 3) * 0.02f, 0.94f, 0.45f + i * 0.03f),
                    new Vector3(0.16f, 0.006f, 0.008f), steel);
            }
        }

        private static void BuildIvPole(Transform r, Vector3 thorax)
        {
            GameObject pole = new GameObject("SuporteSoro");
            pole.transform.SetParent(r, false);
            Transform p = pole.transform;
            Vector3 origin = new Vector3(0.55f, 0f, _bodyBounds.max.z - 0.15f);
            p.position = origin;

            Material steel = Paint("steel", new Color(0.78f, 0.8f, 0.82f), 0.9f, 0.7f);
            Rod("Pole", p, new Vector3(0f, 1.0f, 0f), 0.012f, 2.0f, steel);
            for (int i = 0; i < 5; i++)
            {
                float a = i * 72f;
                Box("Foot_" + i, p, new Vector3(0f, 0.04f, 0f), new Vector3(0.5f, 0.02f, 0.03f), steel, Quaternion.Euler(0f, a, 0f));
            }

            Box("Hanger", p, new Vector3(0f, 1.98f, 0f), new Vector3(0.3f, 0.01f, 0.01f), steel);

            Material bag = Glass(new Color(0.9f, 0.95f, 1f, 0.45f));
            Material label = Paint("label", new Color(0.95f, 0.95f, 0.95f));
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -0.12f : 0.12f;
                Box("Bag_" + i, p, new Vector3(x, 1.8f, 0f), new Vector3(0.1f, 0.2f, 0.035f), bag, null, false);
                Box("BagLabel_" + i, p, new Vector3(x, 1.8f, -0.019f), new Vector3(0.07f, 0.08f, 0.002f), label, null, false);
                Rod("Drip_" + i, p, new Vector3(x, 1.64f, 0f), 0.008f, 0.05f, bag, null, false);
            }

            Tube("IvLine", p, new[]
            {
                origin + new Vector3(-0.12f, 1.6f, 0f),
                origin + new Vector3(-0.2f, 1.25f, -0.1f),
                origin + new Vector3(-0.28f, TableTopY + 0.14f, -0.25f),
                new Vector3(TableHalfWidth - 0.05f, TableTopY + 0.1f, origin.z - 0.35f),
            }, 0.0022f, Glass(new Color(0.9f, 0.95f, 1f, 0.6f)));
        }

        private static void BuildSmallFurniture(Transform r, Vector3 thorax)
        {
            Material steel = Paint("steel", new Color(0.78f, 0.8f, 0.82f), 0.9f, 0.7f);
            Material red = Paint("bioRed", new Color(0.8f, 0.12f, 0.1f), 0f, 0.4f);
            Material yellow = Paint("bioYellow", new Color(0.95f, 0.8f, 0.1f), 0f, 0.4f);

            // Kick bucket by the surgeon's feet, biohazard bins by the far wall.
            Rod("KickBucket", r, new Vector3(0.75f, 0.17f, thorax.z - 0.55f), 0.17f, 0.3f, steel);
            Rod("Bin_Infectante", r, new Vector3(RoomMinX + 0.3f, 0.35f, -1.6f), 0.2f, 0.7f, red);
            Rod("Bin_Perfuro", r, new Vector3(RoomMinX + 0.3f, 0.2f, -1.1f), 0.12f, 0.4f, yellow);

            // A step stool and a surgeon's stool.
            GameObject stool = new GameObject("Stool");
            stool.transform.SetParent(r, false);
            stool.transform.position = new Vector3(1.0f, 0f, thorax.z + 1.1f);
            Rod("Seat", stool.transform, new Vector3(0f, 0.55f, 0f), 0.18f, 0.06f, Paint("seat", new Color(0.12f, 0.13f, 0.15f)));
            Rod("Column", stool.transform, new Vector3(0f, 0.28f, 0f), 0.025f, 0.5f, steel);
            for (int i = 0; i < 5; i++)
            {
                Box("Leg_" + i, stool.transform, new Vector3(0f, 0.04f, 0f), new Vector3(0.45f, 0.025f, 0.03f), steel, Quaternion.Euler(0f, i * 72f, 0f));
            }
        }

        // ------------------------------------------------------------------ drapes

        /// <summary>
        /// Sterile drapes over the whole patient except a window on the sternum, and the
        /// anaesthesia screen at the neck. They rest on the sampled body and fall over the table
        /// edges like cloth, which is both what a prepared patient looks like and what hides the
        /// coarse body mesh everywhere the surgeon is not working.
        /// </summary>
        private static void BuildDrapes(Transform parent)
        {
            GameObject root = new GameObject("CamposCirurgicos");
            root.transform.SetParent(parent, true);

            float cx = _window.Center.x, cz = _window.Center.z;
            float wx = _window.HalfWidth, wz = _window.HalfLength;
            float tableX = 0f;

            // 4 cm grid: coarse enough for cloth simulation on a Quest (about 1.6k vertices), fine
            // enough that the drape still reads as fabric over a body.
            float step = UseDrapeCloth ? 0.04f : 0.03f;
            List<float> xs = Axis(-0.68f, 0.68f, step, cx - wx, cx + wx);
            List<float> zs = Axis(_bodyBounds.min.z - 0.18f, _window.NeckZ, step, cz - wz, cz + wz);

            int nx = xs.Count, nz = zs.Count;
            float[] h = new float[nx * nz];
            float[] floorOf = new float[nx * nz];

            for (int j = 0; j < nz; j++)
            {
                for (int i = 0; i < nx; i++)
                {
                    float x = xs[i], z = zs[j];
                    float top = _surface != null ? _surface.Top(x, z, 2) : float.NegativeInfinity;
                    bool overTable = Mathf.Abs(x - tableX) <= TableHalfWidth && z >= _bodyBounds.min.z - 0.05f;

                    float rest = overTable ? Mathf.Max(TableTopY, top) : TableTopY;
                    float over = Mathf.Max(Mathf.Abs(x - tableX) - TableHalfWidth, (_bodyBounds.min.z - 0.05f) - z, 0f);
                    float height = over > 0f ? Mathf.Max(0.45f, TableTopY - over * 3.2f) : rest;

                    h[j * nx + i] = height + 0.016f;
                    floorOf[j * nx + i] = overTable && !float.IsNegativeInfinity(top) ? top + 0.01f : float.NegativeInfinity;
                }
            }

            // Cloth does not follow every facet: soften, then never sink into the body.
            for (int pass = 0; pass < 3; pass++)
            {
                float[] next = (float[])h.Clone();
                for (int j = 1; j < nz - 1; j++)
                {
                    for (int i = 1; i < nx - 1; i++)
                    {
                        float sum = 0f;
                        for (int dj = -1; dj <= 1; dj++)
                        {
                            for (int di = -1; di <= 1; di++) { sum += h[(j + dj) * nx + i + di]; }
                        }

                        next[j * nx + i] = Mathf.Max(sum / 9f, floorOf[j * nx + i]);
                    }
                }

                h = next;
            }

            List<Vector3> vertices = new List<Vector3>(nx * nz);
            List<Vector2> uvs = new List<Vector2>(nx * nz);
            for (int j = 0; j < nz; j++)
            {
                for (int i = 0; i < nx; i++)
                {
                    vertices.Add(new Vector3(xs[i], h[j * nx + i], zs[j]));
                    uvs.Add(new Vector2(xs[i] * 3f, zs[j] * 3f));
                }
            }

            List<int> triangles = new List<int>();
            for (int j = 0; j < nz - 1; j++)
            {
                for (int i = 0; i < nx - 1; i++)
                {
                    float mx = (xs[i] + xs[i + 1]) * 0.5f, mz = (zs[j] + zs[j + 1]) * 0.5f;
                    if (Mathf.Abs(mx - cx) < wx && Mathf.Abs(mz - cz) < wz) { continue; }

                    int a = j * nx + i, b = a + 1, c = a + nx, d = c + 1;
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            }

            Mesh mesh = new Mesh { name = "Drapes" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            Material cloth = DoubleSided(Paint("drapeBlue", new Color(0.16f, 0.38f, 0.58f), 0f, 0.12f));
            GameObject drape = MeshPart("CampoEstatico", root.transform, mesh, cloth, true);
            drape.GetComponent<MeshRenderer>().receiveShadows = true;

            // Solid underneath: a released heart or instrument comes to rest on the drapes instead
            // of falling through the patient onto the floor. A collider of its own, on the draped
            // rest shape, because Unity's cloth cannot carry a mesh collider.
            GameObject solid = new GameObject("CampoColisor");
            solid.transform.SetParent(root.transform, false);
            solid.AddComponent<MeshCollider>().sharedMesh = mesh;

            if (UseDrapeCloth) { MakeDrapeCloth(root.transform, mesh, drape.GetComponent<MeshRenderer>(), cloth, xs, zs); }

            // Iodine-coloured adhesive film framing the window, where drape meets skin.
            Material film = Paint("ioban", new Color(0.62f, 0.38f, 0.2f), 0f, 0.7f);
            float filmY = SkinTopAt(cx, cz) + 0.004f;
            float border = 0.012f;
            foreach ((Vector3 p, Vector3 s) in new[]
            {
                (new Vector3(cx - wx - border * 0.5f, 0f, cz), new Vector3(border, 0.002f, wz * 2f + border * 2f)),
                (new Vector3(cx + wx + border * 0.5f, 0f, cz), new Vector3(border, 0.002f, wz * 2f + border * 2f)),
                (new Vector3(cx, 0f, cz - wz - border * 0.5f), new Vector3(wx * 2f, 0.002f, border)),
                (new Vector3(cx, 0f, cz + wz + border * 0.5f), new Vector3(wx * 2f, 0.002f, border)),
            })
            {
                float y = SkinTopAt(p.x, p.z) + 0.012f;
                Box("Filme", root.transform, new Vector3(p.x, Mathf.Max(y, filmY), p.z), s, film, null, false);
            }

            // Anaesthesia screen at the neck: a bar and a hanging sheet between the sterile field
            // and the patient's head. The surgeon sees drape; anaesthesia sees the airway.
            Material steel = Paint("steel", new Color(0.78f, 0.8f, 0.82f), 0.9f, 0.7f);
            float screenTop = TableTopY + 0.62f;
            Rod("ArcoAnestesia", root.transform, new Vector3(tableX, screenTop, _window.NeckZ), 0.012f, 0.9f, steel,
                Quaternion.Euler(0f, 0f, 90f));
            for (int side = -1; side <= 1; side += 2)
            {
                Rod("ArcoPoste_" + side, root.transform, new Vector3(tableX + side * 0.45f, (TableTopY + screenTop) * 0.5f, _window.NeckZ),
                    0.01f, screenTop - TableTopY, steel);
            }

            Box("CampoAnestesia", root.transform, new Vector3(tableX, screenTop - 0.24f, _window.NeckZ + 0.01f),
                new Vector3(0.95f, 0.5f, 0.008f), cloth);

            Debug.Log($"[Transplante] campos: {vertices.Count} vértices, janela {wx * 200f:F0}x{wz * 200f:F0} cm");
        }

        /// <summary>
        /// Simulated drapes. Switch off here if a Quest build cannot afford the cloth solver; the
        /// static drape and its collider stay either way.
        /// </summary>
        private const bool UseDrapeCloth = true;

        private static Cloth _drapeCloth;
        private static Renderer _drapeStatic;
        private static int[] _drapeWatch = new int[0];
        private static Vector3[] _drapeWatchRest = new Vector3[0];

        /// <summary>
        /// Turns the drape into Unity cloth held near its draped shape: pinned where the adhesive
        /// film holds it to the skin round the window, a centimetre and a half of give where it
        /// rests on the body, and free to swing where it hangs off the table.
        /// </summary>
        private static void MakeDrapeCloth(Transform parent, Mesh mesh, MeshRenderer staticDrape, Material material,
            List<float> xs, List<float> zs)
        {
            GameObject go = new GameObject("Campo");
            go.transform.SetParent(parent, false);

            SkinnedMeshRenderer skinned = go.AddComponent<SkinnedMeshRenderer>();
            skinned.sharedMesh = mesh;
            skinned.sharedMaterial = material;
            skinned.receiveShadows = true;
            skinned.updateWhenOffscreen = false;

            Cloth cloth = go.AddComponent<Cloth>();
            ClothSkinningCoefficient[] limits = cloth.coefficients;
            Vector3[] vertices = mesh.vertices;

            if (limits == null || limits.Length != vertices.Length)
            {
                Debug.LogWarning($"[Transplante] tecido não configurado ({(limits == null ? 0 : limits.Length)} coeficientes " +
                                 $"para {vertices.Length} vértices); campos ficam estáticos.");
                Object.DestroyImmediate(cloth);
                Object.DestroyImmediate(go);
                return;
            }

            float cx = _window.Center.x, cz = _window.Center.z;
            float wx = _window.HalfWidth, wz = _window.HalfLength;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];

                // Distance outside the window rectangle, 0 on its edge.
                float ox = Mathf.Max(0f, Mathf.Abs(v.x - cx) - wx);
                float oz = Mathf.Max(0f, Mathf.Abs(v.z - cz) - wz);
                float fromWindow = Mathf.Sqrt(ox * ox + oz * oz);

                float overhang = Mathf.Max(Mathf.Abs(v.x) - TableHalfWidth, (_bodyBounds.min.z - 0.05f) - v.z, 0f);

                float limit;
                if (fromWindow < 0.03f) { limit = 0f; }
                else if (overhang <= 0f) { limit = 0.015f; }
                else { limit = Mathf.Min(0.12f, 0.02f + overhang * 0.35f); }

                limits[i].maxDistance = limit;
                limits[i].collisionSphereDistance = 0f;
            }

            cloth.coefficients = limits;
            cloth.useGravity = true;
            cloth.damping = 0.25f;
            cloth.stretchingStiffness = 0.95f;
            cloth.bendingStiffness = 0.5f;
            cloth.friction = 0.6f;
            cloth.worldVelocityScale = 0f;
            cloth.worldAccelerationScale = 0f;
            cloth.clothSolverFrequency = 60f;

            // The static drape stays in the scene, off, as the fallback.
            staticDrape.enabled = false;

            // A spread of vertices for the watchdog to check against their rest positions.
            List<int> watch = new List<int>();
            for (int i = 0; i < vertices.Length; i += Mathf.Max(1, vertices.Length / 24)) { watch.Add(i); }
            _drapeWatch = watch.ToArray();
            _drapeWatchRest = new Vector3[_drapeWatch.Length];
            for (int i = 0; i < _drapeWatch.Length; i++) { _drapeWatchRest[i] = vertices[_drapeWatch[i]]; }

            _drapeCloth = cloth;
            _drapeStatic = staticDrape;

            Debug.Log($"[Transplante] campos simulados como tecido: {vertices.Length} vértices, grade {xs.Count}x{zs.Count}");
        }

        /// <summary>
        /// Hands the cloth its colliders: a sphere on each fingertip and one on each instrument's
        /// working end. Called once the instruments exist.
        /// </summary>
        private static void WireDrapeCloth(params Transform[] toolTips)
        {
            if (_drapeCloth == null) { return; }

            List<SphereCollider> hands = new List<SphereCollider>();
            foreach (Transform hand in FindHands())
            {
                GameObject touch = new GameObject("ToqueTecido");
                touch.transform.SetParent(hand, false);
                SphereCollider sphere = touch.AddComponent<SphereCollider>();
                sphere.radius = 0.02f;

                // Kinematic: moved by the hand, never pushed by anything.
                Rigidbody body = touch.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                hands.Add(sphere);
            }

            List<SphereCollider> tips = new List<SphereCollider>();
            foreach (Transform tip in toolTips)
            {
                if (tip == null) { continue; }
                SphereCollider sphere = tip.gameObject.AddComponent<SphereCollider>();
                sphere.radius = 0.006f;
                tips.Add(sphere);
            }

            DrapeCloth driver = _drapeCloth.gameObject.AddComponent<DrapeCloth>();
            driver.Bind(_drapeCloth, _drapeStatic, hands, tips, _drapeWatch, _drapeWatchRest);

            Debug.Log($"[Transplante] tecido responde a {hands.Count} mão(s) e {tips.Count} instrumento(s)");
        }

        /// <summary>Evenly spaced coordinates that also land exactly on the given edges.</summary>
        private static List<float> Axis(float from, float to, float step, params float[] edges)
        {
            List<float> values = new List<float>();
            for (float v = from; v <= to + 1e-4f; v += step) { values.Add(v); }
            values.AddRange(edges);
            values.Sort();

            List<float> clean = new List<float>();
            foreach (float v in values)
            {
                if (v < from - 1e-4f || v > to + 1e-4f) { continue; }
                if (clean.Count > 0 && v - clean[clean.Count - 1] < step * 0.3f)
                {
                    // Keep the edge rather than the grid line next to it.
                    bool isEdge = System.Array.IndexOf(edges, v) >= 0;
                    if (isEdge) { clean[clean.Count - 1] = v; }
                    continue;
                }

                clean.Add(v);
            }

            return clean;
        }

        // ------------------------------------------------------------------ chest window

        /// <summary>
        /// Removes the body's own skin under the chest patch (front side only), so the patch can
        /// part without the coarse mesh showing through, and saves the result as an asset.
        /// </summary>
        private static void CutSkinUnderPatch(GameObject skin, float halfWidth, float halfLength, float frontAbove)
        {
            MeshFilter filter = skin.GetComponentInChildren<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) { return; }

            Mesh source = filter.sharedMesh;
            Vector3[] local = source.vertices;
            Vector3[] world = new Vector3[local.Length];
            for (int i = 0; i < local.Length; i++) { world[i] = filter.transform.TransformPoint(local[i]); }

            float cx = _window.Center.x, cz = _window.Center.z;
            bool Inside(Vector3 p) =>
                p.y > frontAbove && Mathf.Abs(p.x - cx) < halfWidth - 0.004f && Mathf.Abs(p.z - cz) < halfLength - 0.004f;

            Mesh cut = Object.Instantiate(source);
            cut.name = "PATIENT_BodySkin_Janela";
            int removed = 0;

            for (int s = 0; s < cut.subMeshCount; s++)
            {
                int[] tris = source.GetTriangles(s);
                List<int> kept = new List<int>(tris.Length);
                for (int t = 0; t < tris.Length; t += 3)
                {
                    Vector3 a = world[tris[t]], b = world[tris[t + 1]], c = world[tris[t + 2]];
                    if (Inside(a) || Inside(b) || Inside(c) || Inside((a + b + c) / 3f))
                    {
                        removed++;
                        continue;
                    }

                    kept.Add(tris[t]); kept.Add(tris[t + 1]); kept.Add(tris[t + 2]);
                }

                cut.SetTriangles(kept, s);
            }

            EnsureAssetFolder(GeneratedMeshFolder);
            if (AssetDatabase.LoadAssetAtPath<Mesh>(WindowSkinAsset) != null) { AssetDatabase.DeleteAsset(WindowSkinAsset); }
            AssetDatabase.CreateAsset(cut, WindowSkinAsset);
            filter.sharedMesh = cut;

            Debug.Log($"[Transplante] pele do corpo sob a janela removida: {removed} triângulo(s), salvo em {WindowSkinAsset}");
        }

        private static ChestSkinPatch BuildChestPatch(Transform parent, Vector3 thorax, out GameObject cavity)
        {
            const int columns = 14;
            const int rows = 41;
            float halfWidth = _window.HalfWidth + 0.025f;
            float halfLength = _window.HalfLength + 0.025f;

            GameObject root = new GameObject("PeleToracica");
            root.transform.SetParent(parent, true);
            root.transform.SetPositionAndRotation(_window.Center, Quaternion.identity);

            int full = columns * 2 - 1;
            float[] heights = new float[rows * full];
            for (int r = 0; r < rows; r++)
            {
                float z = Mathf.Lerp(-halfLength, halfLength, r / (float)(rows - 1));
                for (int c = 0; c < full; c++)
                {
                    float x = Mathf.Lerp(-halfWidth, halfWidth, c / (float)(full - 1));
                    heights[r * full + c] = SkinTopAt(_window.Center.x + x, _window.Center.z + z) + 0.0025f;
                }
            }

            // Iodine-prepped skin, not bare skin: brownish orange, and slightly wet.
            Material prepped = Paint("prepSkin", new Color(0.78f, 0.5f, 0.34f), 0f, 0.5f);
            // The cut face shows the layers: skin, dermis, yellow fat, fascia, muscle. White base
            // colour so the texture's own colours come through; wet, so fairly glossy.
            Material wound = DoubleSided(Dress(Paint("woundLayers", Color.white, 0f, 0.72f), _woundLayers, new Vector2(1f, 3f), 0.8f));

            MeshFilter left = MeshPart("Pele_E", root.transform, null, prepped, true).GetComponent<MeshFilter>();
            MeshFilter right = MeshPart("Pele_D", root.transform, null, prepped, true).GetComponent<MeshFilter>();
            MeshFilter leftWall = MeshPart("Ferida_E", root.transform, null, wound).GetComponent<MeshFilter>();
            MeshFilter rightWall = MeshPart("Ferida_D", root.transform, null, wound).GetComponent<MeshFilter>();

            cavity = BuildCavity(parent, thorax);

            ChestSkinPatch patch = root.AddComponent<ChestSkinPatch>();
            patch.Bind(columns, rows, halfWidth, halfLength, heights, left, right, leftWall, rightWall, new[] { cavity });

            float margin = (halfLength - _window.IncisionHalfLength) / (2f * halfLength);
            SetPrivateField(patch, "incisionStart", margin);
            SetPrivateField(patch, "incisionEnd", 1f - margin);
            patch.Rebuild(0f);

            CutSkinUnderPatch(GameObject.Find("Body_Skin"), halfWidth, halfLength, thorax.y);
            return patch;
        }

        /// <summary>An open bowl of red tissue under the window: what the wound looks into.</summary>
        private static GameObject BuildCavity(Transform parent, Vector3 thorax)
        {
            float rimY = SkinTopAt(_window.Center.x, _window.Center.z) - 0.012f;
            float depth = Mathf.Max(0.08f, rimY - (thorax.y - 0.05f));
            float rx = _window.HalfWidth * 0.95f;
            float rz = _window.IncisionHalfLength + 0.01f;

            const int around = 40, down = 10;
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();

            for (int d = 0; d <= down; d++)
            {
                float a = d / (float)down * Mathf.PI * 0.5f;
                for (int i = 0; i < around; i++)
                {
                    float phi = i / (float)around * Mathf.PI * 2f;
                    vertices.Add(new Vector3(rx * Mathf.Cos(phi) * Mathf.Cos(a), -depth * Mathf.Sin(a), rz * Mathf.Sin(phi) * Mathf.Cos(a)));
                }
            }

            for (int d = 0; d < down; d++)
            {
                for (int i = 0; i < around; i++)
                {
                    int a = d * around + i, b = d * around + (i + 1) % around;
                    int c = (d + 1) * around + i, e = (d + 1) * around + (i + 1) % around;
                    triangles.Add(a); triangles.Add(b); triangles.Add(c);
                    triangles.Add(b); triangles.Add(e); triangles.Add(c);
                }
            }

            Mesh mesh = new Mesh { name = "CavidadeToracica" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            Material tissue = DoubleSided(Paint("cavity", new Color(0.4f, 0.07f, 0.06f), 0f, 0.7f));
            GameObject cavity = MeshPart("CavidadeToracica", parent, mesh, tissue);
            cavity.transform.position = new Vector3(_window.Center.x, rimY, _window.Center.z);

            // Pericardial fat and the pale fascia over the sternum, as flat patches at the bottom.
            Material fat = Paint("fat", new Color(0.88f, 0.76f, 0.45f), 0f, 0.6f);
            Box("Gordura", cavity.transform, new Vector3(0f, -depth * 0.55f, 0.03f), new Vector3(rx * 0.9f, 0.004f, rz * 0.8f), fat, null, false);

            // Blood lying in the bottom of the cavity: a thin film at rest, rising while a vessel
            // leaks. Wet and dark, the most reflective thing in the wound.
            Material pooled = Paint("bloodPool", new Color(0.28f, 0.01f, 0.02f), 0f, 0.97f);
            GameObject pool = Part(PrimitiveType.Cylinder, "PoçaSangue", cavity.transform, Vector3.zero, Vector3.one, pooled, null, false, false);
            _bloodPool = pool.AddComponent<CavityBloodPool>();
            _bloodPool.Bind(null, rx, rz, depth);

            // A solid floor to the open chest: a heart set down in it rests there instead of
            // dropping through the patient. Flat and low, so it never overlaps the heart already
            // in its seat, which a collider on the curved walls would.
            GameObject floor = new GameObject("FundoCavidade");
            floor.transform.SetParent(cavity.transform, false);
            floor.transform.localPosition = new Vector3(0f, -depth * 0.97f, 0f);
            BoxCollider floorBox = floor.AddComponent<BoxCollider>();
            floorBox.size = new Vector3(rx * 1.6f, 0.01f, rz * 1.6f);

            cavity.SetActive(false);
            return cavity;
        }

        /// <summary>The pool in the cavity, bound to the vessel joins once they exist.</summary>
        private static CavityBloodPool _bloodPool;

        private static SternalRetractor BuildRetractor(Transform parent, SternotomyController sternotomy, GameObject sternum)
        {
            float surface = SkinTopAt(_window.Center.x, _window.Center.z);
            GameObject root = new GameObject("AfastadorFinochietto");
            root.transform.SetParent(parent, true);
            root.transform.SetPositionAndRotation(new Vector3(_window.Center.x, surface, _window.Center.z), Quaternion.identity);

            Material steel = Paint("steelBright", new Color(0.82f, 0.84f, 0.86f), 0.95f, 0.8f);
            float rackZ = _window.IncisionHalfLength * 0.7f;

            // The toothed rack across the wound, near its head end.
            Box("Cremalheira", root.transform, new Vector3(0f, 0.035f, rackZ), new Vector3(0.24f, 0.012f, 0.018f), steel, null, false);
            for (int i = 0; i < 12; i++)
            {
                Box("Dente_" + i, root.transform, new Vector3(-0.1f + i * 0.018f, 0.043f, rackZ), new Vector3(0.006f, 0.005f, 0.018f), steel, null, false);
            }

            // The two halves of the split sternum ride on the blades: ivory cortical bone with the
            // red marrow of the saw cut facing the midline. They replace the whole bone once it
            // parts, so the classic view — two white edges held apart, the heart between them —
            // is what the visitor sees.
            Bounds bone = WorldBounds(sternum);
            float boneLength = Mathf.Clamp(bone.size.z * 0.95f, 0.1f, 0.24f);
            float boneY = Mathf.Clamp(bone.center.y - surface, -0.05f, -0.022f);
            Material cortical = Paint("bone", new Color(0.9f, 0.86f, 0.76f), 0f, 0.3f);
            Material marrow = Paint("marrow", new Color(0.55f, 0.12f, 0.1f), 0f, 0.6f);

            Transform Blade(string name, float side)
            {
                GameObject blade = new GameObject(name);
                blade.transform.SetParent(root.transform, false);
                blade.transform.localPosition = new Vector3(side * 0.015f, 0f, 0f);
                Box("Lamina", blade.transform, new Vector3(0f, -0.022f, 0f), new Vector3(0.005f, 0.05f, 0.075f), steel, null, false);
                Box("Braco", blade.transform, new Vector3(0f, 0.03f, rackZ * 0.5f), new Vector3(0.012f, 0.012f, rackZ + 0.02f), steel, null, false);

                Box("MeioEsterno", blade.transform, new Vector3(side * 0.013f, boneY, 0f),
                    new Vector3(0.018f, 0.011f, boneLength), cortical, null, false);
                Box("Medula", blade.transform, new Vector3(side * 0.0038f, boneY, 0f),
                    new Vector3(0.0012f, 0.008f, boneLength * 0.98f), marrow, null, false);
                return blade.transform;
            }

            Transform left = Blade("Lamina_E", -1f);
            Transform right = Blade("Lamina_D", 1f);

            GameObject crank = new GameObject("Manivela");
            crank.transform.SetParent(right, false);
            crank.transform.localPosition = new Vector3(0.02f, 0.035f, rackZ);
            Rod("Eixo", crank.transform, Vector3.zero, 0.005f, 0.03f, steel, Quaternion.Euler(0f, 0f, 90f), false);
            Box("Alavanca", crank.transform, new Vector3(0.015f, 0.015f, 0f), new Vector3(0.006f, 0.035f, 0.01f), steel, null, false);

            SternalRetractor retractor = root.AddComponent<SternalRetractor>();
            retractor.Bind(sternotomy, left, right, crank.transform);

            Transform model = sternum.transform.Find("SternumModel");
            retractor.BindSternum((model != null ? model.gameObject : sternum).GetComponentsInChildren<Renderer>(true));
            return retractor;
        }

        // ------------------------------------------------------------------ instruments

        /// <summary>
        /// A Mayo stand: a steel tray on one post, cantilevered over the patient's abdomen, in
        /// front of the surgeon. Scalpel, needle holder and the donor heart's basin sit on it.
        /// Replaces the donor pedestal that stood behind the surgeon's shoulder, 98° off the line
        /// of sight, where a first-timer never looked.
        /// </summary>
        private static Transform BuildMayoStand(Vector3 thorax, out Vector3 trayTop)
        {
            GameObject stand = new GameObject("MesaMayo");
            // Close to the chest on purpose: the ergonomics suite wants every instrument within
            // 0.70 m of BOTH shoulders, and the right shoulder is the far one for anything toward
            // the feet. 0.30 m below the thorax is as far down the body as that allows.
            Vector3 centre = new Vector3(thorax.x + 0.14f, 0f, thorax.z - 0.30f);
            float topY = Mathf.Max(SkinTopAt(centre.x, centre.z) + 0.06f, TableTopY + 0.3f);

            Material steel = Paint("steel", new Color(0.78f, 0.8f, 0.82f), 0.9f, 0.7f);
            Material drape = DoubleSided(Paint("drapeBlue", new Color(0.16f, 0.38f, 0.58f), 0f, 0.12f));

            Part(PrimitiveType.Cube, "Bandeja", stand.transform, new Vector3(centre.x, topY - 0.01f, centre.z),
                new Vector3(0.42f, 0.02f, 0.28f), steel, null, true);
            // Solid: an instrument let go over the tray lands on it.
            Box("CampoBandeja", stand.transform, new Vector3(centre.x, topY + 0.001f, centre.z), new Vector3(0.46f, 0.003f, 0.32f), drape)
                .AddComponent<BoxCollider>();
            Box("Aba", stand.transform, new Vector3(centre.x - 0.23f, topY - 0.05f, centre.z), new Vector3(0.003f, 0.1f, 0.32f), drape);

            float postX = thorax.x + TableHalfWidth + 0.13f;
            Rod("Poste", stand.transform, new Vector3(postX, topY * 0.5f, centre.z - 0.12f), 0.018f, topY, steel);
            Box("BracoMayo", stand.transform, new Vector3((postX + centre.x + 0.19f) * 0.5f, topY - 0.03f, centre.z - 0.12f),
                new Vector3(Mathf.Max(0.04f, postX - (centre.x + 0.19f) + 0.04f), 0.025f, 0.03f), steel);
            Box("Base", stand.transform, new Vector3(postX, 0.03f, centre.z - 0.12f), new Vector3(0.5f, 0.03f, 0.05f), steel);
            Box("Base2", stand.transform, new Vector3(postX, 0.03f, centre.z - 0.12f), new Vector3(0.05f, 0.03f, 0.5f), steel);

            // Decorative instruments on the far half of the tray.
            for (int i = 0; i < 4; i++)
            {
                Box("Pinca_" + i, stand.transform, new Vector3(centre.x - 0.17f + i * 0.02f, topY + 0.006f, centre.z - 0.05f),
                    new Vector3(0.008f, 0.006f, 0.13f), steel);
            }

            Box("Gaze", stand.transform, new Vector3(centre.x + 0.02f, topY + 0.008f, centre.z - 0.09f), new Vector3(0.07f, 0.01f, 0.07f),
                Paint("gauze", new Color(0.97f, 0.97f, 0.95f), 0f, 0.1f));

            trayTop = new Vector3(centre.x, topY + 0.003f, centre.z);
            _trayTop = trayTop;

            int operatorLayer = LayerMask.NameToLayer(OperatorLayer);
            if (operatorLayer >= 0)
            {
                foreach (Transform t in stand.GetComponentsInChildren<Transform>(true)) { t.gameObject.layer = operatorLayer; }
            }

            Debug.Log($"[Transplante] mesa de Mayo sobre o abdome, bandeja em {trayTop}");
            return stand.transform;
        }

        /// <summary>
        /// The electrocautery pen in its holster on the drape beside the window, the bleeders on
        /// the wound edges it seals, their scorch marks, and the smoke from its tip.
        /// </summary>
        private static CauteryWorker BuildCautery(GameObject systems, ChestSkinPatch patch,
            out SurgicalInteractable pen, out Transform penTip)
        {
            // Holster on the drape, surgeon's side of the window, pen lying tip toward the feet.
            float x = _window.Center.x + _window.HalfWidth + 0.06f;
            float z = _window.Center.z - 0.02f;
            float y = SkinTopAt(x, z) + 0.035f;
            Quaternion lying = Quaternion.LookRotation(Vector3.back, Vector3.up);

            GameObject tool = GrabbableTool("BisturiEletricoCaneta", "cautery-pen", "Bisturi elétrico", ToolType.Cautery,
                ToolCapability.Cauterize, new Vector3(x, y, z), lying, new Vector3(0f, 0f, -0.02f),
                new Vector3(0f, 0f, 0f), new Vector3(0.022f, 0.022f, 0.16f), out pen);
            Transform t = tool.transform;

            Material body = Paint("penBody", new Color(0.95f, 0.95f, 0.92f), 0f, 0.45f);
            Material cutButton = Paint("penCut", new Color(0.95f, 0.8f, 0.1f), 0f, 0.4f);
            Material coagButton = Paint("penCoag", new Color(0.2f, 0.45f, 0.9f), 0f, 0.4f);
            Material steel = Paint("steelBright", new Color(0.82f, 0.84f, 0.86f), 0.95f, 0.8f);

            Rod("Corpo", t, new Vector3(0f, 0f, -0.01f), 0.0055f, 0.13f, body, Quaternion.Euler(90f, 0f, 0f));
            Box("BotaoCorte", t, new Vector3(0f, 0.006f, 0.01f), new Vector3(0.005f, 0.003f, 0.01f), cutButton);
            Box("BotaoCoag", t, new Vector3(0f, 0.006f, -0.005f), new Vector3(0.005f, 0.003f, 0.01f), coagButton);
            Box("Ponteira", t, new Vector3(0f, 0f, 0.064f), new Vector3(0.0012f, 0.003f, 0.018f), steel);

            GameObject tip = new GameObject("PenTip");
            tip.transform.SetParent(t, false);
            tip.transform.localPosition = new Vector3(0f, 0f, 0.073f);
            penTip = tip.transform;

            // A short length of cable trailing from the back of the pen.
            GameObject cable = MeshPart("Cabo", t, TubeMesh(new[]
            {
                t.TransformPoint(new Vector3(0f, 0f, -0.075f)),
                t.TransformPoint(new Vector3(0.01f, -0.005f, -0.11f)),
                t.TransformPoint(new Vector3(0.03f, -0.012f, -0.15f)),
            }, 0.0022f, 6, 6), Paint("penCable", new Color(0.85f, 0.85f, 0.82f), 0f, 0.3f));
            cable.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            cable.transform.SetParent(null, true);
            cable.transform.SetParent(t, true);

            // The holster: a blue plastic sleeve on the drape the pen rests in.
            Box("Coldre", null, new Vector3(x, y - 0.012f, z - 0.01f), new Vector3(0.03f, 0.012f, 0.14f),
                Paint("holster", new Color(0.25f, 0.5f, 0.8f), 0f, 0.4f));

            // Bleeders on the retracted wound edges, alternating sides, and a scorch for each.
            GameObject field = GameObject.Find("CampoOperatorio");
            Transform parent = field != null ? field.transform : null;
            Material blood = Paint("bloodBead", new Color(0.45f, 0.02f, 0.03f), 0f, 0.95f);
            Material char_ = Paint("scorch", new Color(0.18f, 0.09f, 0.05f), 0f, 0.2f);

            List<Transform> bleeders = new List<Transform>();
            List<GameObject> scorches = new List<GameObject>();
            (float along, float side)[] spots = { (0.3f, -1f), (0.52f, 1f), (0.74f, -1f) };

            foreach ((float along, float side) in spots)
            {
                // Where the edge sits once retracted: the patch's own retraction at that point.
                float profile = Mathf.Pow(Mathf.Sin(along * Mathf.PI), 0.65f);
                Vector3 at = patch.IncisionPoint(along, side * 0.055f * profile, -0.008f);

                GameObject bleeder = Ball("Sangramento", parent, Vector3.zero, Vector3.one * 0.006f, blood);
                bleeder.transform.position = at;
                Ball("Jato", bleeder.transform, new Vector3(0f, -1.2f, 0f), new Vector3(0.5f, 2.2f, 0.5f), blood);
                bleeder.SetActive(false);
                bleeders.Add(bleeder.transform);

                GameObject scorch = Ball("Cauterizado", parent, Vector3.zero, new Vector3(0.007f, 0.0015f, 0.007f), char_);
                scorch.transform.position = at;
                scorch.SetActive(false);
                scorches.Add(scorch);
            }

            ParticleSystem smoke = BuildSmoke(parent);

            CauteryWorker worker = systems.AddComponent<CauteryWorker>();
            worker.Bind(penTip, pen, patch, bleeders, scorches, smoke);

            Debug.Log($"[Transplante] bisturi elétrico no coldre em {tool.transform.position}, {bleeders.Count} sangramento(s) na borda da ferida");
            return worker;
        }

        /// <summary>Soft grey smoke that rises and thins, fed by the cautery in puffs.</summary>
        /// <summary>
        /// What closing leaves behind, shown in the stages it belongs to: steel wires across the
        /// sternum while the bone comes together, two mediastinal drains out below the wound into
        /// the drainage canister on the table rail, and the dressing over the sutured incision
        /// once the operation is finished.
        /// </summary>
        private static void BuildClosureDetails(GameObject systems, TransplantProcedure procedure, ChestSkinPatch patch,
            GameObject sternum)
        {
            Material steel = Paint("steelBright", new Color(0.82f, 0.84f, 0.86f), 0.95f, 0.8f);
            GameObject field = GameObject.Find("CampoOperatorio");
            Transform fieldT = field != null ? field.transform : systems.transform;
            Transform cavity = field != null ? field.transform.Find("CavidadeToracica") : null;

            // ---- sternal wires: figure loops across the midline, twisted on top
            Bounds bone = WorldBounds(sternum);
            List<Renderer> wires = new List<Renderer>();
            for (int k = 0; k < 5; k++)
            {
                float z = Mathf.Lerp(bone.min.z + 0.025f, bone.max.z - 0.025f, k / 4f);
                Vector3 c = new Vector3(bone.center.x, bone.max.y + 0.002f, z);
                wires.Add(Tube("FioEsterno_" + k, cavity != null ? cavity : fieldT, new[]
                {
                    c + new Vector3(-0.016f, -0.006f, 0f), c + new Vector3(-0.012f, 0.001f, 0f),
                    c + new Vector3(0f, 0.0025f, 0.001f), c + new Vector3(0.012f, 0.001f, 0f),
                    c + new Vector3(0.016f, -0.006f, 0f),
                }, 0.0007f, steel).GetComponent<Renderer>());
                wires.Add(Tube("FioTorcido_" + k, cavity != null ? cavity : fieldT, new[]
                {
                    c + new Vector3(0f, 0.0025f, 0.001f), c + new Vector3(0.001f, 0.004f, -0.004f),
                    c + new Vector3(0f, 0.0045f, -0.008f),
                }, 0.0011f, steel).GetComponent<Renderer>());
            }

            // ---- mediastinal drains: out through the skin just below the wound, down to the canister
            float tableX = _window.Center.x;
            Vector3 canister = new Vector3(tableX - TableHalfWidth - 0.06f, TableTopY - 0.45f, _window.Center.z - 0.45f);
            GameObject box = new GameObject("FrascoDrenagem");
            box.transform.SetParent(fieldT, true);
            box.transform.position = canister;
            Box("Corpo", box.transform, Vector3.zero, new Vector3(0.07f, 0.24f, 0.2f), Glass(new Color(0.9f, 0.95f, 1f, 0.45f)));
            Box("SeloAgua", box.transform, new Vector3(0f, -0.07f, 0.06f), new Vector3(0.06f, 0.08f, 0.05f),
                Paint("waterSeal", new Color(0.35f, 0.6f, 0.95f), 0f, 0.8f), null, false);
            Box("Coleta", box.transform, new Vector3(0f, -0.09f, -0.04f), new Vector3(0.06f, 0.05f, 0.1f),
                Paint("bloodVenous", new Color(0.32f, 0.03f, 0.07f), 0f, 0.85f), null, false);
            Box("Gancho", box.transform, new Vector3(0.04f, 0.13f, 0f), new Vector3(0.012f, 0.03f, 0.15f), steel);

            List<Renderer> drains = new List<Renderer>();
            Vector3 low = patch.IncisionPoint(0f, 0f, 0f);
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 exit = low + new Vector3(side * 0.022f, 0f, -0.022f);
                exit.y = patch.SurfaceHeightUnder(exit) + 0.002f;
                Vector3 over = new Vector3(tableX - TableHalfWidth - 0.02f, TableTopY + 0.05f, exit.z - 0.1f);
                drains.Add(Tube("Dreno_" + (side < 0 ? "Esquerdo" : "Direito"), fieldT, new[]
                {
                    exit + new Vector3(0f, -0.01f, 0f), exit + new Vector3(0f, 0.012f, -0.01f),
                    new Vector3((exit.x + over.x) * 0.5f, exit.y + 0.02f, exit.z - 0.06f), over,
                    canister + new Vector3(0f, 0.14f, side * 0.03f),
                }, 0.0045f, Paint("drainTube", new Color(0.85f, 0.72f, 0.7f), 0f, 0.85f)).GetComponent<Renderer>());
            }

            // ---- the dressing: a film and a pad following the curve of the chest over the stitches
            Mesh Strip(float halfWidth, float lift, float extend)
            {
                List<Vector3> v = new List<Vector3>();
                List<int> t = new List<int>();
                const int rows = 24;
                for (int r = 0; r <= rows; r++)
                {
                    float a = Mathf.Lerp(-extend, 1f + extend, r / (float)rows);
                    float along = Mathf.Clamp01(a);
                    Vector3 zShift = new Vector3(0f, 0f, (a - along) * patch.IncisionLength);
                    v.Add(patch.IncisionPoint(along, -halfWidth, lift) + zShift);
                    v.Add(patch.IncisionPoint(along, halfWidth, lift) + zShift);
                    if (r == 0) { continue; }
                    int s0 = (r - 1) * 2;
                    t.Add(s0); t.Add(s0 + 2); t.Add(s0 + 1);
                    t.Add(s0 + 1); t.Add(s0 + 2); t.Add(s0 + 3);
                }

                Mesh m = new Mesh { name = "Curativo" };
                m.SetVertices(v);
                m.SetTriangles(t, 0);
                m.RecalculateNormals();
                m.RecalculateBounds();
                return m;
            }

            GameObject film = MeshPart("CurativoFilme", fieldT, Strip(0.034f, 0.0016f, 0.08f),
                Paint("dressingFilm", new Color(0.93f, 0.94f, 0.95f), 0f, 0.9f));
            GameObject pad = MeshPart("CurativoCompressa", fieldT, Strip(0.019f, 0.003f, 0.03f),
                Paint("dressingPad", new Color(0.98f, 0.98f, 0.96f), 0f, 0.15f));
            GameObject spot = Ball("CurativoMancha", fieldT, Vector3.zero, new Vector3(0.012f, 0.0012f, 0.02f),
                Paint("dressingSpot", new Color(0.72f, 0.3f, 0.3f), 0f, 0.2f));
            spot.transform.position = patch.IncisionPoint(0.62f, 0.003f, 0.0036f);
            film.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            pad.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            systems.AddComponent<StageVisibility>().Bind(procedure, new[] { TransplantStage.CloseSkin }, wires);
            systems.AddComponent<StageVisibility>().Bind(procedure,
                new[] { TransplantStage.CloseSkin, TransplantStage.Complete }, drains);
            systems.AddComponent<StageVisibility>().Bind(procedure, new[] { TransplantStage.Complete },
                new[] { film.GetComponent<Renderer>(), pad.GetComponent<Renderer>(), spot.GetComponent<Renderer>() });

            // Hidden in the saved scene too, not only from the first frame of play.
            foreach (Renderer r in wires) { r.enabled = false; }
            foreach (Renderer r in drains) { r.enabled = false; }
            film.GetComponent<Renderer>().enabled = false;
            pad.GetComponent<Renderer>().enabled = false;
            spot.GetComponent<Renderer>().enabled = false;

            Debug.Log($"[Transplante] fechamento: {wires.Count / 2} fios de aço, 2 drenos até o frasco em {canister}, curativo");
        }

        /// <summary>
        /// The "take this one" arrow: a fat cone pointing down with a short shaft and a halo,
        /// bright and unlit so it reads over any instrument. Hidden until there is something to
        /// point at.
        /// </summary>
        private static Transform BuildNextToolArrow()
        {
            GameObject arrow = new GameObject("SetaProximoInstrumento");
            Material bright = MakeUnlit(new Color(1f, 0.85f, 0.2f, 0.95f));
            bright.renderQueue = 3002;

            // Profile from the tip up: (radius, height).
            Mesh body = LatheMesh(new[]
            {
                new Vector2(0f, 0f), new Vector2(0.02f, 0.032f), new Vector2(0.008f, 0.032f),
                new Vector2(0.008f, 0.07f), new Vector2(0f, 0.07f),
            }, 20);
            MeshPart("Corpo", arrow.transform, body, bright);

            GameObject halo = MeshPart("Halo", arrow.transform, MakeRing(0.028f, 0.034f, 32), MakeUnlit(new Color(1f, 0.9f, 0.4f, 0.5f)));
            halo.transform.localPosition = new Vector3(0f, -0.045f, 0f);

            arrow.SetActive(false);
            return arrow.transform;
        }

        /// <summary>
        /// A solid of revolution about +Y from a profile of (radius, height) points, smooth-shaded.
        /// The shape every turned object has: handles, knobs, bottles, lamp heads, cones.
        /// </summary>
        private static Mesh LatheMesh(IList<Vector2> profile, int segments)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            int rings = profile.Count;

            for (int s = 0; s <= segments; s++)
            {
                float a = s / (float)segments * Mathf.PI * 2f;
                float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
                for (int r = 0; r < rings; r++)
                {
                    vertices.Add(new Vector3(profile[r].x * cos, profile[r].y, profile[r].x * sin));
                }
            }

            for (int s = 0; s < segments; s++)
            {
                for (int r = 0; r < rings - 1; r++)
                {
                    int a = s * rings + r, b = a + 1, c = a + rings, d = c + 1;
                    triangles.Add(a); triangles.Add(b); triangles.Add(c);
                    triangles.Add(b); triangles.Add(d); triangles.Add(c);
                }
            }

            Mesh mesh = new Mesh { name = "Torneado" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Between the shoulders of a visitor standing at the stance: the middle of what they can reach.</summary>
        private static Vector3 ReachCentre() => Stance(_thorax) + new Vector3(0f, 1.3f, 0f);

        /// <summary>
        /// Brings the table to each visitor (recentring and short mode) and marks where to stand:
        /// two footprints on the floor, the way every stand-in-place VR game shows its spot.
        /// </summary>
        private static void WireVisitorFit(GameObject systems, EventSessionController session)
        {
            Vector3 stance = Stance(_thorax);
            Vector3 toPatient = new Vector3(_thorax.x - stance.x, 0f, _thorax.z - stance.z).normalized;
            Quaternion facing = Quaternion.LookRotation(toPatient, Vector3.up);

            GameObject point = new GameObject("PosicaoCirurgiao");
            point.transform.SetParent(systems.transform, true);
            point.transform.SetPositionAndRotation(stance, facing);

            GameObject rig = GameObject.Find("XR Origin");
            Camera head = rig != null ? rig.GetComponentInChildren<Camera>(true) : null;
            if (rig != null && head != null)
            {
                // Not ??: a missing component is a fake null in the Editor, which ?? does not see.
                VisitorFit fit = rig.GetComponent<VisitorFit>();
                if (fit == null) { fit = rig.AddComponent<VisitorFit>(); }
                fit.Bind(rig.transform, head.transform, point.transform, session);
            }
            else
            {
                Debug.LogWarning("[Transplante] sem XR Origin com câmera: o ajuste ao visitante não foi ligado.");
            }

            Material mark = MakeUnlit(new Color(0.35f, 0.95f, 0.65f, 0.55f));
            Mesh foot = FootMesh();
            for (int side = -1; side <= 1; side += 2)
            {
                GameObject print = MeshPart(side < 0 ? "PeEsquerdo" : "PeDireito", point.transform, foot, mark);
                print.transform.localPosition = new Vector3(side * 0.11f, 0.003f, -0.02f);
                print.transform.localRotation = Quaternion.Euler(90f, side * 6f, 0f);
            }

            GameObject ring = MeshPart("AnelPosicao", point.transform, MakeRing(0.33f, 0.35f, 48), mark);
            ring.transform.localPosition = new Vector3(0f, 0.002f, 0f);
        }

        /// <summary>A flat footprint facing +Y after the part's 90° tilt: sole and heel, toes forward (+Y in mesh space).</summary>
        private static Mesh FootMesh()
        {
            List<Vector3> vertices = new List<Vector3> { new Vector3(0f, 0.12f, 0f) };
            List<int> triangles = new List<int>();
            const int n = 28;
            for (int i = 0; i <= n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                float y = Mathf.Sin(a);
                // Wider at the ball of the foot than at the heel.
                float width = y > 0f ? 0.05f : 0.038f;
                vertices.Add(new Vector3(Mathf.Cos(a) * width, 0.12f + y * 0.125f, 0f));
                if (i == 0) { continue; }
                // Faces -Z in mesh space, which the part's 90° tilt turns to face up.
                triangles.Add(0); triangles.Add(i); triangles.Add(i + 1);
            }

            Mesh mesh = new Mesh { name = "Pegada" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// The pericardium: a glistening cap over the heart in two halves hinged at its outer
        /// edges, with a dashed line down the middle to open it on and the slit the pen leaves.
        /// Parented to the cavity, so it shows only once the chest is open.
        /// </summary>
        private static PericardiumWorker BuildPericardium(GameObject systems, GameObject heart, TransplantProcedure procedure,
            Transform penTip, SurgicalInteractable pen, ParticleSystem smoke)
        {
            Bounds b = WorldBounds(heart);
            float rx = Mathf.Clamp(b.extents.x * 1.1f, 0.035f, _window.HalfWidth * 0.9f);
            float rz = Mathf.Clamp(b.extents.z * 1.1f, 0.045f, _window.IncisionHalfLength);
            float sag = Mathf.Clamp(b.extents.y * 0.6f, 0.015f, 0.04f);
            float top = b.max.y + 0.008f;
            Vector3 centre = new Vector3(b.center.x, top, b.center.z);

            GameObject field = GameObject.Find("CampoOperatorio");
            Transform cavity = field != null ? field.transform.Find("CavidadeToracica") : null;
            Transform parent = cavity != null ? cavity : systems.transform;

            GameObject root = new GameObject("Pericardio");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(centre, Quaternion.identity);

            Material membrane = DoubleSided(Paint("pericardium", new Color(0.86f, 0.8f, 0.68f), 0f, 0.82f));
            float Surface(float x, float z)
            {
                float dx = (x - centre.x) / rx, dz = (z - centre.z) / rz;
                return top - sag * (dx * dx + dz * dz);
            }

            Transform[] halves = new Transform[2];
            for (int k = 0; k < 2; k++)
            {
                float side = k == 0 ? -1f : 1f;
                Vector3 pivot = new Vector3(centre.x + side * rx, top - sag, centre.z);

                const int cols = 7, rows = 13;
                List<Vector3> vertices = new List<Vector3>();
                List<int> triangles = new List<int>();
                for (int r = 0; r < rows; r++)
                {
                    float v = -1f + 2f * r / (rows - 1);
                    float w = Mathf.Max(0.002f, rx * Mathf.Sqrt(Mathf.Max(0f, 1f - v * v)));
                    float z = centre.z + v * rz;
                    for (int c = 0; c < cols; c++)
                    {
                        float x = centre.x + side * (c / (float)(cols - 1)) * w;
                        vertices.Add(new Vector3(x, Surface(x, z), z) - pivot);
                    }
                }

                for (int r = 0; r < rows - 1; r++)
                {
                    for (int c = 0; c < cols - 1; c++)
                    {
                        int a = r * cols + c, bb = a + 1, d = a + cols, e = d + 1;
                        triangles.Add(a); triangles.Add(d); triangles.Add(bb);
                        triangles.Add(bb); triangles.Add(d); triangles.Add(e);
                    }
                }

                Mesh mesh = new Mesh { name = "PericardioMetade" };
                mesh.SetVertices(vertices);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();

                GameObject half = MeshPart(side < 0f ? "PericardioEsquerdo" : "PericardioDireito", root.transform, mesh, membrane, true);
                half.transform.SetPositionAndRotation(pivot, Quaternion.identity);
                halves[k] = half.transform;
            }

            GameObject slit = MeshPart("PericardioCorte", root.transform, null, Paint("cutLine", new Color(0.3f, 0.02f, 0.03f), 0f, 0.85f));
            slit.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            PericardiumWorker worker = systems.AddComponent<PericardiumWorker>();
            worker.Shape(centre, rx, rz, top, sag);

            // The dashed line to open along, drawn from the worker's own idea of the line.
            List<Vector3> dashVertices = new List<Vector3>();
            List<int> dashTriangles = new List<int>();
            const int dashes = 12;
            for (int i = 0; i < dashes; i++)
            {
                Vector3 a = worker.LinePoint(i / (float)dashes, 0.0012f);
                Vector3 c = worker.LinePoint((i + 0.55f) / dashes, 0.0012f);
                int s0 = dashVertices.Count;
                dashVertices.Add(a + Vector3.left * 0.0011f);
                dashVertices.Add(a + Vector3.right * 0.0011f);
                dashVertices.Add(c + Vector3.left * 0.0011f);
                dashVertices.Add(c + Vector3.right * 0.0011f);
                dashTriangles.Add(s0); dashTriangles.Add(s0 + 2); dashTriangles.Add(s0 + 1);
                dashTriangles.Add(s0 + 1); dashTriangles.Add(s0 + 2); dashTriangles.Add(s0 + 3);
            }

            Mesh dashMesh = new Mesh { name = "PericardioGuia" };
            dashMesh.SetVertices(dashVertices);
            dashMesh.SetTriangles(dashTriangles, 0);
            dashMesh.RecalculateNormals();
            dashMesh.RecalculateBounds();
            GameObject guide = MeshPart("PericardioGuia", root.transform, dashMesh, Glow(new Color(0.48f, 0.12f, 0.62f)));
            guide.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            guide.GetComponent<MeshRenderer>().enabled = false;

            worker.Bind(penTip, pen, procedure, halves[0], halves[1], guide.GetComponent<MeshRenderer>(),
                slit.GetComponent<MeshFilter>(), smoke);

            Debug.Log($"[Transplante] pericárdio de {rx * 200f:F0} x {rz * 200f:F0} cm sobre o coração, topo em y={top:F3}");
            return worker;
        }

        /// <summary>
        /// Internal defibrillator paddles: two insulated handles, steel shafts and cupped spoons
        /// that hold the heart between them. Joined at the grip so one hand can work them — real
        /// paddles are two separate handles, which a single controller cannot hold. Resting on
        /// the drape beyond the cautery holster, spoons toward the head.
        /// </summary>
        private static GameObject BuildInternalPaddles(out Transform centre, out SurgicalInteractable interactable)
        {
            float x = _window.Center.x + _window.HalfWidth + 0.14f;
            float z = _window.Center.z + 0.06f;
            float y = SkinTopAt(x, z) + 0.03f;

            GameObject tool = GrabbableTool("PasInternas", "internal-paddles", "Pás internas", ToolType.Defibrillator,
                ToolCapability.None, new Vector3(x, y, z), Quaternion.LookRotation(Vector3.forward, Vector3.up),
                new Vector3(0f, 0f, -0.09f), Vector3.zero, new Vector3(0.1f, 0.05f, 0.27f), out interactable);
            Transform t = tool.transform;

            Material grip = Paint("paddleGrip", new Color(0.12f, 0.13f, 0.15f), 0f, 0.35f);
            Material steel = Paint("steelBright", new Color(0.82f, 0.84f, 0.86f), 0.95f, 0.8f);
            Material button = Paint("paddleButton", new Color(0.95f, 0.45f, 0.1f), 0f, 0.4f);
            Material cable = Paint("paddleCable", new Color(0.9f, 0.9f, 0.88f), 0f, 0.3f);

            for (int side = -1; side <= 1; side += 2)
            {
                Rod("Cabo_" + side, t, new Vector3(side * 0.011f, 0f, -0.08f), 0.008f, 0.09f, grip, Quaternion.Euler(90f, 0f, 0f));

                GameObject shaft = MeshPart("Haste_" + side, t, TubeMesh(new[]
                {
                    t.TransformPoint(new Vector3(side * 0.011f, 0f, -0.035f)),
                    t.TransformPoint(new Vector3(side * 0.02f, 0f, 0.01f)),
                    t.TransformPoint(new Vector3(side * 0.038f, 0f, 0.07f)),
                }, 0.0025f, 8, 6), steel, true);
                shaft.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                shaft.transform.SetParent(null, true);
                shaft.transform.SetParent(t, true);

                // The spoon: a shallow cup facing the other one, the heart goes between.
                Ball("Colher_" + side, t, new Vector3(side * 0.042f, 0f, 0.09f), new Vector3(0.008f, 0.045f, 0.05f), steel);

                GameObject lead = MeshPart("Fio_" + side, t, TubeMesh(new[]
                {
                    t.TransformPoint(new Vector3(side * 0.011f, 0f, -0.125f)),
                    t.TransformPoint(new Vector3(side * 0.02f, -0.01f, -0.17f)),
                    t.TransformPoint(new Vector3(side * 0.05f, -0.03f, -0.22f)),
                }, 0.0025f, 6, 6), cable);
                lead.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                lead.transform.SetParent(null, true);
                lead.transform.SetParent(t, true);
            }

            Box("BotaoChoque", t, new Vector3(0.011f, 0.009f, -0.07f), new Vector3(0.006f, 0.003f, 0.012f), button);

            GameObject middle = new GameObject("CentroPas");
            middle.transform.SetParent(t, false);
            middle.transform.localPosition = new Vector3(0f, 0f, 0.09f);
            centre = middle.transform;

            Debug.Log($"[Transplante] pás internas do desfibrilador sobre o campo em {tool.transform.position}");
            return tool;
        }

        private static ParticleSystem BuildSmoke(Transform parent)
        {
            GameObject go = new GameObject("FumacaCauterio");
            go.transform.SetParent(parent, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.015f, 0.05f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.02f);
            main.startColor = new Color(0.86f, 0.86f, 0.84f, 0.4f);
            main.gravityModifier = -0.03f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 80;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.003f;

            ParticleSystem.ColorOverLifetimeModule colour = ps.colorOverLifetime;
            colour.enabled = true;
            Gradient fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.8f, 0.8f, 0.8f), 1f) },
                new[] { new GradientAlphaKey(0.5f, 0f), new GradientAlphaKey(0.3f, 0.4f), new GradientAlphaKey(0f, 1f) });
            colour.color = fade;

            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.2f));

            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.015f;
            noise.frequency = 1.2f;

            Material smokeMaterial = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "OR_Smoke" };
            smokeMaterial.SetTexture("_BaseMap", SoftDot());
            smokeMaterial.SetFloat("_Surface", 1f);
            smokeMaterial.SetFloat("_Blend", 0f);
            smokeMaterial.SetOverrideTag("RenderType", "Transparent");
            smokeMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            smokeMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            smokeMaterial.SetInt("_ZWrite", 0);
            smokeMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            smokeMaterial.renderQueue = 3000;

            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = smokeMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        /// <summary>A round, soft-edged dot for smoke particles.</summary>
        private static Texture2D SoftDot()
        {
            const int size = 64;
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a * a);
                }
            }

            EnsureAssetFolder(SurfaceFolder);
            return WritePng(SurfaceFolder + "/SoftDot.png", size, pixels, false);
        }

        /// <summary>Top of the Mayo tray for this build, for instruments placed after it.</summary>
        private static Vector3 _trayTop;

        /// <summary>
        /// The sternal saw: a pistol-grip body with a reciprocating blade pointing down at the
        /// front and a foot plate under it, the way the real one is set into the sternal notch.
        /// Stood on its grip on the tray. Its cutting point is the bottom of the blade.
        /// </summary>
        private static GameObject BuildSternalSaw(Vector3 position, Quaternion rotation, out Transform bladeTip,
            out SurgicalInteractable interactable)
        {
            Vector3 grip = new Vector3(0f, -0.045f, -0.035f);
            GameObject tool = GrabbableTool("SerraEsternal", "sternal-saw", "Serra esternal", ToolType.Retractor, ToolCapability.Cut,
                position, rotation, grip, new Vector3(0f, -0.01f, 0.02f), new Vector3(0.05f, 0.14f, 0.16f), out interactable);
            Transform t = tool.transform;

            Material body = Paint("sawBody", new Color(0.22f, 0.3f, 0.42f), 0.3f, 0.55f);
            Material rubber = Paint("sawGrip", new Color(0.08f, 0.08f, 0.09f), 0f, 0.3f);
            Material steel = Paint("steelBright", new Color(0.82f, 0.84f, 0.86f), 0.95f, 0.8f);
            Material trigger = Paint("sawTrigger", new Color(0.85f, 0.2f, 0.12f), 0f, 0.4f);

            Box("Corpo", t, new Vector3(0f, 0.03f, 0.02f), new Vector3(0.045f, 0.06f, 0.15f), body);
            Box("Nariz", t, new Vector3(0f, 0.012f, 0.1f), new Vector3(0.03f, 0.03f, 0.03f), body);
            Box("Empunhadura", t, new Vector3(0f, -0.045f, -0.035f), new Vector3(0.034f, 0.1f, 0.04f), rubber,
                Quaternion.Euler(15f, 0f, 0f));
            Box("Gatilho", t, new Vector3(0f, -0.018f, -0.008f), new Vector3(0.012f, 0.025f, 0.01f), trigger);
            Box("Lamina", t, new Vector3(0f, -0.016f, 0.1f), new Vector3(0.0018f, 0.045f, 0.012f), steel);
            Box("Sapata", t, new Vector3(0f, -0.038f, 0.104f), new Vector3(0.012f, 0.004f, 0.032f), steel);

            GameObject tip = new GameObject("BladeTip");
            tip.transform.SetParent(t, false);
            tip.transform.localPosition = new Vector3(0f, -0.036f, 0.1f);
            bladeTip = tip.transform;

            return tool;
        }

        /// <summary>Moves the donor basin from its pedestal behind the surgeon onto the Mayo tray.</summary>
        private static void MoveDonorToTray(Vector3 trayTop)
        {
            GameObject stand = GameObject.Find("DonorStand");
            if (stand == null) { return; }

            Transform column = stand.transform.Find("StandColumn");
            if (column != null) { Object.DestroyImmediate(column.gameObject); }

            // Basin sits at 0.92 above the stand origin; put the origin so the basin rests on the tray.
            Vector3 basinSpot = trayTop + new Vector3(-0.07f, 0f, 0.06f);
            stand.transform.position = basinSpot - new Vector3(0f, 0.89f, 0f);

            Debug.Log($"[Transplante] coração doador na bacia sobre a mesa de Mayo em {basinSpot}");
        }

        private static GameObject GrabbableTool(string name, string id, string label, ToolType type, ToolCapability capability,
            Vector3 position, Quaternion rotation, Vector3 gripLocal, Vector3 boxCentre, Vector3 boxSize, out SurgicalInteractable interactable)
        {
            GameObject tool = new GameObject(name);
            tool.transform.SetPositionAndRotation(position, rotation);

            BoxCollider box = tool.AddComponent<BoxCollider>();
            box.center = boxCentre;
            box.size = boxSize;

            Rigidbody body = tool.AddComponent<Rigidbody>();
            body.mass = 0.08f;
            body.useGravity = false;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            GameObject grip = new GameObject("GripPoint");
            grip.transform.SetParent(tool.transform, false);
            grip.transform.localPosition = gripLocal;

            ToolDefinition definition = ToolDefinition.Create(id, label, type, capability);
            interactable = tool.AddComponent<SurgicalInteractable>();
            SetPrivateField(interactable, "toolDefinition", definition);
            SetPrivateField(interactable, "gripPoint", grip.transform);

            XRGrabInteractable grab = tool.AddComponent<XRGrabInteractable>();
            grab.attachTransform = grip.transform;
            grab.useDynamicAttach = false;

            // Job Simulator rules: let go and it falls where it is, toss it and it flies; only if
            // it ends up out of reach does it pop back onto the tray.
            grab.throwOnDetach = true;
            grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;

            // Lights up while a hand is close enough to take it.
            tool.AddComponent<GrabGlow>();

            ReturnHomeOnRelease release = tool.AddComponent<ReturnHomeOnRelease>();
            release.Bind(interactable);
            release.UseDropAndRespawn(ReachCentre(), 0.85f, TableTopY - 0.3f);
            return tool;
        }

        /// <summary>The real scalpel model, aligned so its blade points along +Z, with a tip at the belly of the blade.</summary>
        private static GameObject BuildScalpel(Vector3 position, Quaternion rotation, out Transform tip, out SurgicalInteractable interactable,
            out bool bladeKnown, out Renderer bladeBlood)
        {
            bladeKnown = false;
            bladeBlood = null;
            Material blood = Paint("bladeBlood", new Color(0.33f, 0.01f, 0.02f), 0f, 0.92f);
            const float gripZ = -0.02f;
            GameObject tool = GrabbableTool("Bisturi", "scalpel", "Bisturi", ToolType.Scalpel, ToolCapability.Cut,
                position, rotation, new Vector3(0f, 0f, gripZ), new Vector3(0f, 0f, gripZ - 0.005f),
                new Vector3(0.024f, 0.024f, 0.13f), out interactable);

            float tipZ = 0.055f;
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(ScalpelFbx);
            if (asset != null)
            {
                ModelImporter importer = AssetImporter.GetAtPath(ScalpelFbx) as ModelImporter;
                if (importer != null && !importer.isReadable)
                {
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                }

                GameObject mesh = Instantiate(ScalpelFbx, tool.transform);
                mesh.name = "ScalpelMesh";
                mesh.transform.localPosition = new Vector3(0f, 0f, gripZ);
                tipZ = AlignForward(mesh, "filo", tipZ);
                bladeKnown = RollBladeUpright(mesh, "filo");
                bladeBlood = BloodCoat(mesh, "filo", tool.transform, tipZ, blood);

                Material m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(ScalpelAlbedo);
                Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(ScalpelNormal);
                Texture2D metallic = AssetDatabase.LoadAssetAtPath<Texture2D>(ScalpelMetallic);
                if (albedo != null) { m.SetTexture("_BaseMap", albedo); }
                if (normal != null) { m.SetTexture("_BumpMap", normal); m.EnableKeyword("_NORMALMAP"); }
                if (metallic != null) { m.SetTexture("_MetallicGlossMap", metallic); m.EnableKeyword("_METALLICSPECGLOSSMAP"); }
                m.SetFloat("_Smoothness", 0.8f);
                foreach (MeshRenderer renderer in mesh.GetComponentsInChildren<MeshRenderer>()) { renderer.sharedMaterial = m; }
            }
            else
            {
                // No model: a handle and a blade, enough to operate with.
                Material steel = Paint("steelBright", new Color(0.82f, 0.84f, 0.86f), 0.95f, 0.8f);
                Box("Cabo", tool.transform, new Vector3(0f, 0f, -0.03f), new Vector3(0.003f, 0.008f, 0.12f), steel);
                Box("Lamina", tool.transform, new Vector3(0f, -0.002f, 0.045f), new Vector3(0.0015f, 0.008f, 0.03f), steel);

                // Blade in the tool's Y-Z plane by construction, so its face is the tool's X.
                bladeKnown = true;
                GameObject coat = Box("SangueLamina", tool.transform, new Vector3(0f, -0.002f, 0.052f),
                    new Vector3(0.0019f, 0.0083f, 0.016f), blood, null, false);
                bladeBlood = coat.GetComponent<Renderer>();
            }

            if (bladeBlood != null) { bladeBlood.enabled = false; }

            GameObject tipObject = new GameObject("BladeTip");
            tipObject.transform.SetParent(tool.transform, false);
            tipObject.transform.localPosition = new Vector3(0f, 0f, tipZ);
            tip = tipObject.transform;

            Debug.Log($"[Transplante] bisturi com ponta a {(tipZ - gripZ) * 100f:F1} cm da empunhadura; " +
                      (bladeKnown ? "lâmina de pé no plano Y-Z (o fio conta)" : "orientação da lâmina desconhecida (o fio não conta)"));
            return tool;
        }

        /// <summary>
        /// Rolls the scalpel model about its length so the blade stands in the tool's Y-Z plane,
        /// belly down: held naturally, the edge then meets the skin, and the incision worker can
        /// tell a blade on its edge from one laid flat by reading the tool's X axis.
        ///
        /// The blade's face is the direction the named part is thinnest across (the smallest
        /// principal axis of its vertices seen down the blade). Returns false, leaving the model
        /// as it was, when the part is missing or not clearly flat — a guess here would make the
        /// scalpel refuse to cut when held correctly.
        /// </summary>
        private static bool RollBladeUpright(GameObject meshRoot, string partName)
        {
            MeshFilter filter = FindPartFilter(meshRoot, partName);
            if (filter == null) { return false; }

            Transform tool = meshRoot.transform.parent;
            Vector3[] vertices = filter.sharedMesh.vertices;
            if (vertices.Length < 3) { return false; }

            Vector2 mean = Vector2.zero;
            Vector2[] points = new Vector2[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 local = tool.InverseTransformPoint(filter.transform.TransformPoint(vertices[i]));
                points[i] = new Vector2(local.x, local.y);
                mean += points[i];
            }

            mean /= points.Length;
            double a = 0, b = 0, c = 0;
            foreach (Vector2 p in points)
            {
                Vector2 d = p - mean;
                a += d.x * d.x; b += d.x * d.y; c += d.y * d.y;
            }

            double half = (a - c) * 0.5, radius = System.Math.Sqrt(half * half + b * b);
            double smallest = (a + c) * 0.5 - radius, largest = (a + c) * 0.5 + radius;
            if (largest <= 1e-14 || smallest / largest > 0.25)
            {
                Debug.LogWarning($"[Transplante] parte '{partName}' do bisturi não é claramente chata; lâmina deixada como está.");
                return false;
            }

            Vector2 face = System.Math.Abs(b) > 1e-14
                ? new Vector2((float)b, (float)(smallest - a)).normalized
                : (a < c ? Vector2.right : Vector2.up);

            // Belly down: the blade's bulk lies below the line from the handle to the tip.
            Vector2 inPlane = new Vector2(-face.y, face.x);
            if (Vector2.Dot(mean, inPlane) > 0f) { face = -face; }

            float angle = -Mathf.Atan2(face.y, face.x) * Mathf.Rad2Deg;
            meshRoot.transform.localRotation = Quaternion.AngleAxis(angle, Vector3.forward) * meshRoot.transform.localRotation;
            Debug.Log($"[Transplante] lâmina do bisturi girada {angle:F0}° para ficar de pé");
            return true;
        }

        /// <summary>
        /// A film of blood over the working end of the blade, shown from the first cut: the blade
        /// part's own triangles near the tip, pushed out a fraction of a millimetre along their
        /// normals so the film hugs the steel exactly instead of floating as a red box.
        /// </summary>
        private static Renderer BloodCoat(GameObject meshRoot, string partName, Transform tool, float tipZ, Material blood)
        {
            MeshFilter filter = FindPartFilter(meshRoot, partName);
            if (filter == null) { return null; }

            Mesh source = filter.sharedMesh;
            Vector3[] vertices = source.vertices;
            Vector3[] normals = source.normals;
            int[] triangles = source.triangles;

            List<Vector3> coatVertices = new List<Vector3>();
            List<int> coatTriangles = new List<int>();
            Dictionary<int, int> remap = new Dictionary<int, int>();
            const float reach = 0.025f, film = 0.00025f;

            Vector3 ToTool(int index)
            {
                Vector3 p = tool.InverseTransformPoint(filter.transform.TransformPoint(vertices[index]));
                if (normals != null && normals.Length == vertices.Length)
                {
                    Vector3 n = tool.InverseTransformDirection(filter.transform.TransformDirection(normals[index])).normalized;
                    p += n * film;
                }

                return p;
            }

            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                bool near = false;
                for (int k = 0; k < 3; k++)
                {
                    float z = tool.InverseTransformPoint(filter.transform.TransformPoint(vertices[triangles[t + k]])).z;
                    if (z > tipZ - reach) { near = true; }
                }

                if (!near) { continue; }

                for (int k = 0; k < 3; k++)
                {
                    int index = triangles[t + k];
                    if (!remap.TryGetValue(index, out int mapped))
                    {
                        mapped = coatVertices.Count;
                        coatVertices.Add(ToTool(index));
                        remap[index] = mapped;
                    }

                    coatTriangles.Add(mapped);
                }
            }

            if (coatTriangles.Count == 0) { return null; }

            Mesh coat = new Mesh { name = "SangueLamina" };
            coat.SetVertices(coatVertices);
            coat.SetTriangles(coatTriangles, 0);
            coat.RecalculateNormals();
            coat.RecalculateBounds();

            GameObject go = MeshPart("SangueLamina", tool, coat, blood);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            return go.GetComponent<Renderer>();
        }

        private static MeshFilter FindPartFilter(GameObject meshRoot, string partName)
        {
            foreach (Transform t in meshRoot.GetComponentsInChildren<Transform>())
            {
                if (t.name != partName) { continue; }
                MeshFilter filter = t.GetComponent<MeshFilter>();
                return filter != null && filter.sharedMesh != null && filter.sharedMesh.isReadable ? filter : null;
            }

            return null;
        }

        /// <summary>Rotates a tool mesh so the named part's farthest vertex lies on +Z; returns that Z in tool space.</summary>
        private static float AlignForward(GameObject meshRoot, string partName, float fallback)
        {
            Transform part = null;
            foreach (Transform t in meshRoot.GetComponentsInChildren<Transform>())
            {
                if (t.name == partName) { part = t; break; }
            }

            MeshFilter filter = part != null ? part.GetComponent<MeshFilter>() : null;
            if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable)
            {
                Debug.LogWarning($"[Transplante] modelo do bisturi sem parte '{partName}' legível; usando comprimento padrão.");
                return fallback;
            }

            meshRoot.transform.localRotation = Quaternion.identity;
            Vector3 far = Vector3.zero;
            float best = -1f;
            foreach (Vector3 v in filter.sharedMesh.vertices)
            {
                Vector3 local = meshRoot.transform.InverseTransformPoint(part.TransformPoint(v));
                if (local.magnitude > best) { best = local.magnitude; far = local; }
            }

            meshRoot.transform.localRotation = Quaternion.FromToRotation(far.normalized, Vector3.forward);

            Transform tool = meshRoot.transform.parent;
            float tipZ = float.MinValue;
            foreach (Vector3 v in filter.sharedMesh.vertices)
            {
                float z = tool.InverseTransformPoint(part.TransformPoint(v)).z;
                if (z > tipZ) { tipZ = z; }
            }

            return tipZ;
        }

        /// <summary>
        /// A Mayo-Hegar needle holder with a curved 3/8 needle and a trailing thread. Built from
        /// primitives: there is no needle holder model in the project.
        /// </summary>
        private static GameObject BuildNeedleHolder(Vector3 position, Quaternion rotation, out Transform needleTip,
            out SurgicalInteractable interactable)
        {
            GameObject tool = GrabbableTool("PortaAgulha", "needle-holder", "Porta-agulha", ToolType.NeedleHolder, ToolCapability.Suture,
                position, rotation, new Vector3(0f, 0f, -0.03f), new Vector3(0f, 0f, 0.0f), new Vector3(0.04f, 0.02f, 0.16f), out interactable);

            Material steel = Paint("steelBright", new Color(0.82f, 0.84f, 0.86f), 0.95f, 0.8f);
            Material gold = Paint("gold", new Color(0.85f, 0.68f, 0.25f), 0.9f, 0.7f);
            Transform t = tool.transform;

            for (int side = -1; side <= 1; side += 2)
            {
                Box("Haste_" + side, t, new Vector3(side * 0.005f, 0f, -0.01f), new Vector3(0.004f, 0.004f, 0.11f), steel,
                    Quaternion.Euler(0f, -side * 2.5f, 0f));

                GameObject ring = MeshPart("Argola_" + side, t, MakeRing(0.007f, 0.011f, 20), steel, true);
                ring.transform.localPosition = new Vector3(side * 0.014f, 0f, -0.07f);
            }

            Box("Trava", t, new Vector3(0f, 0f, 0.045f), new Vector3(0.01f, 0.006f, 0.008f), steel);
            // Gold handles: the tungsten-carbide jaws every needle holder is recognised by.
            Box("Mandibula", t, new Vector3(0f, 0f, 0.062f), new Vector3(0.006f, 0.005f, 0.028f), gold);

            // The needle: a 3/8 circle held at its middle by the jaw tip, curving down.
            const float r = 0.009f;
            Vector3 centre = new Vector3(0f, -r, 0.076f);
            List<Vector3> arc = new List<Vector3>();
            for (int i = 0; i <= 12; i++)
            {
                float a = Mathf.Lerp(-40f, 95f, i / 12f) * Mathf.Deg2Rad;
                arc.Add(t.TransformPoint(centre + new Vector3(Mathf.Sin(a) * r, Mathf.Cos(a) * r, 0f)));
            }

            GameObject needle = MeshPart("Agulha", t, TubeMesh(arc, 0.0006f, 6, 2), steel);
            needle.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            needle.transform.SetParent(null, true);
            needle.transform.SetParent(t, true);

            GameObject tipObject = new GameObject("NeedleTip");
            tipObject.transform.SetParent(t, false);
            tipObject.transform.position = arc[arc.Count - 1];
            needleTip = tipObject.transform;

            // Thread trailing from the swaged end.
            Vector3 swage = arc[0];
            GameObject thread = MeshPart("Fio", t, TubeMesh(new[]
            {
                swage,
                swage + rotation * new Vector3(0.01f, -0.01f, -0.02f),
                swage + rotation * new Vector3(0.02f, -0.03f, -0.05f),
                swage + rotation * new Vector3(0.015f, -0.05f, -0.08f),
            }, 0.0005f, 5, 6), Paint("nylon", new Color(0.05f, 0.08f, 0.2f), 0f, 0.6f));
            thread.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            thread.transform.SetParent(null, true);
            thread.transform.SetParent(t, true);

            return tool;
        }

        // ------------------------------------------------------------------ skin stages

        /// <summary>
        /// The incision and the suture, and everything that shows them: purple guide line, cut
        /// line, beads of blood, stitch marks and tied stitches. Returns the workers so the caller
        /// can wire them into the round, the monitor and the feedback.
        /// </summary>
        private static void BuildSkinStages(GameObject systems, TransplantProcedure procedure, GameObject sternum,
            Vector3 thorax, out SkinIncisionWorker incision, out SutureWorker suture, out ChestSkinPatch patch,
            out SurgicalInteractable scalpel, out SurgicalInteractable needleHolder, out StageResultPopup popup)
        {
            EnsureLayer(OperatorLayer);
            GameObject field = new GameObject("CampoOperatorio");
            patch = BuildChestPatch(field.transform, thorax, out GameObject cavity);
            BuildDrapes(field.transform);

            SternotomyController sternotomy = sternum.GetComponent<SternotomyController>();
            BuildRetractor(field.transform, sternotomy, sternum);

            Transform stand = BuildMayoStand(thorax, out Vector3 trayTop);
            MoveDonorToTray(trayTop);

            // Handles toward the surgeon, working ends away, on the near half of the tray.
            Quaternion away = Quaternion.LookRotation(Vector3.left, Vector3.up);
            GameObject scalpelTool = BuildScalpel(trayTop + new Vector3(0.12f, 0.012f, 0.05f), away, out Transform blade, out scalpel,
                out bool bladeKnown, out Renderer bladeBlood);
            GameObject holder = BuildNeedleHolder(trayTop + new Vector3(0.12f, 0.014f, -0.04f), away, out Transform needle, out needleHolder);

            // ---- incision visuals
            GameObject cut = MeshPart("IncisaoCorte", field.transform, null,
                Paint("cutLine", new Color(0.3f, 0.02f, 0.03f), 0f, 0.85f));
            cut.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            GameObject guide = MeshPart("IncisaoGuia", field.transform, GuideMesh(patch), Glow(new Color(0.48f, 0.12f, 0.62f)));
            guide.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            Material blood = Paint("bloodBead", new Color(0.45f, 0.02f, 0.03f), 0f, 0.95f);
            List<GameObject> beads = new List<GameObject>();
            System.Random random = new System.Random(5);
            for (int i = 0; i < 28; i++)
            {
                float along = (i + 0.5f) / 28f;
                float lateral = ((float)random.NextDouble() - 0.5f) * 0.004f;
                float size = 0.0025f + (float)random.NextDouble() * 0.0025f;
                GameObject bead = Ball("Gota_" + i, field.transform, Vector3.zero, new Vector3(size, size * 0.5f, size * 1.3f), blood);
                bead.transform.position = patch.IncisionPoint(along, lateral, 0.0015f);
                bead.SetActive(false);
                beads.Add(bead);
            }

            incision = systems.AddComponent<SkinIncisionWorker>();
            incision.Bind(blade, scalpel, patch, procedure, cut.GetComponent<MeshFilter>(),
                guide.GetComponent<MeshRenderer>(), beads);

            // Nicks off the line stay on the skin, and the blade comes away bloody.
            GameObject scratches = MeshPart("IncisaoArranhoes", field.transform, null,
                Paint("scratch", new Color(0.42f, 0.03f, 0.04f), 0f, 0.7f));
            scratches.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            scratches.GetComponent<MeshRenderer>().enabled = false;
            incision.BindMarks(scratches.GetComponent<MeshFilter>(), bladeBlood);
            incision.SetEdgeRequirement(bladeKnown);

            // The result card over the chest when the incision is finished.
            GameObject anchor = new GameObject("PopupAncora");
            anchor.transform.SetParent(field.transform, true);
            anchor.transform.position = patch.IncisionPoint(0.55f, 0f, 0.15f);
            popup = BuildResultPopup(anchor.transform, incision);

            // Trickles running from the cut down the side of the chest: a small pool of stretched
            // drops, reused, so nothing is created while the visitor cuts.
            List<Transform> trickles = new List<Transform>();
            for (int i = 0; i < 8; i++)
            {
                GameObject drop = Part(PrimitiveType.Capsule, "Escorrido_" + i, field.transform, Vector3.zero,
                    Vector3.one * 0.002f, blood, null, false, false);
                drop.SetActive(false);
                trickles.Add(drop.transform);
            }

            systems.AddComponent<IncisionBleeding>().Bind(incision, patch, trickles);

            // ---- suture visuals
            suture = systems.AddComponent<SutureWorker>();
            const int stitches = 5;
            suture.Bind(needle, needleHolder, patch, procedure, stitches, null, null);

            Mesh markRing = MakeRing(0.0022f, 0.0042f, 16);
            List<Renderer> marks = new List<Renderer>();
            List<GameObject> knots = new List<GameObject>();
            Material nylon = Paint("nylon", new Color(0.05f, 0.08f, 0.2f), 0f, 0.6f);

            for (int k = 0; k < stitches; k++)
            {
                for (int phase = 0; phase < 2; phase++)
                {
                    GameObject mark = MeshPart($"Ponto_{k}_{(phase == 0 ? "entrada" : "saida")}", field.transform, markRing,
                        MakeUnlit(new Color(0.2f, 0.55f, 1f, 0.95f)));
                    mark.transform.position = suture.MarkPosition(k, phase) + new Vector3(0f, 0.001f, 0f);
                    MeshRenderer renderer = mark.GetComponent<MeshRenderer>();
                    renderer.enabled = false;
                    marks.Add(renderer);
                }

                Vector3 a = suture.MarkPosition(k, 0);
                Vector3 b = suture.MarkPosition(k, 1);
                Vector3 mid = (a + b) * 0.5f + new Vector3(0f, 0.0025f, 0f);

                GameObject knot = new GameObject($"PontoDado_{k}");
                knot.transform.SetParent(field.transform, true);
                Tube("Laco", knot.transform, new[] { a, mid, b }, 0.0005f, nylon);
                Ball("No", knot.transform, Vector3.zero, Vector3.one * 0.003f, nylon).transform.position = a + new Vector3(0f, 0.001f, 0f);
                Tube("Ponta_1", knot.transform, new[] { a, a + new Vector3(0.006f, 0.002f, 0.003f) }, 0.0004f, nylon);
                Tube("Ponta_2", knot.transform, new[] { a, a + new Vector3(0.006f, 0.002f, -0.003f) }, 0.0004f, nylon);
                knot.SetActive(false);
                knots.Add(knot);
            }

            suture.Bind(needle, needleHolder, patch, procedure, stitches, marks, knots);

            // The chest closes itself once the heart beats, then waits for the stitches.
            systems.AddComponent<ChestClosure>().Bind(procedure, sternotomy, patch);
            procedure.SetSkinStages(true);

            Debug.Log($"[Transplante] incisão de {patch.IncisionLength * 100f:F0} cm e {stitches} pontos de sutura; " +
                      $"bisturi em {scalpelTool.transform.position}, porta-agulha em {holder.transform.position}, " +
                      $"mesa de Mayo '{stand.name}'");
        }

        /// <summary>
        /// The result card: a dark rounded plate, three stars, a title and a detail line. Hidden
        /// until a stage is graded.
        /// </summary>
        private static StageResultPopup BuildResultPopup(Transform anchor, SkinIncisionWorker incision)
        {
            GameObject root = new GameObject("CartaoResultado");
            root.transform.position = anchor.position;

            GameObject body = new GameObject("Corpo");
            body.transform.SetParent(root.transform, false);

            // The card's +Z faces away from the viewer (it is turned like the progress ring), so
            // everything readable sits on its -Z side.
            Box("Placa", body.transform, new Vector3(0f, 0f, 0.002f), new Vector3(0.2f, 0.085f, 0.002f),
                MakeUnlit(new Color(0.04f, 0.07f, 0.1f, 0.88f)), null, false);
            Box("Borda", body.transform, new Vector3(0f, 0.0415f, 0.001f), new Vector3(0.2f, 0.002f, 0.001f),
                MakeUnlit(new Color(0.3f, 0.8f, 1f)), null, false);

            // One queue step after the plate, so the stars never sort behind it.
            Material lit = MakeUnlit(new Color(1f, 0.82f, 0.25f));
            Material dim = MakeUnlit(new Color(0.25f, 0.28f, 0.32f));
            lit.renderQueue = dim.renderQueue = 3001;
            Mesh star = StarMesh(0.013f, 0.0055f);
            Renderer[] stars = new Renderer[3];
            for (int i = 0; i < 3; i++)
            {
                GameObject s = MeshPart("Estrela_" + i, body.transform, star, dim);
                // The star's winding already faces -Z, the card's readable side.
                s.transform.localPosition = new Vector3((i - 1) * 0.032f, 0.022f, -0.001f);
                stars[i] = s.GetComponent<Renderer>();
            }

            TextMesh title = BuildScreenText(body.transform, "Titulo", new Vector3(0f, -0.006f, -0.001f), 0.016f);
            title.transform.localRotation = Quaternion.identity;
            TextMesh detail = BuildScreenText(body.transform, "Detalhe", new Vector3(0f, -0.028f, -0.001f), 0.0085f);
            detail.transform.localRotation = Quaternion.identity;

            StageResultPopup popup = root.AddComponent<StageResultPopup>();
            popup.Bind(body.transform, title, detail, stars, lit, dim, incision, anchor);
            body.SetActive(false);
            return popup;
        }

        /// <summary>A flat five-pointed star facing -Z, for the result card.</summary>
        private static Mesh StarMesh(float outer, float inner)
        {
            List<Vector3> vertices = new List<Vector3> { Vector3.zero };
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.PI * 0.5f + i * Mathf.PI / 5f;
                float r = i % 2 == 0 ? outer : inner;
                vertices.Add(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
            }

            List<int> triangles = new List<int>();
            for (int i = 0; i < 10; i++)
            {
                int a = 1 + i, b = 1 + (i + 1) % 10;
                triangles.Add(0); triangles.Add(a); triangles.Add(b);
            }

            Mesh mesh = new Mesh { name = "Estrela" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>The surgical-marker line: short purple dashes down the midline.</summary>
        private static Mesh GuideMesh(ChestSkinPatch patch)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            const int dashes = 22;
            const float width = 0.0011f;

            for (int i = 0; i < dashes; i++)
            {
                float a = i / (float)dashes;
                float b = a + 0.6f / dashes;
                int s = vertices.Count;
                vertices.Add(patch.IncisionPoint(a, -width, 0.0009f));
                vertices.Add(patch.IncisionPoint(a, width, 0.0009f));
                vertices.Add(patch.IncisionPoint(b, -width, 0.0009f));
                vertices.Add(patch.IncisionPoint(b, width, 0.0009f));
                triangles.Add(s); triangles.Add(s + 2); triangles.Add(s + 1);
                triangles.Add(s + 1); triangles.Add(s + 2); triangles.Add(s + 3);
            }

            Mesh mesh = new Mesh { name = "LinhaGuia" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ------------------------------------------------------------------ feedback & monitors

        private static WorkProgressIndicator BuildProgressIndicator(SternotomyWorker sternotomy, BypassWorker bypass,
            AnastomosisWorker anastomosis)
        {
            GameObject root = new GameObject("IndicadorProgresso");

            GameObject back = MeshPart("Fundo", root.transform, MakeArc(0.027f, 0.035f), MakeUnlit(new Color(1f, 1f, 1f, 0.18f)));
            GameObject fill = MeshPart("Preenchimento", root.transform, null, MakeUnlit(new Color(0.25f, 0.85f, 1f, 0.95f)));
            fill.transform.localPosition = new Vector3(0f, 0f, -0.001f);

            TextMesh label = BuildScreenText(root.transform, "Rotulo", new Vector3(0f, -0.05f, 0f), 0.012f);
            // The ring's +Z points away from the camera, so the text must read from its -Z side,
            // which is a TextMesh's natural front.
            label.transform.localRotation = Quaternion.identity;

            WorkProgressIndicator indicator = root.AddComponent<WorkProgressIndicator>();
            indicator.Bind(sternotomy, bypass, anastomosis, fill.GetComponent<MeshFilter>(),
                fill.GetComponent<MeshRenderer>(), back.GetComponent<MeshRenderer>(), label);
            return indicator;
        }

        private static Mesh MakeArc(float inner, float outer)
        {
            Mesh mesh = new Mesh { name = "ProgressBack" };
            WorkProgressIndicator.BuildArc(mesh, inner, outer, 1f);
            return mesh;
        }

        private static List<Renderer> BuildBypassMarkers(List<BypassSite> sites)
        {
            List<Renderer> rings = new List<Renderer>();
            Mesh ring = MakeRing(0.018f, 0.028f, 28);

            foreach (BypassSite site in sites)
            {
                if (site?.Point == null) { rings.Add(null); continue; }
                GameObject marker = MeshPart("MarcaCEC", site.Point, ring, MakeUnlit(new Color(1f, 0.82f, 0.25f, 0.8f)));
                marker.transform.localPosition = Vector3.zero;
                MeshRenderer renderer = marker.GetComponent<MeshRenderer>();
                renderer.enabled = false;
                rings.Add(renderer);
            }

            return rings;
        }

        /// <summary>
        /// The kit every cardiac theatre has around the table that the visitor does not use but
        /// would miss: diathermy generator, suction, crash cart with defibrillator, and the ceiling
        /// pendants that carry gases and power down to the team.
        /// </summary>
        private static void BuildTheatreEquipment(Transform r, Vector3 thorax)
        {
            Material casing = Paint("casing", new Color(0.9f, 0.91f, 0.92f), 0.1f, 0.45f);
            Material dark = Paint("dark", new Color(0.12f, 0.13f, 0.15f), 0.2f, 0.4f);
            Material steel = Paint("steel", new Color(0.78f, 0.8f, 0.82f), 0.9f, 0.7f);
            Material glass = Glass(new Color(0.85f, 0.92f, 0.95f, 0.35f));
            Material blood = Paint("bloodVenous", new Color(0.32f, 0.03f, 0.07f), 0f, 0.85f);
            Material red = Paint("crashRed", new Color(0.72f, 0.08f, 0.08f), 0.1f, 0.5f);

            // Diathermy generator on a small cart across the table, toward the head.
            GameObject esu = new GameObject("BisturiEletrico");
            esu.transform.SetParent(r, false);
            esu.transform.position = new Vector3(-0.95f, 0f, thorax.z + 0.95f);
            esu.transform.rotation = Quaternion.LookRotation(new Vector3(1f, 0f, -0.4f).normalized);
            Box("Carrinho", esu.transform, new Vector3(0f, 0.45f, 0f), new Vector3(0.45f, 0.9f, 0.4f), casing);
            Box("Gerador", esu.transform, new Vector3(0f, 0.98f, 0f), new Vector3(0.38f, 0.14f, 0.34f), casing);
            Box("PainelCorte", esu.transform, new Vector3(-0.08f, 0.99f, 0.171f), new Vector3(0.12f, 0.06f, 0.002f),
                Glow(new Color(1f, 0.85f, 0.2f)), null, false);
            Box("PainelCoag", esu.transform, new Vector3(0.08f, 0.99f, 0.171f), new Vector3(0.12f, 0.06f, 0.002f),
                Glow(new Color(0.25f, 0.55f, 1f)), null, false);
            TextMesh esuText = BuildScreenText(esu.transform, "Leitura", new Vector3(0f, 1.075f, 0.172f), 0.022f);
            esuText.text = "CORTE 30W   COAG 40W";
            esuText.color = new Color(0.1f, 0.1f, 0.1f);

            // Suction: two canisters on a rolling stand behind the surgeon's left, half full.
            GameObject suction = new GameObject("Aspirador");
            suction.transform.SetParent(r, false);
            suction.transform.position = new Vector3(0.95f, 0f, thorax.z - 1.05f);
            Rod("Haste", suction.transform, new Vector3(0f, 0.55f, 0f), 0.015f, 1.1f, steel);
            for (int i = 0; i < 5; i++)
            {
                Box("Pe_" + i, suction.transform, new Vector3(0f, 0.04f, 0f), new Vector3(0.4f, 0.02f, 0.03f), steel, Quaternion.Euler(0f, i * 72f, 0f));
            }

            for (int i = 0; i < 2; i++)
            {
                Vector3 at = new Vector3(i == 0 ? -0.09f : 0.09f, 0.95f, 0f);
                Rod("Frasco_" + i, suction.transform, at, 0.07f, 0.24f, glass, null, false);
                Rod("Conteudo_" + i, suction.transform, at - new Vector3(0f, 0.05f + i * 0.02f, 0f), 0.064f, 0.12f - i * 0.04f, blood, null, false);
                Rod("Tampa_" + i, suction.transform, at + new Vector3(0f, 0.125f, 0f), 0.072f, 0.02f, dark, null, false);
            }

            Tube("MangueiraAspiracao", suction.transform, new[]
            {
                suction.transform.position + new Vector3(0.09f, 1.09f, 0f),
                suction.transform.position + new Vector3(-0.2f, 1.3f, 0.3f),
                new Vector3(TableHalfWidth + 0.02f, TableTopY + 0.2f, _window.Center.z - 0.25f),
                new Vector3(_window.Center.x + _window.HalfWidth + 0.04f, SkinTopAt(_window.Center.x + 0.1f, _window.Center.z - 0.12f) + 0.025f, _window.Center.z - 0.12f),
            }, 0.005f, Glass(new Color(0.9f, 0.95f, 1f, 0.6f)));

            // Crash cart: red drawers and a defibrillator on top, against the far wall.
            GameObject crash = new GameObject("CarrinhoDeParada");
            crash.transform.SetParent(r, false);
            crash.transform.position = new Vector3(RoomMinX + 0.35f, 0f, -1.65f);
            crash.transform.rotation = Quaternion.LookRotation(Vector3.right);
            Box("Gaveteiro", crash.transform, new Vector3(0f, 0.5f, 0f), new Vector3(0.7f, 1.0f, 0.5f), red);
            for (int d = 0; d < 5; d++)
            {
                Box("Puxador_" + d, crash.transform, new Vector3(0f, 0.15f + d * 0.18f, 0.255f), new Vector3(0.3f, 0.02f, 0.02f), steel);
            }

            Box("Desfibrilador", crash.transform, new Vector3(0f, 1.1f, 0f), new Vector3(0.36f, 0.2f, 0.28f), Paint("defib", new Color(0.95f, 0.8f, 0.1f), 0f, 0.5f));
            Box("TelaDesfib", crash.transform, new Vector3(-0.06f, 1.13f, 0.141f), new Vector3(0.15f, 0.1f, 0.002f), Glow(new Color(0.1f, 0.3f, 0.2f)), null, false);
            for (int p = 0; p < 2; p++)
            {
                Box("Pa_" + p, crash.transform, new Vector3(0.1f + p * 0.06f, 1.22f, 0.05f), new Vector3(0.05f, 0.04f, 0.1f), dark);
            }

            // Ceiling pendants either side of the head of the table: columns of gas outlets and
            // sockets brought down to working height, as in every modern theatre.
            Color[] gases = { new Color(0.1f, 0.6f, 0.25f), new Color(0.95f, 0.8f, 0.1f), new Color(0.55f, 0.55f, 0.58f) };
            foreach (float side in new[] { -1f, 1f })
            {
                GameObject pendant = new GameObject(side < 0 ? "Pendente_Anestesia" : "Pendente_Cirurgia");
                pendant.transform.SetParent(r, false);
                pendant.transform.position = new Vector3(side * 1.35f, 0f, thorax.z + 1.25f);

                Rod("Braco", pendant.transform, new Vector3(0f, RoomHeight - 0.25f, 0f), 0.05f, 0.5f, casing);
                Box("Coluna", pendant.transform, new Vector3(0f, 1.9f, 0f), new Vector3(0.22f, 1.0f, 0.22f), casing);
                for (int g = 0; g < 3; g++)
                {
                    Part(PrimitiveType.Cylinder, "Saida_" + g, pendant.transform, new Vector3(-side * 0.111f, 1.65f + g * 0.1f, 0f),
                        new Vector3(0.05f, 0.006f, 0.05f), Paint("gas" + g, gases[g], 0.2f, 0.5f), Quaternion.Euler(0f, 0f, 90f), false, false);
                }

                for (int s = 0; s < 4; s++)
                {
                    Box("Tomada_" + s, pendant.transform, new Vector3(-side * 0.111f, 2.0f + s * 0.08f, 0f), new Vector3(0.004f, 0.05f, 0.07f), dark, null, false);
                }
            }
        }

        /// <summary>
        /// A second vitals display, large, on the far wall in the surgeon's line of sight: the
        /// heartbeat screen the whole room — and the queue — can read.
        /// </summary>
        private static void MountVitalsOnWall(VitalSignsMonitor vitals, Vector3 thorax)
        {
            Transform t = vitals.transform;
            t.name = "TelaBatimentos_Parede";
            // Straight ahead of the surgeon, above the instruction monitor, clear of the clock
            // (to its left) and the supply cabinet (to its right).
            t.position = new Vector3(RoomMinX + 0.08f, 2.05f, thorax.z + 0.28f);

            // Front (+Z) toward the room.
            t.rotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
            t.localScale = Vector3.one * 2.4f;

            Transform arm = t.Find("Braco");
            if (arm != null) { Object.DestroyImmediate(arm.gameObject); }

            // One beep in the room is enough; the anaesthesia monitor carries it.
            AudioSource source = t.GetComponent<AudioSource>();
            if (source != null) { source.mute = true; }
        }

        /// <summary>
        /// Bakes one reflection probe filling the room, so steel and wet tissue reflect the theatre
        /// rather than the template's sky. Baked, not realtime: free at runtime on a Quest.
        /// </summary>
        private static void BakeRoomReflections(Vector3 thorax)
        {
            ReflectionProbe probe = null;
            GameObject existing = GameObject.Find("Reflection Probe");
            if (existing != null) { probe = existing.GetComponent<ReflectionProbe>(); }
            if (probe == null)
            {
                probe = new GameObject("Reflection Probe").AddComponent<ReflectionProbe>();
            }

            Vector3 roomCentre = new Vector3((RoomMinX + RoomMaxX) * 0.5f, RoomHeight * 0.5f, (RoomMinZ + RoomMaxZ) * 0.5f);
            probe.transform.position = new Vector3(0f, 1.5f, thorax.z);
            probe.center = roomCentre - probe.transform.position;
            probe.size = new Vector3(RoomMaxX - RoomMinX, RoomHeight, RoomMaxZ - RoomMinZ);
            probe.boxProjection = true;
            probe.resolution = 128;
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Custom;

            const string path = GeneratedTextureFolder + "/OR_Reflection.exr";
            try
            {
                EnsureAssetFolder(GeneratedTextureFolder);
                if (Lightmapping.BakeReflectionProbe(probe, path))
                {
                    AssetDatabase.ImportAsset(path);
                    probe.customBakedTexture = AssetDatabase.LoadAssetAtPath<Texture>(path);
                    Debug.Log($"[Transplante] reflexos da sala assados em {path}");
                }
                else
                {
                    Debug.LogWarning("[Transplante] não foi possível assar os reflexos; o aço reflete o céu padrão.");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Transplante] falha ao assar reflexos: " + e.Message);
            }
        }

        private static VitalSignsMonitor BuildVitalsMonitor(Vector3 thorax, TransplantProcedure procedure, Heartbeat donor,
            AnastomosisWorker anastomosis)
        {
            Vector3 stance = Stance(thorax);
            Vector3 position = new Vector3(thorax.x - 0.62f, 1.62f, _bodyBounds.max.z + 0.05f);

            GameObject monitor = new GameObject("MonitorSinaisVitais");
            monitor.transform.position = position;
            Vector3 toSurgeon = new Vector3(stance.x - position.x, 0f, stance.z - position.z);
            monitor.transform.rotation = Quaternion.LookRotation(toSurgeon.normalized, Vector3.up);

            Material dark = Paint("monitorBody", new Color(0.16f, 0.17f, 0.19f), 0.2f, 0.4f);
            Box("Corpo", monitor.transform, new Vector3(0f, 0f, -0.03f), new Vector3(0.46f, 0.32f, 0.06f), dark);
            GameObject screen = Part(PrimitiveType.Quad, "Tela", monitor.transform, new Vector3(0f, 0f, 0.001f),
                new Vector3(0.42f, 0.28f, 1f), Glow(new Color(0.01f, 0.02f, 0.03f)), Quaternion.Euler(0f, 180f, 0f), false, false);
            screen.name = "Tela";

            Material steel = Paint("steel", new Color(0.78f, 0.8f, 0.82f), 0.9f, 0.7f);
            float boom = RoomHeight - position.y - 0.16f;
            Rod("Braco", monitor.transform, new Vector3(0f, 0.16f + boom * 0.5f, -0.05f), 0.02f, boom, steel);

            LineRenderer Trace(string name, float y, Color colour)
            {
                GameObject go = new GameObject(name);
                go.transform.SetParent(monitor.transform, false);
                // Seen from +Z the parent's -X is the viewer's right: mirror, so the sweep runs left
                // to right, and sit on the viewer's left half of the screen.
                go.transform.localPosition = new Vector3(0.075f, y, 0.004f);
                go.transform.localScale = new Vector3(-1f, 1f, 1f);
                LineRenderer line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.alignment = LineAlignment.TransformZ;
                line.widthMultiplier = 0.0022f;
                line.numCapVertices = 0;
                Material ink = Glow(colour);
                ink.SetFloat("_Cull", 0f);
                line.sharedMaterial = ink;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.positionCount = 2;
                return line;
            }

            LineRenderer ecg = Trace("ECG", 0.06f, new Color(0.3f, 1f, 0.45f));
            LineRenderer pleth = Trace("Pleth", -0.06f, new Color(0.3f, 0.85f, 1f));

            TextMesh Number(string name, Vector3 at, float size, Color colour)
            {
                TextMesh text = BuildScreenText(monitor.transform, name, at, size);
                text.color = colour;
                text.anchor = TextAnchor.MiddleLeft;
                text.alignment = TextAlignment.Left;
                return text;
            }

            // Numbers on the viewer's right. The text is turned to face +Z, so its reading direction
            // runs along the parent's -X: anchored left at -0.065 it grows toward the right edge.
            TextMesh rate = Number("FC", new Vector3(-0.065f, 0.06f, 0.004f), 0.03f, new Color(0.3f, 1f, 0.45f));
            TextMesh sat = Number("SpO2", new Vector3(-0.065f, -0.02f, 0.004f), 0.021f, new Color(0.3f, 0.85f, 1f));
            TextMesh pressure = Number("PA", new Vector3(-0.065f, -0.075f, 0.004f), 0.02f, new Color(1f, 0.9f, 0.9f));
            TextMesh status = BuildScreenText(monitor.transform, "Status", new Vector3(0f, 0.12f, 0.004f), 0.016f);
            status.color = new Color(1f, 0.85f, 0.35f);

            AudioSource source = monitor.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.minDistance = 1f;
            source.maxDistance = 10f;

            VitalSignsMonitor vitals = monitor.AddComponent<VitalSignsMonitor>();
            vitals.Bind(procedure, donor, anastomosis, ecg, pleth, rate, sat, pressure, status, source);
            SetPrivateField(vitals, "traceSize", new Vector2(0.25f, 0.07f));

            Debug.Log($"[Transplante] monitor de sinais vitais em {position}, virado para o cirurgião");
            return vitals;
        }

        private static List<XRHandInteractor> RigHands()
        {
            List<XRHandInteractor> hands = new List<XRHandInteractor>();
            GameObject rig = GameObject.Find("XR Origin");
            if (rig == null) { return hands; }
            hands.AddRange(rig.GetComponentsInChildren<XRHandInteractor>(true));
            return hands;
        }

        /// <summary>Post-processing on the headset camera where it can be afforded.</summary>
        private static void WireHeadsetPostProcessing()
        {
            Camera head = Camera.main;
            if (head == null)
            {
                foreach (Camera c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                {
                    if (c.CompareTag("MainCamera")) { head = c; break; }
                }
            }

            if (head == null)
            {
                Debug.LogWarning("[Transplante] sem câmera do headset; o pós-processamento continua desligado.");
                return;
            }

            if (head.GetComponent<HeadsetPostProcessing>() == null) { head.gameObject.AddComponent<HeadsetPostProcessing>(); }
            Debug.Log($"[Transplante] pós-processamento ligado em '{head.name}' no PC (desligado no Quest standalone)");
        }
    }
}
