#nullable disable
using System;
using System.Collections.Generic;
using NNMitsubishiServer;

namespace NetToGXSim3.Core
{
    /// <summary>
    /// Peta buffer device MC Protocol. Setiap operasi baca/tulis pada buffer ini
    /// diteruskan (redirect) ke GX Simulator 3 melalui GxSimulatorEngine,
    /// sehingga data yang dibaca HMI selalu berasal dari simulator yang sedang berjalan.
    /// </summary>
    internal sealed class GxSimDeviceBufferMap
    {
        private const int BitBufferCapacityBytes = 65536;
        private const int WordBufferCapacityBytes = 262144;

        private readonly GxSimulatorEngine _engine;

        private readonly Dictionary<ushort, MemoryBuffer> _bitByDataCode = new Dictionary<ushort, MemoryBuffer>();
        private readonly Dictionary<ushort, MemoryBuffer> _wordByDataCode = new Dictionary<ushort, MemoryBuffer>();
        private readonly Dictionary<string, MemoryBuffer> _bitByAsciiCode = new Dictionary<string, MemoryBuffer>();
        private readonly Dictionary<string, MemoryBuffer> _wordByAsciiCode = new Dictionary<string, MemoryBuffer>();

        public GxSimDeviceBufferMap(GxSimulatorEngine engine)
        {
            _engine = engine;

            // Device bit (index buffer = nomor bit)
            AddBitDevice(MelsecMcDataType.X, "X");
            AddBitDevice(MelsecMcDataType.Y, "Y");
            AddBitDevice(MelsecMcDataType.M, "M");
            AddBitDevice(MelsecMcDataType.L, "L");
            AddBitDevice(MelsecMcDataType.B, "B");
            AddBitDevice(MelsecMcDataType.S, "S");
            AddBitDevice(MelsecMcDataType.F, "F");

            // Device word (index buffer = nomor word)
            AddWordDevice(MelsecMcDataType.D, "D");
            AddWordDevice(MelsecMcDataType.W, "W");
            AddWordDevice(MelsecMcDataType.R, "R");
            AddWordDevice(MelsecMcDataType.ZR, "ZR");
        }

        public MemoryBuffer GetBitBufferByDataCode(ushort dataCode)
        {
            MemoryBuffer buffer;
            if (_bitByDataCode.TryGetValue(dataCode, out buffer))
            {
                return buffer;
            }
            return null;
        }

        public MemoryBuffer GetWordBufferByDataCode(ushort dataCode)
        {
            MemoryBuffer buffer;
            if (_wordByDataCode.TryGetValue(dataCode, out buffer))
            {
                return buffer;
            }
            return null;
        }

        public MemoryBuffer GetBitBufferByAsciiCode(string asciiCode)
        {
            if (string.IsNullOrEmpty(asciiCode))
            {
                return null;
            }

            MemoryBuffer buffer;
            if (_bitByAsciiCode.TryGetValue(asciiCode, out buffer))
            {
                return buffer;
            }
            return null;
        }

        public MemoryBuffer GetWordBufferByAsciiCode(string asciiCode)
        {
            if (string.IsNullOrEmpty(asciiCode))
            {
                return null;
            }

            MemoryBuffer buffer;
            if (_wordByAsciiCode.TryGetValue(asciiCode, out buffer))
            {
                return buffer;
            }
            return null;
        }

        #region Redirect Handlers (GX Simulator 3 Access)

        // Membaca blok bit dari GX Simulator 3. Index buffer = nomor bit device.
        private Func<int, int, byte[]> CreateBitReader(string prefix)
        {
            string baseDevice = prefix + "0";

            return (index, length) =>
            {
                if (length <= 0)
                {
                    return new byte[0];
                }

                byte[] result = new byte[length];
                string startDevice = GxSimulatorEngine.StepDeviceName(baseDevice, index);

                byte[] block;
                int res = _engine.ReadDeviceBlockBits(startDevice, length, out block);
                if (res == 0 && block != null && block.Length >= length)
                {
                    return block;
                }

                // Fallback: baca satu bit per satu bit bila pembacaan blok tidak tersedia
                for (int i = 0; i < length; i++)
                {
                    string deviceName = GxSimulatorEngine.StepDeviceName(startDevice, i);
                    int value;
                    if (_engine.ReadDevice(deviceName, out value) == 0)
                    {
                        if (value != 0)
                        {
                            result[i] = 1;
                        }
                        else
                        {
                            result[i] = 0;
                        }
                    }
                }

                return result;
            };
        }

