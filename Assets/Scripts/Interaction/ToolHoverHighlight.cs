using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace VRSurgery.Interaction
{
    /// <summary>
    /// Outlines an instrument while a hand is close enough to grab it, so the player can tell
    /// what is reachable before committing to a grab.
    ///
    /// Implemented as a material swap rather than a custom outline shader: the affordance only
    /// has to read as "this one is grabbable", and a swap costs nothing to maintain.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class ToolHoverHighlight : MonoBehaviour
    {
        [SerializeField] private XRGrabInteractable grab;
        [SerializeField] private Renderer[] renderers;
        [SerializeField] private Material highlightMaterial;

        private Material[] _original;

        /// <summary>True while the highlight is showing.</summary>
        public bool IsHighlighted { get; private set; }

        private void Awake()
        {
            if (grab == null) { grab = GetComponent<XRGrabInteractable>(); }
            if (renderers == null || renderers.Length == 0)
            {
                renderers = GetComponentsInChildren<Renderer>();
            }

            _original = new Material[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                _original[i] = renderers[i] != null ? renderers[i].sharedMaterial : null;
            }
        }

        private void OnEnable()
        {
            grab.hoverEntered.AddListener(OnHoverEntered);
            grab.hoverExited.AddListener(OnHoverExited);
        }

        private void OnDisable()
        {
            grab.hoverEntered.RemoveListener(OnHoverEntered);
            grab.hoverExited.RemoveListener(OnHoverExited);
            SetHighlight(false);
        }

        private void OnHoverEntered(HoverEnterEventArgs args) => SetHighlight(true);

        private void OnHoverExited(HoverExitEventArgs args)
        {
            // Two hands can hover the same tool; only drop the highlight when the last one leaves.
            if (grab.isHovered) { return; }

            SetHighlight(false);
        }

        public void SetHighlight(bool on)
        {
            if (highlightMaterial == null || renderers == null) { return; }

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) { continue; }

                renderers[i].sharedMaterial = on ? highlightMaterial : _original[i];
            }

            IsHighlighted = on;
        }
    }
}
