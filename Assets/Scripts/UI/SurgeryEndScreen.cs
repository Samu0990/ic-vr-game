using UnityEngine;
using VRSurgery.Session;
using VRSurgery.Transplant;

namespace VRSurgery.Surgery
{
    /// <summary>
    /// The screen that closes a visitor's turn, when the clock runs out or the heart beats again.
    ///
    /// The monitor already changed one line of text at the end, which is enough to be correct and
    /// nowhere near enough to be read. A stand visitor takes the headset off within seconds of the
    /// round ending, and the queue behind them learns what happened only from whatever stayed on
    /// the screen. So the result gets a surface of its own: what happened, how long it took, and
    /// what to do next.
    ///
    /// It owns no state about the operation. Like <see cref="TransplantHUD"/>, every value is read
    /// from the session each frame — a result panel that caches its own copy of the score is a
    /// panel that will eventually disagree with the scoreboard, in front of an audience.
    /// </summary>
    public class SurgeryEndScreen : MonoBehaviour
    {
        [Header("Sources")]
        [SerializeField] private EventSessionController session;

        [Tooltip("Optional. While a name is being typed, the panel says so instead of telling the " +
                 "visitor to take the headset off mid-word.")]
        [SerializeField] private NameEntryController nameEntry;

        [Header("Screen")]
        [Tooltip("Everything that is hidden between rounds: panel, frame and text alike.")]
        [SerializeField] private GameObject body;

        [SerializeField] private TextMesh headlineText;

        [Tooltip("The human line. The headline says what happened to the operation; this says " +
                 "what happened to the person on the table.")]
        [SerializeField] private TextMesh subtitleText;

        [SerializeField] private TextMesh detailText;
        [SerializeField] private TextMesh promptText;

        [Tooltip("The panel itself, tinted by the outcome.")]
        [SerializeField] private Renderer panel;

        [Tooltip("Turned off while the result is up. The instruction monitor shares this wall, " +
                 "and two lit panels overlapping each other is how a readable sentence becomes " +
                 "two unreadable ones. One surface at a time.")]
        [SerializeField] private GameObject[] hideWhileShown;

        [Header("Colours")]
        [SerializeField] private Color successColour = new Color(0.46f, 0.89f, 1f);

        [Tooltip("Warm rather than red: the round ended, the visitor did not fail a person.")]
        [SerializeField] private Color failureColour = new Color(1f, 0.70f, 0.36f);

        [Header("Pulso")]
        [Tooltip("The heart the visitor restarted. The panel beats with it, in step, because the " +
                 "whole operation was for this and a static card throws that away. Optional: " +
                 "without it the panel falls back to its own clock.")]
        [SerializeField] private Heartbeat heart;

        [Tooltip("Used only when no heart is bound.")]
        [SerializeField, Min(20f)] private float fallbackBeatsPerMinute = 72f;

        [Tooltip("How hard the panel swells on each beat, over its resting brightness.")]
        [SerializeField, Range(0f, 2f)] private float pulseStrength = 0.9f;

        [Tooltip("Seconds the pulse takes to fade to a flat line after a round lost on the " +
                 "clock. Long enough to notice it stopping, short enough not to dwell.")]
        [SerializeField, Min(0.1f)] private float flatlineSeconds = 2.4f;

        [Tooltip("Resting values of the hologram, which the beat swells from.")]
        [SerializeField] private float restingOpacity = 0.30f;

        [SerializeField] private float restingEdgeGlow = 3.4f;

        private static readonly int BaseColour = Shader.PropertyToID("_BaseColor");
        private static readonly int Opacity = Shader.PropertyToID("_Opacity");
        private static readonly int EdgeGlow = Shader.PropertyToID("_EdgeGlow");
        private MaterialPropertyBlock _block;
        private float _shownFor;
        private float _ownPhase;

        /// <summary>How hard the panel is lit by the beat right now, 0 flat to 1 full. For tests.</summary>
        public float Beat { get; private set; }

        /// <summary>Whether the panel is up. Exposed so tests can assert on it.</summary>
        public bool IsShown { get; private set; }

        /// <summary>The outcome line, as shown.</summary>
        public string Headline { get; private set; } = string.Empty;

        /// <summary>What happened to the patient, as shown.</summary>
        public string Subtitle { get; private set; } = string.Empty;

        /// <summary>The time and score line, as shown.</summary>
        public string Detail { get; private set; } = string.Empty;

        /// <summary>What the visitor is told to do next.</summary>
        public string Prompt { get; private set; } = string.Empty;

        /// <summary>Wires the panel up from the scene builder.</summary>
        public void Bind(EventSessionController controller, NameEntryController names,
            Heartbeat beating = null)
        {
            session = controller;
            nameEntry = names;
            heart = beating;
        }

        private void Awake() => Refresh();

        private void LateUpdate() => Refresh();

