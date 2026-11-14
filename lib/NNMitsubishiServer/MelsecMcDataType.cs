using System;

namespace NNMitsubishiServer
{
    /// <summary>
    /// Definisi tipe data register untuk PLC Mitsubishi MC Protocol.
    /// Menyimpan kode biner (DataCode), kode ASCII, tipe register (Bit/Word), dan basis bilangan (10/16).
    /// </summary>
    public class MelsecMcDataType
    {
        #region Static Device Definitions

        // X: Input Relay (Bit, Hexadecimal)
        public static readonly MelsecMcDataType X = new MelsecMcDataType(156, 1, "X*", 16);

        // Y: Output Relay (Bit, Hexadecimal)
        public static readonly MelsecMcDataType Y = new MelsecMcDataType(157, 1, "Y*", 16);

        // M: Internal Relay (Bit, Decimal)
        public static readonly MelsecMcDataType M = new MelsecMcDataType(144, 1, "M*", 10);

        // SM: Special Relay (Bit, Decimal)
        public static readonly MelsecMcDataType SM = new MelsecMcDataType(145, 1, "SM", 10);

        // L: Latch Relay (Bit, Decimal)
        public static readonly MelsecMcDataType L = new MelsecMcDataType(146, 1, "L*", 10);

        // F: Annunciator (Bit, Decimal)
        public static readonly MelsecMcDataType F = new MelsecMcDataType(147, 1, "F*", 10);

        // V: Edge Relay (Bit, Decimal)
        public static readonly MelsecMcDataType V = new MelsecMcDataType(148, 1, "V*", 10);

        // B: Link Relay (Bit, Hexadecimal)
        public static readonly MelsecMcDataType B = new MelsecMcDataType(160, 1, "B*", 16);

        // S: Step Relay (Bit, Decimal)
        public static readonly MelsecMcDataType S = new MelsecMcDataType(152, 1, "S*", 10);

        // D: Data Register (Word, Decimal)
        public static readonly MelsecMcDataType D = new MelsecMcDataType(168, 0, "D*", 10);

        // SD: Special Data Register (Word, Decimal)
        public static readonly MelsecMcDataType SD = new MelsecMcDataType(169, 0, "SD", 10);

        // W: Link Register (Word, Hexadecimal)
        public static readonly MelsecMcDataType W = new MelsecMcDataType(180, 0, "W*", 16);

        // SW: Special Link Register (Word, Hexadecimal)
        public static readonly MelsecMcDataType SW = new MelsecMcDataType(181, 0, "SW", 16);

        // R: File Register (Word, Decimal)
        public static readonly MelsecMcDataType R = new MelsecMcDataType(175, 0, "R*", 10);

        // ZR: Extended File Register (Word, Hexadecimal)
        public static readonly MelsecMcDataType ZR = new MelsecMcDataType(176, 0, "ZR", 16);

        #endregion

        #region Public Properties

        /// <summary>
        /// Kode numerik device dalam format frame biner MC Protocol.
        /// </summary>
        public ushort DataCode { get; private set; }

        /// <summary>
        /// Jenis tipe data: 0 untuk Word, 1 untuk Bit.
        /// </summary>
        public byte DataType { get; private set; }

        /// <summary>
        /// Representasi kode 2-karakter ASCII untuk frame ASCII MC Protocol.
        /// </summary>
        public string AsciiCode { get; private set; }

        /// <summary>
        /// Basis format penomoran alamat (10 untuk desimal, 16 untuk heksadesimal).
        /// </summary>
        public int FromBase { get; private set; }

        #endregion

        #region Constructor

        /// <summary>
        /// Konstruktor inisialisasi tipe data register.
        /// </summary>
        public MelsecMcDataType(ushort dataCode, byte dataType, string asciiCode, int fromBase)
        {
            DataCode = dataCode;
            DataType = dataType;
            AsciiCode = asciiCode;
            FromBase = fromBase;
        }

        #endregion
    }
}

