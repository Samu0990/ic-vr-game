using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRSurgery.Interaction;
using VRSurgery.Stand;

namespace VRSurgery.Tests
{
    /// <summary>
    /// The stand link between the headset and the notebook beside the mannequin: the wire
    /// format, the day's ranking, a real connection over loopback, and the built scene.
    /// </summary>
    public class StandTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private string _folder;

        [SetUp]
        public void CreateFolder()
        {
            _folder = Path.Combine(Path.GetTempPath(), "estande-teste-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
        }

        [TearDown]
        public void CleanUp()
        {
            foreach (GameObject go in _spawned) { if (go != null) { UnityEngine.Object.DestroyImmediate(go); } }
            _spawned.Clear();
            try { Directory.Delete(_folder, true); } catch (Exception) { }
        }

        // ------------------------------------------------------------------ protocolo

        [Test]
        public void UmaMensagemSobreviveIdaEVolta()
        {
            byte[] packed = StandProtocol.Pack(StandProtocol.MessageType.Result, "{\"id\":\"a-1\"}");
            using MemoryStream stream = new MemoryStream(packed);

            Assert.IsTrue(StandProtocol.TryRead(stream, out StandProtocol.MessageType type, out byte[] payload));
            Assert.AreEqual(StandProtocol.MessageType.Result, type);
            Assert.AreEqual("{\"id\":\"a-1\"}", StandProtocol.Text(payload));
            Assert.IsFalse(StandProtocol.TryRead(stream, out _, out _), "Fim limpo entre mensagens não é erro.");
        }

        [Test]
        public void MensagemQueChegaAosPedacosAindaEhLidaInteira()
        {
            // O Wi-Fi entrega TCP em pedaços de qualquer tamanho; um quadro JPEG chega em dezenas.
            byte[] jpeg = new byte[50_000];
            new System.Random(7).NextBytes(jpeg);
            byte[] packed = StandProtocol.Pack(StandProtocol.MessageType.Frame, jpeg);

            using TrickleStream stream = new TrickleStream(packed, 3);
            Assert.IsTrue(StandProtocol.TryRead(stream, out StandProtocol.MessageType type, out byte[] payload));
            Assert.AreEqual(StandProtocol.MessageType.Frame, type);
            CollectionAssert.AreEqual(jpeg, payload);
        }

        [Test]
        public void ConexaoQueCaiNoMeioDaMensagemEhErroNaoMensagemVazia()
        {
            byte[] packed = StandProtocol.Pack(StandProtocol.MessageType.State, "{\"session\":\"Running\"}");
            using MemoryStream cut = new MemoryStream(packed, 0, packed.Length - 4);
            Assert.Throws<EndOfStreamException>(() => StandProtocol.TryRead(cut, out _, out _));
        }

        [Test]
        public void TamanhoCorrompidoNaoAlocaMemoriaAbsurda()
        {
            byte[] corrupt = { (byte)'F', 0xFF, 0xFF, 0xFF, 0x7F };
            using MemoryStream stream = new MemoryStream(corrupt);
            Assert.Throws<InvalidDataException>(() => StandProtocol.TryRead(stream, out _, out _));
        }

        [Test]
        public void OAvisoNaRedeDizAPortaEIgnoraOutrosProgramas()
        {
            string beacon = StandProtocol.BuildBeacon(47821, "Quest 3");
            Assert.IsTrue(StandProtocol.TryParseBeacon(beacon, out int port, out string device));
            Assert.AreEqual(47821, port);
            Assert.AreEqual("Quest 3", device);

            Assert.IsFalse(StandProtocol.TryParseBeacon("OUTRO_JOGO 1 47821 x", out _, out _));
            Assert.IsFalse(StandProtocol.TryParseBeacon($"{StandProtocol.BeaconMagic} 99 47821 x", out _, out _),
                "Uma versão de protocolo diferente não pode ser tratada como esta.");
            Assert.IsFalse(StandProtocol.TryParseBeacon($"{StandProtocol.BeaconMagic} 1 99999 x", out _, out _));
        }

        [Test]
        public void ONomeGuardaAcentosETiraOResto()
        {
            Assert.AreEqual("João", StandProtocol.SanitizeName("  João!!  "));
            Assert.AreEqual("Ana Clara", StandProtocol.SanitizeName("Ana   Clara"), "Espaços repetidos viram um.");
            Assert.AreEqual("Conceição", StandProtocol.SanitizeName("Conceição"));
            Assert.AreEqual("Abcdefghij K", StandProtocol.SanitizeName("Abcdefghij KLmnop"), "Doze caracteres contando o espaço, como o teclado do óculos.");
            Assert.AreEqual(string.Empty, StandProtocol.SanitizeName("<>;\"'"));
        }

        // ------------------------------------------------------------------ ranking

        [Test]
        public void SoQuemConcluiEntraNoRankingDoMaisRapidoAoMaisLento()
        {
            StandRanking ranking = new StandRanking(_folder, () => new DateTime(2026, 10, 24, 10, 0, 0));
            ranking.Upsert(Round("a", true, 180f, 1));
            ranking.Upsert(Round("b", false, 217f, 2));
            ranking.Upsert(Round("c", true, 150f, 3));
            ranking.Upsert(Round("d", true, 150f, 4));

            List<StandResult> ranked = ranking.Ranked();
            CollectionAssert.AreEqual(new[] { "c", "d", "a" }, ranked.ConvertAll(r => r.id),
                "Mais rápido primeiro; no empate, quem fez antes fica na frente.");
            Assert.AreEqual(4, ranking.Rounds.Count, "Quem não terminou não aparece no ranking, mas fica nos dados da pesquisa.");
            Assert.AreEqual(1, ranking.PositionOf("c"));
            Assert.AreEqual(0, ranking.PositionOf("b"));
        }

        [Test]
        public void AMesmaRodadaRepetidaNaoDuplicaENomeDoOperadorVence()
        {
            StandRanking ranking = new StandRanking(_folder, () => new DateTime(2026, 10, 24, 10, 0, 0));
            ranking.Upsert(Round("x", true, 170f, 1));
            ranking.Upsert(Round("x", true, 170f, 1));
            Assert.AreEqual(1, ranking.Rounds.Count, "O óculos reenvia rodadas ao reconectar.");

            StandResult fromHeadset = Round("x", true, 170f, 1);
            fromHeadset.name = "Bia";
            fromHeadset.nameSource = NameSource.Headset;
            ranking.Upsert(fromHeadset);
            Assert.AreEqual("Bia", ranking.Find("x").name);

            ranking.SetOperatorName("x", "Beatriz");
            fromHeadset.name = "Bia2";
            ranking.Upsert(fromHeadset);
            Assert.AreEqual("Beatriz", ranking.Find("x").name, "O que o operador digitou não é sobrescrito pelo óculos.");
        }

        [Test]
        public void ORankingSobreviveAReiniciarEZeraSozinhoNoDiaSeguinte()
        {
            DateTime now = new DateTime(2026, 10, 24, 18, 0, 0);
            StandRanking first = new StandRanking(_folder, () => now);
            first.Upsert(Round("s1", true, 160f, 1));

            StandRanking reopened = new StandRanking(_folder, () => now);
            Assert.AreEqual(1, reopened.Ranked().Count, "Fechar e abrir o programa no meio do dia não apaga o ranking.");

            now = new DateTime(2026, 10, 25, 9, 0, 0);
            Assert.AreEqual(0, reopened.Ranked().Count, "O primeiro acesso depois da meia-noite começa um dia novo.");
            StringAssert.Contains("ranking_2026-10-25", reopened.FilePath);
            Assert.IsTrue(File.Exists(Path.Combine(_folder, "ranking_2026-10-24.json")), "O dia anterior continua guardado.");
        }

        [Test]
        public void ArquivoCorrompidoNaoApagaODia()
        {
            DateTime day = new DateTime(2026, 10, 24, 12, 0, 0);
            File.WriteAllText(Path.Combine(_folder, "ranking_2026-10-24.json"), "{ isto não é json");

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Ranking ilegível"));
            StandRanking ranking = new StandRanking(_folder, () => day);

            Assert.AreEqual(0, ranking.Rounds.Count);
            Assert.IsNotEmpty(Directory.GetFiles(_folder, "*.corrompido-*"), "Uma cópia do arquivo estragado fica guardada.");
        }

        [Test]
        public void RemoverTiraDoRankingEOCsvTemUmaLinhaPorRodada()
        {
            StandRanking ranking = new StandRanking(_folder, () => new DateTime(2026, 10, 24, 10, 0, 0));
            ranking.Upsert(Round("ok", true, 150f, 1));
            ranking.Upsert(Round("ruim", true, 140f, 2));
            ranking.SetOperatorName("ruim", "Palavrao");

            Assert.IsTrue(ranking.Remove("ruim"));
            Assert.AreEqual(1, ranking.Ranked().Count);

            string csv = File.ReadAllText(ranking.ExportCsv());
            Assert.AreEqual(2, csv.Trim().Split('\n').Length, "Cabeçalho mais uma rodada.");
        }

        // ------------------------------------------------------------------ conexão de verdade

        [UnityTest]
        public IEnumerator ONotebookRecebeRodadasPerdidasEDevolveONome()
        {
            StandLinkServer server = Inactive<StandLinkServer>("Oculos");
            server.Configure(0, beacon: false);

            // Uma rodada termina antes do notebook ligar: ele tem de recebê-la ao conectar.
            server.gameObject.SetActive(true);
            Assert.IsTrue(server.IsRunning, "O servidor do óculos abriu a porta.");
            server.PublishResult(Round("q-1", true, 172f, 1));

            StandLinkClient client = Inactive<StandLinkClient>("Notebook");
            client.ManualHost = "127.0.0.1";
            client.ManualPort = server.Port;
            List<StandResult> results = new List<StandResult>();
            client.ResultReceived += results.Add;
            client.gameObject.SetActive(true);

            yield return Until(() => client.IsConnected && results.Count > 0, 5f);
            Assert.AreEqual("q-1", results[0].id, "A rodada perdida chegou ao conectar.");

            server.PublishResult(Round("q-2", false, 217f, 2));
            yield return Until(() => results.Exists(r => r.id == "q-2"), 5f);
            Assert.IsFalse(results.Find(r => r.id == "q-2").succeeded);

            StandName received = null;
            server.NameReceived += n => received = n;
            Assert.IsTrue(client.SendName("q-1", "Marina"));
            yield return Until(() => received != null, 5f);
            Assert.AreEqual("q-1", received.id);
            Assert.AreEqual("Marina", received.name);
        }

        [UnityTest]
        public IEnumerator ONotebookReconectaSozinhoQuandoOOculosVolta()
        {
            StandLinkServer server = Inactive<StandLinkServer>("Oculos");
            server.Configure(0, beacon: false);
            server.gameObject.SetActive(true);
            int port = server.Port;

            StandLinkClient client = Inactive<StandLinkClient>("Notebook");
            client.ManualHost = "127.0.0.1";
            client.ManualPort = port;
            client.gameObject.SetActive(true);
            yield return Until(() => client.IsConnected, 5f);

            // O óculos é tirado e o jogo fecha.
            server.gameObject.SetActive(false);
            yield return Until(() => !client.IsConnected, 8f);
            Assert.IsFalse(client.IsConnected);

            // Volta na mesma porta: o notebook reconecta sem ninguém mexer nele.
            server.Configure(port, beacon: false);
            server.gameObject.SetActive(true);
            yield return Until(() => client.IsConnected, 8f);
            Assert.IsTrue(client.IsConnected);
        }

        // ------------------------------------------------------------------ cena construída

        [UnityTest]
        public IEnumerator ACenaTemOEstandeLigadoENenhumaFerramentaFicaPresaNoPaciente()
        {
            yield return HeadlessScene.Load("TransplanteCardiaco");

            StandLinkServer[] servers = UnityEngine.Object.FindObjectsByType<StandLinkServer>(FindObjectsSortMode.None);
            Assert.AreEqual(1, servers.Length, "Um, e só um, rádio do estande na cena.");
            StandState state = servers[0].BuildState();
            Assert.IsFalse(string.IsNullOrEmpty(state.session), "Ligado à sessão: o notebook sabe se há cirurgia rodando.");
            Assert.IsFalse(string.IsNullOrEmpty(state.stage), "Ligado ao procedimento: o notebook sabe a etapa.");

            Assert.AreEqual(1, UnityEngine.Object.FindObjectsByType<StandFrameStreamer>(FindObjectsSortMode.None).Length);
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsByType<StandOutputsForPlatform>(FindObjectsSortMode.None).Length);

            Renderer skin = GameObject.Find("Body_Skin").GetComponent<Renderer>();
            Bounds body = skin.bounds;
            Vector3 onTheChest = new Vector3(body.center.x, body.max.y + 0.03f, Mathf.Lerp(body.min.z, body.max.z, 0.72f));
            Vector3 throughTheBody = new Vector3(body.center.x, body.min.y + 0.02f, Mathf.Lerp(body.min.z, body.max.z, 0.6f));

            int tools = 0;
            foreach (ReturnHomeOnRelease tool in UnityEngine.Object.FindObjectsByType<ReturnHomeOnRelease>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (tool.Mode != ReturnHomeOnRelease.ReleaseMode.DropAndRespawn) { continue; }
                tools++;

                Assert.IsTrue(tool.IsReachable(tool.transform.position),
                    $"{tool.name}: a casa na bandeja tem de continuar alcançável, senão a ferramenta reaparece e some de novo.");
                Assert.IsFalse(tool.IsReachable(onTheChest), $"{tool.name}: largada sobre o peito, tem de voltar para a bandeja.");
                Assert.IsFalse(tool.IsReachable(throughTheBody),
                    $"{tool.name}: a pele não tem colisor; largada no paciente cai dentro do corpo e tem de voltar.");
            }

            Assert.Greater(tools, 0, "A cena tem instrumentos que voltam para a bandeja.");
        }

        // ------------------------------------------------------------------ apoio

        private static StandResult Round(string id, bool succeeded, float elapsed, long order) => new StandResult
        {
            id = id,
            succeeded = succeeded,
            elapsed = elapsed,
            remaining = Mathf.Max(0f, 217f - elapsed),
            score = succeeded ? Mathf.RoundToInt((217f - elapsed) * 100f) : 0,
            name = string.Empty,
            stageReached = succeeded ? "Complete" : "ConnectVessels",
            utcTicks = DateTime.UtcNow.Ticks + order,
        };

        private T Inactive<T>(string name) where T : Component
        {
            GameObject go = new GameObject(name);
            go.SetActive(false);
            _spawned.Add(go);
            return go.AddComponent<T>();
        }

        private static IEnumerator Until(Func<bool> condition, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < end) { yield return null; }
        }

        /// <summary>A stream that hands back at most a few bytes per read, like TCP over Wi-Fi.</summary>
        private sealed class TrickleStream : MemoryStream
        {
            private readonly int _chunk;

            public TrickleStream(byte[] data, int chunk) : base(data) => _chunk = chunk;

            public override int Read(byte[] buffer, int offset, int count) =>
                base.Read(buffer, offset, Math.Min(count, _chunk));
        }
    }
}
