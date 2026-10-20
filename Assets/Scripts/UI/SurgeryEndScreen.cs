using UnityEngine;
using VRSurgery.Session;

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

        private static readonly int BaseColour = Shader.PropertyToID("_BaseColor");
        private MaterialPropertyBlock _block;

        /// <summary>Whether the panel is up. Exposed so tests can assert on it.</summary>
        public bool IsShown { get; private set; }

        /// <summary>The outcome line, as shown.</summary>
        public string Headline { get; private set; } = string.Empty;

        /// <summary>The time and score line, as shown.</summary>
        public string Detail { get; private set; } = string.Empty;

        /// <summary>What the visitor is told to do next.</summary>
        public string Prompt { get; private set; } = string.Empty;

        /// <summary>Wires the panel up from the scene builder.</summary>
        public void Bind(EventSessionController controller, NameEntryController names)
        {
            session = controller;
            nameEntry = names;
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
                Headline = string.Empty;
                Detail = string.Empty;
                Prompt = string.Empty;
                return;
            }

            SessionResult result = session.LastResult;

            Headline = result.Succeeded ? "CIRURGIA CONCLUÍDA" : "TEMPO ESGOTADO";

            // The elapsed time is what the scoreboard ranks on, so it leads. Points come from
            // the clock the visitor did not spend, which only means anything when they won.
            Detail = result.Succeeded
                ? $"Tempo: {Clock(result.ElapsedSeconds)}     Pontos: {result.Score}"
                : $"Tempo: {Clock(result.ElapsedSeconds)}     O transplante não foi concluído";

            Prompt = nameEntry != null && nameEntry.IsAwaitingName
                ? "Digite seu nome no teclado"
                : "Tire o visor para o próximo visitante";

            Write(headlineText, Headline);
            Write(detailText, Detail);
            Write(promptText, Prompt);

            Color tint = result.Succeeded ? successColour : failureColour;
            if (headlineText != null) { headlineText.color = tint; }
            Tint(tint);
        }

        /// <summary>
        /// Tints the panel without making a material instance per round. Instantiating the
        /// material here would leak one copy per visitor over a day on the stand.
        /// </summary>
        private void Tint(Color colour)
        {
            if (panel == null) { return; }

            _block ??= new MaterialPropertyBlock();
            panel.GetPropertyBlock(_block);
            _block.SetColor(BaseColour, colour);
            panel.SetPropertyBlock(_block);
        }

        private static void Write(TextMesh text, string value)
        {
            if (text != null && text.text != value) { text.text = value; }
        }

        /// <summary>Seconds as m:ss. A visitor reads "3:07", not "187 s".</summary>
        public static string Clock(float seconds)
        {
            int whole = Mathf.Max(0, Mathf.RoundToInt(seconds));
            return $"{whole / 60}:{whole % 60:00}";
        }
    }
}
