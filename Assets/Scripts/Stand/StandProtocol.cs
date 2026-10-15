using System;
using System.IO;
using System.Text;

namespace VRSurgery.Stand
{
    /// <summary>
    /// How the headset and the stand's notebook talk.
    ///
    /// The operation runs inside the Quest. The notebook beside the mannequin has no copy of the
    /// game: it shows what the Quest sends — the overhead picture of the patient for the
    /// projector, the round's clock and stage, each finished round for the day's ranking — and
    /// sends back the name the operator typed.
    ///
    /// Discovery is one way on purpose. The Quest shouts a beacon by UDP broadcast and the
    /// notebook connects to it by TCP. Sending a broadcast from Android is reliable; receiving
    /// one is not (the Wi-Fi stack filters broadcasts to save power), so the headset never has to
    /// listen for the notebook.
    ///
    /// On the wire every message is [type: 1 byte][length: int32 little-endian][payload].
    /// Text payloads are UTF-8 JSON; a frame is a JPEG.
    /// </summary>
    public static class StandProtocol
    {
        public const int Version = 1;

        /// <summary>UDP port the Quest's beacon is sent to.</summary>
        public const int BeaconPort = 47820;

        /// <summary>TCP port the Quest listens on for the stand's notebook.</summary>
        public const int DefaultTcpPort = 47821;

        public const string BeaconMagic = "VRSURGERY_QUEST";

        /// <summary>Anything larger is a corrupt length, not a message.</summary>
        public const int MaxPayloadBytes = 8 * 1024 * 1024;

        /// <summary>Longest name the stand accepts; the same limit as the headset's own keyboard.</summary>
        public const int MaxNameLength = 12;

        public enum MessageType : byte
        {
            Hello = (byte)'H',
            State = (byte)'S',
            Result = (byte)'R',
            Frame = (byte)'F',
            Name = (byte)'N',
        }

        public static byte[] Pack(MessageType type, byte[] payload)
        {
            payload ??= Array.Empty<byte>();
            byte[] message = new byte[5 + payload.Length];
            message[0] = (byte)type;
            WriteInt32(message, 1, payload.Length);
            Buffer.BlockCopy(payload, 0, message, 5, payload.Length);
            return message;
        }

        public static byte[] Pack(MessageType type, string json) =>
            Pack(type, Encoding.UTF8.GetBytes(json ?? string.Empty));

        /// <summary>
        /// Reads one whole message. Blocks until it arrives; false when the other side closed the
        /// connection cleanly between messages. Throws on a broken stream or a corrupt length.
        /// </summary>
        public static bool TryRead(Stream stream, out MessageType type, out byte[] payload)
        {
            type = default;
            payload = null;

            byte[] header = new byte[5];
            if (!ReadExactly(stream, header, 5, allowCleanEnd: true))
            {
                return false;
            }

            int length = ReadInt32(header, 1);
            if (length < 0 || length > MaxPayloadBytes)
            {
                throw new InvalidDataException($"Tamanho de mensagem impossível: {length} bytes.");
            }

            payload = new byte[length];
            if (length > 0)
            {
                ReadExactly(stream, payload, length, allowCleanEnd: false);
            }

            type = (MessageType)header[0];
            return true;
        }

        public static string Text(byte[] payload) =>
            payload == null ? string.Empty : Encoding.UTF8.GetString(payload);

        public static string BuildBeacon(int tcpPort, string deviceName) =>
            $"{BeaconMagic} {Version} {tcpPort} {Clean(deviceName)}";

        public static bool TryParseBeacon(string text, out int tcpPort, out string deviceName)
        {
            tcpPort = 0;
            deviceName = string.Empty;
            if (string.IsNullOrEmpty(text)) { return false; }

            string[] parts = text.Trim().Split(new[] { ' ' }, 4);
            if (parts.Length < 3 || parts[0] != BeaconMagic) { return false; }
            if (!int.TryParse(parts[1], out int version) || version != Version) { return false; }
            if (!int.TryParse(parts[2], out tcpPort) || tcpPort <= 0 || tcpPort > 65535) { return false; }

            deviceName = parts.Length > 3 ? parts[3] : string.Empty;
            return true;
        }

        /// <summary>
        /// The name as it will appear on the public ranking: letters, digits and single spaces,
        /// trimmed, at most <see cref="MaxNameLength"/> characters. Accented letters are kept —
        /// "João" and "Conceição" are names, not typos.
        /// </summary>
        public static string SanitizeName(string name, int maxLength = MaxNameLength)
        {
            if (string.IsNullOrWhiteSpace(name)) { return string.Empty; }

            StringBuilder clean = new StringBuilder(name.Length);
            bool lastWasSpace = true;
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c))
                {
                    clean.Append(c);
                    lastWasSpace = false;
                }
                else if (char.IsWhiteSpace(c) && !lastWasSpace)
                {
                    clean.Append(' ');
                    lastWasSpace = true;
                }

                if (clean.Length >= maxLength) { break; }
            }

            return clean.ToString().Trim();
        }

        private static string Clean(string text) =>
            string.IsNullOrWhiteSpace(text) ? "Quest" : text.Replace('\n', ' ').Replace('\r', ' ').Trim();

        private static bool ReadExactly(Stream stream, byte[] buffer, int count, bool allowCleanEnd)
        {
            int read = 0;
            while (read < count)
            {
                int n = stream.Read(buffer, read, count - read);
                if (n <= 0)
                {
                    if (read == 0 && allowCleanEnd) { return false; }
                    throw new EndOfStreamException("A conexão caiu no meio de uma mensagem.");
                }

                read += n;
            }

            return true;
        }

        private static void WriteInt32(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        private static int ReadInt32(byte[] buffer, int offset) =>
            buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24);
    }

    /// <summary>What the Quest is doing right now. Sent a few times a second.</summary>
    [Serializable]
    public class StandState
    {
        public int v = StandProtocol.Version;
        public string device;
        public string session;
        public float remaining;
        public float round;
        public float elapsed;
        public string stage;
        public string instruction;
        public float urgency;
        public bool awaitingName;
        public string typedName;
        public string pendingRoundId;
    }

    /// <summary>
    /// One finished round. Sent when the round ends, and again when the visitor's name is known.
    /// The id is unique across restarts of the headset, so the notebook can merge the two.
    /// </summary>
    [Serializable]
    public class StandResult
    {
        public string id;
        public bool succeeded;
        public float elapsed;
        public float remaining;
        public int score;
        public string name;
        public string nameSource;
        public string stageReached;
        public long utcTicks;
    }

    /// <summary>The name typed on the notebook for a round, sent back to the headset.</summary>
    [Serializable]
    public class StandName
    {
        public string id;
        public string name;
    }

    public static class NameSource
    {
        public const string None = "";
        public const string Headset = "oculos";
        public const string Operator = "operador";
    }
}
