using System.Collections.Generic;
using UnityEngine;
using VRSurgery.Session;
using VRSurgery.Transplant;

namespace VRSurgery.Feedback
{
    /// <summary>
    /// The team talking to the visitor: short spoken lines at each step and at each emergency.
    ///
    /// Nobody reads a monitor in a headset while holding a scalpel; everybody hears "pressione,
    /// está vazando!". The lines are recorded files in Resources/Voz, one per line id (the list
    /// and the text are in Tools/voz/roteiro.tsv, with a script that generates them). A line with
    /// no file is simply skipped, so the stand works silent until the voices are made.
    ///
    /// One voice at a time. Ordinary lines wait their turn and are dropped if they go stale;
    /// urgent ones (a leak, the heart fibrillating) cut in. Each line has a cooldown so a
    /// wobbly hand does not hear "fique na linha" ten times in a row.
    /// </summary>
    public class VoiceGuide : MonoBehaviour
    {
        [SerializeField] private AudioSource source;

        [Header("Sources")]
        [SerializeField] private EventSessionController session;
        [SerializeField] private TransplantProcedure procedure;
        [SerializeField] private SkinIncisionWorker incision;
        [SerializeField] private CauteryWorker cautery;
        [SerializeField] private DefibrillationWorker defibrillation;
        [SerializeField] private PaceAssist pace;
        [SerializeField] private VesselAnastomosis[] vessels = new VesselAnastomosis[0];

        [Tooltip("Folder under Resources the line files live in.")]
        [SerializeField] private string folder = "Voz";

        [SerializeField, Min(0f)] private float cooldownSeconds = 8f;
        [SerializeField, Min(0.5f)] private float staleSeconds = 6f;
        [SerializeField, Min(0f)] private float gapSeconds = 0.25f;

        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private readonly Dictionary<string, float> _lastSaid = new Dictionary<string, float>();
        private readonly List<(string id, float at)> _queue = new List<(string, float)>();
        private float _clock;
        private float _busyUntil;
        private bool _loaded;
        private bool _subscribed;
        private int _bleedersSeen;
        private bool _saidThirty;

        /// <summary>Raised as a line starts, with its id. Tests and the log listen.</summary>
        public event System.Action<string> Said;

        public int Queued => _queue.Count;

        private void Awake() => Load();

        private void Load()
        {
            if (_loaded) { return; }
            _loaded = true;
            foreach (AudioClip clip in Resources.LoadAll<AudioClip>(folder))
            {
                if (clip != null) { _clips[clip.name] = clip; }
            }

            Debug.Log(_clips.Count == 0
                ? "[Voz] nenhuma fala em Resources/" + folder + " — o estande fica sem narração (veja Tools/voz)."
                : $"[Voz] {_clips.Count} falas carregadas.");
        }

        /// <summary>Adds or replaces a line's audio. Used by tests; the scene loads from Resources.</summary>
        public void Register(string id, AudioClip clip)
        {
            _loaded = true;
            if (clip != null) { _clips[id] = clip; }
        }

        private void OnEnable() => Subscribe();

        private void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            if (_subscribed) { return; }
            _subscribed = true;

            if (session != null)
            {
                session.StateChanged += HandleSession;
                session.RoundEnded += HandleRoundEnded;
            }

            if (procedure != null) { procedure.StageChanged += HandleStage; }

            if (incision != null)
            {
                incision.BladeMisaligned += SayBlade;
                incision.Deviated += SayOffLine;
                incision.DeepCut += SayTooDeep;
                incision.Graded += HandleIncisionGraded;
            }

            if (defibrillation != null)
            {
                defibrillation.FibrillationStarted += SayFibrillation;
                defibrillation.ChargeStarted += SayClear;
                defibrillation.Converted += HandleConverted;
            }

            if (pace != null) { pace.Helped += HandleHelped; }

            foreach (VesselAnastomosis vessel in vessels)
            {
                if (vessel != null) { vessel.BleedingStarted += HandleLeak; }
            }
        }

        private void Unsubscribe()
        {
            if (!_subscribed) { return; }
            _subscribed = false;

            if (session != null)
            {
                session.StateChanged -= HandleSession;
                session.RoundEnded -= HandleRoundEnded;
            }

            if (procedure != null) { procedure.StageChanged -= HandleStage; }

            if (incision != null)
            {
                incision.BladeMisaligned -= SayBlade;
                incision.Deviated -= SayOffLine;
                incision.DeepCut -= SayTooDeep;
                incision.Graded -= HandleIncisionGraded;
            }

            if (defibrillation != null)
            {
                defibrillation.FibrillationStarted -= SayFibrillation;
                defibrillation.ChargeStarted -= SayClear;
                defibrillation.Converted -= HandleConverted;
            }

            if (pace != null) { pace.Helped -= HandleHelped; }

            foreach (VesselAnastomosis vessel in vessels)
            {
                if (vessel != null) { vessel.BleedingStarted -= HandleLeak; }
            }
        }

        // ------------------------------------------------------------------ what gets said when

