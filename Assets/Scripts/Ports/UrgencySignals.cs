using UnityEngine;
using VRSurgery.Session;

namespace VRSurgery.Ports
{
    /// <summary>
    /// Tells the player the clock is running out without using sound.
    ///
    /// That constraint is the concept's, and it is the right one for a stand: the room is loud,
    /// the headset's speakers leak, and half the audience is talking over it. So the signal is
    /// carried by the two channels a headset owns completely — the controllers buzzing harder,
    /// and a red vignette pulsing at the edge of vision.
    ///
    /// The vignette lives at the periphery on purpose. Tinting the centre of the view fights the
    /// thing the player is trying to aim at, and a full-screen colour wash in a headset is the
    /// fastest route to making someone queasy.
    /// </summary>
    public class UrgencySignals : MonoBehaviour
    {
        [SerializeField] private EventSessionController session;

        [Header("Vignette")]
        [Tooltip("Renderer of the peripheral ring. Expected to sit on the head, facing the eyes.")]
        [SerializeField] private Renderer vignette;

        [SerializeField] private Color vignetteColour = new Color(0.85f, 0.05f, 0.05f);

        [Tooltip("Opacity at a fully spent clock, at the peak of the pulse.")]
        [SerializeField, Range(0f, 1f)] private float peakAlpha = 0.55f;

        [Header("Pulse")]
        [Tooltip("Pulses per second when the clock has just started to matter.")]
        [SerializeField, Min(0.1f)] private float slowestPulseHz = 0.7f;

        [Tooltip("Pulses per second as the clock runs out. A rising rate reads as urgency even " +
                 "when the colour is already saturated.")]
        [SerializeField, Min(0.1f)] private float fastestPulseHz = 2.6f;

        [Tooltip("Urgency below this leaves the player alone.")]
        [SerializeField, Range(0f, 1f)] private float onsetUrgency = 0.4f;

        [Header("Haptics")]
        [Tooltip("Controller amplitude at the peak of the pulse, at a fully spent clock.")]
        [SerializeField, Range(0f, 1f)] private float peakAmplitude = 0.8f;

        [SerializeField] private float pulseDurationSeconds = 0.12f;

        /// <summary>0..1 after the onset curve. What both channels are driven from.</summary>
        public float Intensity01 { get; private set; }

        /// <summary>Current vignette opacity, including the pulse. Read by tests.</summary>
        public float VignetteAlpha { get; private set; }

        /// <summary>How many haptic pulses have been dispatched this round.</summary>
        public int PulseCount { get; private set; }

        private MaterialPropertyBlock _block;
        private float _phase;
        private bool _pulsedThisCycle;

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            ApplyVignette(0f);
        }

        private void OnDisable() => ApplyVignette(0f);

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Advances the signal. Split out so the pacing is testable without frames.</summary>
        public void Tick(float deltaTime)
        {
            if (session == null || !session.IsRunning || onsetUrgency >= 1f)
            {
                Intensity01 = 0f;
                _phase = 0f;
                ApplyVignette(0f);
                return;
            }

            Intensity01 = Mathf.InverseLerp(onsetUrgency, 1f, session.Urgency01);

            if (Intensity01 <= 0f)
            {
                _phase = 0f;
                ApplyVignette(0f);
                return;
            }

            float hz = Mathf.Lerp(slowestPulseHz, fastestPulseHz, Intensity01);
            _phase += deltaTime * hz;
            if (_phase >= 1f)
            {
                _phase -= Mathf.Floor(_phase);
                _pulsedThisCycle = false;
            }

            // A raised cosine rather than a sine: it sits near zero for most of the cycle and
            // spikes, which reads as a heartbeat instead of a throb.
            float shape = Mathf.Pow(Mathf.Max(0f, Mathf.Cos((_phase - 0.5f) * Mathf.PI * 2f) * 0.5f + 0.5f), 3f);

            ApplyVignette(shape * peakAlpha * Intensity01);

            if (!_pulsedThisCycle && shape > 0.75f)
            {
                _pulsedThisCycle = true;
                PulseCount++;
                SendPulse(peakAmplitude * Intensity01);
            }
        }

        private void SendPulse(float amplitude)
        {
            // Routed through the XR input system's own haptic channel. Kept here rather than in
            // HapticManager because that one is keyed to procedure events; this is a continuous
            // signal about the clock, which is a different thing entirely.
            UnityEngine.XR.InputDevice left = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(
                UnityEngine.XR.XRNode.LeftHand);
            UnityEngine.XR.InputDevice right = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(
                UnityEngine.XR.XRNode.RightHand);

            foreach (UnityEngine.XR.InputDevice device in new[] { left, right })
            {
                if (device.isValid &&
                    device.TryGetHapticCapabilities(out UnityEngine.XR.HapticCapabilities caps) &&
                    caps.supportsImpulse)
                {
                    device.SendHapticImpulse(0, Mathf.Clamp01(amplitude), pulseDurationSeconds);
                }
            }
        }

        private void ApplyVignette(float alpha)
        {
            VignetteAlpha = alpha;

            if (vignette == null)
            {
                return;
            }

            // Property block, not a material instance: writing to sharedMaterial would leak an
            // edit into the asset, and instancing one per frame would churn the heap.
            vignette.GetPropertyBlock(_block);
            _block.SetColor("_BaseColor", new Color(
                vignetteColour.r, vignetteColour.g, vignetteColour.b, alpha));
            vignette.SetPropertyBlock(_block);

            vignette.enabled = alpha > 0.001f;
        }

        public void Bind(EventSessionController controller, Renderer peripheralVignette)
        {
            session = controller;
            vignette = peripheralVignette;
        }
    }
}
