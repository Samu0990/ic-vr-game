using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace VRSurgery.Stand
{
    /// <summary>
    /// The stand's ranking for one day, kept on the notebook.
    ///
    /// One file per calendar day, named by the date. That is the whole of "resetting the board
    /// every morning": the first round after midnight lands in a new file, and nobody has to
    /// remember to clear anything. It also survives the notebook or the app restarting in the
    /// middle of the day, which wiping on start would not.
    ///
    /// Every round is kept, including the ones that ran out of time — they are the research data
    /// for the project's evaluation. Only completed operations are ranked, fastest first, as the
    /// headset's own leaderboard does.
    /// </summary>
    public class StandRanking
    {
        public const string FallbackName = "Anônimo";

        [Serializable]
        private class DayFile
        {
            public string date;
            public List<StandResult> rounds = new List<StandResult>();
        }

        private readonly string _folder;
        private readonly Func<DateTime> _clock;
        private DayFile _day = new DayFile();

        public StandRanking(string folder, Func<DateTime> clock = null)
        {
            _folder = folder;
            _clock = clock ?? (() => DateTime.Now);
            Directory.CreateDirectory(_folder);
            LoadToday();
        }

        public string Folder => _folder;
        public string FilePath => PathFor(_day.date);
        public string Date => _day.date;

        /// <summary>Raised whenever a round is added, renamed or removed.</summary>
        public event Action Changed;

        /// <summary>Every round of the day, in the order they were played.</summary>
        public IReadOnlyList<StandResult> Rounds
        {
            get { RollOverIfNewDay(); return _day.rounds; }
        }

        /// <summary>Completed operations, fastest first; on a tie, whoever got there first.</summary>
        public List<StandResult> Ranked()
        {
            RollOverIfNewDay();
            List<StandResult> ranked = _day.rounds.FindAll(r => r.succeeded);
            ranked.Sort((a, b) =>
            {
                int byTime = Mathf.RoundToInt(a.elapsed * 1000f).CompareTo(Mathf.RoundToInt(b.elapsed * 1000f));
                return byTime != 0 ? byTime : a.utcTicks.CompareTo(b.utcTicks);
            });
            return ranked;
        }

        /// <summary>1-based position of a round among today's completed operations, or 0.</summary>
        public int PositionOf(string id)
        {
            List<StandResult> ranked = Ranked();
            int index = ranked.FindIndex(r => r.id == id);
            return index < 0 ? 0 : index + 1;
        }

        public StandResult Find(string id)
        {
            RollOverIfNewDay();
            return string.IsNullOrEmpty(id) ? null : _day.rounds.Find(r => r.id == id);
        }

        /// <summary>
        /// Adds a round, or merges what the headset sent again about one already known. The
        /// headset repeats a round when the visitor's name arrives and again after a reconnect,
        /// so this must be safe to call any number of times with the same id.
        /// </summary>
        public bool Upsert(StandResult incoming)
        {
            if (incoming == null || string.IsNullOrEmpty(incoming.id)) { return false; }
            RollOverIfNewDay();

            StandResult known = Find(incoming.id);
            if (known == null)
            {
                StandResult copy = Copy(incoming);
                copy.name = StandProtocol.SanitizeName(copy.name);
                if (string.IsNullOrEmpty(copy.name)) { copy.nameSource = NameSource.None; }
                _day.rounds.Add(copy);
                SaveAndNotify();
                return true;
            }

            // Só o nome pode chegar depois; o tempo da rodada não muda.
            string name = StandProtocol.SanitizeName(incoming.name);
            if (!string.IsNullOrEmpty(name) && known.nameSource != NameSource.Operator &&
                name != known.name)
            {
                known.name = name;
                known.nameSource = string.IsNullOrEmpty(incoming.nameSource) ? NameSource.Headset : incoming.nameSource;
                SaveAndNotify();
                return true;
            }

            return false;
        }

        /// <summary>
        /// The name typed on the notebook. It wins over whatever the visitor typed in the
        /// headset: the operator is the one looking at the public screen.
        /// </summary>
        public bool SetOperatorName(string id, string name)
        {
            StandResult known = Find(id);
            if (known == null) { return false; }

            string clean = StandProtocol.SanitizeName(name);
            known.name = string.IsNullOrEmpty(clean) ? FallbackName : clean;
            known.nameSource = NameSource.Operator;
            SaveAndNotify();
            return true;
        }

        /// <summary>Takes a round off the board — for a name that should not be on a public screen.</summary>
        public bool Remove(string id)
        {
            RollOverIfNewDay();
            int removed = _day.rounds.RemoveAll(r => r.id == id);
            if (removed == 0) { return false; }
            SaveAndNotify();
            return true;
        }

        public static string DisplayName(StandResult round) =>
            round == null || string.IsNullOrWhiteSpace(round.name) ? FallbackName : round.name;

        /// <summary>Writes the day as CSV next to the JSON, for the project's data analysis.</summary>
        public string ExportCsv()
        {
            RollOverIfNewDay();
            StringBuilder csv = new StringBuilder("id;data_hora;concluiu;tempo_s;restante_s;pontos;nome;origem_nome;etapa_alcancada\n");
            foreach (StandResult r in _day.rounds)
            {
                string when = new DateTime(r.utcTicks, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                csv.Append(r.id).Append(';').Append(when).Append(';').Append(r.succeeded ? "sim" : "nao").Append(';')
                   .Append(r.elapsed.ToString("0.0", CultureInfo.InvariantCulture)).Append(';')
                   .Append(r.remaining.ToString("0.0", CultureInfo.InvariantCulture)).Append(';')
                   .Append(r.score).Append(';').Append(DisplayName(r).Replace(';', ' ')).Append(';')
                   .Append(r.nameSource).Append(';').Append(r.stageReached).Append('\n');
            }

            string path = Path.ChangeExtension(FilePath, ".csv");
            File.WriteAllText(path, csv.ToString(), new UTF8Encoding(true));
            return path;
        }

        private void RollOverIfNewDay()
        {
            if (_day.date != Today()) { LoadToday(); }
        }

        private string Today() => _clock().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private string PathFor(string date) => Path.Combine(_folder, $"ranking_{date}.json");

        private void LoadToday()
        {
            string date = Today();
            string path = PathFor(date);
            DayFile loaded = null;

            // Desligou entre apagar o antigo e renomear o novo: o dia inteiro está no temporário.
            if (!File.Exists(path) && File.Exists(path + ".tmp")) { File.Move(path + ".tmp", path); }

            if (File.Exists(path))
            {
                try
                {
                    loaded = JsonUtility.FromJson<DayFile>(File.ReadAllText(path));
                }
                catch (Exception error)
                {
                    // Um arquivo corrompido não pode apagar o dia: guarda uma cópia antes de recomeçar.
                    string backup = path + ".corrompido-" + DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture);
                    File.Copy(path, backup, true);
                    Debug.LogError($"[Estande] Ranking ilegível em {path}; cópia guardada em {backup}. {error.Message}");
                }
            }

            _day = loaded ?? new DayFile();
            _day.date = date;
            _day.rounds ??= new List<StandResult>();
        }

        private void SaveAndNotify()
        {
            // Grava num temporário e troca: um desligamento no meio da escrita não deixa o dia pela metade.
            string path = FilePath;
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(_day, true), new UTF8Encoding(false));
            if (File.Exists(path)) { File.Delete(path); }
            File.Move(temp, path);
            Changed?.Invoke();
        }

        private static StandResult Copy(StandResult r) => new StandResult
        {
            id = r.id, succeeded = r.succeeded, elapsed = r.elapsed, remaining = r.remaining, score = r.score,
            name = r.name, nameSource = r.nameSource, stageReached = r.stageReached, utcTicks = r.utcTicks,
        };
    }
}
