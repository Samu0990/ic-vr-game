using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using VRSurgery.Surgery;
using VRSurgery.Transplant;

namespace VRSurgery.Tests
{
    /// <summary>One anonymous line per visitor: stage times, errors, grades, written even for a turn that was not finished.</summary>
    public class ResearchLogTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private string _path;

        [SetUp]
        public void SetUp()
        {
            _path = Path.Combine(Application.temporaryCachePath, "teste_sessoes.jsonl");
            if (File.Exists(_path)) { File.Delete(_path); }
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
            {
                if (go != null) { Object.DestroyImmediate(go); }
            }

            _spawned.Clear();
            SurgeryEvents.ResetAll();
            if (File.Exists(_path)) { File.Delete(_path); }
        }

        [Test]
        public void AnUnfinishedTurnIsStillOneLineWithEveryStageTimed()
        {
            GameObject host = new GameObject("Log");
            _spawned.Add(host);
            ResearchLog log = host.AddComponent<ResearchLog>();
            log.Bind(null, null, null, null, null, null, null, null);
            log.FilePath = _path;

            log.BeginRound(10f);
            log.EnterStage(TransplantStage.SkinIncision, 10f);
            log.EnterStage(TransplantStage.OpenChest, 40f);
            SurgeryEvents.RaiseError(ErrorSeverity.MinorError, "teste");
            log.EnterStage(TransplantStage.OpenPericardium, 70f);
            log.EndRound(false, 75f, 0, 80f);

            string[] lines = File.ReadAllLines(_path);
            Assert.AreEqual(1, lines.Length, "One line per visitor.");

            RoundRecord record = JsonUtility.FromJson<RoundRecord>(lines[0]);
            Assert.IsFalse(record.completed);
            Assert.AreEqual(75f, record.seconds, 1e-3f);
            Assert.AreEqual(1, record.minorErrors);
            Assert.AreEqual(3, record.stages.Count, "The stage they stopped in is timed too.");
            Assert.AreEqual("SkinIncision", record.stages[0].stage);
            Assert.AreEqual(30f, record.stages[0].seconds, 1e-3f);
            Assert.AreEqual(10f, record.stages[2].seconds, 1e-3f);
            Assert.IsNull(log.Current, "Nothing is carried over to the next visitor.");
        }

        [Test]
        public void NothingIsWrittenBetweenTurns()
        {
            GameObject host = new GameObject("Log");
            _spawned.Add(host);
            ResearchLog log = host.AddComponent<ResearchLog>();
            log.Bind(null, null, null, null, null, null, null, null);
            log.FilePath = _path;

            SurgeryEvents.RaiseError(ErrorSeverity.MinorError, "no one is playing");
            log.EndRound(true, 10f, 100, 10f);

            Assert.IsFalse(File.Exists(_path));
        }
    }
}
