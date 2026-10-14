using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using VRSurgery.Session;
using VRSurgery.Transplant;

namespace VRSurgery.Stand
{
    /// <summary>
    /// The headset's side of the stand link: tells the notebook beside the mannequin what is
    /// happening in the operation, and takes the visitor's name back from it.
    ///
    /// Everything that touches the network runs on its own threads. A Wi-Fi drop, a notebook
    /// that never connects or one that vanishes mid-round must not cost the headset a single
    /// frame — on a Quest a stall is felt in the stomach, not just seen. With no notebook
    /// connected the only work done is one beacon a second; the overhead camera does not even
    /// render.
    /// </summary>
    public class StandLinkServer : MonoBehaviour
    {
        [SerializeField] private EventSessionController session;
        [SerializeField] private NameEntryController nameEntry;
        [SerializeField] private TransplantProcedure procedure;
        [SerializeField] private StandFrameStreamer streamer;

        [Tooltip("TCP port the notebook connects to. 0 picks a free one (tests).")]
        [SerializeField] private int tcpPort = StandProtocol.DefaultTcpPort;

        [Tooltip("Announce the headset on the network so the notebook finds it without an IP.")]
        [SerializeField] private bool sendBeacon = true;

        [SerializeField, Min(1f)] private float statesPerSecond = 5f;

        [Tooltip("How many finished rounds are replayed to a notebook that (re)connects.")]
        [SerializeField, Min(1)] private int resultsKept = 40;

        private const int MaxClients = 4;

        private sealed class Client
        {
            public TcpClient Tcp;
            public NetworkStream Stream;
            public string Remote;
            public readonly ConcurrentQueue<byte[]> Outbox = new ConcurrentQueue<byte[]>();
            public readonly SemaphoreSlim Signal = new SemaphoreSlim(0);
            public byte[] PendingFrame;
            public volatile bool WantsFrames = true;
            public volatile bool Closed;
        }

        [Serializable]
        private class HelloFromNotebook
        {
            public bool wantsFrames = true;
            public string role;
        }

        private readonly List<Client> _clients = new List<Client>();
        private readonly ConcurrentQueue<Client> _newClients = new ConcurrentQueue<Client>();
        private readonly ConcurrentQueue<string> _incomingNames = new ConcurrentQueue<string>();
        private readonly List<StandResult> _recent = new List<StandResult>();

        private TcpListener _listener;
        private Thread _acceptThread;
        private Thread _beaconThread;
        private volatile bool _running;
        private int _boundPort;
        private string _deviceName = "Quest";
        private string _bootId;
        private int _roundCounter;
        private float _stateTimer;
        private string _pendingRoundId;

        /// <summary>Repeated by the writer threads while the game is paused (headset taken off).</summary>
        private volatile byte[] _lastState;
        private bool _nameFromOperator;
        private bool _bound;

        public int Port => _boundPort;
        public bool IsRunning => _running;

        public int ClientCount
        {
            get { lock (_clients) { return _clients.Count; } }
        }

        public bool HasClients => ClientCount > 0;

        public IReadOnlyList<StandResult> RecentResults => _recent;

        /// <summary>A name typed on the notebook, raised on the main thread.</summary>
        public event Action<StandName> NameReceived;

        public void Bind(EventSessionController controller, NameEntryController names, TransplantProcedure operation,
            StandFrameStreamer frames)
        {
            Unsubscribe();
            session = controller;
            nameEntry = names;
            procedure = operation;
            streamer = frames;
            if (isActiveAndEnabled) { Subscribe(); }
        }

        /// <summary>Test hook: the port, and whether to shout on the network about it.</summary>
        public void Configure(int port, bool beacon)
        {
            tcpPort = port;
            sendBeacon = beacon;
        }

        private void OnEnable()
        {
            _bootId = Guid.NewGuid().ToString("N").Substring(0, 6);
            _deviceName = string.IsNullOrWhiteSpace(SystemInfo.deviceName) ? "Quest" : SystemInfo.deviceName;
            Subscribe();
            StartNetwork();
        }

        private void OnDisable()
        {
            Unsubscribe();
            StopNetwork();
        }

        private void OnApplicationQuit() => StopNetwork();

        private void Subscribe()
        {
            if (_bound) { return; }
            if (session != null) { session.RoundEnded += HandleRoundEnded; }
            if (nameEntry != null) { nameEntry.NameFiled += HandleNameFiled; }
            if (streamer != null) { streamer.FrameEncoded += BroadcastFrame; }
            _bound = true;
        }

        private void Unsubscribe()
        {
            if (!_bound) { return; }
            if (session != null) { session.RoundEnded -= HandleRoundEnded; }
            if (nameEntry != null) { nameEntry.NameFiled -= HandleNameFiled; }
            if (streamer != null) { streamer.FrameEncoded -= BroadcastFrame; }
            _bound = false;
        }

