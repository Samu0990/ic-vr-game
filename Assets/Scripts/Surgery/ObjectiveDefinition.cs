using UnityEngine;
using VRSurgery.Tools;

namespace VRSurgery.Surgery
{
    public enum ObjectiveState
    {
        Locked,
        Available,
        Active,
        Completed,
        Failed,
        Skipped,
    }

    /// <summary>
    /// What finishes an objective. Keeping this as data means new procedures are authored as
    /// assets instead of new code, which is the point of §38 of the spec.
    /// </summary>
    public enum ObjectiveTrigger
    {
        GrabTool,
        CompleteIncision,
        ControlBleeding,
        ReleaseTool,
        Manual,
    }

    [CreateAssetMenu(fileName = "ObjectiveDefinition", menuName = "VRSurgery/Data/Objective Definition")]
    public class ObjectiveDefinition : ScriptableObject
    {
        [SerializeField] private string objectiveId;
        [SerializeField, TextArea] private string description;
        [SerializeField] private ObjectiveTrigger trigger = ObjectiveTrigger.Manual;

        [Tooltip("Which tool the trigger applies to, for tool-related triggers.")]
        [SerializeField] private ToolType requiredTool = ToolType.Scalpel;
        [SerializeField] private bool toolTypeMatters = true;

        [Header("Scoring")]
        [SerializeField] private float reward = 100f;
        [SerializeField] private float failurePenalty = 50f;

        public string ObjectiveId => string.IsNullOrEmpty(objectiveId) ? name : objectiveId;
        public string Description => description;
        public ObjectiveTrigger Trigger => trigger;
        public ToolType RequiredTool => requiredTool;
        public bool ToolTypeMatters => toolTypeMatters;
        public float Reward => reward;
        public float FailurePenalty => failurePenalty;

        /// <summary>Builds a definition in memory. Used by the scene builder and by tests.</summary>
        public static ObjectiveDefinition Create(
            string id,
            string text,
            ObjectiveTrigger objectiveTrigger,
            ToolType tool = ToolType.Scalpel,
            bool toolMatters = true,
            float rewardValue = 100f)
        {
            ObjectiveDefinition definition = CreateInstance<ObjectiveDefinition>();
            definition.name = id;
            definition.objectiveId = id;
            definition.description = text;
            definition.trigger = objectiveTrigger;
            definition.requiredTool = tool;
            definition.toolTypeMatters = toolMatters;
            definition.reward = rewardValue;
            return definition;
        }
    }
}
