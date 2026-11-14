using System;
using System.Text;

namespace NNMitsubishiServer
{
    /// <summary>
    /// Virtual Server MC Protocol A-1E Frame (A1E).
    /// Mengemulasikan protokol MC 1E Frame yang umum digunakan pada PLC Mitsubishi FX3U (dengan modul FX3U-ENET) dan A-Series.
    /// Dibuat 100% Native C# murni tanpa dependensi eksternal.
    /// </summary>
    public class MelsecA1EServer : Fx5uServer
    {
        #region Constructors

        /// <summary>
        /// Inisialisasi server virtual MC-A1E baru.
        /// </summary>
        /// <param name="isBinary">True untuk format Binary (default), False untuk format ASCII.</param>
        public MelsecA1EServer(bool isBinary = true) : base(isBinary)
        {
        }

        /// <summary>
        /// Inisialisasi server virtual MC-A1E dengan port tertentu.
        /// </summary>
        /// <param name="port">Port TCP listening server (default: 6000 atau 5002).</param>
        /// <param name="isBinary">True untuk format Binary, False untuk format ASCII.</param>
        public MelsecA1EServer(int port = 6000, bool isBinary = true) : base(port, isBinary)
        {
        }

        #endregion

        #region Frame Processing Overrides

        /// <summary>
        /// Memproses paket data frame A1E (Binary maupun ASCII).
        /// Format A1E tidak memiliki header 11-byte seperti 3E frame.
        /// </summary>
        public override byte[] ProcessMcFrame(byte[] receive)
        {
            if (receive == null)
            {
                return null;
            }

            if (IsBinary)
            {
                if (receive.Length < 12)
                {
                    return null;
                }

                return ReadFromA1ECore(receive);
            }
            else
            {
                if (receive.Length < 24)
                {
                    return null;
                }

                return ReadFromA1EAsciiCore(receive);
            }
        }

        #endregion

        #region Binary A1E Core Processing

