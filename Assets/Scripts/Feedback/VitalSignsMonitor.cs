using UnityEngine;
using VRSurgery.Transplant;

namespace VRSurgery.Feedback
{
    /// <summary>
    /// The anaesthesia monitor: a sweeping ECG, a pleth wave, heart rate, saturation and blood
    /// pressure — and the one beep everybody in an operating room is listening to.
    ///
    /// It tells the story of this operation in the numbers a visitor already recognises from
    /// television. Before the pump the failing heart runs fast and weak. On bypass the trace goes
    /// flat and the pressure stops pulsing, because the machine is doing the work. When the donor
    /// heart starts, the rhythm comes back strong. A leaking anastomosis drops the pressure and
    /// lifts the rate, in red, while it lasts.
    /// </summary>
    public class VitalSignsMonitor : MonoBehaviour
    {
        [Header("Sources")]
        [SerializeField] private TransplantProcedure procedure;
        [SerializeField] private Heartbeat donorHeart;
        [SerializeField] private Heartbeat nativeHeart;
        [SerializeField] private AnastomosisWorker anastomosis;

        [Header("Screen")]
        [SerializeField] private LineRenderer ecg;
        [SerializeField] private LineRenderer pleth;
        [SerializeField] private TextMesh heartRateText;
        [SerializeField] private TextMesh saturationText;
        [SerializeField] private TextMesh pressureText;
        [SerializeField] private TextMesh statusText;

        [Tooltip("Width and height of each trace, in the monitor's local metres.")]
        [SerializeField] private Vector2 traceSize = new Vector2(0.34f, 0.07f);

        [SerializeField, Min(16)] private int samples = 160;

        [Tooltip("Seconds of signal across the width of the screen.")]
        [SerializeField, Min(1f)] private float sweepSeconds = 4f;

        [Header("Sound")]
        [SerializeField] private AudioSource beepSource;
        [SerializeField, Range(0f, 1f)] private float beepVolume = 0.35f;

        private float[] _ecgValues;
        private float[] _plethValues;
        private float _sampleClock;
        private float _beatPhase;
        private int _cursor;
        private float _textClock = 1f;
        private float _noiseSeed;
        private bool _widthScaled;

        /// <summary>The rhythm currently shown. Exposed for tests.</summary>
        public VitalsState State { get; private set; } = VitalsState.FailingHeart;

        public int HeartRate { get; private set; }
        public int Systolic { get; private set; }
        public int Diastolic { get; private set; }
        public int Saturation { get; private set; }

        public enum VitalsState { FailingHeart, OnBypass, NewHeart, Bleeding }

        private void Awake()
        {
            _ecgValues = new float[samples];
            _plethValues = new float[samples];
            _noiseSeed = Random.value * 100f;

            if (ecg != null) { ecg.positionCount = samples; ecg.useWorldSpace = false; }
            if (pleth != null) { pleth.positionCount = samples; pleth.useWorldSpace = false; }

            // Line width is in world units and ignores the transform's scale; a monitor enlarged
            // for the wall would otherwise draw its trace hair-thin.
            if (!_widthScaled)
            {
                _widthScaled = true;
                float scale = Mathf.Abs(transform.lossyScale.y);
                if (ecg != null) { ecg.widthMultiplier *= scale; }
                if (pleth != null) { pleth.widthMultiplier *= scale; }
            }
        }

        private void Update() => Tick(Time.deltaTime);

        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f) { return; }
            if (_ecgValues == null || _ecgValues.Length != samples) { Awake(); }

            State = Resolve();
            SetTargets(State);

            // The heart in the chest sets the pace when it can be seen: the trace, the number and
            // the beep follow the organ the visitor is looking at, beat for beat.
            Heartbeat visible = VisibleHeart();
            if (visible != null && State != VitalsState.Bleeding)
            {
                HeartRate = Mathf.RoundToInt(visible.BeatsPerMinute);
            }

            float frameStart = _beatPhase;
            float beatsPerSecond = HeartRate / 60f;
            float step = sweepSeconds / samples;
            _sampleClock += deltaTime;

            while (_sampleClock >= step)
            {
                _sampleClock -= step;

                _beatPhase += beatsPerSecond * step;
                if (_beatPhase >= 1f) { _beatPhase -= 1f; }

                _ecgValues[_cursor] = State == VitalsState.OnBypass
                    ? Mathf.PerlinNoise(_noiseSeed, Time.time * 3f) * 0.06f - 0.03f
                    : Ecg(_beatPhase);

                _plethValues[_cursor] = State == VitalsState.OnBypass
                    ? 0.15f
                    : Pleth(_beatPhase) * (State == VitalsState.FailingHeart ? 0.6f : 1f);

                _cursor = (_cursor + 1) % samples;
            }

            if (visible != null && State != VitalsState.OnBypass)
            {
                // Electrical just ahead of mechanical: the R wave leads the squeeze slightly.
                _beatPhase = Mathf.Repeat(visible.Phase01 + 0.1f, 1f);
            }

            // R wave: the beep lands on it, as on a real monitor.
            if (State != VitalsState.OnBypass && Crossed(frameStart, _beatPhase, 0.12f)) { Beep(); }

            Draw(ecg, _ecgValues, 0.5f);
            Draw(pleth, _plethValues, 0.8f);