        // Menulis blok bit ke GX Simulator 3. Index buffer = nomor bit device.
        private Action<int, byte[]> CreateBitWriter(string prefix)
        {
            string baseDevice = prefix + "0";

            return (index, data) =>
            {
                if (data == null || data.Length == 0)
                {
                    return;
                }

                string startDevice = GxSimulatorEngine.StepDeviceName(baseDevice, index);

                if (_engine.WriteDeviceBlockBits(startDevice, data.Length, data) == 0)
                {
                    return;
                }

                // Fallback: tulis satu bit per satu bit bila penulisan blok tidak tersedia
                for (int i = 0; i < data.Length; i++)
                {
                    string deviceName = GxSimulatorEngine.StepDeviceName(startDevice, i);

                    int value;
                    if (data[i] != 0)
                    {
                        value = 1;
                    }
                    else
                    {
                        value = 0;
                    }

                    _engine.WriteDevice(deviceName, value);
                }
            };
        }

        #endregion

        #region Redirect Handlers (GX Simulator 3 Access - Word)

        // Membaca blok word dari GX Simulator 3. Index buffer = byte offset (word * 2).
        private Func<int, int, byte[]> CreateWordReader(string prefix)
        {
            string baseDevice = prefix + "0";

            return (index, byteLength) =>
            {
                if (byteLength <= 0)
                {
                    return new byte[0];
                }
                byte[] result = new byte[byteLength];

                int fullWords = byteLength / 2;
                bool hasTrailingByte = (byteLength % 2) != 0;

                int wordsToRead = fullWords;
                if (hasTrailingByte)
                {
                    wordsToRead = fullWords + 1;
                }

                string startDevice = GxSimulatorEngine.StepDeviceName(baseDevice, index / 2);

                short[] words;
                int res = _engine.ReadDeviceBlockWords(startDevice, wordsToRead, out words);

                if (res != 0 || words == null)
                {
                    // Fallback: baca satu word per satu word
                    words = new short[wordsToRead];
                    for (int i = 0; i < wordsToRead; i++)
                    {
                        string deviceName = GxSimulatorEngine.StepDeviceName(startDevice, i);
                        int value;
                        if (_engine.ReadDevice(deviceName, out value) == 0)
                        {
                            words[i] = (short)value;
                        }
                    }
                }

                int availableBytes = words.Length * 2;
                int copyBytes = result.Length;
                if (copyBytes > availableBytes)
                {
                    copyBytes = availableBytes;
                }

                if (copyBytes > 0)
                {
                    Buffer.BlockCopy(words, 0, result, 0, copyBytes);
                }

                return result;
            };
        }

        // Menulis blok word ke GX Simulator 3. Index buffer = byte offset (word * 2).
        private Action<int, byte[]> CreateWordWriter(string prefix)
        {
            string baseDevice = prefix + "0";

            return (index, data) =>
            {
                if (data == null || data.Length == 0)
                {
                    return;
                }

                int fullWords = data.Length / 2;

                if (fullWords > 0)
                {
                    short[] words = new short[fullWords];
                    Buffer.BlockCopy(data, 0, words, 0, fullWords * 2);

                    string startDevice = GxSimulatorEngine.StepDeviceName(baseDevice, index / 2);

                    if (_engine.WriteDeviceBlockWords(startDevice, fullWords, words) != 0)
                    {
                        // Fallback: tulis satu word per satu word
                        for (int i = 0; i < fullWords; i++)
                        {
                            string deviceName = GxSimulatorEngine.StepDeviceName(startDevice, i);
                            _engine.WriteDevice(deviceName, words[i]);
                        }
                    }
                }

                // Byte sisa (panjang ganjil): ubah hanya byte rendah word terakhir
                if ((data.Length % 2) != 0)
                {
                    string lastDevice = GxSimulatorEngine.StepDeviceName(baseDevice, index / 2 + fullWords);

                    int current;
                    if (_engine.ReadDevice(lastDevice, out current) == 0)
                    {
                        int newValue = (current & 0xFF00) | data[data.Length - 1];
                        _engine.WriteDevice(lastDevice, newValue);
                    }
                }
            };
        }