        /// <summary>
        /// Reads the session and rebuilds the panel. Split out so a test can drive it without
        /// waiting for a frame.
        /// </summary>
        public void Refresh()
        {
            bool over = session != null && session.HasResult &&
                        session.State is SessionState.Success or SessionState.Failure;

            IsShown = over;
            if (body != null && body.activeSelf != over) { body.SetActive(over); }

            if (hideWhileShown != null)
            {
                for (int i = 0; i < hideWhileShown.Length; i++)
                {
                    GameObject other = hideWhileShown[i];
                    if (other != null && other.activeSelf == over) { other.SetActive(!over); }
                }
            }

            if (!over)
            {
                _shownFor = 0f;
                _ownPhase = 0f;
                Beat = 0f;
                Headline = string.Empty;
                Subtitle = string.Empty;
                Detail = string.Empty;
                Prompt = string.Empty;
                return;
            }

            _shownFor += Time.deltaTime;

            SessionResult result = session.LastResult;

            // The headline is the operation; the subtitle is the person. "Cirurgia concluída"
            // is what a form says. The visitor spent five minutes trying to make a heart beat,
            // and that is the sentence they came for.
            Headline = result.Succeeded ? "O CORAÇÃO VOLTOU A BATER" : "O TEMPO ACABOU";
            Subtitle = result.Succeeded
                ? "O paciente está vivo."
                : "A equipe assumiu daqui.";

            // The elapsed time is what the scoreboard ranks on, so it leads. Points come from
            // the clock the visitor did not spend, which only means anything when they won.
            Detail = result.Succeeded
                ? $"Transplante concluído em {Clock(result.ElapsedSeconds)}  ·  {result.Score} pontos"
                : $"{Clock(result.ElapsedSeconds)} no relógio  ·  transplante não concluído";

            Prompt = nameEntry != null && nameEntry.IsAwaitingName
                ? "Digite seu nome no teclado"
                : result.Succeeded
                    ? "Tire o visor para o próximo visitante"
                    : "Tente de novo. O relógio é a parte mais difícil.";

            Write(headlineText, Headline);
            Write(subtitleText, Subtitle);
            Write(detailText, Detail);
            Write(promptText, Prompt);

            Color tint = result.Succeeded ? successColour : failureColour;
            if (headlineText != null) { headlineText.color = tint; }

            Beat = PulseNow(result.Succeeded);
            Paint(tint, Beat);
        }

        /// <summary>
        /// How lit the panel is this frame.
        ///
        /// On a win it beats with the heart, in step: the organ the visitor restarted drives the
        /// screen announcing it. On a loss the beat it had fades out over a couple of seconds
        /// and the panel goes flat — the one shape everybody in the room already reads, and
        /// nothing has to be spelled out.
        /// </summary>
        private float PulseNow(bool succeeded)
        {
            float phase;
            if (heart != null && heart.IsBeating)
            {
                phase = heart.Phase01;
            }
            else
            {
                float cycle = 60f / Mathf.Max(20f, fallbackBeatsPerMinute);
                _ownPhase = Mathf.Repeat(_ownPhase + Time.deltaTime / cycle, 1f);
                phase = _ownPhase;
            }

            float envelope = Envelope(phase);
            if (succeeded) { return envelope; }

            return envelope * Mathf.Clamp01(1f - _shownFor / flatlineSeconds);
        }

        /// <summary>
        /// One beat: the hard first sound and the softer second, the shape of a pulse rather
        /// than a sine wave, which reads as breathing instead of a heart.
        /// </summary>
        private static float Envelope(float phase)
        {
            return Thump(phase, 0f, 0.040f)
                   + Thump(phase, 1f, 0.040f)
                   + 0.55f * Thump(phase, 0.22f, 0.052f);
        }

        private static float Thump(float phase, float at, float width)
        {
            float d = (phase - at) / width;
            return Mathf.Exp(-0.5f * d * d);
        }

        /// <summary>
        /// Tints and lights the panel without making a material instance per round.
        /// Instantiating the material here would leak one copy per visitor over a day
        /// on the stand.
        /// </summary>
        private void Paint(Color colour, float beat)
        {
            if (panel == null) { return; }

            _block ??= new MaterialPropertyBlock();
            panel.GetPropertyBlock(_block);
            _block.SetColor(BaseColour, colour);
            _block.SetFloat(Opacity, restingOpacity * (1f + pulseStrength * beat));
            _block.SetFloat(EdgeGlow, restingEdgeGlow * (1f + pulseStrength * 1.3f * beat));
            panel.SetPropertyBlock(_block);
        }

        private static void Write(TextMesh text, string value)
        {
            if (text != null && text.text != value) { text.text = value; }
        }

        /// <summary>Seconds as m:ss.mmm. A visitor reads "3:07.250", not "187 s".</summary>
        public static string Clock(float seconds) => ClockFormat.Elapsed(seconds);
    }
}