            _textClock += deltaTime;
            if (_textClock >= 1f)
            {
                _textClock = 0f;
                WriteNumbers();
            }
        }

        /// <summary>The beating heart the visitor can actually see, if any: the new one first.</summary>
        private Heartbeat VisibleHeart()
        {
            if (donorHeart != null && donorHeart.IsBeating && donorHeart.isActiveAndEnabled) { return donorHeart; }
            if (nativeHeart != null && nativeHeart.IsBeating && nativeHeart.isActiveAndEnabled) { return nativeHeart; }
            return null;
        }

        /// <summary>True if going from <paramref name="from"/> to <paramref name="to"/> passed <paramref name="mark"/>, wrapping at 1.</summary>
        private static bool Crossed(float from, float to, float mark)
        {
            if (Mathf.Approximately(from, to)) { return false; }
            return to >= from ? from < mark && to >= mark : from < mark || to >= mark;
        }

        /// <summary>The patient's own heart, so the trace follows it until it is arrested.</summary>
        public void BindNativeHeart(Heartbeat heart) => nativeHeart = heart;

        private VitalsState Resolve()
        {
            if (anastomosis != null && anastomosis.BleedingSite != null) { return VitalsState.Bleeding; }
            if (donorHeart != null && donorHeart.IsBeating) { return VitalsState.NewHeart; }
            if (procedure != null && procedure.Bypass.IsOnPump) { return VitalsState.OnBypass; }
            return VitalsState.FailingHeart;
        }

        private void SetTargets(VitalsState state)
        {
            switch (state)
            {
                case VitalsState.OnBypass:
                    HeartRate = 0; Systolic = 65; Diastolic = 65; Saturation = 99; break;
                case VitalsState.NewHeart:
                    HeartRate = 88; Systolic = 118; Diastolic = 74; Saturation = 98; break;
                case VitalsState.Bleeding:
                    HeartRate = 128; Systolic = 78; Diastolic = 46; Saturation = 95; break;
                default:
                    HeartRate = 112; Systolic = 86; Diastolic = 54; Saturation = 92; break;
            }
        }

        /// <summary>One PQRST complex over a beat, as a sum of Gaussians.</summary>
        public static float Ecg(float phase)
        {
            float p = 0.12f * G(phase, 0.02f, 0.025f);
            float q = -0.12f * G(phase, 0.105f, 0.008f);
            float r = 1.0f * G(phase, 0.12f, 0.009f);
            float s = -0.22f * G(phase, 0.137f, 0.01f);
            float t = 0.28f * G(phase, 0.36f, 0.045f);
            return p + q + r + s + t;
        }

        private static float Pleth(float phase)
        {
            float rise = G(phase, 0.25f, 0.07f);
            float notch = 0.35f * G(phase, 0.48f, 0.06f);
            return rise + notch;
        }

        private static float G(float x, float mu, float sigma)
        {
            float d = (x - mu) / sigma;
            return Mathf.Exp(-0.5f * d * d);
        }

        private void Draw(LineRenderer line, float[] values, float scale)
        {
            if (line == null) { return; }
            if (line.positionCount != samples) { line.positionCount = samples; }

            for (int i = 0; i < samples; i++)
            {
                // Sweep display: the newest sample is at the cursor and the old trace is
                // overwritten ahead of it, with a short gap so the eye can find the write head.
                int age = (_cursor - i + samples) % samples;
                float value = age < 4 ? 0f : values[i];
                float x = (i / (float)(samples - 1) - 0.5f) * traceSize.x;
                float y = value * traceSize.y * scale;
                line.SetPosition(i, new Vector3(x, y, 0f));
            }
        }

        private void WriteNumbers()
        {
            int jitter = Mathf.RoundToInt((Mathf.PerlinNoise(Time.time * 0.3f, _noiseSeed) - 0.5f) * 4f);

            if (heartRateText != null)
            {
                heartRateText.text = HeartRate <= 0 ? "FC --" : $"FC {HeartRate + jitter}";
                heartRateText.color = State == VitalsState.Bleeding ? new Color(1f, 0.35f, 0.3f) : new Color(0.3f, 1f, 0.45f);
            }

            if (saturationText != null)
            {
                saturationText.text = $"SpO2 {Saturation}%";
            }

            if (pressureText != null)
            {
                pressureText.text = State == VitalsState.OnBypass
                    ? $"PAM {Systolic}"
                    : $"PA {Systolic + jitter}/{Diastolic}";
                pressureText.color = State == VitalsState.Bleeding || State == VitalsState.FailingHeart
                    ? new Color(1f, 0.45f, 0.35f)
                    : new Color(1f, 0.9f, 0.9f);
            }

            if (statusText != null)
            {
                statusText.text = State switch
                {
                    VitalsState.OnBypass => "CEC — BOMBA LIGADA",
                    VitalsState.NewHeart => "RITMO SINUSAL",
                    VitalsState.Bleeding => "ALARME: HIPOTENSÃO",
                    _ => "INSUFICIÊNCIA CARDÍACA",
                };
                statusText.color = State == VitalsState.Bleeding ? new Color(1f, 0.3f, 0.25f) : new Color(1f, 0.85f, 0.35f);
            }
        }

        private void Beep()
        {
            if (beepSource == null) { return; }
            // Pitch follows saturation, the way real monitors do: a falling tone is a warning
            // everyone in the room hears without looking.
            beepSource.pitch = Mathf.Lerp(0.8f, 1f, Mathf.InverseLerp(85f, 99f, Saturation));
            beepSource.PlayOneShot(ProceduralTones.MonitorBeep, beepVolume);
        }

        public void Bind(TransplantProcedure transplant, Heartbeat donor, AnastomosisWorker vessels,
            LineRenderer ecgLine, LineRenderer plethLine, TextMesh rate, TextMesh saturation,
            TextMesh pressure, TextMesh status, AudioSource source)
        {
            procedure = transplant;
            donorHeart = donor;
            anastomosis = vessels;
            ecg = ecgLine;
            pleth = plethLine;
            heartRateText = rate;
            saturationText = saturation;
            pressureText = pressure;
            statusText = status;
            beepSource = source;
        }
    }
}
