using System.IO;
using UnityEditor;
using UnityEngine;

namespace VRSurgery.EditorTools
{
    /// <summary>
    /// Surfaces for the theatre: ceramic wall tile with sunken grout, seamless epoxy floor with
    /// flecks, woven drape fabric, brushed steel and perforated ceiling panels.
    ///
    /// Generated here rather than downloaded — the build machine may have no network, and nothing
    /// generated here carries a licence question. Each surface is an albedo and a normal map,
    /// written as PNG and imported with the right settings (sRGB colour, a real normal map), so
    /// URP lights them as surfaces with relief instead of flat colour, which is most of what made
    /// the room read as a test level.
    ///
    /// Small textures on purpose: 512 px at most, mipmapped. The Quest pays for texture bandwidth.
    /// </summary>
    public static partial class TransplanteSceneBuilder
    {
        private const string SurfaceFolder = GeneratedTextureFolder + "/Surfaces";

        private sealed class SurfaceSet
        {
            public Texture2D Albedo;
            public Texture2D Normal;
        }

        private static SurfaceSet _wallTile, _floor, _steel, _ceiling, _woundLayers, _drapeWeave, _drapeFolds;

        /// <summary>Generates (or reuses) every surface once per build.</summary>
        private static void PrepareSurfaces()
        {
            EnsureAssetFolder(SurfaceFolder);
            _wallTile = Surface("WallTile", 512, WallTileAt, 6f);
            _floor = Surface("EpoxyFloor", 512, FloorAt, 2f);
            _steel = Surface("BrushedSteel", 256, SteelAt, 1.5f);
            _ceiling = Surface("CeilingPanel", 256, CeilingAt, 3f);
            _woundLayers = Surface("WoundLayers", 128, WoundLayersAt, 5f);
            _drapeWeave = Surface("DrapeWeave", 512, CottonWeaveAt, 4f, true);
            _drapeFolds = Surface("DrapeFolds", 256, DrapeFoldsAt, 5f);
            Debug.Log("[Transplante] superfícies da sala geradas: azulejo, piso epóxi, aço escovado, forro, camadas da ferida, algodão dos campos");
        }

        /// <summary>Colour and height at a texel, both tileable.</summary>
        private delegate void SurfaceSample(int x, int y, int size, out Color colour, out float height);

        private static SurfaceSet Surface(string name, int size, SurfaceSample sample, float bump, bool linearAlbedo = false)
        {
            Color[] colours = new Color[size * size];
            float[] heights = new float[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    sample(x, y, size, out Color c, out float h);
                    colours[y * size + x] = c;
                    heights[y * size + x] = h;
                }
            }

            Color[] normals = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Central differences, wrapping at the edges so the normal map tiles too.
                    float l = heights[y * size + (x - 1 + size) % size];
                    float r = heights[y * size + (x + 1) % size];
                    float d = heights[((y - 1 + size) % size) * size + x];
                    float u = heights[((y + 1) % size) * size + x];
                    Vector3 n = new Vector3((l - r) * bump, (d - u) * bump, 1f).normalized;
                    normals[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            }

            return new SurfaceSet
            {
                Albedo = WritePng($"{SurfaceFolder}/{name}_Albedo.png", size, colours, false, linearAlbedo),
                Normal = WritePng($"{SurfaceFolder}/{name}_Normal.png", size, normals, true),
            };
        }

