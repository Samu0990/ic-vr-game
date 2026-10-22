using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using VRSurgery.Session;
using VRSurgery.Transplant;
using VRSurgery.VR;

namespace VRSurgery.Surgery
{
    /// <summary>
    /// What the queue reads over the surgeon's view on the stand's monitor (Display 1): the clock,
    /// the points the visitor would make if they finished now, the step they are on, and every
    /// second gained or lost as it happens.
    ///
    /// A screen-space overlay on the PC's own display: it is never in the headset, which draws
    /// its eyes on its own, so it can sit over the picture the way a sports broadcast does.
    /// Also the operator's crib: H shows the keys, and the projector's calibration reports here
    /// because nothing may be written on the mannequin itself.
    /// </summary>
    public class SpectatorHud : MonoBehaviour
    {
        [SerializeField] private EventSessionController session;
        [SerializeField] private TransplantProcedure procedure;
        [SerializeField] private ProjectionOutput projection;

        [Tooltip("Seconds a gained or lost second stays on screen.")]
        [SerializeField, Min(0.5f)] private float feedSeconds = 3.5f;

        [SerializeField] private Color gain = new Color(0.35f, 1f, 0.45f);
        [SerializeField] private Color loss = new Color(1f, 0.35f, 0.3f);

        private Text _clock;
        private Text _points;
        private Text _stage;
        private Text _status;
        private Text _hint;
        private Text _calibration;
        private GameObject _help;
        private RectTransform _feedRoot;
        private Font _font;

        private readonly List<(Text text, float age)> _feed = new List<(Text, float)>();

        /// <summary>The adjustments currently on screen, newest last. Exposed for tests.</summary>
        public IReadOnlyList<string> Feed
        {
            get
            {
                List<string> lines = new List<string>(_feed.Count);
                foreach ((Text text, float _) in _feed) { lines.Add(text.text); }
                return lines;
            }
        }

        public bool HelpShown => _help != null && _help.activeSelf;

        public string StageLabel => _stage != null ? _stage.text : string.Empty;

        /// <summary>The keys the operator has on the PC, as shown with H.</summary>
        public const string HelpText =
            "TECLAS DO OPERADOR\n\n" +
            "R   recentralizar a mesa em quem está de óculos\n" +
            "N   encerrar a vez e preparar o próximo\n" +
            "↑ ↓  altura do visitante ±2 cm (fora da rodada)\n" +
            "C   calibrar o projetor no manequim\n" +
            "      setas movem · + − escala · [ ] gira · X Y espelham · Enter salva\n" +
            "P   alternar o monitor: visão do cirurgião / visão de cima\n" +
            "H   mostrar/esconder esta ajuda";

        private void Awake() => Build();

        private void Build()
        {
            if (_clock != null) { return; }

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            Canvas canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null) { canvas = gameObject.AddComponent<Canvas>(); }
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.targetDisplay = 0;
            canvas.sortingOrder = 10;

            CanvasScaler scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null) { scaler = gameObject.AddComponent<CanvasScaler>(); }
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform panel = Panel("Placar", new Vector2(0f, 1f), new Vector2(30f, -30f), new Vector2(520f, 190f));
            _clock = Label(panel, "Relogio", 96, TextAnchor.UpperLeft, new Vector2(24f, -12f), new Vector2(480f, 110f));
            _points = Label(panel, "Pontos", 44, TextAnchor.UpperLeft, new Vector2(24f, -122f), new Vector2(480f, 56f));

            RectTransform stagePanel = Panel("Etapa", new Vector2(0.5f, 1f), new Vector2(-460f, -30f), new Vector2(920f, 80f));
            _stage = Label(stagePanel, "TextoEtapa", 44, TextAnchor.MiddleCenter, new Vector2(10f, -10f), new Vector2(900f, 60f));

