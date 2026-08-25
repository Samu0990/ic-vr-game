using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VRSurgery.EditorTools
{
    /// <summary>
    /// Lists what the projection camera actually renders, so "does the audience see anything they
    /// should not" is answered by the frustum rather than by looking at a picture.
    /// </summary>
    public static class ProjectionAudit
    {
        private const string ScenePath = "Assets/Scenes/SurgeryMVP.unity";

        public static void RunFromCommandLine()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            Camera cam = null;
            foreach (Camera c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (c.name == "ProjectionCamera") { cam = c; break; }
            }

            if (cam == null) { Debug.LogError("[Audit] No ProjectionCamera."); EditorApplication.Exit(1); return; }

            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(cam);
            List<string> visible = new List<string>();

            foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) { continue; }
                if ((cam.cullingMask & (1 << r.gameObject.layer)) == 0) { continue; }
                if (!GeometryUtility.TestPlanesAABB(planes, r.bounds)) { continue; }

                visible.Add(Path(r.transform) + $"  [layer {LayerMask.LayerToName(r.gameObject.layer)}]");
            }

            // World-space UI draws through CanvasRenderer, which is not a Renderer — enumerating
            // only Renderers hid exactly the two things that were still leaking into the frame.
            foreach (Canvas c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!c.isActiveAndEnabled || c.renderMode != RenderMode.WorldSpace) { continue; }
                if ((cam.cullingMask & (1 << c.gameObject.layer)) == 0) { continue; }

                RectTransform rt = c.transform as RectTransform;
                Bounds b = rt != null
                    ? new Bounds(rt.position, rt.rect.size * rt.lossyScale.x)
                    : new Bounds(c.transform.position, Vector3.one * 0.05f);

                if (!GeometryUtility.TestPlanesAABB(planes, b)) { continue; }

                visible.Add("[CANVAS] " + Path(c.transform) + $"  [layer {LayerMask.LayerToName(c.gameObject.layer)}]");
            }

            visible.Sort();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"@@A | {visible.Count} renderers dentro do frustum da projecao");
            foreach (string v in visible) { sb.AppendLine("@@A |   " + v); }
            Debug.Log(sb.ToString());
            EditorApplication.Exit(0);
        }

        private static string Path(Transform t)
        {
            string p = t.name;
            while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }
    }
}
