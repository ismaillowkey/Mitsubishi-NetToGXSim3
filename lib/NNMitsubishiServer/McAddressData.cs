using System;

namespace NNMitsubishiServer
{
    /// <summary>
    /// Hasil parsing informasi alamat register Mitsubishi MC Protocol.
    /// Memuat tipe data register, alamat awal (integer), dan panjang data.
    /// </summary>
    public class McAddressData
    {
        #region Public Properties

        /// <summary>
        /// Tipe register Mitsubishi (D, M, X, Y, W, dll.).
        /// </summary>
        public MelsecMcDataType DataType { get; set; }

        /// <summary>
        /// Offset alamat awal register (misal D100 -> 100).
        /// </summary>
        public int AddressStart { get; set; }

        /// <summary>
        /// Indeks bit khusus jika mengakses bit dari word register (misal D100.2 -> 2, selain itu -1).
        /// </summary>
        public int BitIndex { get; set; } = -1;

        /// <summary>
        /// Panjang elemen data.
        /// </summary>
        public ushort Length { get; set; }

        #endregion

        #region Address Parsing Logic

        /// <summary>
        /// Mengurai string alamat register PLC Mitsubishi menjadi objek McAddressData.
        /// Contoh: "D100", "M10", "X0", "Y10", "W1A", "D100.1".
        /// </summary>
        /// <param name="address">String alamat PLC</param>
        /// <param name="length">Panjang elemen</param>
        /// <param name="isBit">True jika operasi bit, False jika word</param>
        /// <returns>Objek McAddressData atau null jika format tidak valid</returns>
        public static McAddressData Parse(string address, ushort length, bool isBit)
        {
            if (string.IsNullOrEmpty(address))
            {
                return null;
            }

            address = address.Trim().ToUpper();

            int bitIndex = -1;

            // Cek apakah ada notasi bit dot (misal D100.1)
            int dotIndex = address.IndexOf('.');
            if (dotIndex > 0)
            {
                string bitPart = address.Substring(dotIndex + 1);
                address = address.Substring(0, dotIndex);

                // Konversi bit index (biasanya hex atau desimal 0-15)
                if (int.TryParse(bitPart, System.Globalization.NumberStyles.HexNumber, null, out int parsedBit))
                {
                    bitIndex = parsedBit;
                }
            }

            McAddressData result = new McAddressData();
            result.Length = length;
            result.BitIndex = bitIndex;

            // Identifikasi prefix tipe device
            if (address.StartsWith("SM"))
            {
                result.DataType = MelsecMcDataType.SM;
                result.AddressStart = ParseAddressNumber(address.Substring(2), 10);
            }
            else if (address.StartsWith("SD"))
            {
                result.DataType = MelsecMcDataType.SD;
                result.AddressStart = ParseAddressNumber(address.Substring(2), 10);
            }
            else if (address.StartsWith("SW"))
            {
                result.DataType = MelsecMcDataType.SW;
                result.AddressStart = ParseAddressNumber(address.Substring(2), 16);
            }
            else if (address.StartsWith("ZR"))
            {
                result.DataType = MelsecMcDataType.ZR;
                result.AddressStart = ParseAddressNumber(address.Substring(2), 16);
            }
            else if (address.StartsWith("D"))
            {
                result.DataType = MelsecMcDataType.D;
                result.AddressStart = ParseAddressNumber(address.Substring(1), 10);
            }
            else if (address.StartsWith("W"))
            {
                result.DataType = MelsecMcDataType.W;
                result.AddressStart = ParseAddressNumber(address.Substring(1), 16);
            }
            else if (address.StartsWith("R"))
            {
                result.DataType = MelsecMcDataType.R;
                result.AddressStart = ParseAddressNumber(address.Substring(1), 10);
            }
            else if (address.StartsWith("M"))
            {
                result.DataType = MelsecMcDataType.M;
                result.AddressStart = ParseAddressNumber(address.Substring(1), 10);
            }
            else if (address.StartsWith("X"))
            {
                result.DataType = MelsecMcDataType.X;
                result.AddressStart = ParseAddressNumber(address.Substring(1), 16);
            }
            else if (address.StartsWith("Y"))
            {
                result.DataType = MelsecMcDataType.Y;
                result.AddressStart = ParseAddressNumber(address.Substring(1), 16);
            }
            else if (address.StartsWith("L"))
            {
                result.DataType = MelsecMcDataType.L;
                result.AddressStart = ParseAddressNumber(address.Substring(1), 10);
            }
            else if (address.StartsWith("B"))
            {
                result.DataType = MelsecMcDataType.B;
                result.AddressStart = ParseAddressNumber(address.Substring(1), 16);
            }
            else if (address.StartsWith("S"))
            {
                result.DataType = MelsecMcDataType.S;
                result.AddressStart = ParseAddressNumber(address.Substring(1), 10);
            }
            else if (address.StartsWith("F"))
            {
                result.DataType = MelsecMcDataType.F;
                result.AddressStart = ParseAddressNumber(address.Substring(1), 10);
            }
            else
            {
                // Format prefix tidak dikenali
                return null;
            }

            return result;
        }

        // Helper untuk parse angka alamat sesuai basis bilangan (10 atau 16)
        private static int ParseAddressNumber(string numberStr, int fromBase)
        {
            if (string.IsNullOrEmpty(numberStr))
            {
                return 0;
            }

            if (fromBase == 16)
            {
                return Convert.ToInt32(numberStr, 16);
            }
            else
            {
                return Convert.ToInt32(numberStr, 10);
            }
        }

        #endregion
    }
}

