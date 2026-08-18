using System.Collections;
using UnityEngine;
using VRSurgery.Interaction;
using VRSurgery.Tissue;
using VRSurgery.Tools;

namespace VRSurgery.Diagnostics
{
    /// <summary>
    /// Drives the scalpel through a complete incision with no hardware attached: grabs the tool,
    /// lowers it onto the tissue, sweeps it along the guide, then lifts off.
    ///
    /// This exists because "the cut works" cannot be verified by reading code, and on this
    /// machine there is not always a headset to verify it by hand. Press Play with this enabled
    /// and the whole milestone runs and reports itself in the console.
    /// </summary>
    public class SurgeryProbe : MonoBehaviour
    {
        [Header("Targets")]
        [SerializeField] private ScalpelTool scalpel;
        [SerializeField] private IncisionSystem incisionSystem;

        [Header("Motion")]
        [Tooltip("How deep below the tissue surface the tip is driven, in metres.")]
        [SerializeField] private float cutDepth = 0.012f;
        [Tooltip("Seconds the sweep along the guide takes.")]
        [SerializeField] private float sweepDuration = 2.5f;
        [Tooltip("Extra travel past each end of the guide, in metres.")]
        [SerializeField] private float overshoot = 0.012f;

        [Header("Execution")]
        [SerializeField] private bool runOnStart = true;
        [SerializeField] private float startDelay = 0.5f;

        public bool IsRunning { get; private set; }
        public bool HasCompleted { get; private set; }

        /// <summary>
        /// Whether the probe drives itself on Start. Tests drive the coroutine directly and
        /// must be able to switch the automatic run off before the object activates.
        /// </summary>
        public bool RunOnStart
        {
            get => runOnStart;
            set => runOnStart = value;
        }

        private ProbeHand _hand;

        private void Awake()
        {
            _hand = GetComponent<ProbeHand>();
            if (_hand == null)
            {
                _hand = gameObject.AddComponent<ProbeHand>();
            }
        }

        private IEnumerator Start()
        {
            if (!runOnStart)
            {
                yield break;
            }

            yield return new WaitForSeconds(startDelay);
            yield return RunIncision();
        }

        public void Bind(ScalpelTool tool, IncisionSystem system)
        {
            scalpel = tool;
            incisionSystem = system;
        }

        /// <summary>
        /// Runs the full grab -> cut -> release sequence. Yieldable so PlayMode tests can await it.
        /// </summary>
        public IEnumerator RunIncision()
        {
            if (scalpel == null || incisionSystem == null || incisionSystem.Tissue == null)
            {
                Debug.LogError("[SurgeryProbe] Not bound to a scalpel and tissue; nothing to run.", this);
                yield break;
            }

            IsRunning = true;

            TissueSurface tissue = incisionSystem.Tissue;
            IncisionGuide guide = incisionSystem.Guide;

            Vector3 startLocal;
            Vector3 endLocal;
            if (guide != null && guide.Path.Count >= 2)
            {
                startLocal = guide.Path[0];
                endLocal = guide.Path[guide.Path.Count - 1];
            }
            else
            {
                startLocal = new Vector3(-tissue.HalfExtents.x * 0.6f, 0f, 0f);
                endLocal = new Vector3(tissue.HalfExtents.x * 0.6f, 0f, 0f);
            }

            Vector3 direction = (endLocal - startLocal).normalized;
            startLocal -= direction * overshoot;
            endLocal += direction * overshoot;

            SurgicalInteractable interactable = scalpel.GetComponent<SurgicalInteractable>();
            CuttingInteractor cutter = scalpel.CuttingInteractor;
            BladeTip tip = scalpel.BladeTip;

            if (cutter != null)
            {
                cutter.BindTissue(incisionSystem);
            }

            // 1. Grab the scalpel.
            _hand.TryGrab(interactable);
            yield return new WaitForFixedUpdate();

            // 2. Approach: place the tip above the start of the guide, then settle so the tip
            //    history is continuous before the blade ever touches tissue.
            Vector3 approachLocal = startLocal + Vector3.up * 0.03f;
            MoveTipTo(tissue, tip, approachLocal);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // 3. Lower to cutting depth.
            Vector3 cutStart = startLocal - Vector3.up * cutDepth;
            MoveTipTo(tissue, tip, cutStart);
            yield return new WaitForFixedUpdate();

            // 4. Sweep along the guide.
            Vector3 cutEnd = endLocal - Vector3.up * cutDepth;
            float elapsed = 0f;
            while (elapsed < sweepDuration)
            {
                elapsed += Time.fixedDeltaTime;
                float t = Mathf.Clamp01(elapsed / sweepDuration);
                MoveTipTo(tissue, tip, Vector3.Lerp(cutStart, cutEnd, t));
                yield return new WaitForFixedUpdate();
            }

            // 5. Lift off.
            MoveTipTo(tissue, tip, endLocal + Vector3.up * 0.04f);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            IsRunning = false;
            HasCompleted = true;

            Debug.Log($"[SurgeryProbe] Finished. state={tissue.State} " +
                      $"length={tissue.IncisionLength:F4}m peakDepth={tissue.PeakDepth01:F2} " +
                      $"points={tissue.IncisionPoints.Count} progress={incisionSystem.Progress01:P0} " +
                      $"avgDeviation={incisionSystem.AverageDeviation * 1000f:F1}mm");
        }

        /// <summary>
        /// Moves the whole tool so that the blade tip lands on the requested tissue-local point.
        /// Moving the tip's own transform directly would desync it from the tool body.
        /// </summary>
        private void MoveTipTo(TissueSurface tissue, BladeTip tip, Vector3 tissueLocalTarget)
        {
            Vector3 worldTarget = tissue.TissueLocalToWorld(tissueLocalTarget);

            if (tip == null)
            {
                scalpel.transform.position = worldTarget;
                return;
            }

            Vector3 offset = scalpel.transform.position - tip.transform.position;
            scalpel.transform.position = worldTarget + offset;
        }
    }
}
