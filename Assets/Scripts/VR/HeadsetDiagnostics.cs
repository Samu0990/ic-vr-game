using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace VRSurgery.VR
{
    /// <summary>
    /// Says in the Console, once per change, how the picture reaches the headset: whether a
    /// headset is running at all, through which OpenXR runtime, on which graphics API and colour
    /// space, with which render settings.
    ///
    /// A headset that looks wrong while the laptop looks right has a handful of causes and they
    /// all look the same from inside the headset: Unity not drawing to it at all (the runtime's
    /// own dark room shows instead), a streaming runtime that mishandles sRGB (the whole picture
    /// darker), or a camera setting that only the headset has. This line tells them apart
    /// without anyone having to go looking through menus with the headset on.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class HeadsetDiagnostics : MonoBehaviour
    {
        private bool _lastActive;

        private void Start() => Report();

        private void Update()
        {
            // XR can come up after the scene, or drop when the headset is taken off Link.
            if (XRSettings.isDeviceActive != _lastActive) { Report(); }
        }

        private void Report()
        {
            _lastActive = XRSettings.isDeviceActive;

            XRLoader loader = XRGeneralSettings.Instance != null && XRGeneralSettings.Instance.Manager != null
                ? XRGeneralSettings.Instance.Manager.activeLoader
                : null;

            UniversalRenderPipelineAsset urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            UniversalAdditionalCameraData camera = GetComponent<UniversalAdditionalCameraData>();

            string line = Describe(
                _lastActive,
                loader != null ? loader.name : string.Empty,
                _lastActive ? OpenXRRuntime.name : string.Empty,
                _lastActive ? OpenXRRuntime.version : string.Empty,
                SystemInfo.graphicsDeviceType.ToString(),
                QualitySettings.activeColorSpace.ToString(),
                QualitySettings.names.Length > 0 ? QualitySettings.names[QualitySettings.GetQualityLevel()] : "?",
                urp != null ? urp.name : "?",
                urp != null && urp.additionalLightsRenderingMode != LightRenderingMode.Disabled,
                camera != null && camera.renderPostProcessing,
                Application.isEditor);

            // A plain log, not a warning: Play without a headset is the everyday case on the
            // Linux laptop, with the simulator.
            Debug.Log(line);
        }

        /// <summary>The Console line, from plain values so it can be checked without a headset.</summary>
        public static string Describe(bool headsetRunning, string loader, string runtime, string runtimeVersion,
            string graphicsApi, string colorSpace, string quality, string pipeline, bool additionalLights,
            bool postProcessing, bool inEditor)
        {
            StringBuilder text = new StringBuilder("[Óculos] ");

            if (headsetRunning)
            {
                text.Append("rodando");
                if (!string.IsNullOrEmpty(runtime))
                {
                    text.Append(" pelo runtime '").Append(runtime).Append('\'');
                    if (!string.IsNullOrEmpty(runtimeVersion)) { text.Append(' ').Append(runtimeVersion); }
                }
                else if (!string.IsNullOrEmpty(loader))
                {
                    text.Append(" (").Append(loader).Append(')');
                }
            }
            else
            {
                text.Append("NENHUM óculos rodando");
                if (inEditor)
                {
                    text.Append(": o Play está só no notebook. Se o óculos está conectado e mostra uma sala " +
                                "escura, o Unity não está desenhando nele — ligue o Link no óculos com o app " +
                                "Meta Quest Link aberto (Windows) ou o streamer (ALVR/WiVRn no Linux) antes do Play.");
                }
            }

            text.Append(" · API ").Append(graphicsApi)
                .Append(" · cor ").Append(colorSpace)
                .Append(" · qualidade ").Append(quality)
                .Append(" · URP '").Append(pipeline).Append('\'')
                .Append(" · luzes extras ").Append(additionalLights ? "ligadas" : "DESLIGADAS")
                .Append(" · pós-processamento ").Append(postProcessing ? "ligado" : "desligado");

            return text.ToString();
        }
    }
}
