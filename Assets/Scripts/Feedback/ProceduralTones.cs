using System.Collections.Generic;
using UnityEngine;

namespace VRSurgery.Feedback
{
    /// <summary>
    /// Short sounds synthesised at runtime, so the operating room can beep, chime and alarm
    /// without the project having to carry (or license) a single audio file.
    ///
    /// Every clip is a sum of sine partials under an attack/release envelope. That is enough for
    /// what a theatre actually sounds like — monitors and pumps are electronic tones, not
    /// recordings — and it costs a few kilobytes of memory built once and cached.
    /// </summary>
    public static class ProceduralTones
    {
        private const int SampleRate = 44100;

        private static readonly Dictionary<string, AudioClip> Cache = new Dictionary<string, AudioClip>();

        /// <summary>The monitor's pulse beep: one short, clean tone.</summary>
        public static AudioClip MonitorBeep => Get("monitor-beep", () => Tone("monitor-beep", 0.09f, 0.35f, 1046f));

        /// <summary>A step done: two rising notes.</summary>
        public static AudioClip StepChime => Get("step-chime", () => Sequence("step-chime", 0.32f,
            new[] { (659f, 0f, 0.14f), (988f, 0.12f, 0.20f) }));

        /// <summary>A vessel sewn clean: one soft note.</summary>
        public static AudioClip SoftConfirm => Get("soft-confirm", () => Tone("soft-confirm", 0.16f, 0.3f, 880f, 1320f));

        /// <summary>A step refused or an error: low, rough and short.</summary>
        public static AudioClip ErrorBuzz => Get("error-buzz", () => Tone("error-buzz", 0.22f, 0.35f, 196f, 207f, 392f));

        /// <summary>The bleeding alarm: the three-pulse high-priority pattern monitors use.</summary>
        public static AudioClip BleedAlarm => Get("bleed-alarm", () => Sequence("bleed-alarm", 0.55f,
            new[] { (988f, 0f, 0.10f), (988f, 0.15f, 0.10f), (988f, 0.30f, 0.10f) }));

        /// <summary>The operation finished: a major arpeggio.</summary>
        public static AudioClip Triumph => Get("triumph", () => Sequence("triumph", 1.1f,
            new[] { (523f, 0f, 0.25f), (659f, 0.18f, 0.25f), (784f, 0.36f, 0.30f), (1046f, 0.54f, 0.5f) }));

        /// <summary>The blade on skin: a brief filtered hiss.</summary>
        /// <summary>One chime per star on the result card, a major third higher each time.</summary>
        public static AudioClip Star(int index)
        {
            int i = Mathf.Clamp(index, 0, 2);
            float hz = 1046.5f * Mathf.Pow(2f, i * 4f / 12f);
            return Get("star-" + i, () => Tone("star-" + i, 0.22f, 0.3f, hz, hz * 2f));
        }

        public static AudioClip Slice => Get("slice", () => Noise("slice", 0.12f, 0.18f));

        /// <summary>Needle through skin: a tiny click.</summary>
        public static AudioClip Stitch => Get("stitch", () => Tone("stitch", 0.05f, 0.4f, 1760f, 2637f));

        /// <summary>Electrocautery on tissue: a crackling sizzle.</summary>
        public static AudioClip Sizzle => Get("sizzle", () => Crackle("sizzle", 0.35f));

        /// <summary>The sternal saw: an oscillating blade's rasp, meant to loop while it cuts.</summary>
        public static AudioClip SawBuzz => Get("saw-buzz", () => Saw("saw-buzz", 0.5f));

        /// <summary>A constant soft ventilator/room hum, meant to loop.</summary>
        public static AudioClip RoomHum => Get("room-hum", () => Hum("room-hum", 2f));

        /// <summary>A lost instrument popping back into its place: a quick bright blip.</summary>
        public static AudioClip Pop => Get("pop", () => Sweep("pop", 0.12f, 0.35f, 500f, 1400f));

        /// <summary>The defibrillator charging: the rising whine everyone recognises from television.</summary>
        public static AudioClip DefibCharge => Get("defib-charge", () => Sweep("defib-charge", 1.1f, 0.22f, 600f, 2400f));

        /// <summary>The shock: a dull thump through the table, more felt than heard.</summary>
        public static AudioClip DefibShock => Get("defib-shock", () => Thump("defib-shock", 0.35f));

        private static AudioClip Get(string key, System.Func<AudioClip> build)
        {
            if (!Cache.TryGetValue(key, out AudioClip clip) || clip == null)
            {
                clip = build();
                Cache[key] = clip;
            }

            return clip;
        }

        private static AudioClip Tone(string name, float seconds, float volume, params float[] partials)
        {
            int length = Mathf.Max(1, Mathf.RoundToInt(seconds * SampleRate));
            float[] data = new float[length];

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                float sample = 0f;
                for (int p = 0; p < partials.Length; p++)
                {
                    sample += Mathf.Sin(2f * Mathf.PI * partials[p] * t) / (p + 1);
                }

                data[i] = sample * volume * Envelope(t, seconds);
            }

            return Build(name, data);
        }

