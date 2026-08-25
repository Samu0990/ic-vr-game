using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRSurgery.Surgery;

namespace VRSurgery.Tests
{
    /// <summary>
    /// Loads a scene for headless verification.
    ///
    /// Batch mode still runs the render loop under `-nographics`, and drawing this scene against
    /// the null graphics device segfaults the editor inside Unity's batching/shadow jobs. That is
    /// an editor crash, not a game defect, but it makes the automated suite unusable — so cameras
    /// are told to draw nothing the moment a scene activates, before the first frame is submitted.
    /// Disabling them after the load is too late: frames render while the async load completes.
    ///
    /// Consequence to be honest about: this suite verifies simulation, layout and wiring, and
    /// deliberately verifies nothing about rendering. Anything visual — shadows, materials,
    /// the wound's on-screen appearance — is validated by eye and on hardware, never here.
    /// </summary>
    public static class HeadlessScene
    {
        private static bool _hooked;

        /// <summary>
        /// True only when Unity is running against the null graphics device (`-nographics`),
        /// which is the sole situation the crash occurs in. With a real device present the
        /// scene renders normally, so screenshots and visual checks still work.
        /// </summary>
        private static bool NullGraphicsDevice =>
            SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InstallRenderSuppression()
        {
            if (!NullGraphicsDevice)
            {
                return;
            }

            QualitySettings.shadows = ShadowQuality.Disable;
            QualitySettings.shadowDistance = 0f;

            if (_hooked)
            {
                return;
            }

            _hooked = true;
            SceneManager.sceneLoaded += (_, __) => { SuppressRendering(); RemoveSimulator(); };
            SuppressRendering();
            RemoveSimulator();
        }

        /// <summary>
        /// Takes the XR Interaction Simulator out of the scene under the null graphics device.
        ///
        /// The simulator exists so the project can be played with keyboard and mouse on a machine
        /// with no headset, which is how anyone without hardware opens it. Its Start() reaches for
        /// input devices that do not exist in batch mode and segfaults the editor before the first
        /// test reports — the whole run dies, not one test. Removing it here keeps the scene
        /// playable for people and runnable for the suite.
        /// </summary>
        private static void RemoveSimulator()
        {
            if (!NullGraphicsDevice)
            {
                return;
            }

            GameObject simulator = GameObject.Find("XR Interaction Simulator");
            if (simulator != null)
            {
                Object.DestroyImmediate(simulator);
            }
        }

        /// <summary>
        /// Blanks every camera's culling mask rather than disabling the component, so tests can
        /// still find the camera and read the player's head pose from its transform.
        /// </summary>
        private static void SuppressRendering()
        {
            if (!NullGraphicsDevice)
            {
                return;
            }

            Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            for (int i = 0; i < cameras.Length; i++)
            {
                cameras[i].cullingMask = 0;
                cameras[i].clearFlags = CameraClearFlags.SolidColor;
            }

            Light[] lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            for (int i = 0; i < lights.Length; i++)
            {
                lights[i].shadows = LightShadows.None;
            }
        }

        public static IEnumerator Load(string sceneName)
        {
            SurgeryEvents.ResetAll();
            InstallRenderSuppression();

            yield return SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);

            SuppressRendering();
            yield return null;
        }
    }
}