        #endregion

        private void AddBitDevice(MelsecMcDataType type, string prefix)
        {
            MemoryBuffer buffer = new MemoryBuffer(BitBufferCapacityBytes);
            buffer.ReadHandler = CreateBitReader(prefix);
            buffer.WriteHandler = CreateBitWriter(prefix);

            _bitByDataCode[type.DataCode] = buffer;
            _bitByAsciiCode[type.AsciiCode] = buffer;
        }

        private void AddWordDevice(MelsecMcDataType type, string prefix)
        {
            MemoryBuffer buffer = new MemoryBuffer(WordBufferCapacityBytes);
            buffer.ReadHandler = CreateWordReader(prefix);
            buffer.WriteHandler = CreateWordWriter(prefix);

            _wordByDataCode[type.DataCode] = buffer;
            _wordByAsciiCode[type.AsciiCode] = buffer;
        }
    }

    /// <summary>
    /// Frame parser MC Protocol Qna-3E (gaya FX5U / Q-series). Seluruh parsing frame
    /// memakai library NNMitsubishiServer.Fx5uServer, sedangkan akses device
    /// di-redirect ke GX Simulator 3 (lihat GxSimDeviceBufferMap).
    /// </summary>
    internal sealed class GxSim3EFrameParser : Fx5uServer
    {
        // End code error standar MC Protocol yang dipakai library
        private const ushort ErrorEndCode = 49242;

        private readonly GxSimDeviceBufferMap _map;
        private readonly GxSimulatorEngine _engine;

        // Identitas CPU yang diumumkan pada command 0x0101 (Read CPU Type)
        public string CpuModel { get; set; } = "Q02UCPU";
        public ushort CpuTypeCode { get; set; } = 0x0022;

        public GxSim3EFrameParser(GxSimDeviceBufferMap map, GxSimulatorEngine engine) : base(true)
        {
            _map = map;
            _engine = engine;
        }

        protected override MemoryBuffer GetBoolBufferByDataCode(ushort dataCode)
        {
            return _map.GetBitBufferByDataCode(dataCode);
        }

        protected override MemoryBuffer GetWordBufferByDataCode(ushort dataCode)
        {
            return _map.GetWordBufferByDataCode(dataCode);
        }

        protected override MemoryBuffer GetBoolBufferByAsciiCode(string asciiCode)
        {
            return _map.GetBitBufferByAsciiCode(asciiCode);
        }

        protected override MemoryBuffer GetWordBufferByAsciiCode(string asciiCode)
        {
            return _map.GetWordBufferByAsciiCode(asciiCode);
        }

        protected override byte[] ReadFromMcCore(byte[] mcCore)
        {
            if (mcCore == null || mcCore.Length < 2)
            {
                return PackCommand(ErrorEndCode, null);
            }

            byte commandLow = mcCore[0];
            byte commandHigh = mcCore[1];

            // 0101: Read CPU Type -> balas identitas CPU yang dipilih di UI
            if (commandLow == 0x01 && commandHigh == 0x01)
            {
                return BuildCpuTypeResponse();
            }

            // 0402: Random Read (belum tersedia di library, ditangani di sini)
            if (commandLow == 0x02 && commandHigh == 0x04)
            {
                return HandleRandomRead(mcCore);
            }

            // 1402: Random Write (belum tersedia di library, ditangani di sini)
            if (commandLow == 0x02 && commandHigh == 0x14)
            {
                return HandleRandomWrite(mcCore);
            }

            return base.ReadFromMcCore(mcCore);
        }

