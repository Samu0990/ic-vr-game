using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VRSurgery.Session;
using VRSurgery.Surgery;

namespace VRSurgery.Transplant
{
    /// <summary>One visitor's turn, as the research sees it. Anonymous: no name, nothing that identifies them.</summary>
    [Serializable]
    public class RoundRecord
    {
        [Serializable]
        public struct StageTime
        {
            public string stage;
            public float seconds;
        }

        public string when;
        public int visitor;
        public string build;
        public string device;
        public bool completed;
        public bool aborted;
        public float seconds;
        public float roundSeconds;
        public int score;
        public string reachedStage;
        public List<StageTime> stages = new List<StageTime>();
        public int warnings;
        public int minorErrors;
        public int majorErrors;
        public int incisionStars;
        public int incisionStrokes;
        public float incisionDeviationMm;
        public int incisionScratches;
        public bool bladeHelp;
        public int sutureStars;
        public float sutureAccuracyMm;
        public int sutureMisses;
        public int shocks;
        public int leaks;
        public int assists;
        public float eyeHeight;
        public float shortModeLift;
    }

    /// <summary>
    /// Writes one line per visitor to a file on the headset, for the research the stand exists to
    /// support: how long each step took, what went wrong, how the graded steps went, how much
    /// help the game had to give.
    ///
    /// JSON Lines, so a crash loses at most the turn in progress and the file can be read by
    /// anything — the dashboard page, a spreadsheet, Python. Anonymous by construction: the name
    /// a visitor types for the scoreboard is never read here. The file lives in the app's own
    /// folder (on the Quest, Android/data/&lt;app&gt;/files/pesquisa) and is copied off with a cable.
    /// </summary>
    public class ResearchLog : MonoBehaviour
    {
        [SerializeField] private bool record = true;
        [SerializeField] private string fileName = "sessoes.jsonl";

        [Header("Sources")]
        [SerializeField] private EventSessionController session;
        [SerializeField] private TransplantProcedure procedure;
        [SerializeField] private SkinIncisionWorker incision;
        [SerializeField] private SutureWorker suture;
        [SerializeField] private DefibrillationWorker defibrillation;
        [SerializeField] private VesselAnastomosis[] vessels = new VesselAnastomosis[0];
        [SerializeField] private VR.VisitorFit fit;
        [SerializeField] private Transform head;

        private RoundRecord _current;
        private TransplantStage _stage = TransplantStage.Idle;
        private float _stageStarted;
        private float _roundStarted;
        private bool _subscribed;

        /// <summary>The turn being recorded, or null between turns.</summary>
        public RoundRecord Current => _current;

        /// <summary>The last line written, for tests and the Console.</summary>
        public string LastLine { get; private set; }

        /// <summary>Where the lines go. Set by tests; defaults to the app's persistent folder.</summary>
        public string FilePath { get; set; }

        public static string DefaultFolder => Path.Combine(Application.persistentDataPath, "pesquisa");

        private void OnEnable() => Subscribe();

        private void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            if (_subscribed) { return; }
            _subscribed = true;

            if (session != null)
            {
                session.RoundStarted += HandleRoundStarted;
                session.RoundEnded += HandleRoundEnded;
            }

            if (procedure != null) { procedure.StageChanged += HandleStage; }
            SurgeryEvents.OnError += HandleError;

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
                session.RoundStarted -= HandleRoundStarted;
                session.RoundEnded -= HandleRoundEnded;
            }

            if (procedure != null) { procedure.StageChanged -= HandleStage; }
            SurgeryEvents.OnError -= HandleError;

