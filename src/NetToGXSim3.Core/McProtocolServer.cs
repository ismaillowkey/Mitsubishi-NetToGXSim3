using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace NetToGXSim3.Core
{
    public enum ServerTransportMode { Both, TcpOnly, UdpOnly }

    // Identitas CPU yang diumumkan ke client MC (command 0x0101 Read CPU Type).
    // FX3U : identitas Q-series klasik "Q02UCPU" (code 0x0022)
    // FX5  : identitas iQ-F "FX5UCPU" (code 0x4105), dipakai HMI seperti EasyBuilder Pro
    public enum McCpuMode { FX3U, FX5 }

    /// <summary>
    /// MC Protocol server (Qna-3E binary, TCP &amp; UDP) yang membaca/menulis langsung ke
    /// GX Simulator 3. Parsing frame memakai library NNMitsubishiServer.Fx5uServer
    /// (lihat GxSim3EFrameParser), sedangkan socket transport tetap ditangani di sini
    /// agar mendukung TCP + UDP sekaligus, auto-increment port, dan UDP WSAECONNRESET fix.
    /// </summary>
    public class McProtocolServer
    {
        // Panjang header Qna-3E binary (subheader + net/pc/io/station + data length)
        private const int Qna3EHeaderLength = 9;

        private TcpListener? _listener;
        private UdpClient? _udp;
        private CancellationTokenSource? _cts;
        private readonly List<TcpClient> _clients = new List<TcpClient>();
        private readonly GxSim3EFrameParser _parser;
        private readonly object _parseLock = new object();

        public string Name { get; set; }
        public ServerTransportMode TransportMode { get; set; }
        public McCpuMode CpuMode { get; set; }
        public int Port { get; private set; }
        public bool IsRunning { get; private set; }
        public long RequestsProcessed { get; private set; }

        // Nama model CPU yang diumumkan pada command 0x0101 (Read CPU Type).
        public string CpuModel
        {
            get
            {
                if (CpuMode == McCpuMode.FX5)
                {
                    return "FX5UCPU";
                }
                return "Q02UCPU";
            }
        }

        // Kode tipe CPU yang menyertai nama model (command 0x0101).
        public ushort CpuTypeCode
        {
            get
            {
                if (CpuMode == McCpuMode.FX5)
                {
                    return 0x4105;
                }
                return 0x0022;
            }
        }

        public event Action<string>? LogMessage;

        public McProtocolServer(GxSimulatorEngine engine, string name = "MC Protocol", int defaultPort = 5001,
            ServerTransportMode transportMode = ServerTransportMode.Both, McCpuMode cpuMode = McCpuMode.FX5)
        {
            Name = name;
            Port = defaultPort;
            TransportMode = transportMode;
            CpuMode = cpuMode;

            _parser = new GxSim3EFrameParser(new GxSimDeviceBufferMap(engine), engine);
            _parser.OnLogMessage += (msg) => LogMessage?.Invoke($"[{Name}] {msg}");
        }

        public static bool IsPortAvailable(int port, ServerTransportMode mode = ServerTransportMode.Both)
        {
            try
            {
                if (mode != ServerTransportMode.UdpOnly)
                {
                    using (Socket probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                    {
                        probe.ExclusiveAddressUse = true;
                        probe.Bind(new IPEndPoint(IPAddress.Any, port));
                    }
                }

                if (mode != ServerTransportMode.TcpOnly)
                {
                    using (Socket probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                    {
                        probe.ExclusiveAddressUse = true;
                        probe.Bind(new IPEndPoint(IPAddress.Any, port));
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static int GetNextAvailablePort(int startingPort, ServerTransportMode mode = ServerTransportMode.Both)
        {
            for (int port = Math.Max(1, startingPort); port <= 65535; port++)
            {
                if (IsPortAvailable(port, mode))
                {
                    return port;
                }
            }
            return -1;
        }

        public int Start(int requestedPort = 5001)
        {
            if (IsRunning)
            {
                return Port;
            }

            int port = GetNextAvailablePort(requestedPort, TransportMode);
            if (port < 1)
            {
                LogMessage?.Invoke($"[{Name}] No free port available on TCP/UDP.");
                return -1;
            }

            try
            {
                _cts = new CancellationTokenSource();

                if (TransportMode != ServerTransportMode.UdpOnly)
                {
                    _listener = new TcpListener(IPAddress.Any, port);
                    _listener.Server.ExclusiveAddressUse = true;
                    _listener.Start();
                }

                if (TransportMode != ServerTransportMode.TcpOnly)
                {
                    _udp = new UdpClient(port);

                    // Cegah exception WSAECONNRESET (10054) saat client UDP menutup socket
                    try
                    {
                        const int SioUdpConnReset = -1744830452;
                        _udp.Client.IOControl((IOControlCode)SioUdpConnReset, new byte[] { 0, 0, 0, 0 }, null);
                    }
                    catch { }
                }

                Port = port;
                IsRunning = true;

                CancellationToken token = _cts.Token;
                if (_listener != null)
                {
                    _ = AcceptLoop(token);
                }
                if (_udp != null)
                {
                    _ = UdpLoop(token);
                }

                LogMessage?.Invoke($"[{Name}] Started listening on port {Port} (parsing: NNMitsubishiServer)");
                return Port;
            }
            catch (Exception ex)
            {
                Stop();
                LogMessage?.Invoke($"[{Name}] Failed to start on port {port}: {ex.Message}");
                return -1;
            }
        }

        public void Stop()
        {
            bool hadSocket = _listener != null || _udp != null;
            bool wasRunning = IsRunning;

            IsRunning = false;

            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            _listener = null;
            try { _udp?.Close(); } catch { }
            _udp = null;

            lock (_clients)
            {
                foreach (TcpClient client in _clients)
                {
                    try { client.Close(); } catch { }
                }
                _clients.Clear();
            }

            if (wasRunning && hadSocket)
            {
                LogMessage?.Invoke($"[{Name}] Server stopped");
            }
        }

        #region Transport (TCP + UDP) - Qna-3E Binary

        // Semua frame lengkap (header Qna-3E + isi) diserahkan ke parser library.
        private byte[]? Process(byte[] frame)
        {
            lock (_parseLock)
            {
                _parser.CpuModel = CpuModel;
                _parser.CpuTypeCode = CpuTypeCode;

                RequestsProcessed++;
                return _parser.ProcessMcFrame(frame);
            }
        }

        private async Task AcceptLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    TcpClient client = await _listener!.AcceptTcpClientAsync();

                    lock (_clients)
                    {
                        _clients.Add(client);
                    }

                    _ = Task.Run(() => HandleClient(client, token));
                }
                catch
                {
                    break;
                }
            }
        }

        private static async Task<bool> ReadExact(NetworkStream stream, byte[] buffer, int offset, int count, CancellationToken token)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = await stream.ReadAsync(buffer, offset + totalRead, count - totalRead, token);
                if (read <= 0)
                {
                    return false;
                }
                totalRead += read;
            }
            return true;
        }

        private async Task HandleClient(TcpClient client, CancellationToken token)
        {
            try
            {
                using (client)
                {
                    NetworkStream stream = client.GetStream();

                    while (!token.IsCancellationRequested)
                    {
                        // 1. Header MC Protocol Qna-3E: 9 byte (subheader + net/pc/io/station + data length)
                        byte[] header = new byte[Qna3EHeaderLength];
                        if (!await ReadExact(stream, header, 0, Qna3EHeaderLength, token))
                        {
                            break;
                        }

                        // Server ini melayani Qna-3E binary (subheader 0x50 0x00)
                        if (header[0] != 0x50 || header[1] != 0x00)
                        {
                            break;
                        }

                        int dataLength = BitConverter.ToUInt16(header, 7);
                        if (dataLength < 2)
                        {
                            break;
                        }

                        // 2. Sisa frame: 2 byte timer + isi command
                        byte[] frame = new byte[Qna3EHeaderLength + dataLength];
                        Buffer.BlockCopy(header, 0, frame, 0, Qna3EHeaderLength);
                        if (!await ReadExact(stream, frame, Qna3EHeaderLength, dataLength, token))
                        {
                            break;
                        }

                        byte[]? response = Process(frame);
                        if (response != null && response.Length > 0)
                        {
                            await stream.WriteAsync(response, 0, response.Length, token);
                            await stream.FlushAsync(token);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    LogMessage?.Invoke($"[{Name}] TCP client error: {ex.Message}");
                }
            }
            finally
            {
                lock (_clients)
                {
                    _clients.Remove(client);
                }
            }
        }

        private async Task UdpLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    UdpReceiveResult packet = await _udp!.ReceiveAsync();
                    byte[] frame = packet.Buffer;

                    // Datagram UDP = satu frame penuh
                    if (frame.Length < Qna3EHeaderLength + 2)
                    {
                        continue;
                    }
                    if (frame[0] != 0x50 || frame[1] != 0x00)
                    {
                        continue;
                    }
                    if (frame.Length != Qna3EHeaderLength + BitConverter.ToUInt16(frame, 7))
                    {
                        continue;
                    }

                    byte[]? response = Process(frame);
                    if (response != null && response.Length > 0)
                    {
                        await _udp.SendAsync(response, response.Length, packet.RemoteEndPoint);
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (token.IsCancellationRequested)
                    {
                        break;
                    }
                    LogMessage?.Invoke($"[{Name}] UDP error: {ex.Message}");
                }
            }
        }

        #endregion
    }
}
