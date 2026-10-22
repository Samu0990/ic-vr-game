using UnityEngine;

namespace VRSurgery.Transplant
{
    /// <summary>
    /// The patient's own, failing heart: beating fast, weak and irregular when the chest is
    /// opened, and still from the moment cardioplegia arrests it.
    ///
    /// Before this the native heart lay motionless in an open chest, which reads as a dead patient
    /// rather than a sick one — and robbed the operation of its one before-and-after: the heart
    /// that comes out struggled, the heart that goes in beats strong.
    ///
    /// Lives on the always-active systems object and polls the pump, because the heart itself is
    /// switched off until the sternum opens and cannot watch anything while it is.
    /// </summary>
    public class NativeHeartRhythm : MonoBehaviour
    {
        [SerializeField] private TransplantProcedure procedure;
        [SerializeField] private Heartbeat heart;

        [Header("A failing heart")]
        [SerializeField, Range(30f, 140f)] private float beatsPerMinute = 112f;
        [SerializeField, Range(0f, 0.12f)] private float strength = 0.028f;
        [SerializeField, Range(0f, 0.4f)] private float irregularity = 0.18f;

        private void Start()
        {
            if (heart != null) { heart.Configure(beatsPerMinute, strength, irregularity); }
        }

        /// <summary>Pump steps up to and including cardioplegia: cannulate, cross-clamp, cardioplegia.</summary>
        private const int StepsToArrest = 3;

        private void Update() => Tick();

        public void Tick()
        {
            if (heart == null) { return; }

            // Beats until cardioplegia arrests it, and never again this round; the next visitor's
            // patient beats again. Asking only "is the pump arrested now" brought it back to life
            // the moment the cross-clamp came off — by then it was out of the body, in the basin,
            // and the monitor could follow its beat instead of the new heart's.
            bool shouldBeat = procedure == null || procedure.Bypass.CompletedSteps < StepsToArrest;

            if (shouldBeat && !heart.IsBeating) { heart.StartBeating(); }
            else if (!shouldBeat && heart.IsBeating) { heart.StopBeating(); }
        }

        public void Bind(TransplantProcedure transplant, Heartbeat nativeHeart)
        {
            procedure = transplant;
            heart = nativeHeart;
        }
    }
}