            foreach (VesselAnastomosis vessel in vessels)
            {
                if (vessel != null) { vessel.BleedingStarted -= HandleLeak; }
            }
        }

        private void HandleRoundStarted() => BeginRound(Time.unscaledTime);

        private void HandleRoundEnded(SessionResult result) =>
            EndRound(result.Succeeded, result.ElapsedSeconds, result.Score, Time.unscaledTime);

        private void HandleStage(TransplantStage stage) => EnterStage(stage, Time.unscaledTime);

        private void HandleLeak(VesselAnastomosis vessel)
        {
            if (_current != null) { _current.leaks++; }
        }

        private void HandleError(ErrorSeverity severity, string message)
        {
            if (_current == null) { return; }
            switch (severity)
            {
                case ErrorSeverity.Warning: _current.warnings++; break;
                case ErrorSeverity.MinorError: _current.minorErrors++; break;
                default: _current.majorErrors++; break;
            }
        }

        /// <summary>A visitor's clock has started.</summary>
        public void BeginRound(float now)
        {
            _current = new RoundRecord
            {
                when = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                visitor = session != null ? session.SessionCount : 0,
                build = Application.version,
                device = SystemInfo.deviceModel,
                roundSeconds = session != null ? session.RoundSeconds : 0f,
                eyeHeight = head != null ? head.localPosition.y : 0f,
                shortModeLift = fit != null ? fit.Lift : 0f,
            };

            _roundStarted = now;
            _stageStarted = now;
            _stage = procedure != null ? procedure.Stage : TransplantStage.Idle;
        }

        /// <summary>The operation moved on: the stage just left is timed.</summary>
        public void EnterStage(TransplantStage stage, float now)
        {
            if (_current != null && _stage != TransplantStage.Idle && stage != _stage)
            {
                _current.stages.Add(new RoundRecord.StageTime { stage = _stage.ToString(), seconds = now - _stageStarted });
            }

            _stage = stage;
            _stageStarted = now;
        }

        /// <summary>Counts a moment the game helped: the blade rule relaxing, the pace assist.</summary>
        public void NoteAssist()
        {
            if (_current != null) { _current.assists++; }
        }

        /// <summary>The turn is over, won, lost or abandoned: fill in the grades and write the line.</summary>
        public void EndRound(bool completed, float seconds, int score, float now)
        {
            if (_current == null) { return; }

            if (!completed && _stage != TransplantStage.Idle && _stage != TransplantStage.Complete)
            {
                _current.stages.Add(new RoundRecord.StageTime { stage = _stage.ToString(), seconds = now - _stageStarted });
            }

            _current.completed = completed;
            _current.aborted = !completed && session != null && session.RemainingSeconds > 0.5f;
            _current.seconds = seconds > 0f ? seconds : now - _roundStarted;
            _current.score = score;
            _current.reachedStage = (procedure != null ? procedure.Stage : _stage).ToString();

            if (incision != null && incision.IsComplete)
            {
                IncisionGrade g = incision.Grade;
                _current.incisionStars = g.Stars;
                _current.incisionStrokes = g.Strokes;
                _current.incisionDeviationMm = g.DeviationMm;
                _current.incisionScratches = g.Scratches;
                _current.bladeHelp = g.NeededBladeHelp;
            }

            if (suture != null && suture.IsComplete)
            {
                _current.sutureStars = suture.Grade.Stars;
                _current.sutureAccuracyMm = suture.Grade.AccuracyMm;
                _current.sutureMisses = suture.Grade.Misses;
            }

            if (defibrillation != null) { _current.shocks = defibrillation.Shocks; }

            LastLine = JsonUtility.ToJson(_current);
            Write(LastLine);
            _current = null;
            _stage = TransplantStage.Idle;
        }

        private void Write(string line)
        {
            if (!record) { return; }

            try
            {
                string path = FilePath;
                if (string.IsNullOrEmpty(path))
                {
                    Directory.CreateDirectory(DefaultFolder);
                    path = Path.Combine(DefaultFolder, fileName);
                }

                File.AppendAllText(path, line + "\n");
                Debug.Log($"[Pesquisa] visitante registrado em {path}");
            }
            catch (Exception exception)
            {
                // A full disk or a locked file must never take the stand down.
                Debug.LogWarning($"[Pesquisa] não foi possível gravar a linha: {exception.Message}");
            }
        }

        public void Bind(EventSessionController controller, TransplantProcedure transplant, SkinIncisionWorker skinIncision,
            SutureWorker skinSuture, DefibrillationWorker defib, VesselAnastomosis[] joins, VR.VisitorFit visitorFit, Transform camera)
        {
            bool live = Application.isPlaying && isActiveAndEnabled;
            if (live) { Unsubscribe(); }

            session = controller;
            procedure = transplant;
            incision = skinIncision;
            suture = skinSuture;
            defibrillation = defib;
            vessels = joins ?? new VesselAnastomosis[0];
            fit = visitorFit;
            head = camera;

            if (live) { Subscribe(); }
        }
    }
}
