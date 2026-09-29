using System.Collections.Generic;
using UnityEngine;

namespace VRSurgery.Session
{
    /// <summary>
    /// Shows the name keyboard only when there is a name to type, and lights the key under the
    /// fingertip as it is being held.
    ///
    /// Left on for the whole round, a 30-key panel beside the patient is clutter in the one place
    /// the visitor is supposed to be looking; and a dwell keyboard with no sign of which key is
    /// filling is one where people press the wrong letter and cannot tell why.
    /// </summary>
    public class NameEntryPanel : MonoBehaviour
    {
        [SerializeField] private NameEntryController controller;
        [SerializeField] private NameEntryWorker worker;

        [Tooltip("Everything drawn by the panel: keycaps, labels, the backing board.")]
        [SerializeField] private List<Renderer> renderers = new List<Renderer>();

        [Tooltip("Keycap renderers, one per key, same order as the keys.")]
        [SerializeField] private List<Renderer> keycaps = new List<Renderer>();
        [SerializeField] private List<NameEntryKey> keys = new List<NameEntryKey>();

        [SerializeField] private Color idle = new Color(0.18f, 0.22f, 0.28f);
        [SerializeField] private Color held = new Color(0.25f, 0.85f, 1f);

        private bool _shown = true;

        private void Update() => Tick();

        public void Tick()
        {
            bool show = controller != null && controller.IsAwaitingName;
            if (show != _shown)
            {
                _shown = show;
                foreach (Renderer r in renderers)
                {
                    if (r != null) { r.enabled = show; }
                }
            }

            if (!show) { return; }

            NameEntryKey hovered = worker != null ? worker.HoveredKey : null;
            float hold = worker != null ? worker.Hold01 : 0f;

            for (int i = 0; i < keycaps.Count && i < keys.Count; i++)
            {
                Renderer cap = keycaps[i];
                if (cap == null || cap.sharedMaterial == null) { continue; }
                cap.sharedMaterial.color = keys[i] == hovered ? Color.Lerp(idle, held, 0.3f + 0.7f * hold) : idle;
            }
        }

        public void Bind(NameEntryController nameController, NameEntryWorker nameWorker,
            IEnumerable<Renderer> panelRenderers, IEnumerable<Renderer> caps, IEnumerable<NameEntryKey> panelKeys)
        {
            controller = nameController;
            worker = nameWorker;
            renderers = new List<Renderer>(panelRenderers);
            keycaps = new List<Renderer>(caps);
            keys = new List<NameEntryKey>(panelKeys);
            _shown = true;
        }
    }
}
