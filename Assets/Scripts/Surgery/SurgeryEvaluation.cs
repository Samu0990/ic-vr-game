using UnityEngine;
using VRSurgery.Tissue;

namespace VRSurgery.Surgery
{
    public enum SurgeryRank
    {
        C,
        B,
        A,
        S,
    }

    public readonly struct SurgeryScore
    {
        public readonly float Accuracy;
        public readonly float Efficiency;
        public readonly float Safety;
        public readonly float Technique;
        public readonly float Overall;
        public readonly SurgeryRank Rank;

        public SurgeryScore(float accuracy, float efficiency, float safety, float technique, float overall, SurgeryRank rank)
        {
            Accuracy = accuracy;
            Efficiency = efficiency;
            Safety = safety;
            Technique = technique;
            Overall = overall;
            Rank = rank;
        }

        public override string ToString() =>
            $"Rank {Rank} — overall {Overall:P0} (accuracy {Accuracy:P0}, efficiency {Efficiency:P0}, " +
            $"safety {Safety:P0}, technique {Technique:P0})";
    }

    /// <summary>
    /// Turns what the simulation recorded into a score. Kept separate from the mechanics so the
    /// scoring rules can be retuned without touching how anything behaves.
    /// </summary>
    public class SurgeryEvaluation : MonoBehaviour
    {
        [SerializeField] private SurgeryObjectiveSystem objectiveSystem;
        [SerializeField] private IncisionSystem incisionSystem;
        [SerializeField] private BleedingSystem bleedingSystem;

        [Header("Error weighting")]
        [SerializeField] private float minorErrorCost = 0.02f;
        [SerializeField] private float majorErrorCost = 0.08f;
        [SerializeField] private float criticalErrorCost = 0.25f;

        public int WarningCount { get; private set; }
        public int MinorErrorCount { get; private set; }
        public int MajorErrorCount { get; private set; }
        public int CriticalErrorCount { get; private set; }

        public int TotalErrorCount => MinorErrorCount + MajorErrorCount + CriticalErrorCount;

        public SurgeryScore LastScore { get; private set; }
        public bool HasScore { get; private set; }

        private void OnEnable()
        {
            SurgeryEvents.OnError += HandleError;
            SurgeryEvents.OnSurgeryCompleted += HandleSurgeryCompleted;
        }

        private void OnDisable()
        {
            SurgeryEvents.OnError -= HandleError;
            SurgeryEvents.OnSurgeryCompleted -= HandleSurgeryCompleted;
        }

        private void HandleError(ErrorSeverity severity, string message)
        {
            switch (severity)
            {
                case ErrorSeverity.Warning: WarningCount++; break;
                case ErrorSeverity.MinorError: MinorErrorCount++; break;
                case ErrorSeverity.MajorError: MajorErrorCount++; break;
                case ErrorSeverity.CriticalError: CriticalErrorCount++; break;
            }
        }

        private void HandleSurgeryCompleted()
        {
            LastScore = Evaluate();
            HasScore = true;
            Debug.Log($"[SurgeryEvaluation] {LastScore}");
        }

        public SurgeryScore Evaluate()
        {
            float accuracy = incisionSystem != null ? incisionSystem.AccuracyScore01() : 0f;
            float efficiency = EvaluateEfficiency();
            float safety = EvaluateSafety();
            float technique = EvaluateTechnique();

            float accuracyWeight = 0.4f;
            float efficiencyWeight = 0.2f;
            float safetyWeight = 0.3f;
            float techniqueWeight = 0.1f;

            SurgeryDefinition definition = objectiveSystem != null ? objectiveSystem.SurgeryDefinition : null;
            if (definition != null)
            {
                accuracyWeight = definition.AccuracyWeight;
                efficiencyWeight = definition.EfficiencyWeight;
                safetyWeight = definition.SafetyWeight;
                techniqueWeight = definition.TechniqueWeight;
            }

            float weightSum = accuracyWeight + efficiencyWeight + safetyWeight + techniqueWeight;
            if (weightSum <= 0f)
            {
                weightSum = 1f;
            }

            float overall = (accuracy * accuracyWeight
                           + efficiency * efficiencyWeight
                           + safety * safetyWeight
                           + technique * techniqueWeight) / weightSum;

            overall = Mathf.Clamp01(overall);
            return new SurgeryScore(accuracy, efficiency, safety, technique, overall, RankFor(overall));
        }

        private float EvaluateEfficiency()
        {
            if (objectiveSystem == null || objectiveSystem.SurgeryDefinition == null)
            {
                return 0f;
            }

            float target = objectiveSystem.SurgeryDefinition.TargetDurationSeconds;
            if (target <= 0f)
            {
                return 1f;
            }

            float elapsed = objectiveSystem.ElapsedSeconds;
            if (elapsed <= target)
            {
                return 1f;
            }

            // Falls off gradually rather than cliffing at the par time.
            return Mathf.Clamp01(1f - (elapsed - target) / (target * 2f));
        }

        private float EvaluateSafety()
        {
            float penalty = MinorErrorCount * minorErrorCost
                          + MajorErrorCount * majorErrorCost
                          + CriticalErrorCount * criticalErrorCost;

            if (incisionSystem != null && incisionSystem.Tissue != null)
            {
                penalty += incisionSystem.Tissue.Damage01 * 0.5f;
            }

            if (bleedingSystem != null)
            {
                penalty += Mathf.Clamp01(bleedingSystem.TotalBloodLoss / 20f) * 0.3f;
            }

            return Mathf.Clamp01(1f - penalty);
        }

        private float EvaluateTechnique()
        {
            if (incisionSystem == null || incisionSystem.Guide == null)
            {
                return 0f;
            }

            // Technique reads the worst moment, not the average: one bad slip matters.
            return incisionSystem.Guide.ScoreDeviation(incisionSystem.WorstDeviation);
        }

        public static SurgeryRank RankFor(float overall01)
        {
            if (overall01 >= 0.9f) return SurgeryRank.S;
            if (overall01 >= 0.75f) return SurgeryRank.A;
            if (overall01 >= 0.55f) return SurgeryRank.B;
            return SurgeryRank.C;
        }

        public void ResetEvaluation()
        {
            WarningCount = 0;
            MinorErrorCount = 0;
            MajorErrorCount = 0;
            CriticalErrorCount = 0;
            HasScore = false;
        }

        public void Bind(SurgeryObjectiveSystem objectives, IncisionSystem incision, BleedingSystem bleeding)
        {
            objectiveSystem = objectives;
            incisionSystem = incision;
            bleedingSystem = bleeding;
        }
    }
}