        private static AudioClip Sequence(string name, float seconds, (float hz, float start, float length)[] notes)
        {
            int length = Mathf.Max(1, Mathf.RoundToInt(seconds * SampleRate));
            float[] data = new float[length];

            foreach ((float hz, float start, float noteLength) in notes)
            {
                int from = Mathf.RoundToInt(start * SampleRate);
                int count = Mathf.RoundToInt(noteLength * SampleRate);

                for (int i = 0; i < count && from + i < length; i++)
                {
                    float t = i / (float)SampleRate;
                    float sample = Mathf.Sin(2f * Mathf.PI * hz * t) + 0.3f * Mathf.Sin(4f * Mathf.PI * hz * t);
                    data[from + i] += sample * 0.28f * Envelope(t, noteLength);
                }
            }

            return Build(name, data);
        }

        private static AudioClip Noise(string name, float seconds, float volume)
        {
            int length = Mathf.Max(1, Mathf.RoundToInt(seconds * SampleRate));
            float[] data = new float[length];
            System.Random random = new System.Random(7);
            float smoothed = 0f;

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                float white = (float)(random.NextDouble() * 2.0 - 1.0);
                // One-pole low-pass: turns white noise into the dull hiss of a blade through tissue.
                smoothed = Mathf.Lerp(smoothed, white, 0.25f);
                data[i] = smoothed * volume * Envelope(t, seconds);
            }

            return Build(name, data);
        }

        private static AudioClip Hum(string name, float seconds)
        {
            int length = Mathf.Max(1, Mathf.RoundToInt(seconds * SampleRate));
            float[] data = new float[length];
            System.Random random = new System.Random(11);
            float smoothed = 0f;

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                float white = (float)(random.NextDouble() * 2.0 - 1.0);
                smoothed = Mathf.Lerp(smoothed, white, 0.02f);

                // 50 and 100 Hz: whole cycles in the loop length, so the loop point never clicks.
                float mains = 0.5f * Mathf.Sin(2f * Mathf.PI * 50f * t) + 0.25f * Mathf.Sin(2f * Mathf.PI * 100f * t);
                data[i] = (mains * 0.05f + smoothed * 0.35f) * 0.5f;
            }

            return Build(name, data);
        }

        /// <summary>Bright hiss broken by random pops: fat and water boiling at the tip.</summary>
        private static AudioClip Crackle(string name, float seconds)
        {
            int length = Mathf.Max(1, Mathf.RoundToInt(seconds * SampleRate));
            float[] data = new float[length];
            System.Random random = new System.Random(29);
            float previous = 0f;

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                float white = (float)(random.NextDouble() * 2.0 - 1.0);

                // High-passed noise: the difference of neighbouring samples keeps only the hiss.
                float hiss = (white - previous) * 0.5f;
                previous = white;

                float pop = random.NextDouble() < 0.004 ? (float)(random.NextDouble() * 2.0 - 1.0) : 0f;
                data[i] = (hiss * 0.25f + pop * 0.8f) * Envelope(t, seconds);
            }

            return Build(name, data);
        }

        /// <summary>
        /// A sawtooth at a whole number of cycles per loop, roughened with filtered noise and
        /// throbbing at the blade's oscillation rate. Loops without a click.
        /// </summary>
        private static AudioClip Saw(string name, float seconds)
        {
            int length = Mathf.Max(1, Mathf.RoundToInt(seconds * SampleRate));
            float[] data = new float[length];
            System.Random random = new System.Random(23);
            float smoothed = 0f;
            const float motorHz = 140f;
            const float throbHz = 24f;

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                float phase = t * motorHz % 1f;
                float saw = phase * 2f - 1f;
                float white = (float)(random.NextDouble() * 2.0 - 1.0);
                smoothed = Mathf.Lerp(smoothed, white, 0.35f);
                float throb = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * throbHz * t);
                data[i] = (saw * 0.22f + smoothed * 0.18f) * throb;
            }

            return Build(name, data);
        }

        /// <summary>Quick attack, exponential tail. Avoids the click a hard-edged tone makes.</summary>
        private static float Envelope(float t, float length)
        {
            const float attack = 0.008f;
            if (t < attack) { return t / attack; }

            float release = Mathf.Clamp01((length - t) / Mathf.Max(0.01f, length * 0.35f));
            return release * Mathf.Exp(-3f * t / Mathf.Max(0.01f, length));
        }

        /// <summary>A sine gliding from one pitch to another, held at the top.</summary>
        private static AudioClip Sweep(string name, float seconds, float volume, float fromHz, float toHz)
        {
            int length = Mathf.Max(1, Mathf.RoundToInt(seconds * SampleRate));
            float[] data = new float[length];
            float phase = 0f;

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                float k = Mathf.Clamp01(t / (seconds * 0.85f));
                float hz = Mathf.Lerp(fromHz, toHz, k * k);
                phase += 2f * Mathf.PI * hz / SampleRate;
                float fade = Mathf.Clamp01(t / 0.02f) * Mathf.Clamp01((seconds - t) / 0.05f);
                data[i] = Mathf.Sin(phase) * volume * fade;
            }

            return Build(name, data);
        }

        /// <summary>A low decaying thud with a crack of noise on top.</summary>
        private static AudioClip Thump(string name, float seconds)
        {
            int length = Mathf.Max(1, Mathf.RoundToInt(seconds * SampleRate));
            float[] data = new float[length];
            System.Random random = new System.Random(11);

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                float body = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(90f, 45f, t / seconds) * t) * Mathf.Exp(-t * 14f);
                float crack = (float)(random.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-t * 60f) * 0.5f;
                data[i] = (body + crack) * 0.7f;
            }

            return Build(name, data);
        }

        private static AudioClip Build(string name, float[] data)
        {
            AudioClip clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