        private byte[] BuildCpuTypeResponse()
        {
            string model = CpuModel;
            if (string.IsNullOrEmpty(model))
            {
                model = "Q02UCPU";
            }
            if (model.Length > 16)
            {
                model = model.Substring(0, 16);
            }

            byte[] data = new byte[18];
            Buffer.BlockCopy(System.Text.Encoding.ASCII.GetBytes(model.PadRight(16)), 0, data, 0, 16);
            data[16] = (byte)(CpuTypeCode & 0xFF);
            data[17] = (byte)((CpuTypeCode >> 8) & 0xFF);

            return PackCommand(0, data);
        }

        #region Random Read / Write (0402 / 1402)

        private byte[] HandleRandomRead(byte[] mcCore)
        {
            if (mcCore.Length < 5)
            {
                return PackCommand(ErrorEndCode, null);
            }

            ushort subCommand = BitConverter.ToUInt16(mcCore, 2);

            // Subcommand 0001: satuan bit
            if (subCommand == 0x0001)
            {
                int bitPoints = mcCore[4];
                byte[] bitValues = new byte[bitPoints];
                int offset = 5;

                for (int i = 0; i < bitPoints; i++)
                {
                    if (offset + 4 > mcCore.Length)
                    {
                        break;
                    }

                    int address = ReadAddress24(mcCore, offset);
                    byte deviceCode = mcCore[offset + 3];
                    offset += 4;

                    string deviceName = BuildDeviceName(deviceCode, address);
                    if (deviceName == null)
                    {
                        continue;
                    }

                    int value;
                    if (_engine.ReadDevice(deviceName, out value) == 0)
                    {
                        if (value != 0)
                        {
                            bitValues[i] = 1;
                        }
                    }
                }

                return PackCommand(0, McProtocolHelper.TransBoolArrayToByteData(ToBoolArray(bitValues)));
            }

            // Subcommand 0000: satuan word dan dword
            int wordPoints = mcCore[4];
            int dwordPoints = 0;
            if (mcCore.Length > 5)
            {
                dwordPoints = mcCore[5];
            }

            byte[] result = new byte[(wordPoints * 2) + (dwordPoints * 4)];
            int outIndex = 0;
            int pos = 6;

            for (int i = 0; i < wordPoints; i++)
            {
                if (pos + 4 > mcCore.Length || outIndex + 2 > result.Length)
                {
                    break;
                }

                int address = ReadAddress24(mcCore, pos);
                byte deviceCode = mcCore[pos + 3];
                pos += 4;

                int value = 0;
                string deviceName = BuildDeviceName(deviceCode, address);
                if (deviceName != null)
                {
                    _engine.ReadDevice(deviceName, out value);
                }

                result[outIndex] = (byte)(value & 0xFF);
                result[outIndex + 1] = (byte)((value >> 8) & 0xFF);
                outIndex += 2;
            }

            for (int i = 0; i < dwordPoints; i++)
            {
                if (pos + 4 > mcCore.Length || outIndex + 4 > result.Length)
                {
                    break;
                }

                int address = ReadAddress24(mcCore, pos);
                byte deviceCode = mcCore[pos + 3];
                pos += 4;

                int lowValue = 0;
                int highValue = 0;

                string lowDevice = BuildDeviceName(deviceCode, address);
                string highDevice = BuildDeviceName(deviceCode, address + 1);

                if (lowDevice != null)
                {
                    _engine.ReadDevice(lowDevice, out lowValue);
                }
                if (highDevice != null)
                {
                    _engine.ReadDevice(highDevice, out highValue);
                }

                result[outIndex] = (byte)(lowValue & 0xFF);
                result[outIndex + 1] = (byte)((lowValue >> 8) & 0xFF);
                result[outIndex + 2] = (byte)(highValue & 0xFF);
                result[outIndex + 3] = (byte)((highValue >> 8) & 0xFF);
                outIndex += 4;
            }

            return PackCommand(0, result);
        }

