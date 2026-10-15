using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRSurgery.Feedback;
using VRSurgery.Session;
using VRSurgery.Surgery;

namespace VRSurgery.Tests
{
    /// <summary>
    /// O farol de LED: o que o ESP32 recebe, e quando.
    ///
    /// O aparelho não está plugado na máquina que roda a suíte, e no dia do evento ninguém vai
    /// ter tempo de depurar por que a fita ficou vermelha na hora errada. O que dá para verificar
    /// sem hardware é justamente o que costuma quebrar: a máquina de estados e o formato do
    /// pacote. O fio em si é problema do multímetro.
    /// </summary>
    public class LedBeaconTests
    {
        private const float Round = 60f;

        private GameObject _host;
        private LedBeacon _beacon;
        private EventSessionController _session;
        private EventSessionDefinition _definition;

        [SetUp]
        public void SetUp()
        {
            SurgeryEvents.ResetAll();

            _definition = EventSessionDefinition.Create(
                Round, briefingTimeout: 0f, resultHold: 4f, scoreboardHold: 6f,
                startOnGrab: false, pointsPerSecond: 100f);

            _host = new GameObject("Farol");
            _host.SetActive(false);

            _session = _host.AddComponent<EventSessionController>();
            _session.Bind(_definition, null);

            _beacon = _host.AddComponent<LedBeacon>();
            _beacon.Bind(_session, null);

            _host.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) { Object.DestroyImmediate(_host); }
            if (_definition != null) { Object.DestroyImmediate(_definition); }
            SurgeryEvents.ResetAll();
        }

        [Test]
        public void ParadoEntreVisitantesOFarolFicaEmRepouso()
        {
            _beacon.Tick(0.1f);
            Assert.AreEqual(LedBeacon.BeaconState.Idle, _beacon.State);
        }

        [Test]
        public void ComARodadaCorrendoEORelogioCheioOFarolFicaVerde()
        {
            _session.BeginSession();
            _session.StartRound();
            _beacon.Tick(0.1f);

            Assert.AreEqual(LedBeacon.BeaconState.Running, _beacon.State,
                "Com tempo de sobra o LED tem que dizer 'tudo certo', não alertar.");
        }

        [Test]
        public void NoUltimoQuartoDoRelogioOFarolEntraEmAlerta()
        {
            _session.BeginSession();
            _session.StartRound();

            // 80% do relógio gasto: dentro do último quarto.
            _session.Tick(Round * 0.8f);
            _beacon.Tick(0.1f);

            Assert.AreEqual(LedBeacon.BeaconState.Warn, _beacon.State);
        }

        [Test]
        public void UmPassoClinicoRecusadoAcendeOVermelhoNaHora()
        {
            _session.BeginSession();
            _session.StartRound();
            _beacon.Tick(0.1f);
            Assert.AreEqual(LedBeacon.BeaconState.Running, _beacon.State);

            SurgeryEvents.RaiseError(ErrorSeverity.CriticalError, "cardioplegia sem clampe");
            _beacon.Tick(0.1f);

            Assert.AreEqual(LedBeacon.BeaconState.Error, _beacon.State,
                "O erro é o único momento em que o LED precisa gritar.");
        }

        [Test]
        public void OErroGanhaDoRelogioEnquantoEstaAceso()
        {
            _session.BeginSession();
            _session.StartRound();
            _session.Tick(Round * 0.9f);           // já estaria em alerta

            SurgeryEvents.RaiseError(ErrorSeverity.CriticalError, "desclampar antes da hora");
            _beacon.Tick(0.1f);

            Assert.AreEqual(LedBeacon.BeaconState.Error, _beacon.State,
                "Perder o aviso do erro para o 'tempo acabando' seria perder o que ensina.");
        }

