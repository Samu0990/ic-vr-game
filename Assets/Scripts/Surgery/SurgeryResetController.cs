using UnityEngine;
using VRSurgery.Interaction;
using VRSurgery.Tissue;

namespace VRSurgery.Surgery
{
    /// <summary>
    /// Puts the procedure back to its starting state without reloading the scene: tissue clean,
    /// bleeding cleared, objectives restarted, tools back in the tray. Fast iteration matters
    /// more here than almost anywhere else, because every test of a cut mechanic burns a wound.
    /// </summary>
    public class SurgeryResetController : MonoBehaviour
    {
        [SerializeField] private IncisionSystem incisionSystem;
        [SerializeField] private SurgeryObjectiveSystem objectiveSystem;
        [SerializeField] private SurgeryEvaluation evaluation;
        [SerializeField] private SurgeryTelemetry telemetry;
        [SerializeField] private SurgicalInteractable[] resettableTools;

        private Vector3[] _toolStartPositions;
        private Quaternion[] _toolStartRotations;

        public int ResetCount { get; private set; }

        private void Awake()
        {
            CaptureToolHomePositions();
        }

        public void CaptureToolHomePositions()
        {
            if (resettableTools == null)
            {
                return;
            }

            _toolStartPositions = new Vector3[resettableTools.Length];
            _toolStartRotations = new Quaternion[resettableTools.Length];

            for (int i = 0; i < resettableTools.Length; i++)
            {
                if (resettableTools[i] == null)
                {
                    continue;
                }

                _toolStartPositions[i] = resettableTools[i].transform.position;
                _toolStartRotations[i] = resettableTools[i].transform.rotation;
            }
        }

        public void ResetSurgery()
        {
            ResetCount++;

            if (incisionSystem != null)
            {
                incisionSystem.ResetSession();
            }

            if (evaluation != null)
            {
                evaluation.ResetEvaluation();
            }

            if (telemetry != null)
            {
                telemetry.Clear();
                telemetry.Log("Surgery reset");
            }

            ReturnToolsHome();

            if (objectiveSystem != null)
            {
                objectiveSystem.StartSurgery();
            }
        }

        private void ReturnToolsHome()
        {
            if (resettableTools == null || _toolStartPositions == null)
            {
                return;
            }

            for (int i = 0; i < resettableTools.Length && i < _toolStartPositions.Length; i++)
            {
                SurgicalInteractable tool = resettableTools[i];
                if (tool == null)
                {
                    continue;
                }

                if (tool.IsHeld)
                {
                    tool.OnReleased();
                }

                Rigidbody body = tool.GetComponent<Rigidbody>();
                if (body != null)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }

                tool.transform.SetPositionAndRotation(_toolStartPositions[i], _toolStartRotations[i]);

                Tools.BladeTip tip = tool.GetComponentInChildren<Tools.BladeTip>();
                if (tip != null)
                {
                    tip.ResetHistory();
                }
            }
        }

        public void Bind(
            IncisionSystem incision,
            SurgeryObjectiveSystem objectives,
            SurgeryEvaluation surgeryEvaluation,
            SurgeryTelemetry surgeryTelemetry,
            SurgicalInteractable[] tools)
        {
            incisionSystem = incision;
            objectiveSystem = objectives;
            evaluation = surgeryEvaluation;
            telemetry = surgeryTelemetry;
            resettableTools = tools;
            CaptureToolHomePositions();
        }
    }
}
