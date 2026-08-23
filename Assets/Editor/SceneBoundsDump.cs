using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VRSurgery.EditorTools
{
    /// <summary>
    /// Measures the template environment as Unity actually resolves it: prefab scale overrides,
    /// FBX unit scale and handedness already applied. Blender-side measurements of the source FBX
    /// cannot answer this because the scene rescales the environment non-uniformly in X.
    /// </summary>
    public static class SceneBoundsDump
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";

        public static void DumpFromCommandLine()
        {
            try
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("[SceneBoundsDump] BEGIN");

                bool any = false;
                Bounds total = default;
                foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    Bounds b = r.bounds;
                    sb.AppendLine(
                        $"@@R | {Path(r.transform)} | center=({b.center.x:F3},{b.center.y:F3},{b.center.z:F3}) " +
                        $"| size=({b.size.x:F3},{b.size.y:F3},{b.size.z:F3}) " +
                        $"| minY={b.min.y:F4} maxY={b.max.y:F4} " +
                        $"| lossyScale={r.transform.lossyScale}");

                    if (!any) { total = b; any = true; } else { total.Encapsulate(b); }
                }

                if (any)
                {
                    sb.AppendLine($"@@TOTAL | center=({total.center.x:F3},{total.center.y:F3},{total.center.z:F3}) " +
                                  $"| size=({total.size.x:F3},{total.size.y:F3},{total.size.z:F3})");
                }

                sb.AppendLine("[SceneBoundsDump] END");
                Debug.Log(sb.ToString());
                Debug.Log("[SceneBoundsDump] DUMP_OK");
                EditorApplication.Exit(0);
            }
            catch (System.Exception exception)
            {
                Debug.LogError("[SceneBoundsDump] DUMP_FAILED " + exception);
                EditorApplication.Exit(1);
            }
        }

        private static string Path(Transform t)
        {
            string path = t.name;
            while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }
    }
}
