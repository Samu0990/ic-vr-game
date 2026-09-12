using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VRSurgery.EditorTools
{
    /// <summary>
    /// Renders the built scene to PNG files from fixed viewpoints.
    ///
    /// Exists because looking at the scene is the only way to catch a whole class of defect that
    /// every programmatic check passes: the port markers were drawn as squares while the hit test
    /// was radial, and the trocar stood upright on the tray like a tap. Both were invisible to
    /// component counts and triangle budgets.
    ///
    /// Renders through a RenderTexture rather than the Game view, so the output resolution is
    /// whatever is asked for instead of whatever the editor's window layout happens to be —
    /// a squashed layout was producing 640x112 strips.
    /// </summary>
    public static class SceneShots
    {
        private struct Shot
        {
            public string Name;
            public Vector3 From;
            public Vector3 Look;
            public int Width, Height;
            public float Fov;
        }

        private static readonly Shot[] Views =
        {
            new Shot { Name = "01_sala",    From = new Vector3(1.6f, 1.9f, -0.7f),
                       Look = new Vector3(0.1f, 1.15f, 0.35f), Width = 1100, Height = 720, Fov = 55f },
            new Shot { Name = "02_abdome",  From = new Vector3(0.52f, 1.62f, 0.10f),
                       Look = new Vector3(0.045f, 1.19f, 0.42f), Width = 1100, Height = 760, Fov = 48f },
            new Shot { Name = "03_portas",  From = new Vector3(0.06f, 1.42f, 0.42f),
                       Look = new Vector3(0.045f, 1.19f, 0.42f), Width = 1000, Height = 1000, Fov = 42f },
            new Shot { Name = "04_bandeja", From = new Vector3(0.30f, 1.18f, 0.62f),
                       Look = new Vector3(0.44f, 0.985f, 0.80f), Width = 1100, Height = 700, Fov = 45f },
            new Shot { Name = "05_trocar",  From = new Vector3(0.30f, 1.06f, 0.72f),
                       Look = new Vector3(0.39f, 0.99f, 0.80f), Width = 1100, Height = 620, Fov = 35f },
        };

        /// <summary>Entry point for `-executeMethod`. Scene path and output folder come from -sceneShotArgs.</summary>
        public static void CaptureFromCommandLine()
        {
            string scene = "Assets/Scenes/PortaDeEntrada.unity";
            string output = "SceneCaptures";

            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-shotScene") { scene = args[i + 1]; }
                if (args[i] == "-shotOut") { output = args[i + 1]; }
            }

            try
            {
                Capture(scene, output);
                Debug.Log("[Shots] SHOTS_OK");
                EditorApplication.Exit(0);
            }
            catch (System.Exception exception)
            {
                Debug.LogError("[Shots] SHOTS_FAILED " + exception);
                EditorApplication.Exit(1);
            }
        }

        public static void Capture(string scenePath, string outputFolder)
        {
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Directory.CreateDirectory(outputFolder);

            foreach (Shot shot in Views)
            {
                string path = Path.Combine(outputFolder, shot.Name + ".png");
                RenderTo(path, shot);
                Debug.Log($"[Shots] {path}");
            }
        }

        private static void RenderTo(string path, Shot shot)
        {
            GameObject holder = new GameObject("ShotCamera");
            Camera cam = holder.AddComponent<Camera>();
            cam.fieldOfView = shot.Fov;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 60f;
            cam.clearFlags = CameraClearFlags.Skybox;
            holder.transform.position = shot.From;
            holder.transform.rotation = Quaternion.LookRotation((shot.Look - shot.From).normalized, Vector3.up);

            RenderTexture target = new RenderTexture(shot.Width, shot.Height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4
            };

            cam.targetTexture = target;
            cam.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;

            Texture2D image = new Texture2D(shot.Width, shot.Height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0f, 0f, shot.Width, shot.Height), 0, 0);
            image.Apply();

            RenderTexture.active = previous;
            File.WriteAllBytes(path, image.EncodeToPNG());

            cam.targetTexture = null;
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(holder);
        }
    }
}
