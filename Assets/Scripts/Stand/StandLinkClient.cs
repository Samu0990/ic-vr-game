using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace VRSurgery.Stand
{
    /// <summary>
    /// The notebook's side of the stand link: finds the headset on the network, stays connected
    /// to it, and hands what arrives to the screens on the main thread.
    ///
    /// It never gives up. The headset is taken off, goes to sleep, loses Wi-Fi, gets restarted
    /// between visitors; every time it comes back the notebook reconnects on its own and the
    /// headset replays the rounds the notebook missed.
    /// </summary>
    public class StandLinkClient : MonoBehaviour
    {
        [Serializable]
        private class Hello
        {
            public bool wantsFrames;
            public string role;
        }

        private readonly ConcurrentQueue<(StandProtocol.MessageType type, byte[] payload)> _inbox =
            new ConcurrentQueue<(StandProtocol.MessageType, byte[])>();

        private readonly object _writeLock = new object();

        private Thread _discoveryThread;
        private Thread _scanThread;
        private volatile string _scannedHost;
        private Thread _connectionThread;
        private volatile bool _running;
        private volatile bool _connected;
        private volatile string _beaconHost;
        private volatile int _beaconPort = StandProtocol.DefaultTcpPort;
        private volatile string _connectedHost;
        private NetworkStream _stream;
        private TcpClient _tcp;
        private float _lastMessageAt = -100f;

        /// <summary>Fixed address, from -quest on the command line. Skips discovery.</summary>
        public string ManualHost { get; set; }

        public int ManualPort { get; set; } = StandProtocol.DefaultTcpPort;

        /// <summary>The projector wants pictures; the ranking screen does not, which halves what the headset sends.</summary>
        public bool WantsFrames { get; set; } = true;

        public string Role { get; set; } = "central";

        /// <summary>Connected, and the headset has said something in the last few seconds.</summary>
        public bool IsConnected => _connected && Time.unscaledTime - _lastMessageAt < 4f;

        public string QuestAddress => _connectedHost ?? _beaconHost ?? ManualHost;

        public event Action<StandState> StateReceived;
        public event Action<StandResult> ResultReceived;
        public event Action<byte[]> FrameReceived;

        private void OnEnable()
        {
            _running = true;

            if (string.IsNullOrEmpty(ManualHost))
            {
                _discoveryThread = new Thread(DiscoveryLoop) { IsBackground = true, Name = "StandDiscovery" };
                _discoveryThread.Start();
                _scanThread = new Thread(ScanLoop) { IsBackground = true, Name = "StandScan" };
                _scanThread.Start();
            }

            _connectionThread = new Thread(ConnectionLoop) { IsBackground = true, Name = "StandConnection" };
            _connectionThread.Start();
        }

        private void OnDisable()
        {
            _running = false;
            Disconnect();
        }

        private void OnApplicationQuit() => OnDisable();

        private void Update()
        {
            byte[] latestFrame = null;
            int handled = 0;

            while (handled++ < 200 && _inbox.TryDequeue(out var message))
            {
                _lastMessageAt = Time.unscaledTime;

                switch (message.type)
                {
                    case StandProtocol.MessageType.State:
                        StandState state = Parse<StandState>(message.payload);
                        if (state != null) { StateReceived?.Invoke(state); }
                        break;

                    case StandProtocol.MessageType.Result:
                        StandResult result = Parse<StandResult>(message.payload);
                        if (result != null) { ResultReceived?.Invoke(result); }
                        break;

                    case StandProtocol.MessageType.Frame:
                        // Vários quadros na fila: só o último vale a decodificação.
                        latestFrame = message.payload;
                        break;
                }
            }

            if (latestFrame != null) { FrameReceived?.Invoke(latestFrame); }
        }

        /// <summary>Sends the operator's name for a round to the headset. False if not connected.</summary>
        public bool SendName(string roundId, string name)
        {
            string json = JsonUtility.ToJson(new StandName { id = roundId, name = name });
            return Send(StandProtocol.Pack(StandProtocol.MessageType.Name, json));
        }

        private bool Send(byte[] message)
        {
            lock (_writeLock)
            {
                if (!_connected || _stream == null) { return false; }
                try
                {
                    _stream.Write(message, 0, message.Length);
                    return true;
                }
                catch (Exception)
                {
                    Disconnect();
                    return false;
                }
            }
        }

        private static T Parse<T>(byte[] payload) where T : class
        {
            try { return JsonUtility.FromJson<T>(StandProtocol.Text(payload)); }
            catch (Exception) { return null; }
        }

        // ------------------------------------------------------------------ threads

        private void DiscoveryLoop()
        {
            UdpClient udp = null;
            try
            {
                udp = new UdpClient();
                // Projetor e ranking são dois programas no mesmo notebook, ouvindo a mesma porta.
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, StandProtocol.BeaconPort));
                udp.Client.ReceiveTimeout = 1000;

                while (_running)
                {
                    try
                    {
                        IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                        byte[] data = udp.Receive(ref from);
                        if (StandProtocol.TryParseBeacon(Encoding.ASCII.GetString(data), out int port, out _))
                        {
                            _beaconHost = from.Address.ToString();
                            _beaconPort = port;
                        }
                    }
                    catch (SocketException)
                    {
                        // Tempo esgotado sem aviso: tenta de novo.
                    }
                }
            }
            catch (Exception error)
            {
                Debug.LogWarning($"[Estande] Não foi possível ouvir o aviso do óculos na porta {StandProtocol.BeaconPort}: {error.Message}. " +
                                 "Use -quest <IP do óculos> para ligar direto.");
            }
            finally
            {
                udp?.Close();
            }
        }

        private void ConnectionLoop()
        {
            while (_running)
            {
                string host = !string.IsNullOrEmpty(ManualHost) ? ManualHost : _beaconHost ?? _scannedHost;
                int port = !string.IsNullOrEmpty(ManualHost) ? ManualPort : _beaconHost != null ? _beaconPort : StandProtocol.DefaultTcpPort;

                if (string.IsNullOrEmpty(host))
                {
                    Thread.Sleep(500);
                    continue;
                }

                TcpClient tcp = new TcpClient { NoDelay = true };
                try
                {
                    if (!tcp.ConnectAsync(host, port).Wait(2000)) { throw new TimeoutException(); }

                    NetworkStream stream = tcp.GetStream();
                    lock (_writeLock)
                    {
                        _tcp = tcp;
                        _stream = stream;
                        _connected = true;
                        _connectedHost = host;
                    }

                    Send(StandProtocol.Pack(StandProtocol.MessageType.Hello,
                        JsonUtility.ToJson(new Hello { wantsFrames = WantsFrames, role = Role })));

                    while (_running && StandProtocol.TryRead(stream, out StandProtocol.MessageType type, out byte[] payload))
                    {
                        _inbox.Enqueue((type, payload));
                    }
                }
                catch (Exception)
                {
                    // Óculos fora do ar: tenta de novo daqui a pouco.
                }

                Disconnect();
                try { tcp.Close(); } catch (Exception) { }
                if (_running) { Thread.Sleep(1500); }
            }
        }

        /// <summary>
        /// Plan B when no beacon arrives: knock on port 47821 at every address of the local
        /// network. A firewall on the notebook (ufw is on by default on Linux Mint) drops the
        /// headset's broadcast, and some access points never forward one — but an outgoing
        /// connection goes through both. A hotspot is a /24, so the whole sweep is 254 short
        /// knocks, done in parallel in a second or two.
        /// </summary>
        private void ScanLoop()
        {
            Thread.Sleep(3000);

            while (_running)
            {
                if (!_connected && _beaconHost == null)
                {
                    string found = Sweep();
                    if (found != null) { _scannedHost = found; }
                }

                Thread.Sleep(_connected ? 5000 : 8000);
            }
        }

        private static string Sweep()
        {
            List<IPAddress> candidates = new List<IPAddress>();
            List<IPAddress> own = new List<IPAddress>();

            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) { continue; }

                    foreach (UnicastIPAddressInformation unicast in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) { continue; }
                        byte[] ip = unicast.Address.GetAddressBytes();
                        if (ip[0] == 127 || (ip[0] == 169 && ip[1] == 254)) { continue; }

                        own.Add(unicast.Address);
                        // Só a /24 do próprio endereço: uma rede maior que isso é de evento, não do estande.
                        for (int last = 1; last < 255; last++)
                        {
                            candidates.Add(new IPAddress(new[] { ip[0], ip[1], ip[2], (byte)last }));
                        }
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }

            candidates.RemoveAll(c => own.Exists(o => o.Equals(c)));
            if (candidates.Count == 0) { return null; }

            string found = null;
            using (CountdownEvent pending = new CountdownEvent(candidates.Count))
            {
                foreach (IPAddress candidate in candidates)
                {
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            using (TcpClient knock = new TcpClient())
                            {
                                if (found == null && knock.ConnectAsync(candidate, StandProtocol.DefaultTcpPort).Wait(600) && knock.Connected)
                                {
                                    Interlocked.CompareExchange(ref found, candidate.ToString(), null);
                                }
                            }
                        }
                        catch (Exception)
                        {
                        }
                        finally
                        {
                            pending.Signal();
                        }
                    });
                }

                pending.Wait(5000);
            }

            return found;
        }

        private void Disconnect()
        {
            lock (_writeLock)
            {
                _connected = false;
                _connectedHost = null;
                try { _stream?.Close(); } catch (Exception) { }
                try { _tcp?.Close(); } catch (Exception) { }
                _stream = null;
                _tcp = null;
            }
        }
    }
}