        // ------------------------------------------------------------------ main thread

        private void Update()
        {
            while (_newClients.TryDequeue(out Client client)) { Welcome(client); }

            while (_incomingNames.TryDequeue(out string json))
            {
                StandName name = null;
                try { name = JsonUtility.FromJson<StandName>(json); } catch (Exception) { }
                if (name != null) { HandleOperatorName(name); }
            }

            PruneClosed();

            if (streamer != null) { streamer.Wanted = AnyClientWantsFrames(); }

            if (!HasClients) { return; }

            _stateTimer += Time.unscaledDeltaTime;
            if (_stateTimer >= 1f / statesPerSecond)
            {
                _stateTimer = 0f;
                byte[] state = StandProtocol.Pack(StandProtocol.MessageType.State, JsonUtility.ToJson(BuildState()));
                _lastState = state;
                Broadcast(state);
            }
        }

        public StandState BuildState()
        {
            StandState state = new StandState { device = _deviceName, pendingRoundId = _pendingRoundId };

            if (session != null)
            {
                state.session = session.State.ToString();
                state.remaining = session.RemainingSeconds;
                state.round = session.RoundSeconds;
                state.elapsed = session.ElapsedSeconds;
                state.urgency = session.Urgency01;
            }

            if (procedure != null)
            {
                state.stage = procedure.Stage.ToString();
                state.instruction = procedure.CurrentInstruction;
            }

            if (nameEntry != null)
            {
                state.awaitingName = nameEntry.IsAwaitingName;
                state.typedName = nameEntry.TypedName;
            }

            return state;
        }

        private void HandleRoundEnded(SessionResult result)
        {
            StandResult round = new StandResult
            {
                id = $"{_bootId}-{++_roundCounter}",
                succeeded = result.Succeeded,
                elapsed = result.ElapsedSeconds,
                remaining = result.RemainingSeconds,
                score = result.Score,
                name = string.Empty,
                nameSource = NameSource.None,
                stageReached = procedure != null ? procedure.Stage.ToString() : string.Empty,
                utcTicks = DateTime.UtcNow.Ticks,
            };

            _pendingRoundId = round.succeeded ? round.id : null;
            PublishResult(round);
        }

        private void HandleNameFiled(string name, SessionResult result)
        {
            StandResult round = Find(_pendingRoundId);
            _pendingRoundId = null;
            if (round == null) { return; }

            // "Anônimo" é o que o teclado do óculos arquiva quando ninguém digitou nada: não é um nome.
            bool typed = _nameFromOperator || !string.Equals(name, StandRanking.FallbackName, StringComparison.Ordinal);
            round.name = typed ? StandProtocol.SanitizeName(name) : string.Empty;
            round.nameSource = !typed ? NameSource.None : _nameFromOperator ? NameSource.Operator : NameSource.Headset;
            _nameFromOperator = false;
            PublishResult(round);
        }

        private void HandleOperatorName(StandName name)
        {
            // O visitante ainda está diante do teclado do óculos: o nome digitado no notebook fecha
            // esse teclado, para ele não ter de digitar de novo.
            if (nameEntry != null && nameEntry.IsAwaitingName && name.id == _pendingRoundId)
            {
                _nameFromOperator = true;
                nameEntry.ConfirmWith(name.name);
            }
            else
            {
                StandResult round = Find(name.id);
                if (round != null)
                {
                    round.name = StandProtocol.SanitizeName(name.name);
                    round.nameSource = NameSource.Operator;
                }
            }

            NameReceived?.Invoke(name);
        }

        /// <summary>Records a finished round and sends it to every connected notebook.</summary>
        public void PublishResult(StandResult round)
        {
            if (round == null) { return; }
            int existing = _recent.FindIndex(r => r.id == round.id);
            if (existing >= 0) { _recent[existing] = round; } else { _recent.Add(round); }
            while (_recent.Count > resultsKept) { _recent.RemoveAt(0); }

            Broadcast(StandProtocol.Pack(StandProtocol.MessageType.Result, JsonUtility.ToJson(round)));
        }

        private StandResult Find(string id) =>
            string.IsNullOrEmpty(id) ? null : _recent.Find(r => r.id == id);

        private void Welcome(Client client)
        {
            Enqueue(client, StandProtocol.Pack(StandProtocol.MessageType.Hello,
                $"{{\"v\":{StandProtocol.Version},\"device\":\"{_deviceName.Replace("\"", "'")}\"}}"));

            // Um notebook que reconecta (ou que foi ligado depois) recebe as rodadas que perdeu.
            foreach (StandResult round in _recent)
            {
                Enqueue(client, StandProtocol.Pack(StandProtocol.MessageType.Result, JsonUtility.ToJson(round)));
            }