            _status = Label((RectTransform)transform, "Estado", 56, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(1500f, 200f));
            Anchor(_status.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-750f, 100f));

            GameObject feed = new GameObject("SegundosGanhosEPerdidos", typeof(RectTransform));
            feed.transform.SetParent(transform, false);
            _feedRoot = (RectTransform)feed.transform;
            Anchor(_feedRoot, new Vector2(1f, 1f), new Vector2(-640f, -40f));
            _feedRoot.sizeDelta = new Vector2(600f, 600f);

            _hint = Label((RectTransform)transform, "Dica", 26, TextAnchor.LowerLeft, Vector2.zero, new Vector2(600f, 40f));
            Anchor(_hint.rectTransform, Vector2.zero, new Vector2(24f, 64f));
            _hint.text = "H — teclas do operador";
            _hint.color = new Color(1f, 1f, 1f, 0.55f);

            RectTransform help = Panel("Ajuda", new Vector2(0.5f, 0.5f), new Vector2(-560f, 260f), new Vector2(1120f, 520f));
            Label(help, "TextoAjuda", 36, TextAnchor.UpperLeft, new Vector2(40f, -30f), new Vector2(1040f, 470f)).text = HelpText;
            _help = help.gameObject;
            _help.SetActive(false);

            _calibration = Label((RectTransform)transform, "Calibracao", 32, TextAnchor.LowerCenter, Vector2.zero, new Vector2(1600f, 140f));
            Anchor(_calibration.rectTransform, new Vector2(0.5f, 0f), new Vector2(-800f, 170f));
            _calibration.color = new Color(1f, 0.85f, 0.35f);
        }

        private RectTransform Panel(string name, Vector2 anchor, Vector2 topLeft, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            Image back = go.AddComponent<Image>();
            back.color = new Color(0f, 0f, 0f, 0.55f);
            back.raycastTarget = false;
            RectTransform rect = (RectTransform)go.transform;
            Anchor(rect, anchor, topLeft);
            rect.sizeDelta = size;
            return rect;
        }

        private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 topLeft)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = topLeft;
        }

        private Text Label(RectTransform parent, string name, int size, TextAnchor alignment, Vector2 topLeft, Vector2 box)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Text text = go.AddComponent<Text>();
            text.font = _font;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            RectTransform rect = (RectTransform)go.transform;
            Anchor(rect, new Vector2(0f, 1f), topLeft);
            rect.sizeDelta = box;
            Outline edge = go.AddComponent<Outline>();
            edge.effectColor = new Color(0f, 0f, 0f, 0.8f);
            edge.effectDistance = new Vector2(2f, -2f);
            return text;
        }

        /// <summary>A second gained or lost, on screen for a few seconds: "+10 s · Esterno aberto".</summary>
        public void ShowAdjustment(string line, bool gained)
        {
            Build();
            Text text = Label(_feedRoot, "Ajuste", 46, TextAnchor.UpperRight, Vector2.zero, new Vector2(600f, 60f));
            text.text = line;
            text.color = gained ? gain : loss;
            _feed.Add((text, 0f));
            Layout();
        }

        private void Layout()
        {
            for (int i = 0; i < _feed.Count; i++)
            {
                _feed[i].text.rectTransform.anchoredPosition = new Vector2(0f, -64f * (_feed.Count - 1 - i));
            }
        }

        private void Update()
        {
            Keyboard keys = Keyboard.current;
            if (keys != null && keys.hKey.wasPressedThisFrame && _help != null) { _help.SetActive(!_help.activeSelf); }
            Tick(Time.unscaledDeltaTime);
        }

        /// <summary>Redraws everything. Stepped by hand in tests.</summary>
        public void Tick(float deltaTime)
        {
            Build();

            for (int i = _feed.Count - 1; i >= 0; i--)
            {
                (Text text, float age) = _feed[i];
                age += Mathf.Max(0f, deltaTime);
                if (age >= feedSeconds)
                {
                    Destroy(text.gameObject);
                    _feed.RemoveAt(i);
                    continue;
                }

                Color c = text.color;
                c.a = Mathf.Clamp01((feedSeconds - age) / 0.6f);
                text.color = c;
                _feed[i] = (text, age);
            }

            Layout();

            bool running = session != null && session.State == SessionState.Running;
            _clock.text = session == null ? string.Empty
                : running || session.State == SessionState.Briefing ? ClockFormat.Countdown(session.RemainingSeconds)
                : string.Empty;
            _clock.color = session != null && running
                ? Color.Lerp(Color.white, new Color(1f, 0.45f, 0.4f), session.Urgency01)
                : Color.white;
            _points.text = session != null && running ? $"Pontos {session.PointsNow:N0}".Replace(',', '.') : string.Empty;

            if (procedure != null && running)
            {
                int index = IndexOf(procedure.Stage);
                _stage.text = index >= 0
                    ? $"Etapa {index + 1} de {procedure.Order.Count} · {StageText.Name(procedure.Stage)}"
                    : StageText.Name(procedure.Stage);
            }
            else
            {
                _stage.text = string.Empty;
            }

            _stage.transform.parent.gameObject.SetActive(!string.IsNullOrEmpty(_stage.text));

            _status.text = session == null ? string.Empty : session.State switch
            {
                SessionState.Attract => "TRANSPLANTE DE CORAÇÃO\nPróximo visitante: coloque o óculos",
                SessionState.Briefing => "Pegue o bisturi para começar",
                SessionState.Success => session.HasResult
                    ? $"CORAÇÃO BATENDO!\n{session.LastResult.Score:N0} pontos".Replace(',', '.')
                    : "CORAÇÃO BATENDO!",
                SessionState.Failure => "TEMPO ESGOTADO",
                _ => string.Empty,
            };

            float offset = fit != null ? fit.OperatorOffset : 0f;
            _hint.text = Mathf.Abs(offset) > 0.001f
                ? $"H — teclas do operador   ·   altura do visitante {offset * 100f:+0;-0} cm"
                : "H — teclas do operador";

            _calibration.text = projection != null && projection.Calibrating
                ? "CALIBRAÇÃO DO PROJETOR — setas movem · + − escala · [ ] gira · X Y espelham · Enter salva · C sai\n" +
                  $"deslocamento {projection.Calibration.offsetX:+0.000;-0.000} {projection.Calibration.offsetY:+0.000;-0.000} · " +
                  $"escala {projection.Calibration.scale:0.000} · giro {projection.Calibration.rotation:0.0}° · " +
                  $"espelho {(projection.Calibration.mirrorX ? "X" : "-")}{(projection.Calibration.mirrorY ? "Y" : "-")}" +
                  (projection.LastSaved != default ? $" · salvo {projection.LastSaved:HH:mm:ss}" : " · não salvo")
                : string.Empty;
        }

        private int IndexOf(TransplantStage stage)
        {
            IReadOnlyList<TransplantStage> order = procedure.Order;
            for (int i = 0; i < order.Count; i++) { if (order[i] == stage) { return i; } }
            return -1;
        }

        public void Bind(EventSessionController controller, TransplantProcedure transplant, ProjectionOutput projector)
        {
            session = controller;
            procedure = transplant;
            projection = projector;
        }

        [SerializeField] private TimeScoring scoring;
        private TimeScoring _subscribedScoring;

        [Tooltip("Optional: the visitor's fit, to show the operator's height correction.")]
        [SerializeField] private VisitorFit fit;

        /// <summary>Shows the operator's ↑/↓ correction for the visitor in the headset.</summary>
        public void BindVisitorFit(VisitorFit visitorFit) => fit = visitorFit;

        private void OnEnable() => SubscribeScoring();

        private void OnDisable()
        {
            if (_subscribedScoring != null) { _subscribedScoring.Adjusted -= HandleAdjusted; }
            _subscribedScoring = null;
        }

        private void SubscribeScoring()
        {
            if (scoring == null || _subscribedScoring == scoring) { return; }
            if (_subscribedScoring != null) { _subscribedScoring.Adjusted -= HandleAdjusted; }
            _subscribedScoring = scoring;
            _subscribedScoring.Adjusted += HandleAdjusted;
        }

        private void HandleAdjusted(TimeAdjustment adjustment) => ShowAdjustment(adjustment.Label, adjustment.Gained);

        /// <summary>Every second won or lost, as it happens, on the queue's monitor too.</summary>
        public void BindScoring(TimeScoring source)
        {
            scoring = source;
            if (Application.isPlaying && isActiveAndEnabled) { SubscribeScoring(); }
        }
    }
}
