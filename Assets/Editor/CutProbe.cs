using System.IO;
using UnityEditor;
using UnityEngine;
using VRSurgery.Cutting;

namespace VRSurgery.EditorTools
{
    /// <summary>
    /// Cuts a test patch and reports what happened to it, then renders the result.
    ///
    /// The measurements are the point. "Looks cut" is not a claim anyone can check; boundary-edge
    /// count is — a whole sheet has only its outer rim, so new open edges exist only if the
    /// surface was genuinely severed.
    /// </summary>
    public static class CutProbe
    {
        public static void RunFromCommandLine()
        {
            string output = "cut.png";
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-cutOut") { output = args[i + 1]; }
            }

            Mesh mesh = BuildPatch(40, 0.14f, 0.10f);
            int v0 = mesh.vertexCount, t0 = mesh.triangles.Length / 3, b0 = BoundaryEdges(mesh);

            bool cut = MeshIncision.Cut(mesh, new Vector2(-0.045f, 0f), new Vector2(0.045f, 0f), 1f, 0.006f);

            Debug.Log($"@@CUT ok={cut} verts {v0}->{mesh.vertexCount} tris {t0}->{mesh.triangles.Length / 3} " +
                      $"bordas {b0}->{BoundaryEdges(mesh)} alturaMalha={mesh.bounds.size.y:F4}m");

            Render(mesh, output);
            Debug.Log("@@CUT_DONE");
            EditorApplication.Exit(0);
        }

        private static Mesh BuildPatch(int n, float w, float h)
        {
            Vector3[] verts = new Vector3[(n + 1) * (n + 1)];
            for (int z = 0; z <= n; z++)
            {
                for (int x = 0; x <= n; x++)
                {
                    // Half-cell offset in Z so the cut line falls between rows rather than along
                    // them; a grid whose vertices sit exactly on the cut never exercises the
                    // edge-clipping path at all.
                    verts[z * (n + 1) + x] = new Vector3(
                        (x / (float)n - 0.5f) * w, 0f, ((z + 0.5f) / n - 0.5f) * h);
                }
            }

            int[] tris = new int[n * n * 6];
            int k = 0;
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    int i = z * (n + 1) + x;
                    tris[k++] = i; tris[k++] = i + n + 1; tris[k++] = i + 1;
                    tris[k++] = i + 1; tris[k++] = i + n + 1; tris[k++] = i + n + 2;
                }
            }

            Mesh mesh = new Mesh();
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            return mesh;
        }

        /// <summary>Edges belonging to exactly one triangle. A closed surface has none.</summary>
        private static int BoundaryEdges(Mesh mesh)
        {
            System.Collections.Generic.Dictionary<long, int> counts = new();
            int[] t = mesh.triangles;

            for (int i = 0; i < t.Length; i += 3)
            {
                for (int e = 0; e < 3; e++)
                {
                    int a = t[i + e], b = t[i + (e + 1) % 3];
                    long key = (long)Mathf.Min(a, b) * 1_000_000L + Mathf.Max(a, b);
                    counts[key] = counts.TryGetValue(key, out int c) ? c + 1 : 1;
                }
            }

            int lone = 0;
            foreach (var kv in counts) { if (kv.Value == 1) { lone++; } }
            return lone;
        }

        private static void Render(Mesh mesh, string path)
        {
            Vector3 origin = new Vector3(0f, 30f, 0f);

            GameObject go = new GameObject("CutPatch", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.position = origin;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            Material skin = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            skin.color = new Color(0.80f, 0.55f, 0.50f);
            skin.SetFloat("_Smoothness", 0.25f);
            go.GetComponent<MeshRenderer>().sharedMaterial = skin;

            GameObject lamp = new GameObject("Key", typeof(Light));
            Light light = lamp.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.7f;
            lamp.transform.rotation = Quaternion.Euler(52f, 25f, 0f);

            Camera cam = new GameObject("Cam", typeof(Camera)).GetComponent<Camera>();
            cam.transform.position = origin + new Vector3(0.010f, 0.070f, -0.030f);
            cam.transform.rotation = Quaternion.LookRotation((origin - cam.transform.position).normalized, Vector3.up);
            cam.fieldOfView = 45f;
            cam.nearClipPlane = 0.004f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.11f, 0.14f);

            RenderTexture rt = new RenderTexture(1200, 780, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            Texture2D img = new Texture2D(1200, 780, TextureFormat.RGB24, false);
            img.ReadPixels(new Rect(0, 0, 1200, 780), 0, 0);
            img.Apply();
            RenderTexture.active = null;

            File.WriteAllBytes(path, img.EncodeToPNG());

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(lamp);
            Object.DestroyImmediate(cam.gameObject);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(img);
        }
    }
}
