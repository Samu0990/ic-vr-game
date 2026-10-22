using UnityEngine;
using UnityEngine.Rendering;

namespace VRSurgery.VR
{
    /// <summary>
    /// What the projector over the mannequin is allowed to show: the patient and what happens on
    /// it, nothing else.
    ///
    /// Most of what must stay off the mannequin — the room, the trays, the text — is moved by the
    /// scene builder to a layer the projection camera does not draw. What cannot change layer is
    /// listed here: anything with a collider, because the hands find grabbable and pokeable
    /// things on the Default layer only, and a restart button that no finger can press is worse
    /// than one that shows on the projector. These are switched off just for this camera's
    /// render and back on straight after, so the headset and the monitor never notice.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class ProjectionView : MonoBehaviour
    {
        [Tooltip("Renderers the projector never shows that cannot be moved off the Default layer.")]
        [SerializeField] private Renderer[] hidden = new Renderer[0];

        private Camera _camera;
        private Renderer[] _switchedOff = new Renderer[0];
        private int _count;

        public int HiddenCount => hidden != null ? hidden.Length : 0;

        /// <summary>True when this renderer is switched off for the projector's render.</summary>
        public bool Hides(Renderer renderer) => hidden != null && System.Array.IndexOf(hidden, renderer) >= 0;

        private void Awake() => _camera = GetComponent<Camera>();

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += HandleBegin;
            RenderPipelineManager.endCameraRendering += HandleEnd;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= HandleBegin;
            RenderPipelineManager.endCameraRendering -= HandleEnd;
            Restore();
        }

        private void HandleBegin(ScriptableRenderContext context, Camera camera)
        {
            if (camera != _camera || hidden == null) { return; }

            if (_switchedOff.Length < hidden.Length) { _switchedOff = new Renderer[hidden.Length]; }
            _count = 0;

            for (int i = 0; i < hidden.Length; i++)
            {
                Renderer r = hidden[i];
                if (r == null || !r.enabled) { continue; }
                r.enabled = false;
                _switchedOff[_count++] = r;
            }
        }

        private void HandleEnd(ScriptableRenderContext context, Camera camera)
        {
            if (camera == _camera) { Restore(); }
        }

        private void Restore()
        {
            for (int i = 0; i < _count; i++)
            {
                if (_switchedOff[i] != null) { _switchedOff[i].enabled = true; }
            }

            _count = 0;
        }

        public void Bind(Renderer[] offProjector) => hidden = offProjector ?? new Renderer[0];
    }
}
