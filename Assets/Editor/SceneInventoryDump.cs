using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VRSurgery.EditorTools
{
    /// <summary>
    /// Prints every renderer in the built scene with its world bounds and material colour.
    /// Exists so questions like "what is that blue object" get answered from scene data rather
    /// than from squinting at a screenshot.
    /// </summary>
    public static class SceneInventoryDump
    {
        private const string ScenePath = "Assets/Scenes/VerticalSlice.unity";

        [MenuItem("VRSurgery/Dump Scene Inventory")]
        public static void DumpMenu() => Dump();

        public static void DumpFromCommandLine()
        {
            Dump();
            EditorApplication.Exit(0);
        }

        private static string Path(Transform t)
        {
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }

            return path;
        }

        public static void Dump()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            StringBuilder sb = new StringBuilder();
            foreach (MeshRenderer r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                Bounds b = r.bounds;
                Material m = r.sharedMaterial;
                string colour = "-";
                if (m != null && m.HasProperty("_Color"))
                {
                    Color c = m.color;
                    colour = $"{c.r:F2},{c.g:F2},{c.b:F2}";
                }

                sb.AppendLine($"@@R | {Path(r.transform)} | mat={(m != null ? m.name : "NONE")} | rgb={colour} | " +
                              $"min={b.min.x:F3},{b.min.y:F3},{b.min.z:F3} | " +
                              $"max={b.max.x:F3},{b.max.y:F3},{b.max.z:F3} | " +
                              $"size={b.size.x:F3},{b.size.y:F3},{b.size.z:F3}");
            }

            Debug.Log(sb.ToString());
        }
    }
}
