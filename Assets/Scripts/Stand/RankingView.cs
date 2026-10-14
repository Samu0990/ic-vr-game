using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace VRSurgery.Stand
{
    /// <summary>
    /// The public monitor: today's fastest operations, what is happening in the headset right
    /// now, and — when someone finishes — the place to type their name.
    ///
    /// The name can come from two places and both work at once. The visitor can type it on the
    /// headset's own keyboard, and the operator can type it here; what the operator types wins,
    /// and is sent to the headset so its keyboard closes. Either way it lands in the day's file
    /// on this notebook, which is what the ranking and the prize are decided from.
    /// </summary>
    public class RankingView : MonoBehaviour
    {
        private const int Rows = 10;
        private const float NameEntryTimeout = 60f;
        private const float HeadsetNameGrace = 6f;

        private static readonly Color Background = StandUi.Hex("#0B1A1D");
        private static readonly Color Surface = StandUi.Hex("#12282C");
        private static readonly Color Accent = StandUi.Hex("#3FC1C9");
        private static readonly Color Ink = StandUi.Hex("#EAF4F3");
        private static readonly Color Muted = StandUi.Hex("#8FA9A8");
        private static readonly Color Gold = StandUi.Hex("#F2C14E");
        private static readonly Color Silver = StandUi.Hex("#C9D3D6");
        private static readonly Color Bronze = StandUi.Hex("#D08C5B");
        private static readonly Color Good = StandUi.Hex("#5DD39E");
        private static readonly Color Warn = StandUi.Hex("#F5A35C");

        private sealed class Row
        {
            public Image Back;
            public Text Position;
            public Text Name;
            public Text Time;
        }

        private StandLinkClient _client;
        private StandRanking _ranking;

        private readonly List<Row> _rows = new List<Row>();
        private Text _date;
        private Text _connection;
        private Text _liveTitle;
        private Text _liveStage;
        private Text _liveInstruction;
        private Image _timeBar;
        private Text _todayStats;
        private GameObject _entry;
        private Text _entryHeadline;
        private Text _entryField;
        private Text _entryHint;
        private GameObject _banner;
        private Text _bannerText;
        private GameObject _help;
        private Text _helpText;

        private StandState _state;
        private readonly Queue<string> _waitingForName = new Queue<string>();
        private string _entryId;
        private string _typed = string.Empty;
        private string _headsetName = string.Empty;
        private float _entryOpenedAt;
        private float _headsetNameAt;
        private float _bannerUntil;
        private string _highlightId;
        private float _highlightUntil;
        private bool _moderating;
        private int _selected;
        private string _deleteArmedId;
        private float _deleteArmedUntil;
        private Keyboard _keyboard;

        public StandRanking Ranking => _ranking;
        public bool IsAskingForName => _entryId != null;

        public static RankingView Create(GameObject host, StandLinkClient client, StandRanking ranking)
        {
            RankingView view = host.AddComponent<RankingView>();
            view._client = client;
            view._ranking = ranking;
            view.Build();
            return view;
        }

        // ------------------------------------------------------------------ layout

        private void Build()
        {
            StandUi.ClearingCamera(transform, Background);
            Transform canvas = StandUi.Canvas("Ranking", transform, new Vector2(1920f, 1080f)).transform;
            StandUi.Panel("Fundo", canvas, Background, Vector2.zero, Vector2.one);

            StandUi.Label("Titulo", canvas, "TRANSPLANTE CARDÍACO VR", 66, Accent, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), Vector2.one, new Vector2(70f, -120f), new Vector2(-70f, -40f), FontStyle.Bold);
            _date = StandUi.Label("Data", canvas, string.Empty, 30, Muted, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), Vector2.one, new Vector2(72f, -175f), new Vector2(-70f, -125f));
            _connection = StandUi.Label("Conexao", canvas, string.Empty, 26, Muted, TextAnchor.UpperRight,
                new Vector2(0.5f, 1f), Vector2.one, new Vector2(0f, -90f), new Vector2(-70f, -48f));

            // Coluna do ranking.
            RectTransform board = StandUi.Rect("Ranking", canvas, new Vector2(0f, 0f), new Vector2(0.6f, 1f),
                new Vector2(70f, 90f), new Vector2(-30f, -210f));
            const float rowHeight = 1f / Rows;
            for (int i = 0; i < Rows; i++)
            {
                float top = 1f - i * rowHeight;
                Image back = StandUi.Panel($"Linha{i + 1}", board, i % 2 == 0 ? Surface : Background,
                    new Vector2(0f, top - rowHeight), new Vector2(1f, top), new Vector2(0f, 3f), new Vector2(0f, -3f));
                Row row = new Row
                {
                    Back = back,
                    Position = StandUi.Label("Pos", back.transform, string.Empty, 44, Ink, TextAnchor.MiddleLeft,
                        Vector2.zero, new Vector2(0.14f, 1f), new Vector2(24f, 0f), Vector2.zero, FontStyle.Bold),
                    Name = StandUi.Label("Nome", back.transform, string.Empty, 44, Ink, TextAnchor.MiddleLeft,
                        new Vector2(0.14f, 0f), new Vector2(0.72f, 1f)),
                    Time = StandUi.Label("Tempo", back.transform, string.Empty, 44, Ink, TextAnchor.MiddleRight,
                        new Vector2(0.72f, 0f), Vector2.one, Vector2.zero, new Vector2(-28f, 0f), FontStyle.Bold),
                };
                row.Name.horizontalOverflow = HorizontalWrapMode.Overflow;
                _rows.Add(row);
            }

            // Coluna do "agora".
            Image live = StandUi.Panel("Agora", canvas, Surface, new Vector2(0.6f, 0f), Vector2.one,
                new Vector2(30f, 90f), new Vector2(-70f, -210f));
            StandUi.Label("Rotulo", live.transform, "AGORA NO ÓCULOS", 26, Accent, TextAnchor.UpperLeft,
                Vector2.zero, Vector2.one, new Vector2(36f, 0f), new Vector2(-36f, -32f), FontStyle.Bold);
            _liveTitle = StandUi.Label("Status", live.transform, string.Empty, 58, Ink, TextAnchor.UpperLeft,
                Vector2.zero, Vector2.one, new Vector2(36f, 0f), new Vector2(-36f, -80f), FontStyle.Bold);
            _liveStage = StandUi.Label("Etapa", live.transform, string.Empty, 36, Accent, TextAnchor.UpperLeft,
                Vector2.zero, Vector2.one, new Vector2(36f, 0f), new Vector2(-36f, -250f));
            _liveInstruction = StandUi.Label("Instrucao", live.transform, string.Empty, 30, Muted, TextAnchor.UpperLeft,
                Vector2.zero, Vector2.one, new Vector2(36f, 0f), new Vector2(-36f, -310f));
            StandUi.Panel("BarraFundo", live.transform, Background, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(36f, 150f), new Vector2(-36f, 172f));
            _timeBar = StandUi.Panel("Barra", live.transform, Accent, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(36f, 150f), new Vector2(-36f, 172f));
            _timeBar.type = Image.Type.Filled;
            _timeBar.fillMethod = Image.FillMethod.Horizontal;
            _timeBar.sprite = WhiteSprite();
            _todayStats = StandUi.Label("Hoje", live.transform, string.Empty, 28, Muted, TextAnchor.LowerLeft,
                Vector2.zero, Vector2.one, new Vector2(36f, 40f), new Vector2(-36f, 0f));

            StandUi.Label("Rodape", canvas, "F1 ajuda  ·  F2 editar ranking", 20, Muted, TextAnchor.LowerLeft,
                Vector2.zero, new Vector2(1f, 0f), new Vector2(72f, 30f), new Vector2(-70f, 70f));

            BuildEntry(canvas);
            BuildBanner(canvas);
            BuildHelp(canvas);

            _ranking.Changed += RefreshBoard;
            _ranking.Changed += ExportForResearch;
            _client.StateReceived += HandleState;
            _client.ResultReceived += HandleResult;

            RefreshBoard();
            RefreshLive();
        }

        private void BuildEntry(Transform canvas)
        {
            Image dim = StandUi.Panel("Nome", canvas, new Color(0f, 0f, 0f, 0.7f), Vector2.zero, Vector2.one);
            _entry = dim.gameObject;
            Image box = StandUi.Panel("Caixa", dim.transform, Accent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-640f, -280f), new Vector2(640f, 280f));
            Image inner = StandUi.Panel("Dentro", box.transform, Surface, Vector2.zero, Vector2.one, new Vector2(6f, 6f), new Vector2(-6f, -6f));

            _entryHeadline = StandUi.Label("Manchete", inner.transform, string.Empty, 46, Ink, TextAnchor.UpperCenter,
                Vector2.zero, Vector2.one, new Vector2(40f, 0f), new Vector2(-40f, -44f), FontStyle.Bold);
            StandUi.Label("Pedido", inner.transform, "Digite o nome do cirurgião:", 34, Muted, TextAnchor.UpperCenter,
                Vector2.zero, Vector2.one, new Vector2(40f, 0f), new Vector2(-40f, -170f));
            _entryField = StandUi.Label("Campo", inner.transform, string.Empty, 84, Accent, TextAnchor.MiddleCenter,
                new Vector2(0f, 0.3f), new Vector2(1f, 0.62f), new Vector2(40f, 0f), new Vector2(-40f, 0f), FontStyle.Bold);
            _entryField.horizontalOverflow = HorizontalWrapMode.Overflow;
            _entryHint = StandUi.Label("Dica", inner.transform, string.Empty, 26, Muted, TextAnchor.LowerCenter,
                Vector2.zero, Vector2.one, new Vector2(40f, 36f), new Vector2(-40f, 0f));
            _entry.SetActive(false);
        }

        private void BuildBanner(Transform canvas)
        {
            Image banner = StandUi.Panel("Aviso", canvas, Warn, new Vector2(0.15f, 1f), new Vector2(0.85f, 1f),
                new Vector2(0f, -330f), new Vector2(0f, -230f));
            _bannerText = StandUi.Label("Texto", banner.transform, string.Empty, 40, Background, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one, new Vector2(20f, 0f), new Vector2(-20f, 0f), FontStyle.Bold);
            _banner = banner.gameObject;
            _banner.SetActive(false);
        }

        private void BuildHelp(Transform canvas)
        {
            Image help = StandUi.Panel("Ajuda", canvas, new Color(0f, 0f, 0f, 0.88f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-700f, -380f), new Vector2(700f, 380f));
            _helpText = StandUi.Label("Texto", help.transform, string.Empty, 26, Ink, TextAnchor.UpperLeft,
                Vector2.zero, Vector2.one, new Vector2(40f, 30f), new Vector2(-40f, -30f));
            _help = help.gameObject;
            _help.SetActive(false);
        }

        private static Sprite _white;

        private static Sprite WhiteSprite()
        {
            if (_white != null) { return _white; }
            Texture2D tex = Texture2D.whiteTexture;
            _white = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            return _white;
        }

        // ------------------------------------------------------------------ data

        private void HandleState(StandState state)
        {
            _state = state;

            // O visitante digitando no óculos aparece aqui ao vivo, para o operador não digitar por cima.
            if (_entryId != null && state.awaitingName && state.pendingRoundId == _entryId &&
                _typed.Length == 0 && !string.IsNullOrEmpty(state.typedName))
            {
                _entryHint.text = $"Digitando no óculos: {state.typedName}   ·   ou digite aqui e Enter";
            }
        }

        private void HandleResult(StandResult result)
        {
            bool isNew = _ranking.Find(result.id) == null;
            _ranking.Upsert(result);

            if (isNew)
            {
                // Um notebook que conecta atrasado recebe rodadas antigas: só pede nome de rodada recém-terminada.
                bool recent = DateTime.UtcNow.Ticks - result.utcTicks < TimeSpan.FromMinutes(3).Ticks;

                if (result.succeeded)
                {
                    _highlightId = result.id;
                    _highlightUntil = Time.unscaledTime + 25f;
                    if (recent && result.nameSource != NameSource.Operator) { _waitingForName.Enqueue(result.id); }
                }
                else if (recent)
                {
                    ShowBanner($"Tempo esgotado em: {StandUi.Stage(result.stageReached)}. Tente de novo!", 9f);
                }
            }
            else if (_entryId == result.id && _typed.Length == 0 && result.nameSource == NameSource.Headset &&
                     !string.IsNullOrEmpty(result.name))
            {
                _headsetName = result.name;
                _headsetNameAt = Time.unscaledTime;
            }
        }

        private void ExportForResearch()
        {
            try { _ranking.ExportCsv(); }
            catch (Exception error) { Debug.LogWarning($"[Estande] CSV não gravado: {error.Message}"); }
        }

        // ------------------------------------------------------------------ frame

        private void OnEnable()
        {
            _keyboard = Keyboard.current;
            if (_keyboard != null) { _keyboard.onTextInput += HandleCharacter; }
        }

        private void OnDisable()
        {
            if (_keyboard != null) { _keyboard.onTextInput -= HandleCharacter; }
        }

        private void OnDestroy()
        {
            if (_ranking != null) { _ranking.Changed -= RefreshBoard; _ranking.Changed -= ExportForResearch; }
            if (_client != null) { _client.StateReceived -= HandleState; _client.ResultReceived -= HandleResult; }
        }

        private void Update()
        {
            if (Keyboard.current != _keyboard) { OnDisable(); OnEnable(); }

            if (_entryId == null && _waitingForName.Count > 0) { OpenEntry(_waitingForName.Dequeue(), rename: false); }

            HandleKeys();
            TickEntry();

            if (_banner.activeSelf && Time.unscaledTime > _bannerUntil) { _banner.SetActive(false); }
            if (_highlightId != null && Time.unscaledTime > _highlightUntil) { _highlightId = null; RefreshBoard(); }

            RefreshLive();
        }

        private void HandleKeys()
        {
            Keyboard k = Keyboard.current;
            if (k == null) { return; }

            if (k.f1Key.wasPressedThisFrame) { ToggleHelp(); }

            if (_entryId != null)
            {
                if (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame) { CommitEntry(); }
                else if (k.escapeKey.wasPressedThisFrame) { CloseEntry(); }
                else if (k.backspaceKey.wasPressedThisFrame && _typed.Length > 0) { _typed = _typed.Substring(0, _typed.Length - 1); }
                return;
            }

            if (k.f2Key.wasPressedThisFrame)
            {
                _moderating = !_moderating;
                _selected = 0;
                RefreshBoard();
            }

            if (!_moderating) { return; }

            List<StandResult> ranked = _ranking.Ranked();
            int visible = Mathf.Min(Rows, ranked.Count);

            if (k.escapeKey.wasPressedThisFrame) { _moderating = false; RefreshBoard(); return; }
            if (k.downArrowKey.wasPressedThisFrame && visible > 0) { _selected = (_selected + 1) % visible; RefreshBoard(); }
            if (k.upArrowKey.wasPressedThisFrame && visible > 0) { _selected = (_selected - 1 + visible) % visible; RefreshBoard(); }
            if (visible == 0 || _selected >= visible) { return; }

            StandResult chosen = ranked[_selected];
            if (k.rKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame) { OpenEntry(chosen.id, rename: true); }

            if (k.deleteKey.wasPressedThisFrame)
            {
                // Apagar pede duas vezes: um toque errado não tira ninguém do ranking.
                if (_deleteArmedId == chosen.id && Time.unscaledTime < _deleteArmedUntil)
                {
                    _ranking.Remove(chosen.id);
                    _deleteArmedId = null;
                    _selected = Mathf.Max(0, _selected - 1);
                    ShowBanner($"Removido: {StandRanking.DisplayName(chosen)}", 4f);
                }
                else
                {
                    _deleteArmedId = chosen.id;
                    _deleteArmedUntil = Time.unscaledTime + 3f;
                    ShowBanner($"Apertar Delete de novo para remover {StandRanking.DisplayName(chosen)}", 3f);
                }
            }
        }

        private void HandleCharacter(char c)
        {
            if (_entryId == null || char.IsControl(c)) { return; }
            if (!char.IsLetterOrDigit(c) && c != ' ') { return; }
            if (_typed.Length >= StandProtocol.MaxNameLength) { return; }
            if (c == ' ' && (_typed.Length == 0 || _typed.EndsWith(" ", StringComparison.Ordinal))) { return; }
            _typed += c;
        }

        // ------------------------------------------------------------------ name entry

        private void OpenEntry(string id, bool rename)
        {
            StandResult round = _ranking.Find(id);
            if (round == null) { return; }

            _entryId = id;
            _typed = string.Empty;
            _headsetName = round.nameSource == NameSource.Headset ? round.name : string.Empty;
            _headsetNameAt = Time.unscaledTime;
            _entryOpenedAt = Time.unscaledTime;

            int position = _ranking.PositionOf(id);
            _entryHeadline.text = rename
                ? $"Corrigir o nome — {StandUi.Time(round.elapsed)}"
                : $"Cirurgia concluída em {StandUi.Time(round.elapsed)}!  {position}º lugar hoje";
            _entry.SetActive(true);
        }

        private void TickEntry()
        {
            if (_entryId == null) { return; }

            bool headsetHasName = !string.IsNullOrEmpty(_headsetName);
            float now = Time.unscaledTime;
            bool blink = Mathf.Repeat(now, 1f) < 0.55f;

            if (_typed.Length > 0)
            {
                _entryField.text = _typed + (blink ? "|" : " ");
                _entryHint.text = "Enter confirma   ·   Backspace apaga   ·   Esc cancela";
            }
            else if (headsetHasName)
            {
                float left = HeadsetNameGrace - (now - _headsetNameAt);
                _entryField.text = _headsetName;
                _entryHint.text = $"Digitado no óculos. Fica assim em {Mathf.CeilToInt(Mathf.Max(0f, left))} s — ou digite outro nome.";
                if (left <= 0f) { CloseEntry(); return; }
            }
            else
            {
                _entryField.text = blink ? "|" : " ";
                if (_state == null || !_state.awaitingName || _state.pendingRoundId != _entryId || string.IsNullOrEmpty(_state.typedName))
                {
                    _entryHint.text = "Digite aqui ou no teclado do óculos   ·   Enter confirma   ·   Esc pula";
                }
            }

            if (now - _entryOpenedAt > NameEntryTimeout) { CloseEntry(); }
        }

        private void CommitEntry()
        {
            string name = StandProtocol.SanitizeName(_typed);
            if (name.Length > 0)
            {
                _ranking.SetOperatorName(_entryId, name);
                _client.SendName(_entryId, name);
            }

            CloseEntry();
        }

        private void CloseEntry()
        {
            _entryId = null;
            _typed = string.Empty;
            _headsetName = string.Empty;
            _entry.SetActive(false);
            RefreshBoard();
        }

        // ------------------------------------------------------------------ drawing

        private void RefreshBoard()
        {
            if (_rows.Count == 0) { return; }

            List<StandResult> ranked = _ranking.Ranked();
            for (int i = 0; i < _rows.Count; i++)
            {
                Row row = _rows[i];
                bool filled = i < ranked.Count;
                StandResult round = filled ? ranked[i] : null;

                row.Position.text = filled ? $"{i + 1}º" : string.Empty;
                row.Name.text = filled ? StandRanking.DisplayName(round) : i == 0 ? "Seja o primeiro a operar hoje!" : string.Empty;
                row.Name.color = filled ? Ink : Muted;
                row.Time.text = filled ? StandUi.Time(round.elapsed) : string.Empty;
                row.Position.color = i == 0 ? Gold : i == 1 ? Silver : i == 2 ? Bronze : Ink;

                bool highlighted = filled && round.id == _highlightId;
                bool selected = _moderating && filled && i == _selected;
                row.Back.color = selected ? Warn : highlighted ? Accent * new Color(1f, 1f, 1f, 0.45f) : i % 2 == 0 ? Surface : Background;
            }

            int total = _ranking.Rounds.Count;
            _date.text = $"Ranking de hoje · {DateTime.Now:dd/MM/yyyy} · cirurgias concluídas, da mais rápida para a mais lenta";
            _todayStats.text = $"Hoje: {total} cirurgia{(total == 1 ? "" : "s")} · {ranked.Count} concluída{(ranked.Count == 1 ? "" : "s")}" +
                               (_moderating ? "\nEDITANDO: ↑↓ escolhe · R renomeia · Delete remove · Esc sai" : string.Empty);
        }

        private void RefreshLive()
        {
            bool connected = _client.IsConnected;
            _connection.text = connected ? $"<color=#5DD39E>•</color> Óculos conectado" : $"<color=#F5A35C>•</color> Procurando o óculos…";
            _connection.supportRichText = true;

            if (!connected || _state == null)
            {
                _liveTitle.text = connected ? "Conectado" : "Óculos desconectado";
                _liveStage.text = string.Empty;
                _liveInstruction.text = connected ? string.Empty : "Ligue o óculos e abra o jogo na mesma rede Wi-Fi deste notebook.";
                _timeBar.fillAmount = 0f;
                return;
            }

            switch (_state.session)
            {
                case "Running":
                    _liveTitle.text = $"Em cirurgia · {StandUi.Clock(_state.remaining)}";
                    _liveStage.text = StandUi.Stage(_state.stage);
                    _liveInstruction.text = _state.instruction;
                    break;
                case "Briefing":
                    _liveTitle.text = "Ouvindo as instruções";
                    _liveStage.text = string.Empty;
                    _liveInstruction.text = "A cirurgia começa quando o cirurgião pegar o primeiro instrumento.";
                    break;
                case "Success":
                    _liveTitle.text = "Cirurgia concluída!";
                    _liveStage.text = $"em {StandUi.Time(_state.elapsed)}";
                    _liveInstruction.text = string.Empty;
                    break;
                case "Failure":
                    _liveTitle.text = "Tempo esgotado";
                    _liveStage.text = $"parou em: {StandUi.Stage(_state.stage)}";
                    _liveInstruction.text = string.Empty;
                    break;
                case "Scoreboard":
                    _liveTitle.text = "Veja o ranking!";
                    _liveStage.text = string.Empty;
                    _liveInstruction.text = string.Empty;
                    break;
                default:
                    _liveTitle.text = "Próximo cirurgião, pode vir!";
                    _liveStage.text = string.Empty;
                    _liveInstruction.text = "Coloque o óculos e faça um transplante de coração.";
                    break;
            }

            bool running = _state.session == "Running" && _state.round > 0f;
            _timeBar.fillAmount = running ? Mathf.Clamp01(_state.remaining / _state.round) : 0f;
            _timeBar.color = Color.Lerp(Accent, Warn, _state.urgency);
        }

        private void ShowBanner(string text, float seconds)
        {
            _bannerText.text = text;
            _banner.SetActive(true);
            _bannerUntil = Time.unscaledTime + seconds;
        }

        private void ToggleHelp()
        {
            _helpText.text =
                "RANKING — teclas do operador\n\n" +
                "Ao fim de uma cirurgia concluída aparece o campo de nome.\n" +
                "   Digite e Enter confirma · Esc pula (fica o nome do óculos ou \"Anônimo\")\n" +
                "   O visitante também pode digitar no teclado do óculos.\n\n" +
                "F2    editar o ranking: ↑↓ escolhe · R renomeia · Delete (2x) remove · Esc sai\n" +
                "F9 / F10   mover a janela de monitor        F11   tela cheia\n" +
                "F1    fechar esta ajuda\n\n" +
                "O ranking zera sozinho a cada dia. Cada dia fica salvo em:\n" +
                $"{_ranking.FilePath}\n" +
                "com uma cópia em planilha (.csv) para a pesquisa.";
            _help.SetActive(!_help.activeSelf);
        }
    }
}
