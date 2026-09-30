using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using VRSurgery.Session;
using VRSurgery.Surgery;
using VRSurgery.Transplant;

namespace VRSurgery.Feedback
{
    /// <summary>
    /// Manda o estado da cirurgia para um LED físico num ESP32, por UDP.
    ///
    /// Por que UDP e não serial: o alvo é o Quest rodando sozinho, um Android sem porta serial e
    /// sem PC no estande. Sobra rede. E UDP em vez de TCP porque não há conexão para cair no meio
    /// do evento nem handshake para travar a thread do jogo — se um pacote se perder, o próximo
    /// conserta.
    ///
    /// O protocolo é texto legível de propósito: dá para depurar com um monitor serial no ESP32
    /// sem ferramenta nenhuma. Uma linha por pacote:
    ///
    ///     VRS:ESTADO:urgencia
    ///
    /// onde urgência vai de 0 a 100. Exemplo: <c>VRS:WARN:78</c>
    ///
    /// Duas decisões que existem por ser um estande e não uma bancada:
    ///
    /// 1. Reenvia o estado a cada <see cref="heartbeatSeconds"/> mesmo sem mudança. UDP perde
    ///    pacote, e um "acabou o erro" perdido deixaria o LED vermelho o resto do dia.
    /// 2. O ESP32 apaga sozinho se parar de receber (ver o sketch). Jogo travado ou Quest
    ///    desligado não pode deixar o LED aceso vermelho na mesa.
    ///
    /// Nada aqui aloca por quadro: o buffer e o StringBuilder são reaproveitados, e o envio só
    /// acontece quando o estado muda ou quando o heartbeat vence.
    /// </summary>
    public class LedBeacon : MonoBehaviour
    {
        /// <summary>O que o LED precisa saber. Traduzido para cor no ESP32, não aqui.</summary>
        public enum BeaconState
        {
            /// <summary>Ninguém jogando.</summary>
            Idle,

            /// <summary>Rodada em andamento, tempo tranquilo.</summary>
            Running,

            /// <summary>O relógio está acabando.</summary>
            Warn,

            /// <summary>O visitante acabou de errar um passo clínico.</summary>
            Error,

            /// <summary>Transplante concluído.</summary>
            Success,

            /// <summary>Tempo esgotado.</summary>
            Fail,
        }

        [Header("Rede")]
        [Tooltip("IP do ESP32 na rede. Fixe o IP no roteador ou use o SoftAP do próprio ESP32.")]
        [SerializeField] private string deviceAddress = "192.168.4.1";

        [SerializeField, Min(1)] private int devicePort = 4210;

        [Header("Fontes")]
        [SerializeField] private EventSessionController session;
        [SerializeField] private BypassWorker bypassWorker;

        [Header("Comportamento")]
        [Tooltip("A partir de quanta urgência o LED entra em alerta. 0,75 é o último quarto do relógio.")]
        [SerializeField, Range(0f, 1f)] private float warnAbove = 0.75f;

        [Tooltip("Quanto tempo o vermelho de erro fica aceso antes de voltar ao estado normal.")]
        [SerializeField, Min(0.2f)] private float errorHoldSeconds = 2.5f;

        [Tooltip("Reenvio periódico do estado, para um pacote perdido não deixar o LED errado.")]
        [SerializeField, Min(0.1f)] private float heartbeatSeconds = 0.5f;

        [Tooltip("Desligue para rodar sem o LED sem tirar o componente da cena.")]
        [SerializeField] private bool enabledBeacon = true;

        private UdpClient _socket;
        private IPEndPoint _target;
        private readonly StringBuilder _line = new StringBuilder(32);
        private byte[] _buffer = new byte[32];

        private BeaconState _state = BeaconState.Idle;
        private int _urgency;
        private float _errorUntil;
        private float _nextSend;
        private bool _lastSendFailed;

        /// <summary>Último estado enviado. Exposto para o HUD de diagnóstico e para os testes.</summary>
        public BeaconState State => _state;

        /// <summary>Quantos pacotes saíram. Zero depois de um minuto de jogo significa problema.</summary>
        public int PacketsSent { get; private set; }

        private void OnEnable()
        {
            SurgeryEvents.OnError += HandleError;
            if (bypassWorker != null) { bypassWorker.StepRefused += HandleRefusal; }

            OpenSocket();
        }

        private void OnDisable()
        {
            SurgeryEvents.OnError -= HandleError;
            if (bypassWorker != null) { bypassWorker.StepRefused -= HandleRefusal; }

            // Apaga ao sair, senão o LED fica no último estado para sempre.
            _state = BeaconState.Idle;
            _urgency = 0;
            Send();

            _socket?.Close();
            _socket = null;
        }

