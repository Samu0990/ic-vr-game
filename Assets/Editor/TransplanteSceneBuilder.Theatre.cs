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

        private static GameObject Box(string name, Transform parent, Vector3 position, Vector3 size, Material m,
            Quaternion? rotation = null, bool shadows = true) =>
            Part(PrimitiveType.Cube, name, parent, position, size, m, rotation, false, shadows);

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

        private static Texture2D TileTexture()
        {
            const int size = 256;
            const int tile = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "OR_WallTile" };
            System.Random random = new System.Random(3);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool grout = x % tile < 3 || y % tile < 3;
                    float shade = 0.97f + 0.03f * (float)random.NextDouble();
                    int tx = x / tile, ty = y / tile;
                    float perTile = 0.96f + 0.04f * Mathf.PerlinNoise(tx * 3.1f, ty * 1.7f);
                    Color c = grout ? new Color(0.78f, 0.80f, 0.80f) : new Color(1f, 1f, 1f) * shade * perTile;
                    c.a = 1f;
                    texture.SetPixel(x, y, c);
                }
            }

            texture.Apply(true);
            texture.wrapMode = TextureWrapMode.Repeat;
            return SaveTexture(texture, "OR_WallTile");
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

            AudioSource hum = room.AddComponent<AudioSource>();
            hum.playOnAwake = false;
            room.AddComponent<AmbientLoop>();

            foreach (Renderer renderer in room.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.GetComponent<LineRenderer>() != null || renderer is SkinnedMeshRenderer) { continue; }
                if (renderer.GetComponentInParent<WallClock>() != null) { continue; }
                if (renderer.GetComponentInParent<VitalSignsMonitor>() != null) { continue; }
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
            GameObject slab = Part(PrimitiveType.Cube, "Floor", r, centre + new Vector3(0f, -0.05f, 0f),
                new Vector3(width, 0.1f, depth), floor, null, true, false);
            slab.GetComponent<MeshRenderer>().receiveShadows = true;

            Texture2D tiles = TileTexture();
            Color wallTint = new Color(0.74f, 0.86f, 0.86f);

            Material WallMat(string key, float w, float h)
            {
                Material m = MakeMaterial(wallTint, 0f, 0.45f);
                m.name = "OR_Wall_" + key;
                m.SetTexture("_BaseMap", tiles);
                m.mainTextureScale = new Vector2(w / 0.6f, h / 0.6f);
                return m;
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
            clock.transform.position = new Vector3(wall + 0.03f, 2.35f, 0.4f);
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
            float drape = SkinTopAt(window.x + 0.02f, window.z) + 0.03f;

            Tube("ArterialLine", m, new[]
            {
                origin + new Vector3(0.1f, 0.92f, -0.48f),
                origin + new Vector3(0.45f, 1.18f, 0.1f),
                new Vector3(-0.42f, TableTopY + 0.12f, window.z + 0.05f),
                new Vector3(window.x, drape, window.z + 0.08f),
            }, 0.0065f, arterial);

            Tube("VenousLine", m, new[]
            {
                origin + new Vector3(0.1f, 0.92f, 0.46f),
                origin + new Vector3(0.45f, 1.1f, 0.6f),
                new Vector3(-0.44f, TableTopY + 0.1f, window.z - 0.02f),
                new Vector3(window.x - 0.005f, drape - 0.005f, window.z - 0.02f),
            }, 0.0085f, venous);
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

            List<float> xs = Axis(-0.68f, 0.68f, 0.03f, cx - wx, cx + wx);
            List<float> zs = Axis(_bodyBounds.min.z - 0.18f, _window.NeckZ, 0.03f, cz - wz, cz + wz);

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
            GameObject drape = MeshPart("Campo", root.transform, mesh, cloth, true);
            drape.GetComponent<MeshRenderer>().receiveShadows = true;

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
            Material wound = DoubleSided(Paint("woundWall", new Color(0.62f, 0.16f, 0.12f), 0f, 0.75f));

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
            Box("CampoBandeja", stand.transform, new Vector3(centre.x, topY + 0.001f, centre.z), new Vector3(0.46f, 0.003f, 0.32f), drape);
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
            grab.throwOnDetach = false;
            grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;

            tool.AddComponent<ReturnHomeOnRelease>().Bind(interactable);
            return tool;
        }

        /// <summary>The real scalpel model, aligned so its blade points along +Z, with a tip at the belly of the blade.</summary>
        private static GameObject BuildScalpel(Vector3 position, Quaternion rotation, out Transform tip, out SurgicalInteractable interactable)
        {
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
                Box("Cabo", tool.transform, new Vector3(0f, 0f, -0.03f), new Vector3(0.008f, 0.003f, 0.12f), steel);
                Box("Lamina", tool.transform, new Vector3(0f, -0.002f, 0.045f), new Vector3(0.0015f, 0.008f, 0.03f), steel);
            }

            GameObject tipObject = new GameObject("BladeTip");
            tipObject.transform.SetParent(tool.transform, false);
            tipObject.transform.localPosition = new Vector3(0f, 0f, tipZ);
            tip = tipObject.transform;

            Debug.Log($"[Transplante] bisturi com ponta a {(tipZ - gripZ) * 100f:F1} cm da empunhadura");
            return tool;
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
            out SurgicalInteractable scalpel, out SurgicalInteractable needleHolder)
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
            GameObject scalpelTool = BuildScalpel(trayTop + new Vector3(0.12f, 0.012f, 0.05f), away, out Transform blade, out scalpel);
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
