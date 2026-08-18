using UnityEngine;
using VRSurgery.Haptics;
using VRSurgery.Tools;

namespace VRSurgery.Data
{
    [CreateAssetMenu(fileName = "ToolDefinition", menuName = "VRSurgery/Data/Tool Definition")]
    public class ToolDefinition : ScriptableObject
    {
        [SerializeField] private string toolId;
        [SerializeField] private string displayName;
        [SerializeField] private ToolType toolType;
        [SerializeField] private ToolCapability capabilities;
        [SerializeField] private HapticProfile hapticProfile;
        [SerializeField] private Vector3 gripPointLocalPosition;
        [SerializeField] private Vector3 gripPointLocalEulerAngles;

        public string ToolId => toolId;
        public string DisplayName => displayName;
        public ToolType ToolType => toolType;
        public ToolCapability Capabilities => capabilities;
        public HapticProfile HapticProfile => hapticProfile;
        public Vector3 GripPointLocalPosition => gripPointLocalPosition;
        public Vector3 GripPointLocalEulerAngles => gripPointLocalEulerAngles;

        public bool HasCapability(ToolCapability capability) => (capabilities & capability) == capability;

        /// <summary>Builds a definition in memory. Used by the scene builder and by tests.</summary>
        public static ToolDefinition Create(
            string id,
            string name,
            ToolType type,
            ToolCapability toolCapabilities,
            HapticProfile haptics = null)
        {
            ToolDefinition definition = CreateInstance<ToolDefinition>();
            definition.name = id;
            definition.toolId = id;
            definition.displayName = name;
            definition.toolType = type;
            definition.capabilities = toolCapabilities;
            definition.hapticProfile = haptics;
            return definition;
        }
    }
}
