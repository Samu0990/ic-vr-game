using UnityEngine;
using VRSurgery.Data;
using VRSurgery.Interaction;
using VRSurgery.Surgery;

namespace VRSurgery.Tools
{
    /// <summary>
    /// Base for all surgical tools. Wires a ToolDefinition (static data) to the
    /// SurgicalInteractable grab component on the same GameObject, and republishes grab
    /// events onto the shared event bus so audio/score/objectives never need a direct
    /// reference to any individual tool. Tool-specific behavior (ScalpelTool, ForcepsTool, ...)
    /// derives from this rather than growing this class.
    /// </summary>
    [RequireComponent(typeof(SurgicalInteractable))]
    public abstract class SurgicalTool : MonoBehaviour
    {
        [SerializeField] private ToolDefinition toolDefinition;

        protected SurgicalInteractable Interactable { get; private set; }

        public ToolDefinition ToolDefinition => toolDefinition;
        public ToolType ToolType => toolDefinition != null ? toolDefinition.ToolType : default;
        public string ToolId => toolDefinition != null ? toolDefinition.ToolId : name;

        public bool IsHeld => Interactable != null && Interactable.IsHeld;
        public IHandInteractor Holder => Interactable != null ? Interactable.CurrentHolder : null;

        public bool HasCapability(ToolCapability capability) =>
            toolDefinition != null && toolDefinition.HasCapability(capability);

        protected virtual void Awake()
        {
            Interactable = GetComponent<SurgicalInteractable>();
        }

        protected virtual void OnEnable()
        {
            Interactable.Grabbed += HandleGrabbedInternal;
            Interactable.Released += HandleReleasedInternal;
        }

        protected virtual void OnDisable()
        {
            Interactable.Grabbed -= HandleGrabbedInternal;
            Interactable.Released -= HandleReleasedInternal;
        }

        private void HandleGrabbedInternal(SurgicalInteractable interactable, IHandInteractor holder)
        {
            SurgeryEvents.RaiseToolGrabbed(this, holder);
            HandleGrabbed(interactable, holder);
        }

        private void HandleReleasedInternal(SurgicalInteractable interactable, IHandInteractor previousHolder)
        {
            SurgeryEvents.RaiseToolReleased(this, previousHolder);
            HandleReleased(interactable, previousHolder);
        }

        /// <summary>Allows the tool definition to be supplied at runtime (scene builder, tests).</summary>
        public void SetToolDefinition(ToolDefinition definition)
        {
            toolDefinition = definition;
        }

        protected virtual void HandleGrabbed(SurgicalInteractable interactable, IHandInteractor holder) { }
        protected virtual void HandleReleased(SurgicalInteractable interactable, IHandInteractor previousHolder) { }
    }
}
