#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace NetToGXSim3.Core
{
    /// <summary>
    /// Server Modbus TCP yang terhubung langsung ke GX Simulator 3 secara on-demand passthrough.
    /// Mendukung pembacaan dan penulisan memory PLC (X, Y, M, D) tanpa latensi polling cache.
    /// </summary>
    public class ModbusTcpServer : IDisposable
    {
        private readonly GxSimulatorEngine _engine;
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private readonly List<TcpClient> _activeClients = new List<TcpClient>();
        private readonly object _clientsLock = new object();
        private bool _disposed = false;

        public ModbusConfig Config { get; }
        public int ActivePort { get; private set; }
        public bool IsRunning { get; private set; } = false;

        private long _requestsProcessed = 0;
        public long RequestsProcessed
        {
            get
            {
                return Interlocked.Read(ref _requestsProcessed);
            }
        }
        public int ConnectedClientsCount
        {
            get
            {
                lock (_clientsLock)
                {
                    return _activeClients.Count;
                }
            }
        }

        public event Action<string>? LogMessage;
        public event Action<int, int>? PortCollisionDetected;
        public event Action<bool>? StateChanged;
        public event Action<string, int>? DeviceWritten;

        public ModbusTcpServer(GxSimulatorEngine engine, ModbusConfig? config = null)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            if (config != null)
            {
                Config = config;
            }
            else
            {
                Config = ModbusConfig.Load();
            }
            ActivePort = Config.Port;
        }

        /// <summary>
        /// Mengecek apakah port TCP tertentu tersedia untuk dibuka.
        /// </summary>
        public static bool IsPortAvailable(int port)
        {
            TcpListener? probe = null;
            try
            {
                probe = new TcpListener(IPAddress.Any, port);
                probe.Start();
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (probe != null)
                {
                    try
                    {
                        probe.Stop();
                    }
                    catch
                    {
                        // Pengabaian saat pembersihan probe
                    }
                }
            }
        }

        /// <summary>
        /// Memulai service Modbus TCP Server.
        /// </summary>
        public bool Start()
        {
            if (IsRunning)
            {
                return true;
            }

            int targetPort = Config.Port;
            if (targetPort <= 0 || targetPort > 65535)
            {
                targetPort = 5020;
            }

            // Penanganan bentrok port (Port Collision Resolution)
            int resolvedPort = targetPort;
            if (!IsPortAvailable(resolvedPort))
            {
                int probePort = targetPort + 1;
                while (probePort < targetPort + 100)
                {
                    if (IsPortAvailable(probePort))
                    {
                        resolvedPort = probePort;
                        break;
                    }
                    probePort++;
                }

                if (resolvedPort != targetPort)
                {
                    LogMessage?.Invoke($"[MODBUS] Port {targetPort} sedang digunakan oleh aplikasi lain. Server dialihkan ke port {resolvedPort}.");
                    PortCollisionDetected?.Invoke(targetPort, resolvedPort);
                }
            }

            ActivePort = resolvedPort;

            try
            {
                _cts = new CancellationTokenSource();
                _listener = new TcpListener(IPAddress.Any, ActivePort);
                _listener.Start();

                IsRunning = true;
                LogMessage?.Invoke($"[MODBUS] Modbus TCP Server aktif dan mendengarkan koneksi pada port {ActivePort} (Zero-Based: {Config.IsZeroBased})");
                StateChanged?.Invoke(true);

                Task.Run(() => AcceptClientsLoopAsync(_cts.Token));
                return true;
            }
            catch (Exception ex)
            {
                IsRunning = false;
                LogMessage?.Invoke($"[MODBUS ERROR] Gagal memulai Modbus TCP Server pada port {ActivePort}: {ex.Message}");
                StateChanged?.Invoke(false);
                return false;
            }
        }

        /// <summary>
        /// Menghentikan service Modbus TCP Server.
        /// </summary>
        public void Stop()
        {
            if (!IsRunning)
            {
                return;
            }

            IsRunning = false;

            if (_cts != null)
            {
                try
                {
                    _cts.Cancel();
                }
                catch
                {
                    // Abaikan kesalahan pembatalan
                }
            }

            if (_listener != null)
            {
                try
                {
                    _listener.Stop();
                }
                catch
                {
                    // Abaikan kesalahan penutupan listener
                }
                _listener = null;
            }

            lock (_clientsLock)
            {
                foreach (var client in _activeClients)
                {
                    try
                    {
                        client.Close();
                    }
                    catch
                    {
                        // Abaikan kesalahan penutupan client
                    }
                }
                _activeClients.Clear();
            }

            LogMessage?.Invoke("[MODBUS] Modbus TCP Server telah dihentikan.");
            StateChanged?.Invoke(false);
        }

        /// <summary>
        /// Loop asynchronous untuk menerima client TCP baru.
        /// </summary>
        private async Task AcceptClientsLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _listener != null)
            {
                try
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    lock (_clientsLock)
                    {
                        _activeClients.Add(client);
                    }

                    string remoteEndPoint = "Unknown";
                    if (client.Client != null && client.Client.RemoteEndPoint != null)
                    {
                        string? rep = client.Client.RemoteEndPoint.ToString();
                        if (rep != null)
                        {
                            remoteEndPoint = rep;
                        }
                    }

                    LogMessage?.Invoke($"[MODBUS] Client terhubung dari {remoteEndPoint}");

                    _ = Task.Run(() => HandleClientAsync(client, token), token);
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
                    LogMessage?.Invoke($"[MODBUS] Error penerimaan client: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Menangani komunikasi data Modbus dengan satu client TCP.
        /// </summary>
        private async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            string remoteEndPoint = "Unknown";
            if (client.Client != null && client.Client.RemoteEndPoint != null)
            {
                string? rep = client.Client.RemoteEndPoint.ToString();
                if (rep != null)
                {
                    remoteEndPoint = rep;
                }
            }

            using (client)
            using (NetworkStream stream = client.GetStream())
            {
                byte[] headerBuffer = new byte[7]; // MBAP Header ukuran 7 byte

                while (!token.IsCancellationRequested && client.Connected)
                {
                    try
                    {
                        // 1. Baca 7-byte MBAP Header:
                        // Transaction ID (2 byte), Protocol ID (2 byte), Length (2 byte), Unit ID (1 byte)
                        int bytesRead = await ReadExactBytesAsync(stream, headerBuffer, 7, token).ConfigureAwait(false);
                        if (bytesRead < 7)
                        {
                            break;
                        }

                        ushort transactionId = (ushort)((headerBuffer[0] << 8) | headerBuffer[1]);
                        ushort protocolId = (ushort)((headerBuffer[2] << 8) | headerBuffer[3]);
                        ushort length = (ushort)((headerBuffer[4] << 8) | headerBuffer[5]);
                        byte unitId = headerBuffer[6];

                        // Validasi protokol Modbus (Protocol ID wajib 0)
                        if (protocolId != 0 || length < 2)
                        {
                            LogMessage?.Invoke($"[MODBUS WARN] Frame Modbus tidak valid dari {remoteEndPoint} (Protocol: {protocolId}, Length: {length})");
                            break;
                        }

                        // 2. Baca sisa paket PDU (Function Code dan payload data)
                        int pduLength = length - 1; // Dikurangi 1 byte untuk Unit ID yang sudah terbaca
                        byte[] pduBuffer = new byte[pduLength];
                        int pduBytesRead = await ReadExactBytesAsync(stream, pduBuffer, pduLength, token).ConfigureAwait(false);
                        if (pduBytesRead < pduLength)
                        {
                            break;
                        }

                        // 3. Proses request Modbus PDU dan dapatkan respon PDU
                        byte[] responsePdu = ProcessModbusPdu(pduBuffer);

                        Interlocked.Increment(ref _requestsProcessed);

                        // 4. Susun paket respon MBAP Header + responsePdu
                        ushort responseLength = (ushort)(1 + responsePdu.Length); // 1 byte Unit ID + panjang response PDU
                        byte[] responsePacket = new byte[7 + responsePdu.Length];

                        // Salin MBAP Header
                        responsePacket[0] = (byte)(transactionId >> 8);
                        responsePacket[1] = (byte)(transactionId & 0xFF);
                        responsePacket[2] = 0; // Protocol ID
                        responsePacket[3] = 0;
                        responsePacket[4] = (byte)(responseLength >> 8);
                        responsePacket[5] = (byte)(responseLength & 0xFF);
                        responsePacket[6] = unitId;

                        // Salin payload respon
                        Buffer.BlockCopy(responsePdu, 0, responsePacket, 7, responsePdu.Length);

                        // Kirim balasan ke client
                        await stream.WriteAsync(responsePacket, 0, responsePacket.Length, token).ConfigureAwait(false);
                        await stream.FlushAsync(token).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        break;
                    }
                }
            }

            lock (_clientsLock)
            {
                _activeClients.Remove(client);
            }
            LogMessage?.Invoke($"[MODBUS] Client terputus: {remoteEndPoint}");
        }

        /// <summary>
        /// Membaca byte secara presisi sejumlah count dari NetworkStream.
        /// </summary>
        private static async Task<int> ReadExactBytesAsync(NetworkStream stream, byte[] buffer, int count, CancellationToken token)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = await stream.ReadAsync(buffer, totalRead, count - totalRead, token).ConfigureAwait(false);
                if (read <= 0)
                {
                    return totalRead;
                }
                totalRead += read;
            }
            return totalRead;
        }

        /// <summary>
        /// Memproses Modbus PDU dan menghasilkan respon sesuai spesifikasi pemetaan PLC GX Simulator 3.
        /// </summary>
        public byte[] ProcessModbusPdu(byte[] pdu)
        {
            if (pdu == null || pdu.Length == 0)
            {
                return CreateExceptionResponse(0x00, 0x01); // Illegal Function
            }

            byte functionCode = pdu[0];

            switch (functionCode)
            {
                case 0x01: // Read Coils (Y0-Y1999 & M0-M7679)
                    return HandleReadCoils(pdu);

                case 0x02: // Read Discrete Inputs (X0-X2999, Read Only)
                    return HandleReadDiscreteInputs(pdu);

                case 0x03: // Read Holding Registers (D0-D5999, Read/Write)
                    return HandleReadHoldingRegisters(pdu);

                case 0x04: // Read Input Registers (D0-D5999, Read Only mirror)
                    return HandleReadInputRegisters(pdu);

                case 0x05: // Write Single Coil (Y & M)
                    return HandleWriteSingleCoil(pdu);

                case 0x06: // Write Single Register (D)
                    return HandleWriteSingleRegister(pdu);

                case 0x0F: // Write Multiple Coils (FC 15)
                    return HandleWriteMultipleCoils(pdu);

                case 0x10: // Write Multiple Registers (FC 16)
                    return HandleWriteMultipleRegisters(pdu);

                default:
                    // Function code tidak didukung
                    return CreateExceptionResponse(functionCode, 0x01); // 0x01: Illegal Function
            }
        }

        /// <summary>
        /// Mengkonversi alamat Modbus dari frame wire menjadi index offset berbasis zero-based setting.
        /// </summary>
        public int CalculateOffset(ushort wireAddress)
        {
            // Jika mode Zero-Based aktif, alamat 0 menunjuk langsung ke index 0
            if (Config.IsZeroBased)
            {
                return wireAddress;
            }
            else
            {
                // Jika mode 1-based aktif, alamat 1 menunjuk ke index 0
                if (wireAddress >= 1)
                {
                    return wireAddress - 1;
                }
                else
                {
                    return wireAddress;
                }
            }
        }

        #region Function Handlers

        /// <summary>
        /// FC 01: Read Coils.
        /// Pemetaan:
        /// - 0 s/d 1999 : Y0 s/d Y1999 (Format Octal di PLC: Y0 s/d Y3717)
        /// - 2000 s/d 9679 : M0 s/d M7679 (Format Desimal di PLC: M0 s/d M7679)
        /// </summary>
        private byte[] HandleReadCoils(byte[] pdu)
        {
            if (pdu.Length < 5)
            {
                return CreateExceptionResponse(0x01, 0x03); // Illegal Data Value
            }

            ushort startAddress = (ushort)((pdu[1] << 8) | pdu[2]);
            ushort quantity = (ushort)((pdu[3] << 8) | pdu[4]);

            if (quantity < 1 || quantity > 2000)
            {
                return CreateExceptionResponse(0x01, 0x03); // Quantity out of range
            }

            int startOffset = CalculateOffset(startAddress);
            int endOffset = startOffset + quantity - 1;

            // Validasi batasan alamat total Coil (Y + M)
            // 2000 titik Y + 7680 titik M = total 9680 titik (offset 0 s/d 9679)
            if (startOffset < 0 || endOffset >= 9680)
            {
                return CreateExceptionResponse(0x01, 0x02); // Illegal Data Address
            }

            int byteCount = (quantity + 7) / 8;
            byte[] response = new byte[2 + byteCount];
            response[0] = 0x01; // Function Code
            response[1] = (byte)byteCount;

            for (int i = 0; i < quantity; i++)
            {
                int currentOffset = startOffset + i;
                bool bitState = false;

                if (currentOffset < 2000)
                {
                    // Area Coil Y (Output Relay)
                    string octalAddress = Convert.ToString(currentOffset, 8);
                    string deviceName = "Y" + octalAddress;
                    int readResult = _engine.ReadDevice(deviceName, out int val);
                    if (readResult == 0 && val != 0)
                    {
                        bitState = true;
                    }
                }
                else
                {
                    // Area Coil M (Internal Relay)
                    int mIndex = currentOffset - 2000;
                    string deviceName = "M" + mIndex;
                    int readResult = _engine.ReadDevice(deviceName, out int val);
                    if (readResult == 0 && val != 0)
                    {
                        bitState = true;
                    }
                }

                if (bitState)
                {
                    int byteIndex = 2 + (i / 8);
                    int bitIndex = i % 8;
                    response[byteIndex] |= (byte)(1 << bitIndex);
                }
            }

            return response;
        }

        /// <summary>
        /// FC 02: Read Discrete Inputs (READ ONLY).
        /// Pemetaan:
        /// - 0 s/d 2999 : X0 s/d X2999 (Format Octal di PLC: X0 s/d X5667)
        /// </summary>
        private byte[] HandleReadDiscreteInputs(byte[] pdu)
        {
            if (pdu.Length < 5)
            {
                return CreateExceptionResponse(0x02, 0x03);
            }

            ushort startAddress = (ushort)((pdu[1] << 8) | pdu[2]);
            ushort quantity = (ushort)((pdu[3] << 8) | pdu[4]);

            if (quantity < 1 || quantity > 2000)
            {
                return CreateExceptionResponse(0x02, 0x03);
            }

            int startOffset = CalculateOffset(startAddress);
            int endOffset = startOffset + quantity - 1;

            // X sebanyak 3000 input (offset 0 s/d 2999)
            if (startOffset < 0 || endOffset >= 3000)
            {
                return CreateExceptionResponse(0x02, 0x02); // Illegal Data Address
            }

            int byteCount = (quantity + 7) / 8;
            byte[] response = new byte[2 + byteCount];
            response[0] = 0x02; // Function Code
            response[1] = (byte)byteCount;

            for (int i = 0; i < quantity; i++)
            {
                int currentOffset = startOffset + i;
                string octalAddress = Convert.ToString(currentOffset, 8);
                string deviceName = "X" + octalAddress;

                int readResult = _engine.ReadDevice(deviceName, out int val);
                if (readResult == 0 && val != 0)
                {
                    int byteIndex = 2 + (i / 8);
                    int bitIndex = i % 8;
                    response[byteIndex] |= (byte)(1 << bitIndex);
                }
            }

            return response;
        }

        /// <summary>
        /// FC 03: Read Holding Registers (READ/WRITE).
        /// Pemetaan:
        /// - 0 s/d 5999 : D0 s/d D5999 (6000 register)
        /// </summary>
        private byte[] HandleReadHoldingRegisters(byte[] pdu)
        {
            if (pdu.Length < 5)
            {
                return CreateExceptionResponse(0x03, 0x03);
            }

            ushort startAddress = (ushort)((pdu[1] << 8) | pdu[2]);
            ushort quantity = (ushort)((pdu[3] << 8) | pdu[4]);

            if (quantity < 1 || quantity > 125)
            {
                return CreateExceptionResponse(0x03, 0x03);
            }

            int startOffset = CalculateOffset(startAddress);
            int endOffset = startOffset + quantity - 1;

            // D sebanyak 6000 register (offset 0 s/d 5999)
            if (startOffset < 0 || endOffset >= 6000)
            {
                return CreateExceptionResponse(0x03, 0x02); // Illegal Data Address
            }

            int byteCount = quantity * 2;
            byte[] response = new byte[2 + byteCount];
            response[0] = 0x03; // Function Code
            response[1] = (byte)byteCount;

            for (int i = 0; i < quantity; i++)
            {
                int currentOffset = startOffset + i;
                string deviceName = "D" + currentOffset;

                int readResult = _engine.ReadDevice(deviceName, out int val);
                short regValue = 0;
                if (readResult == 0)
                {
                    regValue = (short)val;
                }

                int byteIndex = 2 + (i * 2);
                response[byteIndex] = (byte)((regValue >> 8) & 0xFF);     // High Byte
                response[byteIndex + 1] = (byte)(regValue & 0xFF);        // Low Byte
            }

            return response;
        }

        /// <summary>
        /// FC 04: Read Input Registers (READ ONLY).
        /// Pemetaan mirror ke D0 s/d D5999.
        /// </summary>
        private byte[] HandleReadInputRegisters(byte[] pdu)
        {
            if (pdu.Length < 5)
            {
                return CreateExceptionResponse(0x04, 0x03);
            }

            ushort startAddress = (ushort)((pdu[1] << 8) | pdu[2]);
            ushort quantity = (ushort)((pdu[3] << 8) | pdu[4]);

            if (quantity < 1 || quantity > 125)
            {
                return CreateExceptionResponse(0x04, 0x03);
            }

            int startOffset = CalculateOffset(startAddress);
            int endOffset = startOffset + quantity - 1;

            // D sebanyak 6000 register (offset 0 s/d 5999)
            if (startOffset < 0 || endOffset >= 6000)
            {
                return CreateExceptionResponse(0x04, 0x02);
            }

            int byteCount = quantity * 2;
            byte[] response = new byte[2 + byteCount];
            response[0] = 0x04; // Function Code
            response[1] = (byte)byteCount;

            for (int i = 0; i < quantity; i++)
            {
                int currentOffset = startOffset + i;
                string deviceName = "D" + currentOffset;

                int readResult = _engine.ReadDevice(deviceName, out int val);
                short regValue = 0;
                if (readResult == 0)
                {
                    regValue = (short)val;
                }

                int byteIndex = 2 + (i * 2);
                response[byteIndex] = (byte)((regValue >> 8) & 0xFF);
                response[byteIndex + 1] = (byte)(regValue & 0xFF);
            }

            return response;
        }

        /// <summary>
        /// FC 05: Write Single Coil (Y & M, READ/WRITE).
        /// Output value: 0xFF00 untuk ON, 0x0000 untuk OFF.
        /// </summary>
        private byte[] HandleWriteSingleCoil(byte[] pdu)
        {
            if (pdu.Length < 5)
            {
                return CreateExceptionResponse(0x05, 0x03);
            }

            ushort address = (ushort)((pdu[1] << 8) | pdu[2]);
            ushort valueRaw = (ushort)((pdu[3] << 8) | pdu[4]);

            int bitVal = 0;
            if (valueRaw == 0xFF00)
            {
                bitVal = 1;
            }
            else if (valueRaw == 0x0000)
            {
                bitVal = 0;
            }
            else
            {
                return CreateExceptionResponse(0x05, 0x03); // Nilai data coil tidak valid
            }

            int offset = CalculateOffset(address);

            // Validasi batasan alamat total Coil (Y + M)
            if (offset < 0 || offset >= 9680)
            {
                return CreateExceptionResponse(0x05, 0x02); // Illegal Data Address
            }

            if (offset < 2000)
            {
                // Tulis ke Output Relay Y
                string octalAddress = Convert.ToString(offset, 8);
                string deviceName = "Y" + octalAddress;
                _engine.WriteDevice(deviceName, bitVal);
                DeviceWritten?.Invoke(deviceName, bitVal);
            }
            else
            {
                // Tulis ke Internal Relay M
                int mIndex = offset - 2000;
                string deviceName = "M" + mIndex;
                _engine.WriteDevice(deviceName, bitVal);
                DeviceWritten?.Invoke(deviceName, bitVal);
            }

            // Standar respon FC 05 adalah echo dari request 5-byte
            byte[] response = new byte[5];
            Buffer.BlockCopy(pdu, 0, response, 0, 5);
            return response;
        }

        /// <summary>
        /// FC 06: Write Single Register (D, READ/WRITE).
        /// </summary>
        private byte[] HandleWriteSingleRegister(byte[] pdu)
        {
            if (pdu.Length < 5)
            {
                return CreateExceptionResponse(0x06, 0x03);
            }

            ushort address = (ushort)((pdu[1] << 8) | pdu[2]);
            ushort regValue = (ushort)((pdu[3] << 8) | pdu[4]);

            int offset = CalculateOffset(address);

            if (offset < 0 || offset >= 6000)
            {
                return CreateExceptionResponse(0x06, 0x02); // Illegal Data Address
            }

            string deviceName = "D" + offset;
            _engine.WriteDevice(deviceName, (short)regValue);
            DeviceWritten?.Invoke(deviceName, regValue);

            // Standar respon FC 06 adalah echo dari request 5-byte
            byte[] response = new byte[5];
            Buffer.BlockCopy(pdu, 0, response, 0, 5);
            return response;
        }

        /// <summary>
        /// FC 15 (0x0F): Write Multiple Coils (Y & M, READ/WRITE).
        /// </summary>
        private byte[] HandleWriteMultipleCoils(byte[] pdu)
        {
            if (pdu.Length < 6)
            {
                return CreateExceptionResponse(0x0F, 0x03);
            }

            ushort startAddress = (ushort)((pdu[1] << 8) | pdu[2]);
            ushort quantity = (ushort)((pdu[3] << 8) | pdu[4]);
            byte byteCount = pdu[5];

            int expectedBytes = (quantity + 7) / 8;
            if (byteCount != expectedBytes || pdu.Length < 6 + byteCount)
            {
                return CreateExceptionResponse(0x0F, 0x03);
            }

            int startOffset = CalculateOffset(startAddress);
            int endOffset = startOffset + quantity - 1;

            if (startOffset < 0 || endOffset >= 9680)
            {
                return CreateExceptionResponse(0x0F, 0x02);
            }

            for (int i = 0; i < quantity; i++)
            {
                int currentOffset = startOffset + i;
                int byteIdx = 6 + (i / 8);
                int bitIdx = i % 8;
                int bitVal = (pdu[byteIdx] >> bitIdx) & 0x01;

                if (currentOffset < 2000)
                {
                    string octalAddress = Convert.ToString(currentOffset, 8);
                    string deviceName = "Y" + octalAddress;
                    _engine.WriteDevice(deviceName, bitVal);
                    DeviceWritten?.Invoke(deviceName, bitVal);
                }
                else
                {
                    int mIndex = currentOffset - 2000;
                    string deviceName = "M" + mIndex;
                    _engine.WriteDevice(deviceName, bitVal);
                    DeviceWritten?.Invoke(deviceName, bitVal);
                }
            }

            // Respon standar FC 15: FC (1 byte) + Starting Address (2 byte) + Quantity (2 byte)
            byte[] response = new byte[5];
            response[0] = 0x0F;
            response[1] = pdu[1];
            response[2] = pdu[2];
            response[3] = pdu[3];
            response[4] = pdu[4];
            return response;
        }

        /// <summary>
        /// FC 16 (0x10): Write Multiple Registers (D, READ/WRITE).
        /// </summary>
        private byte[] HandleWriteMultipleRegisters(byte[] pdu)
        {
            if (pdu.Length < 6)
            {
                return CreateExceptionResponse(0x10, 0x03);
            }

            ushort startAddress = (ushort)((pdu[1] << 8) | pdu[2]);
            ushort quantity = (ushort)((pdu[3] << 8) | pdu[4]);
            byte byteCount = pdu[5];

            int expectedBytes = quantity * 2;
            if (byteCount != expectedBytes || pdu.Length < 6 + byteCount)
            {
                return CreateExceptionResponse(0x10, 0x03);
            }

            int startOffset = CalculateOffset(startAddress);
            int endOffset = startOffset + quantity - 1;

            if (startOffset < 0 || endOffset >= 6000)
            {
                return CreateExceptionResponse(0x10, 0x02);
            }

            for (int i = 0; i < quantity; i++)
            {
                int currentOffset = startOffset + i;
                int byteIdx = 6 + (i * 2);
                short regValue = (short)((pdu[byteIdx] << 8) | pdu[byteIdx + 1]);

                string deviceName = "D" + currentOffset;
                _engine.WriteDevice(deviceName, regValue);
                DeviceWritten?.Invoke(deviceName, regValue);
            }

            // Respon standar FC 16: FC (1 byte) + Starting Address (2 byte) + Quantity (2 byte)
            byte[] response = new byte[5];
            response[0] = 0x10;
            response[1] = pdu[1];
            response[2] = pdu[2];
            response[3] = pdu[3];
            response[4] = pdu[4];
            return response;
        }

        /// <summary>
        /// Membuat paket respon exception Modbus standar.
        /// </summary>
        private static byte[] CreateExceptionResponse(byte functionCode, byte exceptionCode)
        {
            return new byte[]
            {
                (byte)(functionCode | 0x80), // Error flag (MSB bit 7 diset)
                exceptionCode
            };
        }

        #endregion

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            Stop();
        }
    }
}
