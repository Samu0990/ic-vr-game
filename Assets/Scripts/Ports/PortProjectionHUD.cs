using UnityEngine;
using UnityEngine.UI;
using VRSurgery.Session;

namespace VRSurgery.Ports
{
    /// <summary>
    /// The stand's screen for "A Porta de Entrada".
    ///
    /// Two layers, as the concept describes them: a wash over the body that reddens and brightens
    /// as the clock runs out, and a vital-signs frame around it carrying the two numbers the crowd
    /// needs from across a room — the clock, and how many sites are dealt with.
    ///
    /// The time is shown to the millisecond because that is the unit the scoreboard ranks on. At
    /// a stand where the queue is trying to beat each other by a hair, a clock rounded to whole
    /// seconds hides the thing everyone is arguing about.
    /// </summary>
    public class PortProjectionHUD : MonoBehaviour
    {
        [Header("Sources")]
        [SerializeField] private EventSessionController session;
        [SerializeField] private PortProcedure procedure;
        [SerializeField] private Leaderboard leaderboard;

        [Header("Widgets")]
        [SerializeField] private Text clockText;
        [SerializeField] private Text resolvedText;
        [SerializeField] private Text headlineText;
        [SerializeField] private Text sublineText;

        [Tooltip("Full-frame wash that stands in for the projection reddening over the body.")]
        [SerializeField] private Image bodyTint;

        [Header("Colours")]
        [SerializeField] private Color clockCalm = Color.white;
        [SerializeField] private Color clockUrgent = new Color(1f, 0.42f, 0.36f);
        [SerializeField, Range(0f, 1f)] private float tintPeakAlpha = 0.42f;

        public string ClockLabel { get; private set; } = string.Empty;
        public string Headline { get; private set; } = string.Empty;

        private int _shownResolved = -1;
        private int _shownCentis = -1;

        private void OnEnable()
        {
            if (session != null) { session.StateChanged += HandleState; }
            if (leaderboard != null) { leaderboard.Changed += RefreshIdleMessage; }

            ApplyState(session != null ? session.State : SessionState.Attract);
        }

        private void OnDisable()
        {
            if (session != null) { session.StateChanged -= HandleState; }
            if (leaderboard != null) { leaderboard.Changed -= RefreshIdleMessage; }
        }

        private void Update()
        {
            if (session == null) { return; }

            UpdateClock();
            UpdateResolved();
            UpdateTint();
        }

        private void HandleState(SessionState state) => ApplyState(state);

        private void UpdateClock()
        {
            float remaining = session.IsRunning ? session.RemainingSeconds : session.RoundSeconds;

            // Hundredths on screen: fast enough to read as running, slow enough to actually read.
            // The scoreboard still ranks on the full millisecond.
            int centis = Mathf.CeilToInt(remaining * 100f);
            if (centis != _shownCentis)
            {
                _shownCentis = centis;
                int whole = centis / 100;
                ClockLabel = $"{whole}.{centis % 100:00}";
                if (clockText != null) { clockText.text = ClockLabel; }
            }

            if (clockText != null)
            {
                clockText.color = Color.Lerp(clockCalm, clockUrgent, session.Urgency01);
            }
        }

        private void UpdateResolved()
        {
            if (resolvedText == null || procedure == null) { return; }

            int resolved = procedure.ResolvedCount;
            int bleeding = procedure.BleedingCount;

            // Rebuilt only when it changes: this is a label that moves four times a round.
            int stamp = resolved * 100 + bleeding;
            if (stamp == _shownResolved) { return; }

            _shownResolved = stamp;
            resolvedText.text = bleeding > 0
                ? $"PORTAS  {resolved}/{procedure.Total}    ·    {bleeding} SANGRANDO"
                : $"PORTAS  {resolved}/{procedure.Total}";
            resolvedText.color = bleeding > 0 ? new Color(1f, 0.45f, 0.40f) : Color.white;
        }

        private void UpdateTint()
        {
            if (bodyTint == null) { return; }

            float alpha = session.IsRunning ? session.Urgency01 * tintPeakAlpha : 0f;
            Color c = bodyTint.color;
            bodyTint.color = new Color(c.r, c.g, c.b, alpha);
        }

        private void ApplyState(SessionState state)
        {
            bool showRound = state is SessionState.Briefing or SessionState.Running;
            if (clockText != null) { clockText.gameObject.SetActive(showRound); }
            if (resolvedText != null) { resolvedText.gameObject.SetActive(showRound); }

            switch (state)
            {
                case SessionState.Attract:
                    RefreshIdleMessage();
                    break;

                case SessionState.Briefing:
                    SetMessage("PEGUE O TROCÁTER",
                        "A etapa mais arriscada de uma cirurgia segura não é operar o órgão — é a porta de entrada.");
                    break;

                case SessionState.Running:
                    SetMessage(string.Empty, string.Empty);
                    break;

                case SessionState.Success:
                    SetMessage($"{session.LastResult.ElapsedSeconds:F3}s",
                        procedure != null && procedure.CleanCount == procedure.Total
                            ? "Todas as portas limpas, sem sangramento."
                            : $"{(procedure != null ? procedure.CleanCount : 0)} de "
                              + $"{(procedure != null ? procedure.Total : 0)} portas sem sangramento.");
                    break;

                case SessionState.Failure:
                    SetMessage("TEMPO ESGOTADO",
                        procedure != null
                            ? $"{procedure.ResolvedCount} de {procedure.Total} portas resolvidas."
                            : string.Empty);
                    break;

                case SessionState.Scoreboard:
                    SetMessage("MELHORES TEMPOS", BuildTable());
                    break;
            }
        }

        private void RefreshIdleMessage()
        {
            if (session != null && session.State != SessionState.Attract) { return; }

            LeaderboardEntry? best = leaderboard != null ? leaderboard.Best : null;
            SetMessage(
                best.HasValue ? $"MELHOR TEMPO  {best.Value.TimeLabel}" : "A PORTA DE ENTRADA",
                best.HasValue
                    ? $"por {best.Value.Name} — consegue superar?"
                    : "Insira os trocáteres sem atingir um vaso. Coloque o headset para começar.");
        }

        private string BuildTable()
        {
            if (leaderboard == null || !leaderboard.HasAny)
            {
                return "Seja o primeiro do dia.";
            }

            System.Text.StringBuilder table = new System.Text.StringBuilder();
            for (int i = 0; i < leaderboard.Entries.Count; i++)
            {
                LeaderboardEntry e = leaderboard.Entries[i];
                table.AppendLine($"{i + 1}.  {e.Name,-14} {e.TimeLabel,10}");
            }

            return table.ToString();
        }

        private void SetMessage(string headline, string subline)
        {
            Headline = headline;

            if (headlineText != null)
            {
                headlineText.text = headline;
                headlineText.gameObject.SetActive(!string.IsNullOrEmpty(headline));
            }

            if (sublineText != null)
            {
                sublineText.text = subline;
                sublineText.gameObject.SetActive(!string.IsNullOrEmpty(subline));
            }
        }

        public void Bind(EventSessionController controller, PortProcedure portProcedure, Leaderboard table)
        {
            session = controller;
            procedure = portProcedure;
            leaderboard = table;
        }

        public void BindWidgets(Text clock, Text resolved, Text headline, Text subline, Image tint)
        {
            clockText = clock;
            resolvedText = resolved;
            headlineText = headline;
            sublineText = subline;
            bodyTint = tint;
        }
    }
}