        private static Texture2D WritePng(string path, int size, Color[] pixels, bool normalMap, bool linear = false)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false, !normalMap);
            texture.SetPixels(pixels);
            texture.Apply(false);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
                // A detail albedo is read as a linear multiplier: 0.5 means leave the colour be.
                importer.sRGBTexture = !normalMap && !linear;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.mipmapEnabled = true;
                importer.anisoLevel = 4;
                importer.maxTextureSize = size;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// Adds a surface as URP/Lit's detail layer: fine structure tiled much smaller than the main
        /// texture, so the same material reads right at arm's length and up close.
        /// </summary>
        private static Material DressDetail(Material material, SurfaceSet surface, Vector2 tiling, float bump = 1f)
        {
            if (material == null || surface == null) { return material; }

            if (surface.Albedo != null)
            {
                material.SetTexture("_DetailAlbedoMap", surface.Albedo);
                material.SetTextureScale("_DetailAlbedoMap", tiling);
                material.SetFloat("_DetailAlbedoMapScale", 1f);
            }

            if (surface.Normal != null)
            {
                material.SetTexture("_DetailNormalMap", surface.Normal);
                material.SetFloat("_DetailNormalMapScale", bump);
            }

            material.EnableKeyword("_DETAIL_MULX2");
            return material;
        }

        /// <summary>Puts a surface on a URP/Lit material, tiled <paramref name="tiling"/> times.</summary>
        private static Material Dress(Material material, SurfaceSet surface, Vector2 tiling, float bumpScale = 1f)
        {
            if (material == null || surface == null) { return material; }

            if (surface.Albedo != null) { material.SetTexture("_BaseMap", surface.Albedo); }
            if (surface.Normal != null)
            {
                material.SetTexture("_BumpMap", surface.Normal);
                material.SetFloat("_BumpScale", bumpScale);
                material.EnableKeyword("_NORMALMAP");
            }

            material.mainTextureScale = tiling;
            return material;
        }

        // ------------------------------------------------------------------ samplers

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 144269504;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xffffff) / (float)0xffffff;
            }
        }

        /// <summary>Tileable value noise over a period that divides the texture size.</summary>
        private static float Noise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);

            float a = Hash(((x0 % period) + period) % period, ((y0 % period) + period) % period, seed);
            float b = Hash((((x0 + 1) % period) + period) % period, ((y0 % period) + period) % period, seed);
            float c = Hash(((x0 % period) + period) % period, (((y0 + 1) % period) + period) % period, seed);
            float d = Hash((((x0 + 1) % period) + period) % period, (((y0 + 1) % period) + period) % period, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        /// <summary>
        /// Plain-woven cotton at the scale of the yarn, for the drape's detail map: warp and weft
        /// passing over and under each other, every yarn its own thickness and shade, thicker
        /// slubs along the yarns, dark gaps where the yarns part, and a little fuzz. Grey around
        /// 0.5, because a detail albedo multiplies by twice its value — 0.5 leaves the colour be.
        /// </summary>
        private static void CottonWeaveAt(int x, int y, int size, out Color colour, out float height)
        {
            const int threads = 32;
            int cell = size / threads;
            int i = x / cell, j = y / cell;
            float fx = (x % cell + 0.5f) / cell, fy = (y % cell + 0.5f) / cell;

            // Each yarn keeps its own width and shade across the whole tile.
            float warpWidth = 0.72f + 0.2f * Hash(i, 0, 31);
            float weftWidth = 0.72f + 0.2f * Hash(0, j, 32);
            float warpShade = (Hash(i, 1, 33) - 0.5f) * 0.1f;
            float weftShade = (Hash(1, j, 34) - 0.5f) * 0.1f;

            // Slubs: stretches where the yarn runs thicker and lighter.
            // Noise along the yarn, a different stretch of it for each yarn; periods divide the
            // tile so the pattern repeats without a seam.
            float warpSlub = Mathf.Max(0f, Noise(y / 16f, i * 7, Mathf.Max(1, size / 16), 35) - 0.62f) * 1.6f;
            float weftSlub = Mathf.Max(0f, Noise(x / 16f, j * 7, Mathf.Max(1, size / 16), 36) - 0.62f) * 1.6f;

            float warp = Profile(fx, warpWidth + warpSlub * 0.2f);
            float weft = Profile(fy, weftWidth + weftSlub * 0.2f);
            bool warpOnTop = (i + j) % 2 == 0;

            // The yarn on top arches over its crossing; the one underneath shows only in the gaps.
            float topArch = Mathf.Sin((warpOnTop ? fy : fx) * Mathf.PI);
            float top = warpOnTop ? warp : weft;
            float under = warpOnTop ? weft : warp;
            // Cotton yarn is twisted: faint diagonal ridges along the yarn on top.
            float along = warpOnTop ? fy : fx, across = warpOnTop ? fx : fy;
            float twist = 0.06f * Mathf.Sin((along * 3f + across * 1.5f) * Mathf.PI * 2f);
            float topHeight = top > 0f ? 0.55f + 0.45f * top * (0.7f + 0.3f * topArch) + twist * top : 0f;
            float underHeight = under * 0.35f;
            height = Mathf.Max(topHeight, underHeight);

            float shade = topHeight >= underHeight
                ? (warpOnTop ? warpShade + warpSlub * 0.08f : weftShade + weftSlub * 0.08f)
                : (warpOnTop ? weftShade : warpShade) - 0.05f;
            float gap = height <= 0.02f ? -0.08f : 0f;
            float crest = 0.05f * Mathf.Clamp01(topHeight - 0.8f) * 5f;
            float fuzz = (Noise(x / 2f, y / 2f, Mathf.Max(1, size / 2), 37) - 0.5f) * 0.04f;

            float g = Mathf.Clamp01(0.5f + shade + gap + crest + fuzz);
            colour = new Color(g, g, g, 1f);
        }

        /// <summary>Cross-section of a round yarn of the given width, 1 at its centre, 0 past its edge.</summary>
        private static float Profile(float f, float width)
        {
            float d = (f - 0.5f) / (width * 0.5f);
            return d * d >= 1f ? 0f : Mathf.Sqrt(1f - d * d);
        }

        /// <summary>
        /// The drape at arm's length: soft wrinkles from lying over a body, one crease each way
        /// where the laundry folded and pressed it (a little wavy, deeper in places, with the
        /// cloth raised beside it), and the uneven tone of cotton washed a hundred times.
        /// Near white, tinted by the drape's colour.
        /// </summary>
        private static void DrapeFoldsAt(int x, int y, int size, out Color colour, out float height)
        {
            float half = size * 0.5f;
            float wobbleX = (Noise(y / 32f, 3f, Mathf.Max(1, size / 32), 44) - 0.5f) * 4f;
            float wobbleY = (Noise(x / 32f, 5f, Mathf.Max(1, size / 32), 45) - 0.5f) * 4f;
            float dx = x - half + wobbleX, dy = y - half + wobbleY;
            float pressX = 0.4f + 0.6f * Noise(y / 64f, 1f, Mathf.Max(1, size / 64), 46);
            float pressY = 0.3f + 0.5f * Noise(x / 64f, 2f, Mathf.Max(1, size / 64), 47);
            float crease = Mathf.Exp(-dx * dx / 4f) * pressX + Mathf.Exp(-dy * dy / 4f) * pressY;
            float shoulder = Mathf.Exp(-dx * dx / 50f) * pressX + Mathf.Exp(-dy * dy / 50f) * pressY;

            // Wrinkles: ridged noise, a crest where the field turns, bent a little so the crests
            // wander the way cloth rucks where it was pushed.
            float warp = (Noise(x / 32f, y / 32f, Mathf.Max(1, size / 32), 41) - 0.5f) * 2f;
            float broad = 1f - Mathf.Abs(2f * Noise(x / 32f + warp, y / 32f - warp, Mathf.Max(1, size / 32), 42) - 1f);
            float fine = 1f - Mathf.Abs(2f * Noise(x / 16f + warp, y / 16f, Mathf.Max(1, size / 16), 49) - 1f);
            float wrinkle = 0.65f * broad * broad + 0.35f * fine * fine;
            float ripple = Noise(x / 8f, y / 8f, Mathf.Max(1, size / 8), 48);
            float wash = Noise(x / 64f, y / 64f, Mathf.Max(1, size / 64), 43);

            height = 0.5f + 0.6f * (wrinkle - 0.5f) + 0.1f * (ripple - 0.5f) - 0.3f * crease + 0.1f * shoulder;
            float g = Mathf.Clamp01(0.93f + 0.07f * wash - 0.04f * crease + 0.05f * (wrinkle - 0.5f));
            colour = new Color(g, g, g, 1f);
        }

        /// <summary>
        /// The cut face of the chest wall, from the skin down (U runs 0 at the skin to 1 at the
        /// bottom of the wound; V along the incision, tileable): iodine-stained epidermis, pale
        /// dermis, a thick band of yellow lobulated fat with the odd capillary, white fascia, and
        /// red muscle at the bottom. The layers are what make a cut read as a cut into a body.
        /// </summary>
        private static void WoundLayersAt(int x, int y, int size, out Color colour, out float height)
        {
            float u = x / (float)(size - 1);
            float wobble = (Noise(y / 6f, 1.5f, Mathf.Max(1, size / 6), 21) - 0.5f) * 0.03f;
            float d = u + wobble;

            Color epidermis = new Color(0.7f, 0.43f, 0.3f);
            Color dermis = new Color(0.93f, 0.78f, 0.72f);
            Color fat = new Color(0.96f, 0.82f, 0.4f);
            Color septum = new Color(0.86f, 0.62f, 0.34f);
            Color fascia = new Color(0.93f, 0.91f, 0.86f);
            Color muscle = new Color(0.55f, 0.11f, 0.09f);

            if (d < 0.05f)
            {
                colour = epidermis;
                height = 0.6f;
            }
            else if (d < 0.17f)
            {
                colour = Color.Lerp(dermis, new Color(0.9f, 0.6f, 0.55f), (d - 0.05f) / 0.12f);
                height = 0.55f;
            }
            else if (d < 0.78f)
            {
                // Lobules: bright cushions of fat separated by darker fibrous septa.
                float cell = Noise(x / 7f, y / 7f, Mathf.Max(1, size / 7), 22);
                float fine = Noise(x / 2.5f, y / 2.5f, Mathf.Max(1, Mathf.RoundToInt(size / 2.5f)), 23);
                float lobule = Mathf.SmoothStep(0.25f, 0.75f, cell * 0.75f + fine * 0.25f);
                colour = Color.Lerp(septum, fat, lobule);
                height = 0.3f + lobule * 0.6f;

                // A few capillaries, red, where the blade crossed them.
                if (Hash(x / 3, y / 3, 24) > 0.985f) { colour = new Color(0.7f, 0.1f, 0.1f); height = 0.35f; }
            }
            else if (d < 0.86f)
            {
                colour = fascia;
                height = 0.65f;
            }
            else
            {
                // Muscle fibres run along the incision.
                float fibre = 0.5f + 0.5f * Mathf.Sin(y / (float)size * Mathf.PI * 2f * 24f + Noise(x / 3f, y / 9f, Mathf.Max(1, size / 3), 25) * 3f);
                colour = Color.Lerp(muscle, new Color(0.68f, 0.18f, 0.14f), fibre * 0.6f);
                height = 0.35f + fibre * 0.25f;
            }
        }

        /// <summary>4 x 4 glazed tiles with sunken, bevelled grout.</summary>
        private static void WallTileAt(int x, int y, int size, out Color colour, out float height)
        {
            int tile = size / 4;
            int tx = x / tile, ty = y / tile;
            float lx = x % tile, ly = y % tile;

            const float grout = 3f;
            const float bevel = 5f;
            float edge = Mathf.Min(Mathf.Min(lx, tile - 1 - lx), Mathf.Min(ly, tile - 1 - ly));

            float face = Mathf.Clamp01((edge - grout) / bevel);
            height = Mathf.SmoothStep(0f, 1f, face) + (Noise(x / 16f, y / 16f, size / 16, 7) - 0.5f) * 0.03f;

            float perTile = 0.965f + 0.035f * Hash(tx, ty, 3);
            Color glaze = new Color(0.93f, 0.97f, 0.97f) * perTile;
            Color groutColour = new Color(0.72f, 0.74f, 0.74f) * (0.95f + 0.05f * Noise(x / 4f, y / 4f, size / 4, 11));
            colour = Color.Lerp(groutColour, glaze, Mathf.Clamp01(face * 1.5f));
            colour.a = 1f;
        }

        /// <summary>Seamless epoxy: soft mottling with scattered flecks.</summary>
        private static void FloorAt(int x, int y, int size, out Color colour, out float height)
        {
            float mottle = Noise(x / 64f, y / 64f, size / 64, 1) * 0.6f + Noise(x / 16f, y / 16f, size / 16, 2) * 0.4f;
            Color baseColour = Color.Lerp(new Color(0.30f, 0.39f, 0.38f), new Color(0.36f, 0.45f, 0.44f), mottle);

            float fleck = Hash(x, y, 5);
            height = mottle * 0.2f;
            if (fleck > 0.985f)
            {
                baseColour = Color.Lerp(baseColour, new Color(0.85f, 0.88f, 0.86f), 0.7f);
                height += 0.4f;
            }
            else if (fleck < 0.01f)
            {
                baseColour *= 0.6f;
            }

            colour = baseColour;
            colour.a = 1f;
        }

        /// <summary>Brushed stainless: long streaks along X.</summary>
        private static void SteelAt(int x, int y, int size, out Color colour, out float height)
        {
            float streak = Noise(x / 64f, y * 1f, size / 64, 4) * 0.7f + Hash(0, y, 6) * 0.3f;
            height = streak;
            float shade = 0.72f + 0.14f * streak;
            colour = new Color(shade, shade * 1.01f, shade * 1.03f, 1f);
        }

        /// <summary>Perforated acoustic ceiling panel with seams at the edges.</summary>
        private static void CeilingAt(int x, int y, int size, out Color colour, out float height)
        {
            const int pitch = 8;
            float dx = x % pitch - pitch * 0.5f, dy = y % pitch - pitch * 0.5f;
            bool hole = dx * dx + dy * dy < 2.2f;
            bool seam = x < 2 || y < 2;

            height = hole || seam ? 0f : 1f;
            float shade = seam ? 0.7f : hole ? 0.55f : 0.95f;
            colour = new Color(shade, shade, shade * 1.01f, 1f);
        }
    }
}