        private byte[] HandleRandomWrite(byte[] mcCore)
        {
            if (mcCore.Length < 5)
            {
                return PackCommand(ErrorEndCode, null);
            }

            if (!EnableWrite)
            {
                return PackCommand(49250, null);
            }

            ushort subCommand = BitConverter.ToUInt16(mcCore, 2);

            // Subcommand 0001: satuan bit
            if (subCommand == 0x0001)
            {
                int bitPoints = mcCore[4];
                int offset = 5;

                for (int i = 0; i < bitPoints; i++)
                {
                    if (offset + 5 > mcCore.Length)
                    {
                        break;
                    }

                    int address = ReadAddress24(mcCore, offset);
                    byte deviceCode = mcCore[offset + 3];
                    byte value = mcCore[offset + 4];
                    offset += 5;

                    string deviceName = BuildDeviceName(deviceCode, address);
                    if (deviceName == null)
                    {
                        continue;
                    }

                    if (value != 0)
                    {
                        _engine.WriteDevice(deviceName, 1);
                    }
                    else
                    {
                        _engine.WriteDevice(deviceName, 0);
                    }
                }

                return PackCommand(0, new byte[0]);
            }

            // Subcommand 0000: satuan word dan dword
            int wordPoints = mcCore[4];
            int dwordPoints = 0;
            if (mcCore.Length > 5)
            {
                dwordPoints = mcCore[5];
            }

            int pos = 6;

            for (int i = 0; i < wordPoints; i++)
            {
                if (pos + 6 > mcCore.Length)
                {
                    break;
                }

                int address = ReadAddress24(mcCore, pos);
                byte deviceCode = mcCore[pos + 3];
                short value = BitConverter.ToInt16(mcCore, pos + 4);
                pos += 6;

                string deviceName = BuildDeviceName(deviceCode, address);
                if (deviceName != null)
                {
                    _engine.WriteDevice(deviceName, (int)(ushort)value);
                }
            }

            for (int i = 0; i < dwordPoints; i++)
            {
                if (pos + 8 > mcCore.Length)
                {
                    break;
                }

                int address = ReadAddress24(mcCore, pos);
                byte deviceCode = mcCore[pos + 3];
                int value = BitConverter.ToInt32(mcCore, pos + 4);
                pos += 8;

                string lowDevice = BuildDeviceName(deviceCode, address);
                string highDevice = BuildDeviceName(deviceCode, address + 1);

                if (lowDevice != null)
                {
                    _engine.WriteDevice(lowDevice, value & 0xFFFF);
                }
                if (highDevice != null)
                {
                    _engine.WriteDevice(highDevice, (value >> 16) & 0xFFFF);
                }
            }

            return PackCommand(0, new byte[0]);
        }

        #endregion

        #region Device Name Helpers

        private static int ReadAddress24(byte[] buffer, int offset)
        {
            return buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
        }

        private static bool[] ToBoolArray(byte[] bitValues)
        {
            bool[] result = new bool[bitValues.Length];
            for (int i = 0; i < bitValues.Length; i++)
            {
                result[i] = bitValues[i] != 0;
            }
            return result;
        }

        // Mengubah device code frame biner MC menjadi nama device untuk memory bridge.
        private static string BuildDeviceName(byte deviceCode, int address)
        {
            string prefix = PrefixFromDeviceCode(deviceCode);
            if (prefix == null)
            {
                return null;
            }

            return GxSimulatorEngine.StepDeviceName(prefix + "0", address);
        }

        private static string PrefixFromDeviceCode(byte deviceCode)
        {
            if (deviceCode == MelsecMcDataType.X.DataCode) return "X";
            if (deviceCode == MelsecMcDataType.Y.DataCode) return "Y";
            if (deviceCode == MelsecMcDataType.M.DataCode) return "M";
            if (deviceCode == MelsecMcDataType.L.DataCode) return "L";
            if (deviceCode == MelsecMcDataType.B.DataCode) return "B";
            if (deviceCode == MelsecMcDataType.S.DataCode) return "S";
            if (deviceCode == MelsecMcDataType.F.DataCode) return "F";
            if (deviceCode == MelsecMcDataType.D.DataCode) return "D";
            if (deviceCode == MelsecMcDataType.W.DataCode) return "W";
            if (deviceCode == MelsecMcDataType.R.DataCode) return "R";
            if (deviceCode == MelsecMcDataType.ZR.DataCode) return "ZR";
            return null;
        }

        #endregion
    }
}

