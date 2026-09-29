using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace VRSurgery.Interaction
{
    /// <summary>
    /// The instrument lights up while a hand is close enough to take it, and stops the moment it
    /// is taken — the "you can grab this" glow every hands-on VR game has.
    ///
    /// A tint through a property block, not a material swap: the scalpel keeps its texture and
    /// its shine, it just brightens and turns faintly cyan, with a slow pulse so it reads as alive
    /// rather than selected. Nothing is allocated per frame and no material asset is touched.
    /// </summary>
    public class GrabGlow : MonoBehaviour
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColor = Shader.PropertyToID("_Color");

        [SerializeField] private XRGrabInteractable grab;
        [SerializeField] private Color tint = new Color(0.55f, 1f, 1f, 1f);
        [SerializeField, Range(1f, 2.5f)] private float brighten = 1.45f;
        [SerializeField, Min(0.1f)] private float pulseHz = 1.5f;

        private Renderer[] _renderers = new Renderer[0];
        private Color[] _original = new Color[0];
        private MaterialPropertyBlock _block;
        private float _clock;

        public bool IsGlowing { get; private set; }

        private void Awake()
        {
            if (grab == null) { grab = GetComponent<XRGrabInteractable>(); }
            Capture();
        }

        private void Capture()
        {
            _block = new MaterialPropertyBlock();
            _renderers = GetComponentsInChildren<Renderer>(true);
            _original = new Color[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                Material m = _renderers[i] != null ? _renderers[i].sharedMaterial : null;
                _original[i] = m == null ? Color.white
                    : m.HasProperty(BaseColor) ? m.GetColor(BaseColor)
                    : m.HasProperty(LegacyColor) ? m.GetColor(LegacyColor)
                    : Color.white;
            }
        }

        private void OnEnable()
        {
            if (grab == null) { return; }
            grab.hoverEntered.AddListener(OnHoverEntered);
            grab.hoverExited.AddListener(OnHoverExited);
            grab.selectEntered.AddListener(OnSelected);
        }

        private void OnDisable()
        {
            if (grab != null)
            {
                grab.hoverEntered.RemoveListener(OnHoverEntered);
                grab.hoverExited.RemoveListener(OnHoverExited);
                grab.selectEntered.RemoveListener(OnSelected);
            }

            SetGlow(false);
        }

        private void OnHoverEntered(HoverEnterEventArgs args)
        {
            if (!grab.isSelected) { SetGlow(true); }
        }

        private void OnHoverExited(HoverExitEventArgs args)
        {
            if (!grab.isHovered) { SetGlow(false); }
        }

        private void OnSelected(SelectEnterEventArgs args) => SetGlow(false);

        private void Update()
        {
            if (!IsGlowing) { return; }
            _clock += Time.deltaTime;
            Apply(0.75f + 0.25f * Mathf.Sin(_clock * pulseHz * Mathf.PI * 2f));
        }

        /// <summary>Turns the glow on or off. Called by the hover events; public for tests.</summary>
        public void SetGlow(bool on)
        {
            if (_block == null) { Capture(); }
            IsGlowing = on;
            _clock = 0f;

            if (on)
            {
                Apply(1f);
                return;
            }

            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null) { _renderers[i].SetPropertyBlock(null); }
            }
        }

        private void Apply(float strength)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null) { continue; }
                Color lit = _original[i] * Color.Lerp(Color.white, tint, strength) * Mathf.Lerp(1f, brighten, strength);
                lit.a = _original[i].a;
                _block.Clear();
                _block.SetColor(BaseColor, lit);
                _block.SetColor(LegacyColor, lit);
                _renderers[i].SetPropertyBlock(_block);
            }
        }
    }
}