        private void HandleSession(SessionState state)
        {
            if (state != SessionState.Briefing) { return; }

            // A new visitor: whatever was being said to the last one stops.
            _queue.Clear();
            _busyUntil = 0f;
            if (source != null) { source.Stop(); }
            _saidThirty = false;
            _bleedersSeen = 0;

            Say("boas_vindas");
            if (procedure != null) { Say(StageLine(procedure.Stage)); }
        }

        private void HandleStage(TransplantStage stage)
        {
            // During the briefing the patient is being reset; the welcome covers it.
            if (session != null && session.State == SessionState.Briefing) { return; }
            Say(StageLine(stage));
        }

        private void HandleRoundEnded(SessionResult result)
        {
            if (!result.Succeeded) { Say("tempo_esgotado", true); }
        }

        private void HandleIncisionGraded(IncisionGrade grade)
        {
            if (grade.Stars >= 3) { Say("incisao_perfeita"); }
        }

        private void HandleConverted(int shocks)
        {
            Say("ritmo_sinusal", true);
            Say("desarejar");
        }

        private void HandleHelped(int level) => Say("ajuda_equipe");

        private void HandleLeak(VesselAnastomosis vessel) => Say("sangrando", true);

        private void SayBlade() => Say("dica_lamina");
        private void SayOffLine() => Say("fora_da_linha");
        private void SayTooDeep() => Say("profundo_demais");
        private void SayFibrillation() => Say("fibrilacao", true);
        private void SayClear() => Say("afastar", true);

        /// <summary>The line that announces a stage, or null for stages that need none.</summary>
        public static string StageLine(TransplantStage stage) => stage switch
        {
            TransplantStage.SkinIncision => "etapa_incisao",
            TransplantStage.OpenChest => "etapa_esterno",
            TransplantStage.OpenPericardium => "etapa_pericardio",
            TransplantStage.GoOnBypass => "etapa_cec",
            TransplantStage.RemoveNativeHeart => "etapa_retirar",
            TransplantStage.PlaceDonorHeart => "etapa_doador",
            TransplantStage.ConnectVessels => "etapa_vasos",
            TransplantStage.Restart => "etapa_sair_bomba",
            TransplantStage.CloseSkin => "etapa_fechar",
            TransplantStage.Complete => "concluido",
            _ => null,
        };

        // ------------------------------------------------------------------ playback

        private void Update() => Tick(Time.unscaledDeltaTime);

        /// <summary>Advances the queue and the watched conditions. Public so tests can drive it.</summary>
        public void Tick(float deltaTime)
        {
            _clock += Mathf.Max(0f, deltaTime);

            // Bleeders appear on the wound edge when it opens: once, the first time.
            if (cautery != null)
            {
                int open = cautery.OpenBleeders;
                if (open > 0 && _bleedersSeen == 0) { Say("cauterio"); }
                _bleedersSeen = open;
            }

            if (session != null && session.IsRunning && !_saidThirty && session.RemainingSeconds <= 30f)
            {
                _saidThirty = true;
                Say("trinta_segundos", true);
            }

            if (_clock < _busyUntil) { return; }

            while (_queue.Count > 0)
            {
                (string id, float at) = _queue[0];
                _queue.RemoveAt(0);
                if (_clock - at > staleSeconds) { continue; }
                Play(id);
                break;
            }
        }

        /// <summary>
        /// Says a line if there is audio for it and it has not just been said. Urgent lines
        /// interrupt and jump the queue; the rest wait.
        /// </summary>
        public void Say(string id, bool urgent = false)
        {
            if (string.IsNullOrEmpty(id)) { return; }
            Load();
            if (!_clips.ContainsKey(id)) { return; }
            if (_lastSaid.TryGetValue(id, out float last) && _clock - last < cooldownSeconds) { return; }

            if (urgent)
            {
                _queue.Clear();
                if (source != null) { source.Stop(); }
                Play(id);
                return;
            }

            if (_clock >= _busyUntil && _queue.Count == 0)
            {
                Play(id);
                return;
            }

            if (_queue.Count >= 3) { _queue.RemoveAt(0); }
            _queue.Add((id, _clock));
        }

        private void Play(string id)
        {
            AudioClip clip = _clips[id];
            _lastSaid[id] = _clock;
            _busyUntil = _clock + clip.length + gapSeconds;
            if (source != null) { source.PlayOneShot(clip); }
            Said?.Invoke(id);
        }

        public void Bind(AudioSource speaker, EventSessionController controller, TransplantProcedure transplant,
            SkinIncisionWorker skinIncision, CauteryWorker cauteryWorker, DefibrillationWorker defib, PaceAssist paceAssist,
            VesselAnastomosis[] joins)
        {
            bool live = Application.isPlaying && isActiveAndEnabled;
            if (live) { Unsubscribe(); }

            source = speaker;
            session = controller;
            procedure = transplant;
            incision = skinIncision;
            cautery = cauteryWorker;
            defibrillation = defib;
            pace = paceAssist;
            vessels = joins ?? new VesselAnastomosis[0];

            if (live) { Subscribe(); }
        }
    }
}
