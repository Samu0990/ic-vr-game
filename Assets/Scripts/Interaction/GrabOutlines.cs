using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VRSurgery.Interaction
{
    /// <summary>
    /// Outlines, in light blue, the one thing each empty hand would take if the visitor squeezed
    /// now — the Job Simulator cue. Not everything within reach: with the instruments side by
    /// side on the tray, lighting all of them says nothing, lighting the one the grab will get
    /// says everything.
    ///
    /// "The one it would take" is the first of the hand's valid targets, which XRI keeps sorted
    /// nearest first; a hand already holding something shows nothing. Whatever is outlined stops
    /// being outlined the moment it is taken.
    /// </summary>
    public class GrabOutlines : MonoBehaviour
    {
        private const string ShaderName = "VRSurgery/GrabOutline";

        [Tooltip("Material with the VRSurgery/GrabOutline shader. Assigned by the scene builder so the shader ships in the build.")]
        [SerializeField] private Material material;

        [SerializeField] private XRBaseInteractor[] hands = new XRBaseInteractor[0];

        private readonly List<IXRInteractable> _targets = new List<IXRInteractable>();
        private readonly List<InteractableOutline> _wanted = new List<InteractableOutline>(2);
        private readonly List<InteractableOutline> _shown = new List<InteractableOutline>(2);

        public Material Material => material;

        /// <summary>What is outlined right now.</summary>
        public IReadOnlyList<InteractableOutline> Shown => _shown;

        private void Awake()
        {
            if (material != null) { return; }
            Shader shader = Shader.Find(ShaderName);
            if (shader != null) { material = new Material(shader) { name = "ContornoPega (runtime)" }; }
            else { Debug.LogWarning("[Contorno] shader " + ShaderName + " ausente; nada será contornado."); }
        }

        private void LateUpdate()
        {
            _wanted.Clear();

            for (int h = 0; h < hands.Length; h++)
            {
                XRBaseInteractor hand = hands[h];
                if (hand == null || !hand.isActiveAndEnabled || hand.hasSelection) { continue; }

                _targets.Clear();
                hand.GetValidTargets(_targets);
                InteractableOutline next = FirstFree(_targets);
                if (next != null && !_wanted.Contains(next)) { _wanted.Add(next); }
            }

            Apply(_wanted);
        }

        private static InteractableOutline FirstFree(List<IXRInteractable> targets)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                if (!(targets[i] is IXRSelectInteractable selectable) || selectable.isSelected) { continue; }
                if (!(targets[i] is Component component) || component == null || !component.gameObject.activeInHierarchy) { continue; }
                return OutlineOf(component.gameObject);
            }

            return null;
        }

        /// <summary>Outlines exactly these and nothing else. Public so tests can drive it without a rig.</summary>
        public void Apply(IReadOnlyList<InteractableOutline> wanted)
        {
            for (int i = _shown.Count - 1; i >= 0; i--)
            {
                InteractableOutline outline = _shown[i];
                if (outline != null && Contains(wanted, outline)) { continue; }
                if (outline != null) { outline.Show(false, material); }
                _shown.RemoveAt(i);
            }

            for (int i = 0; i < wanted.Count; i++)
            {
                InteractableOutline outline = wanted[i];
                if (outline == null || _shown.Contains(outline)) { continue; }
                outline.Show(true, material);
                _shown.Add(outline);
            }
        }

        private static bool Contains(IReadOnlyList<InteractableOutline> list, InteractableOutline item)
        {
            for (int i = 0; i < list.Count; i++) { if (list[i] == item) { return true; } }
            return false;
        }

        /// <summary>The outline of an object, added the first time it is asked for.</summary>
        public static InteractableOutline OutlineOf(GameObject target)
        {
            InteractableOutline outline = target.GetComponent<InteractableOutline>();
            if (outline == null) { outline = target.AddComponent<InteractableOutline>(); }
            return outline;
        }

        public void Bind(Material outlineMaterial, XRBaseInteractor[] grabbingHands)
        {
            material = outlineMaterial;
            hands = grabbingHands ?? new XRBaseInteractor[0];
        }
    }
}