        [Test]
        public void UmAvisoLeveNaoAcendeOVermelho()
        {
            _session.BeginSession();
            _session.StartRound();
            _beacon.Tick(0.1f);

            SurgeryEvents.RaiseError(ErrorSeverity.Warning, "atenção");
            _beacon.Tick(0.1f);

            Assert.AreEqual(LedBeacon.BeaconState.Running, _beacon.State,
                "Se todo aviso acendesse vermelho, o vermelho pararia de significar erro.");
        }

        [Test]
        public void TransplanteConcluidoEOFarolComemora()
        {
            _session.BeginSession();
            _session.StartRound();
            SurgeryEvents.RaiseSurgeryCompleted();
            _beacon.Tick(0.1f);

            Assert.AreEqual(LedBeacon.BeaconState.Success, _beacon.State);
        }

        [Test]
        public void TempoEsgotadoEOFarolMarcaDerrota()
        {
            _session.BeginSession();
            _session.StartRound();
            _session.Tick(Round + 1f);
            _beacon.Tick(0.1f);

            Assert.AreEqual(LedBeacon.BeaconState.Fail, _beacon.State);
        }

        /// <summary>
        /// O formato do pacote, lido de um socket de verdade.
        ///
        /// É o contrato com o firmware: o sketch procura "VRS:" e corta em dois-pontos. Um
        /// espaço a mais aqui e o LED nunca mais acende, sem erro em lugar nenhum.
        /// </summary>
        [UnityTest]
        public IEnumerator OPacoteSaiNoFormatoQueOEsp32Espera()
        {
            UdpClient ouvinte;
            int porta;

            try
            {
                ouvinte = new UdpClient(0, AddressFamily.InterNetwork);
                porta = ((IPEndPoint)ouvinte.Client.LocalEndPoint).Port;
                ouvinte.Client.ReceiveTimeout = 1500;
            }
            catch (SocketException)
            {
                Assert.Ignore("Sem socket disponível nesta máquina; o formato não pôde ser lido.");
                yield break;
            }

            _beacon.SetDevice("127.0.0.1", porta);

            _session.BeginSession();
            _session.StartRound();
            _beacon.Tick(0.1f);

            yield return null;

            string recebido = null;
            try
            {
                IPEndPoint remetente = new IPEndPoint(IPAddress.Any, 0);
                byte[] bytes = ouvinte.Receive(ref remetente);
                recebido = Encoding.ASCII.GetString(bytes);
            }
            catch (SocketException)
            {
                // deixa recebido nulo: a asserção abaixo explica melhor que a exceção
            }
            finally
            {
                ouvinte.Close();
            }

            Assert.IsNotNull(recebido, "Nenhum pacote chegou: o ESP32 não receberia nada.");

            string linha = recebido.Trim();
            StringAssert.StartsWith("VRS:", linha, "O sketch descarta o que não começa com VRS:.");

            string[] partes = linha.Split(':');
            Assert.AreEqual(3, partes.Length, $"Esperado VRS:ESTADO:urgencia, veio '{linha}'.");
            Assert.AreEqual("RUN", partes[1]);
            Assert.IsTrue(int.TryParse(partes[2], out int urgencia),
                $"A urgência tem que ser número — o atoi do sketch não perdoa. Veio '{partes[2]}'.");
            Assert.That(urgencia, Is.InRange(0, 100));
        }

        /// <summary>
        /// Ao desligar, o farol manda repouso.
        ///
        /// Sem isso o LED fica no último estado para sempre. Um vermelho preso na mesa depois que
        /// o jogo fechou diz ao público que o paciente está morrendo.
        /// </summary>
        [Test]
        public void AoDesligarOFarolVoltaParaRepouso()
        {
            _session.BeginSession();
            _session.StartRound();
            SurgeryEvents.RaiseError(ErrorSeverity.CriticalError, "erro");
            _beacon.Tick(0.1f);
            Assert.AreEqual(LedBeacon.BeaconState.Error, _beacon.State);

            _host.SetActive(false);

            Assert.AreEqual(LedBeacon.BeaconState.Idle, _beacon.State,
                "O último pacote antes de sair tem que apagar o LED.");
        }
    }
}
