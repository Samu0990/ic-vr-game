using System.Collections.Generic;
using UnityEngine;

namespace VRSurgery.Surgery
{
    /// <summary>
    /// A whole procedure as data: its objectives, its tolerances, its scoring weights.
    /// New surgeries should be new assets, not new code.
    /// </summary>
    [CreateAssetMenu(fileName = "SurgeryDefinition", menuName = "VRSurgery/Data/Surgery Definition")]
    public class SurgeryDefinition : ScriptableObject
    {
        [SerializeField] private string surgeryId = "vertical-slice";
        [SerializeField] private string displayName = "Test Incision";
        [SerializeField, TextArea] private string description;
        [SerializeField, Range(0, 4)] private int difficulty = 1;

        [SerializeField] private List<ObjectiveDefinition> objectives = new List<ObjectiveDefinition>();

        [Header("Targets")]
        [Tooltip("Par time in seconds. Finishing under this scores full efficiency.")]
        [SerializeField] private float targetDurationSeconds = 90f;

        [Header("Score weights (normalised internally)")]
        [SerializeField] private float accuracyWeight = 0.4f;
        [SerializeField] private float efficiencyWeight = 0.2f;
        [SerializeField] private float safetyWeight = 0.3f;
        [SerializeField] private float techniqueWeight = 0.1f;

        public string SurgeryId => surgeryId;
        public string DisplayName => displayName;
        public string Description => description;
        public int Difficulty => difficulty;
        public IReadOnlyList<ObjectiveDefinition> Objectives => objectives;
        public float TargetDurationSeconds => targetDurationSeconds;

        public float AccuracyWeight => accuracyWeight;
        public float EfficiencyWeight => efficiencyWeight;
        public float SafetyWeight => safetyWeight;
        public float TechniqueWeight => techniqueWeight;

        public void SetObjectives(IEnumerable<ObjectiveDefinition> newObjectives)
        {
            objectives = new List<ObjectiveDefinition>(newObjectives);
        }

        public static SurgeryDefinition Create(string id, string name, IEnumerable<ObjectiveDefinition> newObjectives)
        {
            SurgeryDefinition definition = CreateInstance<SurgeryDefinition>();
            definition.name = id;
            definition.surgeryId = id;
            definition.displayName = name;
            definition.SetObjectives(newObjectives);
            return definition;
        }
    }
}
