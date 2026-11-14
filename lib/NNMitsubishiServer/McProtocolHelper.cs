using System;
using System.Text;

namespace NNMitsubishiServer
{
    /// <summary>
    /// Helper utilitas untuk konversi frame, bit packing, dan encoding data MC Protocol Mitsubishi.
    /// 100% Native C# tanpa library eksternal.
    /// </summary>
    public static class McProtocolHelper
    {
        #region Bit & Byte Conversion

        /// <summary>
        /// Mengubah array boolean menjadi array byte dengan kompresi 2 bit per byte
        /// sesuai standar frame biner MC Protocol Mitsubishi (nibble atas = genap, nibble bawah = ganjil).
        /// </summary>
        public static byte[] TransBoolArrayToByteData(bool[] values)
        {
            if (values == null || values.Length == 0)
            {
                return new byte[0];
            }

            int byteCount = (values.Length + 1) / 2;
            byte[] result = new byte[byteCount];

            for (int i = 0; i < byteCount; i++)
            {
                int evenIndex = i * 2;
                int oddIndex = i * 2 + 1;

                // Bit genap disimpan di bit ke-4 (0x10)
                if (values[evenIndex])
                {
                    result[i] = (byte)(result[i] + 16);
                }

                // Bit ganjil disimpan di bit ke-0 (0x01)
                if (oddIndex < values.Length)
                {
                    if (values[oddIndex])
                    {
                        result[i] = (byte)(result[i] + 1);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Mengekstrak byte biner yang diterima dari perintah Write Bit
        /// menjadi array boolean (tiap byte berisi 2 nilai bit).
        /// </summary>
        public static bool[] ExtractActualBitData(byte[] response)
        {
            if (response == null || response.Length == 0)
            {
                return new bool[0];
            }

            bool[] result = new bool[response.Length * 2];

            for (int i = 0; i < response.Length; i++)
            {
                // Bit genap di bit ke-4
                if ((response[i] & 0x10) == 16)
                {
                    result[i * 2] = true;
                }
                else
                {
                    result[i * 2] = false;
                }

                // Bit ganjil di bit ke-0
                if ((response[i] & 0x01) == 1)
                {
                    result[i * 2 + 1] = true;
                }
                else
                {
                    result[i * 2 + 1] = false;
                }
            }

            return result;
        }

        /// <summary>
        /// Mengubah array boolean menjadi byte array di mana 1 byte mewakili 8 bit (Little Endian).
        /// </summary>
        public static byte[] BoolArrayToByteArray(bool[] values)
        {
            if (values == null || values.Length == 0)
            {
                return new byte[0];
            }

            int byteCount = (values.Length + 7) / 8;
            byte[] result = new byte[byteCount];

            for (int i = 0; i < values.Length; i++)
            {
                if (values[i])
                {
                    int byteIndex = i / 8;
                    int bitOffset = i % 8;
                    result[byteIndex] = (byte)(result[byteIndex] | (1 << bitOffset));
                }
            }

            return result;
        }

        /// <summary>
        /// Mengubah array byte menjadi array boolean di mana tiap byte diekspansi menjadi 8 bit.
        /// </summary>
        public static bool[] ByteArrayToBoolArray(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return new bool[0];
            }

            bool[] result = new bool[data.Length * 8];

            for (int i = 0; i < data.Length; i++)
            {
                for (int b = 0; b < 8; b++)
                {
                    int bitIndex = i * 8 + b;
                    byte mask = (byte)(1 << b);
                    result[bitIndex] = (data[i] & mask) != 0;
                }
            }

            return result;
        }

        #endregion

        #region ASCII & Hex Helpers

        /// <summary>
        /// Mengubah array byte menjadi string representasi heksadesimal huruf besar.
        /// </summary>
        public static string BytesToHexString(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder(data.Length * 2);
            for (int i = 0; i < data.Length; i++)
            {
                sb.Append(data[i].ToString("X2"));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Mengubah string heksadesimal menjadi array byte.
        /// </summary>
        public static byte[] HexStringToBytes(string hex)
        {
            if (string.IsNullOrEmpty(hex))
            {
                return new byte[0];
            }

            hex = hex.Replace(" ", "").Replace("-", "");

            if (hex.Length % 2 != 0)
            {
                hex = "0" + hex;
            }

            byte[] result = new byte[hex.Length / 2];

            for (int i = 0; i < result.Length; i++)
            {
                result[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }

            return result;
        }

        /// <summary>
        /// Mengubah array byte biner menjadi byte ASCII heksadesimal.
        /// Contoh: byte 0x12 -> byte ASCII '1' dan '2'.
        /// </summary>
        public static byte[] TransByteArrayToAsciiBytes(byte[] value)
        {
            if (value == null || value.Length == 0)
            {
                return new byte[0];
            }

            string hex = BytesToHexString(value);
            return Encoding.ASCII.GetBytes(hex);
        }

        /// <summary>
        /// Mengubah array byte ASCII heksadesimal menjadi byte biner asli.
        /// </summary>
        public static byte[] TransAsciiBytesToByteArray(byte[] asciiBytes)
        {
            if (asciiBytes == null || asciiBytes.Length == 0)
            {
                return new byte[0];
            }

            string hex = Encoding.ASCII.GetString(asciiBytes);
            return HexStringToBytes(hex);
        }

        #endregion
    }
}