            Enqueue(client, StandProtocol.Pack(StandProtocol.MessageType.State, JsonUtility.ToJson(BuildState())));
        }

        private bool AnyClientWantsFrames()
        {
            lock (_clients)
            {
                foreach (Client c in _clients) { if (c.WantsFrames && !c.Closed) { return true; } }
            }

            return false;
        }

        // ------------------------------------------------------------------ any thread

        private void Broadcast(byte[] message)
        {
            lock (_clients)
            {
                foreach (Client c in _clients) { Enqueue(c, message); }
            }
        }

        /// <summary>Thread-safe; called by the frame streamer's encoder thread.</summary>
        private void BroadcastFrame(byte[] jpeg)
        {
            if (jpeg == null || jpeg.Length == 0) { return; }
            byte[] message = StandProtocol.Pack(StandProtocol.MessageType.Frame, jpeg);

            lock (_clients)
            {
                foreach (Client c in _clients)
                {
                    if (!c.WantsFrames || c.Closed) { continue; }
                    // Só o quadro mais novo importa: um notebook lento pula quadros em vez de acumular atraso.
                    Interlocked.Exchange(ref c.PendingFrame, message);
                    c.Signal.Release();
                }
            }
        }

        private static void Enqueue(Client c, byte[] message)
        {
            if (c.Closed) { return; }
            c.Outbox.Enqueue(message);
            c.Signal.Release();
        }

        // ------------------------------------------------------------------ network threads

        private void StartNetwork()
        {
            if (_running) { return; }

            try
            {
                _listener = new TcpListener(IPAddress.Any, tcpPort);
                // Jogo reaberto logo depois de fechar: as conexões antigas ainda estão em TIME_WAIT
                // nesta porta, e sem isto o óculos voltaria sem notebook até alguém reiniciar.
                _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listener.Start();
                _boundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"[Estande] Não foi possível abrir a porta {tcpPort}: {error.Message}. " +
                                 "O jogo segue normalmente, sem o notebook.");
                _listener = null;
                return;
            }

            _running = true;
            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "StandAccept" };
            _acceptThread.Start();

            if (sendBeacon)
            {
                _beaconThread = new Thread(BeaconLoop) { IsBackground = true, Name = "StandBeacon" };
                _beaconThread.Start();
            }

            Debug.Log($"[Estande] Esperando o notebook na porta {_boundPort} (aviso na rede: {(sendBeacon ? "ligado" : "desligado")}).");
        }

        private void StopNetwork()
        {
            if (!_running) { return; }
            _running = false;

            try { _listener?.Stop(); } catch (Exception) { }
            _listener = null;

            lock (_clients)
            {
                foreach (Client c in _clients) { Close(c); }
                _clients.Clear();
            }
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                TcpClient tcp;
                try
                {
                    tcp = _listener.AcceptTcpClient();
                }
                catch (Exception)
                {
                    // Parar o listener faz o Accept lançar: é a saída normal deste laço.
                    break;
                }

                tcp.NoDelay = true;
                tcp.SendTimeout = 3000;

                Client client = new Client
                {
                    Tcp = tcp,
                    Stream = tcp.GetStream(),
                    Remote = tcp.Client.RemoteEndPoint?.ToString() ?? "?",
                };

                lock (_clients)
                {
                    if (_clients.Count >= MaxClients)
                    {
                        tcp.Close();
                        continue;
                    }

                    _clients.Add(client);
                }

                new Thread(() => WriteLoop(client)) { IsBackground = true, Name = "StandWrite" }.Start();
                new Thread(() => ReadLoop(client)) { IsBackground = true, Name = "StandRead" }.Start();
                _newClients.Enqueue(client);
            }
        }

        private void WriteLoop(Client c)
        {
            try
            {
                DateTime lastSent = DateTime.UtcNow;
                while (_running && !c.Closed)
                {
                    c.Signal.Wait(500);
                    bool sent = false;

                    while (c.Outbox.TryDequeue(out byte[] message))
                    {
                        c.Stream.Write(message, 0, message.Length);
                        sent = true;
                    }

                    byte[] frame = Interlocked.Exchange(ref c.PendingFrame, null);
                    if (frame != null) { c.Stream.Write(frame, 0, frame.Length); sent = true; }

                    // Óculos tirado entre um visitante e outro: o Quest pausa o jogo e o Update para,
                    // mas esta thread não. Repetir o último estado mantém o notebook "conectado" e o
                    // projetor com a última imagem, em vez de piscar "procurando o óculos" a cada troca.
                    byte[] last = _lastState;
                    if (sent) { lastSent = DateTime.UtcNow; }
                    else if (last != null && (DateTime.UtcNow - lastSent).TotalSeconds >= 2)
                    {
                        c.Stream.Write(last, 0, last.Length);
                        lastSent = DateTime.UtcNow;
                    }
                }
            }
            catch (Exception)
            {
                // Notebook desligado ou Wi-Fi caiu: o cliente some e o notebook reconecta sozinho.
            }

            Close(c);
        }

        private void ReadLoop(Client c)
        {
            try
            {
                while (_running && !c.Closed)
                {
                    if (!StandProtocol.TryRead(c.Stream, out StandProtocol.MessageType type, out byte[] payload)) { break; }

                    if (type == StandProtocol.MessageType.Name)
                    {
                        _incomingNames.Enqueue(StandProtocol.Text(payload));
                    }
                    else if (type == StandProtocol.MessageType.Hello)
                    {
                        // JsonUtility funciona fora da thread principal.
                        HelloFromNotebook hello = JsonUtility.FromJson<HelloFromNotebook>(StandProtocol.Text(payload));
                        if (hello != null) { c.WantsFrames = hello.wantsFrames; }
                    }
                }
            }
            catch (Exception)
            {
            }

            Close(c);
        }

        private static void Close(Client c)
        {
            if (c.Closed) { return; }
            c.Closed = true;
            try { c.Stream?.Close(); } catch (Exception) { }
            try { c.Tcp?.Close(); } catch (Exception) { }
            try { c.Signal.Release(); } catch (Exception) { }
        }

        private void PruneClosed()
        {
            lock (_clients) { _clients.RemoveAll(c => c.Closed); }
        }

        private void BeaconLoop()
        {
            UdpClient udp = null;
            try
            {
                udp = new UdpClient { EnableBroadcast = true };
                byte[] beacon = Encoding.ASCII.GetBytes(StandProtocol.BuildBeacon(_boundPort, Ascii(_deviceName)));

                int round = 0;
                List<IPAddress> targets = BroadcastTargets();
                while (_running)
                {
                    // A rede pode mudar com o jogo aberto (hotspot ligado depois): recalcula de vez em quando.
                    if (++round % 10 == 0) { targets = BroadcastTargets(); }

                    foreach (IPAddress target in targets)
                    {
                        try { udp.Send(beacon, beacon.Length, new IPEndPoint(target, StandProtocol.BeaconPort)); }
                        catch (Exception) { }
                    }

                    Thread.Sleep(1000);
                }
            }
            catch (Exception error)
            {
                Debug.LogWarning($"[Estande] Aviso na rede desligado: {error.Message}");
            }
            finally
            {
                udp?.Close();
            }
        }

        /// <summary>
        /// Where to shout: the limited broadcast, plus each network's own broadcast address.
        /// Some access points drop 255.255.255.255 but pass 192.168.x.255, and the reverse.
        /// </summary>
        private static List<IPAddress> BroadcastTargets()
        {
            List<IPAddress> targets = new List<IPAddress> { IPAddress.Broadcast };

            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) { continue; }

                    foreach (UnicastIPAddressInformation unicast in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) { continue; }
                        IPAddress mask = null;
                        try { mask = unicast.IPv4Mask; } catch (Exception) { }
                        AddDirected(targets, unicast.Address, mask);
                    }
                }
            }
            catch (Exception)
            {
                // Android às vezes não deixa listar as interfaces: cai no truque abaixo.
            }

            // O IP pelo qual sairia um pacote, sem enviar nada. Assume /24, o padrão de hotspot e roteador doméstico.
            try
            {
                using (Socket probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    probe.Connect(new IPEndPoint(IPAddress.Parse("10.255.255.255"), 1));
                    if (probe.LocalEndPoint is IPEndPoint local) { AddDirected(targets, local.Address, null); }
                }
            }
            catch (Exception)
            {
            }

            return targets;
        }

        private static void AddDirected(List<IPAddress> targets, IPAddress address, IPAddress mask)
        {
            byte[] ip = address.GetAddressBytes();
            byte[] m = mask != null && !mask.Equals(IPAddress.Any) ? mask.GetAddressBytes() : new byte[] { 255, 255, 255, 0 };
            if (ip.Length != 4 || m.Length != 4 || ip[0] == 127) { return; }

            byte[] broadcast = new byte[4];
            for (int i = 0; i < 4; i++) { broadcast[i] = (byte)(ip[i] | ~m[i]); }

            IPAddress directed = new IPAddress(broadcast);
            if (!targets.Exists(t => t.Equals(directed))) { targets.Add(directed); }
        }

        private static string Ascii(string text)
        {
            StringBuilder clean = new StringBuilder();
            foreach (char c in text) { clean.Append(c < 128 && c >= 32 ? c : '_'); }
            return clean.ToString();
        }
    }
}
