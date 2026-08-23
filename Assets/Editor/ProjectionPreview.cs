using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VRSurgery.EditorTools
{
    /// <summary>
    /// Renders exactly what the projector would show, from the ProjectionCamera itself.
    /// Target-display output cannot be inspected in the editor, so the only way to see whether
    /// the projected framing is any good is to render through that camera off-screen.
    /// </summary>
    public static class ProjectionPreview
    {
        private const string ScenePath = "Assets/Scenes/SurgeryMVP.unity";
        private const string OutputPath = "SceneCaptures/projection_view.png";

        public static void CaptureFromCommandLine()
        {
            try
            {
                Capture();
                EditorApplication.Exit(0);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[Projection] FAILED " + e);
                EditorApplication.Exit(1);
            }
        }

        [MenuItem("VRSurgery/Preview Projection View")]
        public static void Capture()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            Camera cam = null;
            foreach (Camera c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (c.name == "ProjectionCamera") { cam = c; break; }
            }

            if (cam == null) { Debug.LogError("[Projection] No ProjectionCamera in the scene."); return; }

            Debug.Log($"[Projection] camera at {cam.transform.position} euler={cam.transform.eulerAngles} " +
                      $"fov={cam.fieldOfView} display={cam.targetDisplay + 1} clear={cam.clearFlags}");

            RenderTexture rt = new RenderTexture(1280, 800, 24);
            RenderTexture previous = cam.targetTexture;
            int previousDisplay = cam.targetDisplay;

            // targetDisplay is ignored when rendering to a texture, but reset it anyway so the
            // saved scene never picks up a stray display index from this preview.
            cam.targetDisplay = 0;
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture active = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D shot = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            shot.Apply();
            RenderTexture.active = active;

            cam.targetTexture = previous;
            cam.targetDisplay = previousDisplay;

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
            File.WriteAllBytes(OutputPath, shot.EncodeToPNG());
            Object.DestroyImmediate(shot);
            rt.Release();

            Debug.Log("[Projection] wrote " + OutputPath);
        }
    }
}
