using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRSurgery.Tissue;

namespace VRSurgery.EditorTools
{
    /// <summary>
    /// Renders still images of the built scene from the Editor, without entering Play Mode.
    ///
    /// Needed because this machine cannot screenshot the game any other way: Unity's `-nographics`
    /// batch mode segfaults inside its batching/shadow jobs against the null graphics device, so
    /// the automated suite runs with rendering switched off entirely. Run this with a real
    /// graphics device available (batch mode WITHOUT `-nographics`, DISPLAY set) to get a picture
    /// of what the player actually sees.
    /// </summary>
    public static class SceneStillCapture
    {
        private const string ScenePath = "Assets/Scenes/VerticalSlice.unity";
        private const string OutputDir = "SceneCaptures";

        [MenuItem("VRSurgery/Capture Scene Stills")]
        public static void CaptureMenu() => Capture();

        public static void CaptureFromCommandLine()
        {
            try
            {
                int written = Capture();
                Debug.Log($"[Capture] CAPTURE_OK {written} images");
                EditorApplication.Exit(written > 0 ? 0 : 1);
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"[Capture] CAPTURE_FAILED {exception}");
                EditorApplication.Exit(1);
            }
        }

        public static int Capture()
        {
            Debug.Log($"[Capture] graphicsDeviceType={SystemInfo.graphicsDeviceType}");
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Debug.LogError("[Capture] Null graphics device — cannot render. " +
                               "Run without -nographics and with DISPLAY set.");
                return 0;
            }

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Directory.CreateDirectory(OutputDir);

            TissueSurface tissue = Object.FindFirstObjectByType<TissueSurface>();
            Vector3 field = tissue != null ? tissue.transform.position : new Vector3(0f, 1.25f, 0.42f);

            GameObject rig = new GameObject("CaptureCamera");
            Camera camera = rig.AddComponent<Camera>();
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 50f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.06f, 0.07f);

            int written = 0;
            try
            {
                // The player's real viewpoint: standing eye height, looking at the operative field.
                written += Shoot(camera, "player_view", 1280, 800,
                    new Vector3(0f, 1.65f, 0f), field, 60f);

                // Three-quarter view of the whole workstation, to judge the composition.
                written += Shoot(camera, "workstation_overview", 1280, 800,
                    new Vector3(1.25f, 1.85f, -0.55f), new Vector3(0f, 1.15f, 0.35f), 50f);

                // Close on the patient's abdomen where the incision happens.
                written += Shoot(camera, "surgical_field", 1100, 800,
                    field + new Vector3(0.18f, 0.30f, -0.28f), field, 40f);

                // Along the body, to read the supine pose and the table fit.
                written += Shoot(camera, "patient_length", 1280, 700,
                    new Vector3(1.5f, 1.45f, 0.3f), new Vector3(0f, 1.12f, 0.3f), 45f);
            }
            finally
            {
                Object.DestroyImmediate(rig);
            }

            AssetDatabase.Refresh();
            return written;
        }

        [MenuItem("VRSurgery/Capture Incision Before-After")]
        public static void CaptureIncisionMenu() => CaptureIncision();

        public static void CaptureIncisionFromCommandLine()
        {
            try
            {
                int n = CaptureIncision();
                Debug.Log($"[Capture] INCISION_CAPTURE_OK {n} images");
                EditorApplication.Exit(n == 2 ? 0 : 1);
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"[Capture] INCISION_CAPTURE_FAILED {exception}");
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Renders the surgical region before and after the incision fires. The trigger is driven
        /// through the real code path — a tagged collider handed to TryIncise — rather than by
        /// swapping the material directly, so the picture shows what the mechanic actually does.
        /// The scene is never saved, so this leaves no incised region behind on disk.
        /// </summary>
        public static int CaptureIncision()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Debug.LogError("[Capture] Null graphics device — run without -nographics.");
                return 0;
            }

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Directory.CreateDirectory(OutputDir);

            IncisableSkin skin = Object.FindFirstObjectByType<IncisableSkin>();
            if (skin == null)
            {
                Debug.LogError("[Capture] No IncisableSkin in the scene.");
                return 0;
            }

            Vector3 region = skin.transform.position;

            GameObject rig = new GameObject("CaptureCamera");
            Camera camera = rig.AddComponent<Camera>();
            camera.nearClipPlane = 0.005f;
            camera.farClipPlane = 50f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.06f, 0.07f);

            Vector3 eye = region + new Vector3(0.16f, 0.26f, -0.24f);
            int written = 0;

            GameObject blade = null;
            try
            {
                written += Shoot(camera, "incision_before", 1100, 800, eye, region, 38f);

                blade = new GameObject("TempBladeProbe") { tag = "Scalpel" };
                SphereCollider probe = blade.AddComponent<SphereCollider>();
                probe.radius = 0.004f;
                probe.isTrigger = true;
                blade.transform.position = region;

                bool fired = skin.TryIncise(probe, region);
                Debug.Log($"[Capture] TryIncise fired={fired} count={skin.IncisionCount} " +
                          $"log='{skin.LastLogEntry}'");

                written += Shoot(camera, "incision_after", 1100, 800, eye, region, 38f);
            }
            finally
            {
                if (blade != null)
                {
                    Object.DestroyImmediate(blade);
                }

                Object.DestroyImmediate(rig);
            }

            return written;
        }

        private static int Shoot(Camera camera, string name, int width, int height,
            Vector3 position, Vector3 lookAt, float fieldOfView)
        {
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position));
            camera.fieldOfView = fieldOfView;

            RenderTexture target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4,
            };

            Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;

            try
            {
                camera.targetTexture = target;
                camera.Render();

                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();

                string path = Path.Combine(OutputDir, name + ".png");
                File.WriteAllBytes(path, image.EncodeToPNG());
                Debug.Log($"[Capture] wrote {path}");
                return 1;
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                Object.DestroyImmediate(image);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }
    }
}
