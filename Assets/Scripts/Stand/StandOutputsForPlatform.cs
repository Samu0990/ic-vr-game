using UnityEngine;

namespace VRSurgery.Stand
{
    /// <summary>
    /// Switches off, on the headset, the outputs that only exist on a PC.
    ///
    /// The scene carries a spectator camera for Display 1, the projector camera for Display 2
    /// and the audience monitor for Display 3. A Quest running on its own has none of those
    /// displays, but a camera with nowhere to draw can still be drawn every frame — work the
    /// visitor pays for in frame rate and gets nothing back. On the headset the projector's
    /// picture goes to the notebook through <see cref="StandFrameStreamer"/> instead, and the
    /// audience monitor is the notebook's ranking screen.
    /// </summary>
    public class StandOutputsForPlatform : MonoBehaviour
    {
        [SerializeField] private GameObject[] pcOnlyOutputs = new GameObject[0];

        public void Bind(params GameObject[] outputs) => pcOnlyOutputs = outputs ?? new GameObject[0];

        /// <summary>True on a headset running on its own, where Displays 1 to 3 do not exist.</summary>
        public static bool IsStandaloneHeadset => Application.platform == RuntimePlatform.Android;

        private void Awake()
        {
            if (!IsStandaloneHeadset) { return; }

            foreach (GameObject output in pcOnlyOutputs)
            {
                if (output != null) { output.SetActive(false); }
            }

            Debug.Log($"[Estande] Óculos sozinho: {pcOnlyOutputs.Length} saídas de PC desligadas; projeção e ranking vão pelo notebook.");
        }
    }
}
