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

        private static SurfaceSet _wallTile, _floor, _fabric, _steel, _ceiling;

        /// <summary>Generates (or reuses) every surface once per build.</summary>
        private static void PrepareSurfaces()
        {
            EnsureAssetFolder(SurfaceFolder);
            _wallTile = Surface("WallTile", 512, WallTileAt, 6f);
            _floor = Surface("EpoxyFloor", 512, FloorAt, 2f);
            _fabric = Surface("DrapeFabric", 256, FabricAt, 3f);
            _steel = Surface("BrushedSteel", 256, SteelAt, 1.5f);
            _ceiling = Surface("CeilingPanel", 256, CeilingAt, 3f);
            Debug.Log("[Transplante] superfícies da sala geradas: azulejo, piso epóxi, tecido, aço escovado, forro");
        }

        /// <summary>Colour and height at a texel, both tileable.</summary>
        private delegate void SurfaceSample(int x, int y, int size, out Color colour, out float height);

        private static SurfaceSet Surface(string name, int size, SurfaceSample sample, float bump)
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
                Albedo = WritePng($"{SurfaceFolder}/{name}_Albedo.png", size, colours, false),
                Normal = WritePng($"{SurfaceFolder}/{name}_Normal.png", size, normals, true),
            };
        }

        private static Texture2D WritePng(string path, int size, Color[] pixels, bool normalMap)
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
                importer.sRGBTexture = !normalMap;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.mipmapEnabled = true;
                importer.anisoLevel = 4;
                importer.maxTextureSize = size;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
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

        /// <summary>Non-woven surgical drape: a fine crosshatch with fibre noise.</summary>
        private static void FabricAt(int x, int y, int size, out Color colour, out float height)
        {
            const float threads = 64f;
            float u = x / (float)size * threads * Mathf.PI * 2f;
            float v = y / (float)size * threads * Mathf.PI * 2f;
            float weave = 0.5f + 0.25f * Mathf.Sin(u) * Mathf.Sin(v) + 0.25f * Mathf.Sin(u + v) * 0.5f;
            float fibre = Noise(x / 3f, y / 3f, size / 3 > 0 ? size / 3 : 1, 9);

            height = weave * 0.6f + fibre * 0.4f;
            float shade = 0.92f + 0.08f * fibre;
            colour = new Color(0.17f, 0.4f, 0.6f) * shade;
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