        private void OpenSocket()
        {
            if (!enabledBeacon) { return; }

            try
            {
                _target = new IPEndPoint(IPAddress.Parse(deviceAddress), devicePort);
                _socket = new UdpClient();
                _socket.Client.Blocking = false;
                Debug.Log($"[LED] farol apontado para {deviceAddress}:{devicePort}");
            }
            catch (Exception exception)
            {
                // Endereço inválido não pode derrubar a cirurgia. O estande roda sem o LED.
                Debug.LogWarning($"[LED] não foi possível abrir o socket ({exception.Message}); " +
                                 "o jogo segue sem o farol.");
                _socket = null;
            }
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Avalia o estado e envia se for preciso. Passo dado à mão nos testes.</summary>
        public void Tick(float deltaTime)
        {
            BeaconState resolved = Resolve();

            bool changed = resolved != _state;
            int urgency = session != null ? Mathf.RoundToInt(session.Urgency01 * 100f) : 0;

            // A urgência muda a cada quadro; só vale reenviar de dez em dez para o ESP32 poder
            // acelerar a pulsação sem receber trinta pacotes por segundo.
            bool urgencyMoved = Mathf.Abs(urgency - _urgency) >= 10;

            _state = resolved;
            _urgency = urgency;
            _nextSend -= deltaTime;

            if (changed || urgencyMoved || _nextSend <= 0f)
            {
                Send();
                _nextSend = heartbeatSeconds;
            }
        }

        /// <summary>
        /// Qual estado vale agora. O erro ganha de tudo enquanto está aceso — é o único momento em
        /// que o LED precisa gritar, e perder isso para um "tempo acabando" seria perder o aviso
        /// que ensina.
        /// </summary>
        private BeaconState Resolve()
        {
            if (Time.time < _errorUntil) { return BeaconState.Error; }

            if (session == null) { return BeaconState.Idle; }

            switch (session.State)
            {
                case SessionState.Success: return BeaconState.Success;
                case SessionState.Failure: return BeaconState.Fail;
                case SessionState.Running:
                    return session.Urgency01 >= warnAbove ? BeaconState.Warn : BeaconState.Running;
                default:
                    return BeaconState.Idle;
            }
        }

        private void HandleError(ErrorSeverity severity, string message)
        {
            if (severity == ErrorSeverity.Warning) { return; }
            Flash();
        }

        private void HandleRefusal(string reason) => Flash();

        /// <summary>Acende o vermelho de erro por alguns segundos.</summary>
        public void Flash() => _errorUntil = Time.time + errorHoldSeconds;

        private void Send()
        {
            if (_socket == null) { return; }

            _line.Clear();
            _line.Append("VRS:").Append(Name(_state)).Append(':').Append(_urgency).Append('\n');

            // Um caractere por byte, sem passar por string: ASCII e o conteúdo é sempre letra,
            // dois-pontos ou dígito. A versão anterior chamava ToString() duas vezes por envio,
            // ou seja, duas alocações por pacote, duas vezes por segundo, a tarde inteira.
            if (_buffer.Length < _line.Length) { _buffer = new byte[_line.Length]; }
            for (int i = 0; i < _line.Length; i++) { _buffer[i] = (byte)_line[i]; }

            int written = _line.Length;

            try
            {
                _socket.Send(_buffer, written, _target);
                PacketsSent++;
                _lastSendFailed = false;
            }
            catch (Exception exception)
            {
                // Uma vez só: com o ESP32 desligado isto dispararia a cada meio segundo e
                // encheria o log até esconder tudo que importa.
                if (!_lastSendFailed)
                {
                    Debug.LogWarning($"[LED] falha ao enviar ({exception.Message}); " +
                                     "silenciando até voltar a funcionar.");
                    _lastSendFailed = true;
                }
            }
        }

        private static string Name(BeaconState state) => state switch
        {
            BeaconState.Running => "RUN",
            BeaconState.Warn => "WARN",
            BeaconState.Error => "ERR",
            BeaconState.Success => "OK",
            BeaconState.Fail => "FAIL",
            _ => "IDLE",
        };

        public void Bind(EventSessionController controller, BypassWorker worker)
        {
            if (bypassWorker != null) { bypassWorker.StepRefused -= HandleRefusal; }

            session = controller;
            bypassWorker = worker;

            if (bypassWorker != null && isActiveAndEnabled) { bypassWorker.StepRefused += HandleRefusal; }
        }

        /// <summary>Aponta para outro aparelho. Útil para trocar de rede no dia do evento.</summary>
        public void SetDevice(string address, int port)
        {
            deviceAddress = address;
            devicePort = port;

            _socket?.Close();
            _socket = null;
            OpenSocket();
        }
    }
}
