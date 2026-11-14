using System;

namespace NNMitsubishiServer
{
    /// <summary>
    /// Definisi tipe data register untuk MC Protocol A-1E Frame (A1E).
    /// Umumnya digunakan untuk komunikasi Ethernet PLC FX3U dengan modul FX3U-ENET / A-Series.
    /// </summary>
    public class MelsecA1EDataType
    {
        #region Static Device Definitions

        // X: Input Relay (Bit, Hexadecimal)
        public static readonly MelsecA1EDataType X = new MelsecA1EDataType(22560, 1, "X*", 16);

        // Y: Output Relay (Bit, Hexadecimal)
        public static readonly MelsecA1EDataType Y = new MelsecA1EDataType(22816, 1, "Y*", 16);

        // M: Internal Relay (Bit, Decimal)
        public static readonly MelsecA1EDataType M = new MelsecA1EDataType(19744, 1, "M*", 10);

        // S: Step Relay (Bit, Decimal)
        public static readonly MelsecA1EDataType S = new MelsecA1EDataType(21280, 1, "S*", 10);

        // F: Annunciator (Bit, Decimal)
        public static readonly MelsecA1EDataType F = new MelsecA1EDataType(17952, 1, "F*", 10);

        // B: Link Relay (Bit, Hexadecimal)
        public static readonly MelsecA1EDataType B = new MelsecA1EDataType(16928, 1, "B*", 16);

        // D: Data Register (Word, Decimal)
        public static readonly MelsecA1EDataType D = new MelsecA1EDataType(17440, 0, "D*", 10);

        // W: Link Register (Word, Hexadecimal)
        public static readonly MelsecA1EDataType W = new MelsecA1EDataType(22304, 0, "W*", 16);

        // R: File Register (Word, Decimal)
        public static readonly MelsecA1EDataType R = new MelsecA1EDataType(21024, 0, "R*", 10);

        // TS: Timer Contact (Bit, Decimal)
        public static readonly MelsecA1EDataType TS = new MelsecA1EDataType(21587, 1, "TS", 10);

        // TC: Timer Coil (Bit, Decimal)
        public static readonly MelsecA1EDataType TC = new MelsecA1EDataType(21571, 1, "TC", 10);

        // TN: Timer Current Value (Word, Decimal)
        public static readonly MelsecA1EDataType TN = new MelsecA1EDataType(21582, 0, "TN", 10);

        // CS: Counter Contact (Bit, Decimal)
        public static readonly MelsecA1EDataType CS = new MelsecA1EDataType(17235, 1, "CS", 10);

        // CC: Counter Coil (Bit, Decimal)
        public static readonly MelsecA1EDataType CC = new MelsecA1EDataType(17219, 1, "CC", 10);

        // CN: Counter Current Value (Word, Decimal)
        public static readonly MelsecA1EDataType CN = new MelsecA1EDataType(17230, 0, "CN", 10);

        #endregion

        #region Public Properties

        /// <summary>
        /// Kode numerik register untuk frame A1E (contoh: D = 0x4420 = 17440).
        /// </summary>
        public ushort DataCode { get; private set; }

        /// <summary>
        /// 0 untuk Word, 1 untuk Bit.
        /// </summary>
        public byte DataType { get; private set; }

        /// <summary>
        /// Deskripsi kode ASCII.
        /// </summary>
        public string AsciiCode { get; private set; }

        /// <summary>
        /// Basis format penomoran (10 desimal, 16 heksadesimal).
        /// </summary>
        public int FromBase { get; private set; }

        #endregion

        #region Constructor

        public MelsecA1EDataType(ushort dataCode, byte dataType, string asciiCode, int fromBase)
        {
            DataCode = dataCode;
            DataType = dataType;
            AsciiCode = asciiCode;
            FromBase = fromBase;
        }

        #endregion
    }
}

