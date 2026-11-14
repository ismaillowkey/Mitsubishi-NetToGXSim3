using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NNMitsubishiServer
{
    /// <summary>
    /// Virtual Server MC Protocol Mitsubishi MELSEC iQ-F (FX5U) dan Q-Series.
    /// Dibuat 100% Native C# murni tanpa ketergantungan library pihak ketiga.
    /// Mengimplementasikan protokol standar Mitsubishi MC Protocol Qna-3E Frame (Binary / ASCII).
    /// </summary>
    public class Fx5uServer : IDisposable
    {
        #region Device Buffers (FX5U / Q-Series Memory Pools)

        // Buffer untuk register Bit (1 byte per bit address untuk akses O(1) ultra cepat)
        private MemoryBuffer _mBuffer;  // M: Internal Relay
        private MemoryBuffer _xBuffer;  // X: Input Relay
        private MemoryBuffer _yBuffer;  // Y: Output Relay
        private MemoryBuffer _lBuffer;  // L: Latch Relay
        private MemoryBuffer _bBuffer;  // B: Link Relay
        private MemoryBuffer _sBuffer;  // S: Step Relay
        private MemoryBuffer _fBuffer;  // F: Annunciator

        // Buffer untuk register Word (2 bytes per word address, Little Endian)
        private MemoryBuffer _dBuffer;   // D: Data Register (262,144 bytes = 131,072 words)
        private MemoryBuffer _wBuffer;   // W: Link Register (131,072 bytes = 65,536 words)
        private MemoryBuffer _rBuffer;   // R: File Register (131,072 bytes = 65,536 words)
        private MemoryBuffer _zrBuffer;  // ZR: Extended File Register (262,144 bytes = 131,072 words)

        #endregion

        #region Network Listener Fields

        private TcpListener _tcpListener;
        private UdpClient _udpClient;
        private CancellationTokenSource _cts;

        private bool _isRunning;
        private int _port;
        private bool _isBinary;
        private bool _isTcp;
        private bool _isDisposed;

        #endregion

        #region Public Properties

        /// <summary>
        /// Menunjukkan apakah server sedang aktif berjalan.
        /// </summary>
        public bool IsRunning
        {
            get
            {
                return _isRunning;
            }
        }

        /// <summary>
        /// Port koneksi server.
        /// </summary>
        public int Port
        {
            get
            {
                return _port;
            }
        }

        /// <summary>
        /// Format komunikasi frame:
        /// True = Binary (standar FX5U Ethernet).
        /// False = ASCII.
        /// </summary>
        public bool IsBinary
        {
            get
            {
                return _isBinary;
            }
        }

        /// <summary>
        /// Mengatur atau memeriksa apakah operasi Write diizinkan oleh server.
        /// </summary>
        public bool EnableWrite { get; set; } = true;

        #endregion

        #region Events

        /// <summary>
        /// Dipicu setiap kali server menerima paket frame data dari client / PLC.
        /// Parameter: (sender, byte[] data)
        /// </summary>
        public event Action<object, byte[]> OnDataReceived;

        /// <summary>
        /// Dipicu saat server menghasilkan pesan log status atau aktivitas.
        /// </summary>
        public event Action<string> OnLogMessage;

        #endregion

        #region Constructors

        /// <summary>
        /// Inisialisasi virtual server MC Protocol FX5U baru.
        /// </summary>
        /// <param name="isBinary">True untuk Binary Qna-3E frame (default), False untuk ASCII frame.</param>
        public Fx5uServer(bool isBinary = true)
        {
            _isBinary = isBinary;
            _port = 6000;
            _isRunning = false;

            // Alokasikan memori register virtual PLC
            InitializeBuffers();
        }

        /// <summary>
        /// Inisialisasi virtual server MC Protocol FX5U dengan port tertentu.
        /// </summary>
        /// <param name="port">Port server (default FX5U: 5000 atau 6000).</param>
        /// <param name="isBinary">True untuk Binary Qna-3E frame (default), False untuk ASCII frame.</param>
        public Fx5uServer(int port, bool isBinary = true) : this(isBinary)
        {
            _port = port;
        }

        // Inisialisasi kapasitas pool memori register
        private void InitializeBuffers()
        {
            _mBuffer = new MemoryBuffer(65536);
            _xBuffer = new MemoryBuffer(65536);
            _yBuffer = new MemoryBuffer(65536);
            _lBuffer = new MemoryBuffer(65536);
            _bBuffer = new MemoryBuffer(65536);
            _sBuffer = new MemoryBuffer(65536);
            _fBuffer = new MemoryBuffer(65536);

            _dBuffer = new MemoryBuffer(262144);
            _wBuffer = new MemoryBuffer(131072);
            _rBuffer = new MemoryBuffer(131072);
            _zrBuffer = new MemoryBuffer(262144);
        }

        #endregion

        #region Server Lifecycle Methods

        /// <summary>
        /// Menjalankan server pada port yang telah ditentukan.
        /// </summary>
        public void Start(bool isTcp = true)
        {
            Start(_port, isTcp);
        }

        /// <summary>
        /// Menjalankan server pada port tertentu.
        /// </summary>
        /// <param name="port">Nomor port TCP / UDP</param>
        /// <param name="isTcp">True jika TCP (default), False jika UDP</param>
        public void Start(int port, bool isTcp = true)
        {
            if (_isRunning)
            {
                Log("Server sudah dalam kondisi aktif.");
                return;
            }

            _port = port;
            _isTcp = isTcp;
            _cts = new CancellationTokenSource();

            if (isTcp)
            {
                try
                {
                    _tcpListener = new TcpListener(IPAddress.Any, port);
                    _tcpListener.Start();
                    _isRunning = true;

                    // Jalankan worker thread penerima koneksi TCP client
                    Task.Run(() => AcceptTcpClientsLoop(_cts.Token), _cts.Token);

                    string formatText;
                    if (_isBinary)
                    {
                        formatText = "Binary (Qna-3E)";
                    }
                    else
                    {
                        formatText = "ASCII";
                    }

                    Log(string.Format("FX5U MC Virtual Server aktif di port {0} (TCP, {1}).", _port, formatText));
                }
                catch (Exception ex)
                {
                    _isRunning = false;
                    Log(string.Format("Gagal menjalankan TCP Server pada port {0}: {1}", port, ex.Message));
                    throw;
                }
            }
            else
            {
                try
                {
                    _udpClient = new UdpClient(port);
                    _isRunning = true;

                    // Jalankan worker loop penerima paket UDP
                    Task.Run(() => AcceptUdpLoop(_cts.Token), _cts.Token);

                    Log(string.Format("FX5U MC Virtual Server aktif di port {0} (UDP).", _port));
                }
                catch (Exception ex)
                {
                    _isRunning = false;
                    Log(string.Format("Gagal menjalankan UDP Server pada port {0}: {1}", port, ex.Message));
                    throw;
                }
            }
        }

        /// <summary>
        /// Menghentikan dan menutup listening server.
        /// </summary>
        public void Stop()
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;

            if (_cts != null)
            {
                _cts.Cancel();
            }

            if (_tcpListener != null)
            {
                try
                {
                    _tcpListener.Stop();
                }
                catch
                {
                }
                _tcpListener = null;
            }

            if (_udpClient != null)
            {
                try
                {
                    _udpClient.Close();
                }
                catch
                {
                }
                _udpClient = null;
            }

            Log("FX5U MC Virtual Server telah dinonaktifkan.");
        }

        #endregion

        #region TCP & UDP Request Handling Loops

        // Loop penerima koneksi client TCP
        private async Task AcceptTcpClientsLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _isRunning)
            {
                try
                {
                    TcpClient client = await _tcpListener.AcceptTcpClientAsync();

                    // Tangani komunikasi tiap client di task terpisah
                    _ = Task.Run(() => HandleTcpClientAsync(client, token), token);
                }
                catch
                {
                    if (token.IsCancellationRequested || !_isRunning)
                    {
                        break;
                    }
                }
            }
        }

        // Handler komunikasi untuk 1 client TCP
        private async Task HandleTcpClientAsync(TcpClient client, CancellationToken token)
        {
            using (client)
            {
                NetworkStream stream = client.GetStream();
                byte[] readBuffer = new byte[8192];

                while (!token.IsCancellationRequested && _isRunning && client.Connected)
                {
                    try
                    {
                        int bytesRead = await stream.ReadAsync(readBuffer, 0, readBuffer.Length, token);

                        if (bytesRead <= 0)
                        {
                            break; // Client menutup koneksi
                        }

                        byte[] receivedData = new byte[bytesRead];
                        Array.Copy(readBuffer, 0, receivedData, 0, bytesRead);

                        // Pemicu event OnDataReceived
                        TriggerDataReceived(client, receivedData);

                        // Proses permintaan frame MC Protocol
                        byte[] response = ProcessMcFrame(receivedData);

                        if (response != null && response.Length > 0)
                        {
                            await stream.WriteAsync(response, 0, response.Length, token);
                            await stream.FlushAsync(token);
                        }
                    }
                    catch
                    {
                        break;
                    }
                }
            }
        }

        // Loop penerima paket UDP
        private async Task AcceptUdpLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _isRunning && _udpClient != null)
            {
                try
                {
                    UdpReceiveResult result = await _udpClient.ReceiveAsync();
                    byte[] receivedData = result.Buffer;

                    TriggerDataReceived(result.RemoteEndPoint, receivedData);

                    byte[] response = ProcessMcFrame(receivedData);

                    if (response != null && response.Length > 0)
                    {
                        await _udpClient.SendAsync(response, response.Length, result.RemoteEndPoint);
                    }
                }
                catch
                {
                    if (token.IsCancellationRequested || !_isRunning)
                    {
                        break;
                    }
                }
            }
        }

        #endregion

        #region MC Protocol Qna-3E Core Processing Engine

        /// <summary>
        /// Memproses paket frame utuh MC Protocol (Binary atau ASCII) dan mengembalikan frame respon.
        /// </summary>
        public virtual byte[] ProcessMcFrame(byte[] receive)
        {
            if (receive == null || receive.Length < 11)
            {
                return null;
            }

            if (_isBinary)
            {
                // Format Binary Qna-3E:
                // Header berjumlah 11 bytes:
                // 2 bytes: Subheader (0x50, 0x00)
                // 1 byte : Network No
                // 1 byte : PLC No
                // 2 bytes: IO No
                // 1 byte : Station No
                // 2 bytes: Data Length
                // 2 bytes: CPU Monitoring Timer
                // Mulai index ke-11 adalah MC Core Command

                if (receive.Length < 11)
                {
                    return null;
                }

                byte[] mcCore = new byte[receive.Length - 11];
                Array.Copy(receive, 11, mcCore, 0, mcCore.Length);

                return ReadFromMcCore(mcCore);
            }
            else
            {
                // Format ASCII Qna-3E:
                // Header berjumlah 22 karakter ASCII
                if (receive.Length < 22)
                {
                    return null;
                }

                byte[] mcCoreAscii = new byte[receive.Length - 22];
                Array.Copy(receive, 22, mcCoreAscii, 0, mcCoreAscii.Length);

                return ReadFromMcAsciiCore(mcCoreAscii);
            }
        }

        /// <summary>
        /// Memproses data inti perintah biner MC Protocol.
        /// </summary>
        protected virtual byte[] ReadFromMcCore(byte[] mcCore)
        {
            if (mcCore == null || mcCore.Length < 4)
            {
                return PackCommand(49242, null);
            }

            byte commandLow = mcCore[0];
            byte commandHigh = mcCore[1];

            // 01 01: Read PLC Model / CPU Type
            if (commandLow == 1 && commandHigh == 1)
            {
                return PackCommand(0, Encoding.ASCII.GetBytes("FX5UCPU         \u0005A"));
            }

            // 01 04: Batch Read (Baca Data)
            if (commandLow == 1 && commandHigh == 4)
            {
                return ReadByCommand(mcCore);
            }

            // 01 14 (14 01 Little Endian): Batch Write (Tulis Data)
            if (commandLow == 1 && commandHigh == 20)
            {
                if (!EnableWrite)
                {
                    return PackCommand(49250, null); // Error: Write disabled
                }

                byte[] writeResult = WriteByMessage(mcCore);
                return PackCommand(0, writeResult);
            }

            // 03 04: Random Read
            if (commandLow == 3 && commandHigh == 4)
            {
                return ReadRandomByCommand(mcCore);
            }

            // 06 04: Block Read
            if (commandLow == 6 && commandHigh == 4)
            {
                return ReadBlockByCommand(mcCore);
            }

            // Perintah remote run/stop atau acknowledgement lainnya
            if (commandLow == 1 && commandHigh == 16)
            {
                return PackCommand(0, null);
            }

            if (commandLow == 2 && commandHigh == 16)
            {
                return PackCommand(0, null);
            }

            if (commandLow == 6 && commandHigh == 16)
            {
                return PackCommand(0, null);
            }

            // Kode perintah tidak dikenali
            return PackCommand(49242, null);
        }

        // Memproses perintah Batch Read (Command 0401)
        private byte[] ReadByCommand(byte[] command)
        {
            if (command.Length < 10)
            {
                return PackCommand(49242, null);
            }

            byte subcommand = command[2]; // 0: Word, 1: Bit
            int addressStart = command[4] + (command[5] * 256) + (command[6] * 65536);
            byte dataCode = command[7];
            ushort length = BitConverter.ToUInt16(command, 8);

            // Operasi Pembacaan Bit
            if (subcommand == 1)
            {
                if (length > 7168)
                {
                    return PackCommand(49233, null); // Error: Jumlah melebihi batas
                }

                MemoryBuffer bitBuffer = GetBoolBufferByDataCode(dataCode);
                if (bitBuffer == null)
                {
                    return PackCommand(49242, null); // Error: Tipe register tidak didukung
                }

                // Ambil byte status bit
                byte[] rawBitBytes = bitBuffer.GetBytes(addressStart, length);
                bool[] boolValues = new bool[rawBitBytes.Length];
                for (int i = 0; i < rawBitBytes.Length; i++)
                {
                    boolValues[i] = rawBitBytes[i] != 0;
                }

                // Kompresi bit menjadi format MC Protocol (2 bit per byte)
                byte[] packedData = McProtocolHelper.TransBoolArrayToByteData(boolValues);
                return PackCommand(0, packedData);
            }

            // Operasi Pembacaan Word
            if (length > 960)
            {
                return PackCommand(49233, null);
            }

            // Cek apakah dataCode merujuk ke Bit Buffer (M, X, Y dsb dibaca sebagai Word)
            MemoryBuffer bitBufAsWord = GetBoolBufferByDataCode(dataCode);
            if (bitBufAsWord != null)
            {
                int totalBits = length * 16;
                byte[] rawBitBytes = bitBufAsWord.GetBytes(addressStart, totalBits);
                bool[] boolValues = new bool[rawBitBytes.Length];
                for (int i = 0; i < rawBitBytes.Length; i++)
                {
                    boolValues[i] = rawBitBytes[i] != 0;
                }

                byte[] wordBytes = McProtocolHelper.BoolArrayToByteArray(boolValues);
                return PackCommand(0, wordBytes);
            }

            // Pembacaan dari Word Buffer (D, W, R, ZR)
            MemoryBuffer wordBuf = GetWordBufferByDataCode(dataCode);
            if (wordBuf != null)
            {
                byte[] wordData = wordBuf.GetBytes(addressStart * 2, length * 2);
                return PackCommand(0, wordData);
            }

            return PackCommand(49242, null);
        }

        // Memproses perintah Batch Write (Command 1401)
        private byte[] WriteByMessage(byte[] command)
        {
            if (command.Length < 10)
            {
                return null;
            }

            byte subcommand = command[2]; // 0: Word, 1: Bit
            int addressStart = command[4] + (command[5] * 256) + (command[6] * 65536);
            byte dataCode = command[7];
            ushort count = BitConverter.ToUInt16(command, 8);

            int dataOffset = 10;
            int dataLength = command.Length - dataOffset;

            byte[] payload = new byte[dataLength];
            Array.Copy(command, dataOffset, payload, 0, dataLength);

            // Tulis Bit
            if (subcommand == 1)
            {
                bool[] bitValues = McProtocolHelper.ExtractActualBitData(payload);
                int writeCount = count;
                if (writeCount > bitValues.Length)
                {
                    writeCount = bitValues.Length;
                }

                byte[] bytesToWrite = new byte[writeCount];
                for (int i = 0; i < writeCount; i++)
                {
                    if (bitValues[i])
                    {
                        bytesToWrite[i] = 1;
                    }
                    else
                    {
                        bytesToWrite[i] = 0;
                    }
                }

                MemoryBuffer bitBuf = GetBoolBufferByDataCode(dataCode);
                if (bitBuf != null)
                {
                    bitBuf.SetBytes(bytesToWrite, addressStart);
                }

                return new byte[0];
            }

            // Tulis Word ke Bit Device (misal M0 ditulis 1 word = 16 bit)
            MemoryBuffer bitBufTarget = GetBoolBufferByDataCode(dataCode);
            if (bitBufTarget != null)
            {
                bool[] bitValues = McProtocolHelper.ByteArrayToBoolArray(payload);
                byte[] bytesToWrite = new byte[bitValues.Length];
                for (int i = 0; i < bitValues.Length; i++)
                {
                    if (bitValues[i])
                    {
                        bytesToWrite[i] = 1;
                    }
                    else
                    {
                        bytesToWrite[i] = 0;
                    }
                }

                bitBufTarget.SetBytes(bytesToWrite, addressStart);
                return new byte[0];
            }

            // Tulis Word ke Word Register (D, W, R, ZR)
            MemoryBuffer wordBuf = GetWordBufferByDataCode(dataCode);
            if (wordBuf != null)
            {
                wordBuf.SetBytes(payload, addressStart * 2);
                return new byte[0];
            }

            return null;
        }

        // Memproses Random Read
        private byte[] ReadRandomByCommand(byte[] command)
        {
            if (command.Length < 6)
            {
                return PackCommand(49242, null);
            }

            int count = command[4];
            byte[] result = new byte[count * 2];

            for (int i = 0; i < count; i++)
            {
                int itemOffset = 6 + (i * 4);
                if (itemOffset + 4 > command.Length)
                {
                    break;
                }

                int address = command[itemOffset] + (command[itemOffset + 1] * 256) + (command[itemOffset + 2] * 65536);
                byte dataCode = command[itemOffset + 3];

                MemoryBuffer wordBuf = GetWordBufferByDataCode(dataCode);
                if (wordBuf != null)
                {
                    byte[] wordData = wordBuf.GetBytes(address * 2, 2);
                    Array.Copy(wordData, 0, result, i * 2, 2);
                }
                else
                {
                    MemoryBuffer bitBuf = GetBoolBufferByDataCode(dataCode);
                    if (bitBuf != null)
                    {
                        byte[] rawBits = bitBuf.GetBytes(address, 16);
                        bool[] bits = new bool[16];
                        for (int b = 0; b < 16; b++)
                        {
                            bits[b] = rawBits[b] != 0;
                        }

                        byte[] convertedWord = McProtocolHelper.BoolArrayToByteArray(bits);
                        Array.Copy(convertedWord, 0, result, i * 2, 2);
                    }
                }
            }

            return PackCommand(0, result);
        }

        // Memproses Block Read
        private byte[] ReadBlockByCommand(byte[] command)
        {
            if (command.Length < 6)
            {
                return PackCommand(49242, null);
            }

            int count = command[4];
            using (MemoryStream ms = new MemoryStream())
            {
                for (int i = 0; i < count; i++)
                {
                    int itemOffset = 6 + (i * 6);
                    if (itemOffset + 6 > command.Length)
                    {
                        break;
                    }

                    int address = command[itemOffset] + (command[itemOffset + 1] * 256) + (command[itemOffset + 2] * 65536);
                    byte dataCode = command[itemOffset + 3];
                    ushort length = BitConverter.ToUInt16(command, itemOffset + 4);

                    MemoryBuffer wordBuf = GetWordBufferByDataCode(dataCode);
                    if (wordBuf != null)
                    {
                        byte[] data = wordBuf.GetBytes(address * 2, length * 2);
                        ms.Write(data, 0, data.Length);
                    }
                    else
                    {
                        MemoryBuffer bitBuf = GetBoolBufferByDataCode(dataCode);
                        if (bitBuf != null)
                        {
                            byte[] rawBits = bitBuf.GetBytes(address, length * 16);
                            bool[] bits = new bool[rawBits.Length];
                            for (int b = 0; b < rawBits.Length; b++)
                            {
                                bits[b] = rawBits[b] != 0;
                            }
                            byte[] data = McProtocolHelper.BoolArrayToByteArray(bits);
                            ms.Write(data, 0, data.Length);
                        }
                    }
                }

                return PackCommand(0, ms.ToArray());
            }
        }

        // Memproses perintah ASCII core
        protected virtual byte[] ReadFromMcAsciiCore(byte[] mcCore)
        {
            if (mcCore.Length < 8)
            {
                return PackCommand(49242, null);
            }

            string commandStr = Encoding.ASCII.GetString(mcCore, 0, 4);

            // "0401": Batch Read ASCII
            if (commandStr == "0401")
            {
                return ReadAsciiByCommand(mcCore);
            }

            // "1401": Batch Write ASCII
            if (commandStr == "1401")
            {
                if (!EnableWrite)
                {
                    return PackCommand(49250, null);
                }

                byte[] writeResult = WriteAsciiByMessage(mcCore);
                return PackCommand(0, writeResult);
            }

            return PackCommand(49242, null);
        }

        private byte[] ReadAsciiByCommand(byte[] command)
        {
            if (command.Length < 20)
            {
                return PackCommand(49242, null);
            }

            string subcommand = Encoding.ASCII.GetString(command, 4, 4);
            string deviceCode = Encoding.ASCII.GetString(command, 8, 2);
            string addressStr = Encoding.ASCII.GetString(command, 10, 6);
            ushort count = Convert.ToUInt16(Encoding.ASCII.GetString(command, 16, 4), 16);

            int address;
            if (deviceCode == "X*" || deviceCode == "Y*" || deviceCode == "B*" || deviceCode == "W*")
            {
                address = Convert.ToInt32(addressStr, 16);
            }
            else
            {
                address = Convert.ToInt32(addressStr, 10);
            }

            // Baca Bit ASCII
            if (subcommand == "0001")
            {
                MemoryBuffer bitBuf = GetBoolBufferByAsciiCode(deviceCode);
                if (bitBuf != null)
                {
                    byte[] rawBits = bitBuf.GetBytes(address, count);
                    byte[] asciiResult = new byte[count];
                    for (int i = 0; i < count; i++)
                    {
                        if (rawBits[i] != 0)
                        {
                            asciiResult[i] = (byte)'1';
                        }
                        else
                        {
                            asciiResult[i] = (byte)'0';
                        }
                    }
                    return PackCommand(0, asciiResult);
                }
                return PackCommand(49242, null);
            }

            // Baca Word ASCII
            MemoryBuffer wordBuf = GetWordBufferByAsciiCode(deviceCode);
            if (wordBuf != null)
            {
                byte[] rawWordBytes = wordBuf.GetBytes(address * 2, count * 2);
                byte[] asciiWordBytes = McProtocolHelper.TransByteArrayToAsciiBytes(rawWordBytes);
                return PackCommand(0, asciiWordBytes);
            }

            MemoryBuffer bitAsWordBuf = GetBoolBufferByAsciiCode(deviceCode);
            if (bitAsWordBuf != null)
            {
                byte[] rawBits = bitAsWordBuf.GetBytes(address, count * 16);
                bool[] bits = new bool[rawBits.Length];
                for (int b = 0; b < rawBits.Length; b++)
                {
                    bits[b] = rawBits[b] != 0;
                }
                byte[] wordBytes = McProtocolHelper.BoolArrayToByteArray(bits);
                byte[] asciiWordBytes = McProtocolHelper.TransByteArrayToAsciiBytes(wordBytes);
                return PackCommand(0, asciiWordBytes);
            }

            return PackCommand(49242, null);
        }

        private byte[] WriteAsciiByMessage(byte[] command)
        {
            if (command.Length < 20)
            {
                return null;
            }

            string subcommand = Encoding.ASCII.GetString(command, 4, 4);
            string deviceCode = Encoding.ASCII.GetString(command, 8, 2);
            string addressStr = Encoding.ASCII.GetString(command, 10, 6);
            ushort count = Convert.ToUInt16(Encoding.ASCII.GetString(command, 16, 4), 16);

            int address;
            if (deviceCode == "X*" || deviceCode == "Y*" || deviceCode == "B*" || deviceCode == "W*")
            {
                address = Convert.ToInt32(addressStr, 16);
            }
            else
            {
                address = Convert.ToInt32(addressStr, 10);
            }

            int payloadOffset = 20;
            int payloadLength = command.Length - payloadOffset;
            byte[] asciiPayload = new byte[payloadLength];
            Array.Copy(command, payloadOffset, asciiPayload, 0, payloadLength);

            // Tulis Bit ASCII
            if (subcommand == "0001")
            {
                MemoryBuffer bitBuf = GetBoolBufferByAsciiCode(deviceCode);
                if (bitBuf != null)
                {
                    byte[] bytesToWrite = new byte[count];
                    for (int i = 0; i < count; i++)
                    {
                        if (i < asciiPayload.Length && asciiPayload[i] == (byte)'1')
                        {
                            bytesToWrite[i] = 1;
                        }
                        else
                        {
                            bytesToWrite[i] = 0;
                        }
                    }
                    bitBuf.SetBytes(bytesToWrite, address);
                }
                return new byte[0];
            }

            // Tulis Word ASCII
            byte[] rawBytes = McProtocolHelper.TransAsciiBytesToByteArray(asciiPayload);
            MemoryBuffer wordBuf = GetWordBufferByAsciiCode(deviceCode);
            if (wordBuf != null)
            {
                wordBuf.SetBytes(rawBytes, address * 2);
                return new byte[0];
            }

            return null;
        }

        /// <summary>
        /// Mengemas data jawaban menjadi paket respon MC Protocol lengkap.
        /// </summary>
        protected virtual byte[] PackCommand(ushort status, byte[] data)
        {
            if (data == null)
            {
                data = new byte[0];
            }

            if (_isBinary)
            {
                // Format Binary Qna-3E Response Frame:
                // Subheader: D0 00
                // Net: 00, PLC: FF, IO: FF 03, Station: 00
                // Length: 2 bytes (data.Length + 2)
                // Complete Status: 2 bytes (status)
                // Data: N bytes

                byte[] array = new byte[11 + data.Length];
                array[0] = 0xD0;
                array[1] = 0x00;
                array[2] = 0x00;
                array[3] = 0xFF;
                array[4] = 0xFF;
                array[5] = 0x03;
                array[6] = 0x00;

                ushort respDataLength = (ushort)(data.Length + 2);
                BitConverter.GetBytes(respDataLength).CopyTo(array, 7);
                BitConverter.GetBytes(status).CopyTo(array, 9);

                if (data.Length > 0)
                {
                    data.CopyTo(array, 11);
                }

                return array;
            }
            else
            {
                // Format ASCII Qna-3E Response Frame:
                // Header 22 ASCII chars: "D00000FF03FF00" + Length (4 hex) + Status (4 hex) + Data
                byte[] array = new byte[22 + data.Length];
                Encoding.ASCII.GetBytes("D00000FF03FF00").CopyTo(array, 0);

                string lenHex = (data.Length + 4).ToString("X4");
                Encoding.ASCII.GetBytes(lenHex).CopyTo(array, 14);

                string statusHex = status.ToString("X4");
                Encoding.ASCII.GetBytes(statusHex).CopyTo(array, 18);

                if (data.Length > 0)
                {
                    data.CopyTo(array, 22);
                }

                return array;
            }
        }

        #endregion

        #region Buffer Resolvers

        protected virtual MemoryBuffer GetBoolBufferByDataCode(ushort dataCode)
        {
            if (dataCode == MelsecMcDataType.M.DataCode)
            {
                return _mBuffer;
            }
            if (dataCode == MelsecMcDataType.X.DataCode)
            {
                return _xBuffer;
            }
            if (dataCode == MelsecMcDataType.Y.DataCode)
            {
                return _yBuffer;
            }
            if (dataCode == MelsecMcDataType.L.DataCode)
            {
                return _lBuffer;
            }
            if (dataCode == MelsecMcDataType.B.DataCode)
            {
                return _bBuffer;
            }
            if (dataCode == MelsecMcDataType.S.DataCode)
            {
                return _sBuffer;
            }
            if (dataCode == MelsecMcDataType.F.DataCode)
            {
                return _fBuffer;
            }
            return null;
        }

        protected virtual MemoryBuffer GetWordBufferByDataCode(ushort dataCode)
        {
            if (dataCode == MelsecMcDataType.D.DataCode)
            {
                return _dBuffer;
            }
            if (dataCode == MelsecMcDataType.W.DataCode)
            {
                return _wBuffer;
            }
            if (dataCode == MelsecMcDataType.R.DataCode)
            {
                return _rBuffer;
            }
            if (dataCode == MelsecMcDataType.ZR.DataCode)
            {
                return _zrBuffer;
            }
            return null;
        }

        protected virtual MemoryBuffer GetBoolBufferByAsciiCode(string asciiCode)
        {
            if (asciiCode == MelsecMcDataType.M.AsciiCode)
            {
                return _mBuffer;
            }
            if (asciiCode == MelsecMcDataType.X.AsciiCode)
            {
                return _xBuffer;
            }
            if (asciiCode == MelsecMcDataType.Y.AsciiCode)
            {
                return _yBuffer;
            }
            if (asciiCode == MelsecMcDataType.L.AsciiCode)
            {
                return _lBuffer;
            }
            if (asciiCode == MelsecMcDataType.B.AsciiCode)
            {
                return _bBuffer;
            }
            if (asciiCode == MelsecMcDataType.S.AsciiCode)
            {
                return _sBuffer;
            }
            if (asciiCode == MelsecMcDataType.F.AsciiCode)
            {
                return _fBuffer;
            }
            return null;
        }

        protected virtual MemoryBuffer GetWordBufferByAsciiCode(string asciiCode)
        {
            if (asciiCode == MelsecMcDataType.D.AsciiCode)
            {
                return _dBuffer;
            }
            if (asciiCode == MelsecMcDataType.W.AsciiCode)
            {
                return _wBuffer;
            }
            if (asciiCode == MelsecMcDataType.R.AsciiCode)
            {
                return _rBuffer;
            }
            if (asciiCode == MelsecMcDataType.ZR.AsciiCode)
            {
                return _zrBuffer;
            }
            return null;
        }

        #endregion

        #region User Bit Read & Write (M, X, Y, L, B, S, F, dan D.bit)

        /// <summary>
        /// Membaca 1 status bit register virtual server (contoh: "M0", "X0", "Y0", "D100.1").
        /// </summary>
        public bool ReadBool(string address, bool defaultValue = false)
        {
            McAddressData addr = McAddressData.Parse(address, 1, isBit: true);
            if (addr == null)
            {
                Log("Alamat tidak valid: " + address);
                return defaultValue;
            }

            // Jika mengakses bit dari Word register (contoh: D100.2)
            if (addr.BitIndex >= 0)
            {
                MemoryBuffer wordBuf = GetWordBufferByDataCode(addr.DataType.DataCode);
                if (wordBuf != null)
                {
                    int totalBitOffset = (addr.AddressStart * 16) + addr.BitIndex;
                    bool[] bits = wordBuf.GetBool(totalBitOffset, 1);
                    if (bits.Length > 0)
                    {
                        return bits[0];
                    }
                }
                return defaultValue;
            }

            // Membaca dari Bit Buffer
            MemoryBuffer bitBuf = GetBoolBufferByDataCode(addr.DataType.DataCode);
            if (bitBuf != null)
            {
                byte[] raw = bitBuf.GetBytes(addr.AddressStart, 1);
                if (raw.Length > 0)
                {
                    return raw[0] != 0;
                }
            }

            return defaultValue;
        }

        /// <summary>
        /// Membaca sekumpulan bit register secara berurutan.
        /// </summary>
        public bool[] ReadBoolArray(string address, ushort length)
        {
            McAddressData addr = McAddressData.Parse(address, length, isBit: true);
            if (addr == null || length == 0)
            {
                return new bool[0];
            }

            if (addr.BitIndex >= 0)
            {
                MemoryBuffer wordBuf = GetWordBufferByDataCode(addr.DataType.DataCode);
                if (wordBuf != null)
                {
                    int totalBitOffset = (addr.AddressStart * 16) + addr.BitIndex;
                    return wordBuf.GetBool(totalBitOffset, length);
                }
                return new bool[0];
            }

            MemoryBuffer bitBuf = GetBoolBufferByDataCode(addr.DataType.DataCode);
            if (bitBuf != null)
            {
                byte[] raw = bitBuf.GetBytes(addr.AddressStart, length);
                bool[] result = new bool[raw.Length];
                for (int i = 0; i < raw.Length; i++)
                {
                    result[i] = raw[i] != 0;
                }
                return result;
            }

            return new bool[0];
        }

        /// <summary>
        /// Menulis nilai ke 1 bit register virtual server.
        /// </summary>
        public bool WriteBool(string address, bool value)
        {
            return WriteBool(address, new bool[] { value });
        }

        /// <summary>
        /// Menulis sekumpulan nilai boolean ke bit register secara berurutan.
        /// </summary>
        public bool WriteBool(string address, bool[] values)
        {
            if (values == null || values.Length == 0)
            {
                return false;
            }

            McAddressData addr = McAddressData.Parse(address, (ushort)values.Length, isBit: true);
            if (addr == null)
            {
                Log("Alamat tidak valid untuk penulisan bit: " + address);
                return false;
            }

            // Jika menulis ke bit word register (contoh: D100.2)
            if (addr.BitIndex >= 0)
            {
                MemoryBuffer wordBuf = GetWordBufferByDataCode(addr.DataType.DataCode);
                if (wordBuf != null)
                {
                    int totalBitOffset = (addr.AddressStart * 16) + addr.BitIndex;
                    wordBuf.SetBool(values, totalBitOffset);
                    return true;
                }
                return false;
            }

            // Menulis ke Bit Buffer
            MemoryBuffer bitBuf = GetBoolBufferByDataCode(addr.DataType.DataCode);
            if (bitBuf != null)
            {
                byte[] raw = new byte[values.Length];
                for (int i = 0; i < values.Length; i++)
                {
                    if (values[i])
                    {
                        raw[i] = 1;
                    }
                    else
                    {
                        raw[i] = 0;
                    }
                }
                bitBuf.SetBytes(raw, addr.AddressStart);
                return true;
            }

            return false;
        }

        #endregion

        #region User Word Read & Write (D, W, R, ZR)

        /// <summary>
        /// Membaca nilai 16-bit signed integer (short) dari register (contoh: "D100").
        /// </summary>
        public short ReadInt16(string address, short defaultValue = 0)
        {
            byte[] bytes = ReadBytes(address, 1);
            if (bytes != null && bytes.Length >= 2)
            {
                return BitConverter.ToInt16(bytes, 0);
            }
            return defaultValue;
        }

        /// <summary>
        /// Membaca array nilai 16-bit signed integer (short) dari register.
        /// </summary>
        public short[] ReadInt16Array(string address, ushort length)
        {
            byte[] bytes = ReadBytes(address, length);
            if (bytes == null || bytes.Length < length * 2)
            {
                return new short[0];
            }

            short[] result = new short[length];
            for (int i = 0; i < length; i++)
            {
                result[i] = BitConverter.ToInt16(bytes, i * 2);
            }
            return result;
        }

        /// <summary>
        /// Menulis nilai 16-bit signed integer (short) ke register.
        /// </summary>
        public bool WriteInt16(string address, short value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            return WriteBytes(address, bytes);
        }

        /// <summary>
        /// Menulis array nilai 16-bit signed integer (short) ke register.
        /// </summary>
        public bool WriteInt16(string address, short[] values)
        {
            if (values == null || values.Length == 0)
            {
                return false;
            }

            byte[] bytes = new byte[values.Length * 2];
            for (int i = 0; i < values.Length; i++)
            {
                BitConverter.GetBytes(values[i]).CopyTo(bytes, i * 2);
            }
            return WriteBytes(address, bytes);
        }

        /// <summary>
        /// Membaca nilai 16-bit unsigned integer (ushort) dari register.
        /// </summary>
        public ushort ReadUInt16(string address, ushort defaultValue = 0)
        {
            byte[] bytes = ReadBytes(address, 1);
            if (bytes != null && bytes.Length >= 2)
            {
                return BitConverter.ToUInt16(bytes, 0);
            }
            return defaultValue;
        }

        /// <summary>
        /// Membaca array nilai 16-bit unsigned integer (ushort) dari register.
        /// </summary>
        public ushort[] ReadUInt16Array(string address, ushort length)
        {
            byte[] bytes = ReadBytes(address, length);
            if (bytes == null || bytes.Length < length * 2)
            {
                return new ushort[0];
            }

            ushort[] result = new ushort[length];
            for (int i = 0; i < length; i++)
            {
                result[i] = BitConverter.ToUInt16(bytes, i * 2);
            }
            return result;
        }

        /// <summary>
        /// Menulis nilai 16-bit unsigned integer (ushort) ke register.
        /// </summary>
        public bool WriteUInt16(string address, ushort value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            return WriteBytes(address, bytes);
        }

        /// <summary>
        /// Menulis array nilai 16-bit unsigned integer (ushort) ke register.
        /// </summary>
        public bool WriteUInt16(string address, ushort[] values)
        {
            if (values == null || values.Length == 0)
            {
                return false;
            }

            byte[] bytes = new byte[values.Length * 2];
            for (int i = 0; i < values.Length; i++)
            {
                BitConverter.GetBytes(values[i]).CopyTo(bytes, i * 2);
            }
            return WriteBytes(address, bytes);
        }

        /// <summary>
        /// Membaca nilai 32-bit signed integer (int) dari 2 word register.
        /// </summary>
        public int ReadInt32(string address, int defaultValue = 0)
        {
            byte[] bytes = ReadBytes(address, 2);
            if (bytes != null && bytes.Length >= 4)
            {
                return BitConverter.ToInt32(bytes, 0);
            }
            return defaultValue;
        }

        /// <summary>
        /// Membaca array nilai 32-bit signed integer (int) dari register.
        /// </summary>
        public int[] ReadInt32Array(string address, ushort length)
        {
            byte[] bytes = ReadBytes(address, (ushort)(length * 2));
            if (bytes == null || bytes.Length < length * 4)
            {
                return new int[0];
            }

            int[] result = new int[length];
            for (int i = 0; i < length; i++)
            {
                result[i] = BitConverter.ToInt32(bytes, i * 4);
            }
            return result;
        }

        /// <summary>
        /// Menulis nilai 32-bit signed integer (int) ke 2 word register.
        /// </summary>
        public bool WriteInt32(string address, int value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            return WriteBytes(address, bytes);
        }

        /// <summary>
        /// Menulis array nilai 32-bit signed integer (int) ke register.
        /// </summary>
        public bool WriteInt32(string address, int[] values)
        {
            if (values == null || values.Length == 0)
            {
                return false;
            }

            byte[] bytes = new byte[values.Length * 4];
            for (int i = 0; i < values.Length; i++)
            {
                BitConverter.GetBytes(values[i]).CopyTo(bytes, i * 4);
            }
            return WriteBytes(address, bytes);
        }

        /// <summary>
        /// Membaca nilai 32-bit unsigned integer (uint) dari 2 word register.
        /// </summary>
        public uint ReadUInt32(string address, uint defaultValue = 0)
        {
            byte[] bytes = ReadBytes(address, 2);
            if (bytes != null && bytes.Length >= 4)
            {
                return BitConverter.ToUInt32(bytes, 0);
            }
            return defaultValue;
        }

        /// <summary>
        /// Membaca array nilai 32-bit unsigned integer (uint) dari register.
        /// </summary>
        public uint[] ReadUInt32Array(string address, ushort length)
        {
            byte[] bytes = ReadBytes(address, (ushort)(length * 2));
            if (bytes == null || bytes.Length < length * 4)
            {
                return new uint[0];
            }

            uint[] result = new uint[length];
            for (int i = 0; i < length; i++)
            {
                result[i] = BitConverter.ToUInt32(bytes, i * 4);
            }
            return result;
        }

        /// <summary>
        /// Menulis nilai 32-bit unsigned integer (uint) ke 2 word register.
        /// </summary>
        public bool WriteUInt32(string address, uint value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            return WriteBytes(address, bytes);
        }

        /// <summary>
        /// Menulis array nilai 32-bit unsigned integer (uint) ke register.
        /// </summary>
        public bool WriteUInt32(string address, uint[] values)
        {
            if (values == null || values.Length == 0)
            {
                return false;
            }

            byte[] bytes = new byte[values.Length * 4];
            for (int i = 0; i < values.Length; i++)
            {
                BitConverter.GetBytes(values[i]).CopyTo(bytes, i * 4);
            }
            return WriteBytes(address, bytes);
        }

        /// <summary>
        /// Membaca nilai 32-bit Floating Point (float) dari register (contoh: "D100").
        /// </summary>
        public float ReadFloat(string address, float defaultValue = 0f)
        {
            byte[] bytes = ReadBytes(address, 2);
            if (bytes != null && bytes.Length >= 4)
            {
                return BitConverter.ToSingle(bytes, 0);
            }
            return defaultValue;
        }

        /// <summary>
        /// Membaca array nilai 32-bit Floating Point (float) dari register.
        /// </summary>
        public float[] ReadFloatArray(string address, ushort length)
        {
            byte[] bytes = ReadBytes(address, (ushort)(length * 2));
            if (bytes == null || bytes.Length < length * 4)
            {
                return new float[0];
            }

            float[] result = new float[length];
            for (int i = 0; i < length; i++)
            {
                result[i] = BitConverter.ToSingle(bytes, i * 4);
            }
            return result;
        }

        /// <summary>
        /// Menulis nilai 32-bit Floating Point (float) ke register.
        /// </summary>
        public bool WriteFloat(string address, float value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            return WriteBytes(address, bytes);
        }

        /// <summary>
        /// Menulis array nilai 32-bit Floating Point (float) ke register.
        /// </summary>
        public bool WriteFloat(string address, float[] values)
        {
            if (values == null || values.Length == 0)
            {
                return false;
            }

            byte[] bytes = new byte[values.Length * 4];
            for (int i = 0; i < values.Length; i++)
            {
                BitConverter.GetBytes(values[i]).CopyTo(bytes, i * 4);
            }
            return WriteBytes(address, bytes);
        }

        /// <summary>
        /// Membaca nilai 64-bit Double dari register (contoh: "D200").
        /// </summary>
        public double ReadDouble(string address, double defaultValue = 0d)
        {
            byte[] bytes = ReadBytes(address, 4);
            if (bytes != null && bytes.Length >= 8)
            {
                return BitConverter.ToDouble(bytes, 0);
            }
            return defaultValue;
        }

        /// <summary>
        /// Membaca array nilai 64-bit Double dari register.
        /// </summary>
        public double[] ReadDoubleArray(string address, ushort length)
        {
            byte[] bytes = ReadBytes(address, (ushort)(length * 4));
            if (bytes == null || bytes.Length < length * 8)
            {
                return new double[0];
            }

            double[] result = new double[length];
            for (int i = 0; i < length; i++)
            {
                result[i] = BitConverter.ToDouble(bytes, i * 8);
            }
            return result;
        }

        /// <summary>
        /// Menulis nilai 64-bit Double ke register.
        /// </summary>
        public bool WriteDouble(string address, double value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            return WriteBytes(address, bytes);
        }

        /// <summary>
        /// Menulis array nilai 64-bit Double ke register.
        /// </summary>
        public bool WriteDouble(string address, double[] values)
        {
            if (values == null || values.Length == 0)
            {
                return false;
            }

            byte[] bytes = new byte[values.Length * 8];
            for (int i = 0; i < values.Length; i++)
            {
                BitConverter.GetBytes(values[i]).CopyTo(bytes, i * 8);
            }
            return WriteBytes(address, bytes);
        }

        /// <summary>
        /// Membaca string teks ASCII dari register (contoh: "D100", panjang karakter: 10).
        /// </summary>
        public string ReadString(string address, ushort length)
        {
            ushort wordLength = (ushort)((length + 1) / 2);
            byte[] bytes = ReadBytes(address, wordLength);
            if (bytes == null || bytes.Length == 0)
            {
                return string.Empty;
            }

            int validLength = length;
            if (validLength > bytes.Length)
            {
                validLength = bytes.Length;
            }

            string rawText = Encoding.ASCII.GetString(bytes, 0, validLength);
            int nullTermIndex = rawText.IndexOf('\0');
            if (nullTermIndex >= 0)
            {
                return rawText.Substring(0, nullTermIndex);
            }
            return rawText;
        }

        /// <summary>
        /// Menulis string teks ASCII ke register.
        /// </summary>
        public bool WriteString(string address, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            byte[] bytes = Encoding.ASCII.GetBytes(value);
            return WriteBytes(address, bytes);
        }

        /// <summary>
        /// Membaca sejumlah word dari register dalam bentuk raw byte array (1 word = 2 bytes).
        /// </summary>
        public byte[] ReadBytes(string address, ushort wordLength)
        {
            McAddressData addr = McAddressData.Parse(address, wordLength, isBit: false);
            if (addr == null)
            {
                Log("Alamat tidak valid untuk pembacaan bytes: " + address);
                return null;
            }

            MemoryBuffer wordBuf = GetWordBufferByDataCode(addr.DataType.DataCode);
            if (wordBuf != null)
            {
                return wordBuf.GetBytes(addr.AddressStart * 2, wordLength * 2);
            }

            // Jika membaca word dari bit device (M, X, Y)
            MemoryBuffer bitBuf = GetBoolBufferByDataCode(addr.DataType.DataCode);
            if (bitBuf != null)
            {
                byte[] rawBits = bitBuf.GetBytes(addr.AddressStart, wordLength * 16);
                bool[] bits = new bool[rawBits.Length];
                for (int i = 0; i < rawBits.Length; i++)
                {
                    bits[i] = rawBits[i] != 0;
                }
                return McProtocolHelper.BoolArrayToByteArray(bits);
            }

            return null;
        }

        /// <summary>
        /// Menulis array byte langsung ke word register.
        /// </summary>
        public bool WriteBytes(string address, byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return false;
            }

            McAddressData addr = McAddressData.Parse(address, 0, isBit: false);
            if (addr == null)
            {
                Log("Alamat tidak valid untuk penulisan bytes: " + address);
                return false;
            }

            MemoryBuffer wordBuf = GetWordBufferByDataCode(addr.DataType.DataCode);
            if (wordBuf != null)
            {
                wordBuf.SetBytes(data, addr.AddressStart * 2);
                return true;
            }

            // Jika menulis ke bit device
            MemoryBuffer bitBuf = GetBoolBufferByDataCode(addr.DataType.DataCode);
            if (bitBuf != null)
            {
                bool[] bits = McProtocolHelper.ByteArrayToBoolArray(data);
                byte[] rawBits = new byte[bits.Length];
                for (int i = 0; i < bits.Length; i++)
                {
                    if (bits[i])
                    {
                        rawBits[i] = 1;
                    }
                    else
                    {
                        rawBits[i] = 0;
                    }
                }
                bitBuf.SetBytes(rawBits, addr.AddressStart);
                return true;
            }

            return false;
        }

        #endregion

        #region Memory Persistence Methods

        /// <summary>
        /// Menyimpan snapshot data seluruh register ke dalam file fisik di disk.
        /// </summary>
        public void SaveMemoryToFile(string filePath)
        {
            try
            {
                byte[] mBytes = _mBuffer.GetAllBytes();
                byte[] xBytes = _xBuffer.GetAllBytes();
                byte[] yBytes = _yBuffer.GetAllBytes();
                byte[] dBytes = _dBuffer.GetAllBytes();
                byte[] wBytes = _wBuffer.GetAllBytes();
                byte[] bBytes = _bBuffer.GetAllBytes();
                byte[] rBytes = _rBuffer.GetAllBytes();
                byte[] zrBytes = _zrBuffer.GetAllBytes();
                byte[] lBytes = _lBuffer.GetAllBytes();

                using (FileStream fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                using (BinaryWriter bw = new BinaryWriter(fs))
                {
                    bw.Write(mBytes.Length);
                    bw.Write(mBytes);

                    bw.Write(xBytes.Length);
                    bw.Write(xBytes);

                    bw.Write(yBytes.Length);
                    bw.Write(yBytes);

                    bw.Write(dBytes.Length);
                    bw.Write(dBytes);

                    bw.Write(wBytes.Length);
                    bw.Write(wBytes);

                    bw.Write(bBytes.Length);
                    bw.Write(bBytes);

                    bw.Write(rBytes.Length);
                    bw.Write(rBytes);

                    bw.Write(zrBytes.Length);
                    bw.Write(zrBytes);

                    bw.Write(lBytes.Length);
                    bw.Write(lBytes);
                }

                Log("Snapshot memori server berhasil disimpan ke: " + filePath);
            }
            catch (Exception ex)
            {
                Log("Gagal menyimpan memori server: " + ex.Message);
            }
        }

        /// <summary>
        /// Memuat kembali snapshot data register dari file fisik di disk.
        /// </summary>
        public void LoadMemoryFromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Log("File snapshot memori tidak ditemukan: " + filePath);
                return;
            }

            try
            {
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                using (BinaryReader br = new BinaryReader(fs))
                {
                    int mLen = br.ReadInt32();
                    _mBuffer.SetBytes(br.ReadBytes(mLen), 0);

                    int xLen = br.ReadInt32();
                    _xBuffer.SetBytes(br.ReadBytes(xLen), 0);

                    int yLen = br.ReadInt32();
                    _yBuffer.SetBytes(br.ReadBytes(yLen), 0);

                    int dLen = br.ReadInt32();
                    _dBuffer.SetBytes(br.ReadBytes(dLen), 0);

                    int wLen = br.ReadInt32();
                    _wBuffer.SetBytes(br.ReadBytes(wLen), 0);

                    int bLen = br.ReadInt32();
                    _bBuffer.SetBytes(br.ReadBytes(bLen), 0);

                    int rLen = br.ReadInt32();
                    _rBuffer.SetBytes(br.ReadBytes(rLen), 0);

                    int zrLen = br.ReadInt32();
                    _zrBuffer.SetBytes(br.ReadBytes(zrLen), 0);

                    int lLen = br.ReadInt32();
                    _lBuffer.SetBytes(br.ReadBytes(lLen), 0);
                }

                Log("Snapshot memori server berhasil dimuat dari: " + filePath);
            }
            catch (Exception ex)
            {
                Log("Gagal memuat memori server: " + ex.Message);
            }
        }

        #endregion

        #region Private Event Helpers

        private void TriggerDataReceived(object sender, byte[] data)
        {
            if (OnDataReceived != null)
            {
                try
                {
                    OnDataReceived.Invoke(sender, data);
                }
                catch (Exception ex)
                {
                    Log("Terjadi kesalahan pada OnDataReceived handler: " + ex.Message);
                }
            }
        }

        private void Log(string message)
        {
            if (OnLogMessage != null)
            {
                try
                {
                    OnLogMessage.Invoke(message);
                }
                catch
                {
                }
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    if (_isRunning)
                    {
                        Stop();
                    }

                    if (_mBuffer != null)
                    {
                        _mBuffer.Dispose();
                    }
                    if (_xBuffer != null)
                    {
                        _xBuffer.Dispose();
                    }
                    if (_yBuffer != null)
                    {
                        _yBuffer.Dispose();
                    }
                    if (_lBuffer != null)
                    {
                        _lBuffer.Dispose();
                    }
                    if (_bBuffer != null)
                    {
                        _bBuffer.Dispose();
                    }
                    if (_sBuffer != null)
                    {
                        _sBuffer.Dispose();
                    }
                    if (_fBuffer != null)
                    {
                        _fBuffer.Dispose();
                    }
                    if (_dBuffer != null)
                    {
                        _dBuffer.Dispose();
                    }
                    if (_wBuffer != null)
                    {
                        _wBuffer.Dispose();
                    }
                    if (_rBuffer != null)
                    {
                        _rBuffer.Dispose();
                    }
                    if (_zrBuffer != null)
                    {
                        _zrBuffer.Dispose();
                    }
                }

                _isDisposed = true;
            }
        }

        #endregion
    }
}