        /// <summary>
        /// Memproses paket frame biner MC-A1E.
        /// </summary>
        protected virtual byte[] ReadFromA1ECore(byte[] mcCore)
        {
            try
            {
                byte subcmd = mcCore[0];
                int address = BitConverter.ToInt32(mcCore, 4);
                ushort dataCode = BitConverter.ToUInt16(mcCore, 8);
                ushort length = BitConverter.ToUInt16(mcCore, 10);

                string addressStr = GetAddressFromDataCode(dataCode, address);
                if (string.IsNullOrEmpty(addressStr))
                {
                    return PackResponseCommand(subcmd, 16, null); // Error: Alamat tidak didukung
                }

                // 00: Baca Bit (Read Bit in Units of Bits)
                if (subcmd == 0)
                {
                    if (length == 0)
                    {
                        length = 256;
                    }

                    if (length > 256)
                    {
                        return PackResponseCommand(subcmd, 16, null);
                    }

                    bool[] boolValues = ReadBoolArray(addressStr, length);
                    if (boolValues == null || boolValues.Length == 0)
                    {
                        return PackResponseCommand(subcmd, 16, null);
                    }

                    byte[] packedBits = McProtocolHelper.TransBoolArrayToByteData(boolValues);
                    return PackResponseCommand(subcmd, 0, packedBits);
                }

                // 01: Baca Word (Read Word in Units of Words)
                if (subcmd == 1)
                {
                    if (length > 64)
                    {
                        return PackResponseCommand(subcmd, 16, null);
                    }

                    byte[] wordBytes = ReadBytes(addressStr, length);
                    if (wordBytes == null)
                    {
                        return PackResponseCommand(subcmd, 16, null);
                    }

                    return PackResponseCommand(subcmd, 0, wordBytes);
                }

                // 02: Tulis Bit (Write Bit in Units of Bits)
                if (subcmd == 2)
                {
                    if (!EnableWrite)
                    {
                        return PackResponseCommand(subcmd, 16, null);
                    }

                    int dataOffset = 12;
                    int dataLength = mcCore.Length - dataOffset;
                    byte[] rawPayload = new byte[dataLength];
                    Array.Copy(mcCore, dataOffset, rawPayload, 0, dataLength);

                    bool[] bitValues = McProtocolHelper.ExtractActualBitData(rawPayload);

                    int writeCount = length;
                    if (writeCount > bitValues.Length)
                    {
                        writeCount = bitValues.Length;
                    }

                    bool[] actualValues = new bool[writeCount];
                    Array.Copy(bitValues, 0, actualValues, 0, writeCount);

                    bool writeSuccess = WriteBool(addressStr, actualValues);
                    if (writeSuccess)
                    {
                        return PackResponseCommand(subcmd, 0, null);
                    }
                    else
                    {
                        return PackResponseCommand(subcmd, 16, null);
                    }
                }

                // 03: Tulis Word (Write Word in Units of Words)
                if (subcmd == 3)
                {
                    if (!EnableWrite)
                    {
                        return PackResponseCommand(subcmd, 16, null);
                    }

                    int dataOffset = 12;
                    int dataLength = mcCore.Length - dataOffset;
                    byte[] wordData = new byte[dataLength];
                    Array.Copy(mcCore, dataOffset, wordData, 0, dataLength);

                    bool writeSuccess = WriteBytes(addressStr, wordData);
                    if (writeSuccess)
                    {
                        return PackResponseCommand(subcmd, 0, null);
                    }
                    else
                    {
                        return PackResponseCommand(subcmd, 16, null);
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        // Mengemas respon format biner A1E: [Subcmd + 128, ErrorCode, Data...]
        private byte[] PackResponseCommand(byte subcmd, byte err, byte[] data)
        {
            int dataLength = 0;
            if (data != null)
            {
                dataLength = data.Length;
            }

            byte[] response = new byte[2 + dataLength];
            response[0] = (byte)(subcmd + 128);
            response[1] = err;

            if (data != null && data.Length > 0)
            {
                Array.Copy(data, 0, response, 2, data.Length);
            }

            return response;
        }

        #endregion

        #region ASCII A1E Core Processing

        /// <summary>
        /// Memproses paket frame teks ASCII MC-A1E.
        /// </summary>
        protected virtual byte[] ReadFromA1EAsciiCore(byte[] mcCore)
        {
            try
            {
                byte subcmd = Convert.ToByte(Encoding.ASCII.GetString(mcCore, 0, 2), 16);
                ushort dataCode = Convert.ToUInt16(Encoding.ASCII.GetString(mcCore, 8, 4), 16);
                int address = Convert.ToInt32(Encoding.ASCII.GetString(mcCore, 12, 8), 16);
                ushort length = Convert.ToUInt16(Encoding.ASCII.GetString(mcCore, 20, 2), 16);

                string addressStr = GetAddressFromDataCode(dataCode, address);
                if (string.IsNullOrEmpty(addressStr))
                {
                    return PackAsciiResponseCommand(subcmd, 16, null);
                }

                // 00: Baca Bit ASCII
                if (subcmd == 0)
                {
                    if (length == 0)
                    {
                        length = 256;
                    }

                    bool[] boolValues = ReadBoolArray(addressStr, length);
                    if (boolValues == null || boolValues.Length == 0)
                    {
                        return PackAsciiResponseCommand(subcmd, 16, null);
                    }

                    // Ubah boolean array menjadi string karakter '0' dan '1'
                    byte[] asciiResult = new byte[boolValues.Length];
                    for (int i = 0; i < boolValues.Length; i++)
                    {
                        if (boolValues[i])
                        {
                            asciiResult[i] = (byte)'1';
                        }
                        else
                        {
                            asciiResult[i] = (byte)'0';
                        }
                    }

                    return PackAsciiResponseCommand(subcmd, 0, asciiResult);
                }

                // 01: Baca Word ASCII
                if (subcmd == 1)
                {
                    byte[] wordBytes = ReadBytes(addressStr, length);
                    if (wordBytes == null)
                    {
                        return PackAsciiResponseCommand(subcmd, 16, null);
                    }

                    byte[] asciiWordBytes = McProtocolHelper.TransByteArrayToAsciiBytes(wordBytes);
                    return PackAsciiResponseCommand(subcmd, 0, asciiWordBytes);
                }

                // 02: Tulis Bit ASCII
                if (subcmd == 2)
                {
                    if (!EnableWrite)
                    {
                        return PackAsciiResponseCommand(subcmd, 16, null);
                    }

                    int payloadOffset = 24;
                    int count = length;
                    bool[] values = new bool[count];

                    for (int i = 0; i < count; i++)
                    {
                        if (payloadOffset + i < mcCore.Length)
                        {
                            if (mcCore[payloadOffset + i] == (byte)'1')
                            {
                                values[i] = true;
                            }
                            else
                            {
                                values[i] = false;
                            }
                        }
                    }

                    bool writeSuccess = WriteBool(addressStr, values);
                    if (writeSuccess)
                    {
                        return PackAsciiResponseCommand(subcmd, 0, null);
                    }
                    else
                    {
                        return PackAsciiResponseCommand(subcmd, 16, null);
                    }
                }

                // 03: Tulis Word ASCII
                if (subcmd == 3)
                {
                    if (!EnableWrite)
                    {
                        return PackAsciiResponseCommand(subcmd, 16, null);
                    }

                    int payloadOffset = 24;
                    int payloadLength = mcCore.Length - payloadOffset;
                    byte[] asciiPayload = new byte[payloadLength];
                    Array.Copy(mcCore, payloadOffset, asciiPayload, 0, payloadLength);

                    byte[] rawBytes = McProtocolHelper.TransAsciiBytesToByteArray(asciiPayload);
                    bool writeSuccess = WriteBytes(addressStr, rawBytes);
                    if (writeSuccess)
                    {
                        return PackAsciiResponseCommand(subcmd, 0, null);
                    }
                    else
                    {
                        return PackAsciiResponseCommand(subcmd, 16, null);
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        // Mengemas respon format ASCII A1E: [SubcmdHex, ErrorHex, DataHex...]
        private byte[] PackAsciiResponseCommand(byte subcmd, byte err, byte[] data)
        {
            string subcmdHex = (subcmd + 128).ToString("X2");
            string errHex = err.ToString("X2");

            int dataLength = 0;
            if (data != null)
            {
                dataLength = data.Length;
            }

            byte[] header = Encoding.ASCII.GetBytes(subcmdHex + errHex);
            byte[] response = new byte[4 + dataLength];

            Array.Copy(header, 0, response, 0, 4);

            if (data != null && data.Length > 0)
            {
                Array.Copy(data, 0, response, 4, data.Length);
            }

            return response;
        }

        #endregion

        #region Address Parsing by A1E DataCode

        // Mengubah kode register A1E (misal 17440) dan integer address menjadi string alamat (misal "D100")
        private string GetAddressFromDataCode(ushort dataCode, int address)
        {
            if (dataCode == MelsecA1EDataType.M.DataCode)
            {
                return "M" + address;
            }

            if (dataCode == MelsecA1EDataType.X.DataCode)
            {
                return "X" + address.ToString("X");
            }

            if (dataCode == MelsecA1EDataType.Y.DataCode)
            {
                return "Y" + address.ToString("X");
            }

            if (dataCode == MelsecA1EDataType.S.DataCode)
            {
                return "S" + address;
            }

            if (dataCode == MelsecA1EDataType.F.DataCode)
            {
                return "F" + address;
            }

            if (dataCode == MelsecA1EDataType.B.DataCode)
            {
                return "B" + address.ToString("X");
            }

            if (dataCode == MelsecA1EDataType.D.DataCode)
            {
                return "D" + address;
            }

            if (dataCode == MelsecA1EDataType.R.DataCode)
            {
                return "R" + address;
            }

            if (dataCode == MelsecA1EDataType.W.DataCode)
            {
                return "W" + address.ToString("X");
            }

            return string.Empty;
        }

        #endregion

        #region String Representation

        public override string ToString()
        {
            return string.Format("MelsecA1EServer[{0}]", Port);
        }

        #endregion
    }
}

