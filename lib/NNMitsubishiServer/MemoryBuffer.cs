using System;
using System.IO;

namespace NNMitsubishiServer
{
    /// <summary>
    /// Buffer memori internal thread-safe untuk menyimpan status bit dan word virtual PLC.
    /// Menggantikan SoftBuffer tanpa dependensi library eksternal.
    /// </summary>
    public class MemoryBuffer : IDisposable
    {
        #region Private Fields

        // Array byte penyimpanan data internal
        private byte[] _buffer;

        // Kapasitas buffer dalam byte
        private int _capacity;

        // Objek sinkronisasi thread-safety
        private readonly object _lockObject = new object();

        // Status dispose
        private bool _isDisposed;

        #endregion

        #region Public Properties

        /// <summary>
        /// Mendapatkan kapasitas buffer dalam satuan byte.
        /// </summary>
        public int Capacity
        {
            get
            {
                return _capacity;
            }
        }

        #endregion

        #region Constructor

        /// <summary>
        /// Inisialisasi buffer memori dengan kapasitas tertentu dalam satuan byte.
        /// </summary>
        public MemoryBuffer(int capacity)
        {
            if (capacity <= 0)
            {
                capacity = 65536;
            }

            _capacity = capacity;
            _buffer = new byte[capacity];
        }

        #endregion

        #region External Handler Hooks

        /// <summary>
        /// Handler baca eksternal. Jika di-set, GetBytes akan mengambil data dari sumber luar
        /// (misalnya memory bridge PLC) alih-alih dari buffer internal.
        /// Parameter: (destIndex, length) -> byte[].
        /// </summary>
        public Func<int, int, byte[]> ReadHandler { get; set; }

        /// <summary>
        /// Handler tulis eksternal. Jika di-set, SetBytes akan meneruskan data ke sumber luar
        /// (misalnya memory bridge PLC) alih-alih menyimpannya di buffer internal.
        /// Parameter: (destIndex, byte[] data).
        /// </summary>
        public Action<int, byte[]> WriteHandler { get; set; }

        #endregion

        #region Raw Byte Operations

        /// <summary>
        /// Membaca sejumlah byte dari indeks offset tertentu dalam buffer secara aman.
        /// </summary>
        public byte[] GetBytes(int destIndex, int length)
        {
            if (ReadHandler != null)
            {
                return ReadHandler(destIndex, length);
            }
            lock (_lockObject)
            {
                if (destIndex < 0)
                {
                    destIndex = 0;
                }

                if (length <= 0)
                {
                    return new byte[0];
                }

                // Cek batas kapasitas buffer
                if (destIndex >= _buffer.Length)
                {
                    return new byte[length];
                }

                int availableLength = _buffer.Length - destIndex;
                int copyLength = length;

                if (copyLength > availableLength)
                {
                    copyLength = availableLength;
                }

                byte[] result = new byte[length];
                Array.Copy(_buffer, destIndex, result, 0, copyLength);
                return result;
            }
        }

        /// <summary>
        /// Mendapatkan seluruh salinan buffer byte saat ini.
        /// </summary>
        public byte[] GetAllBytes()
        {
            lock (_lockObject)
            {
                byte[] copy = new byte[_buffer.Length];
                Array.Copy(_buffer, 0, copy, 0, _buffer.Length);
                return copy;
            }
        }

        /// <summary>
        /// Menulis array byte ke indeks offset tertentu dalam buffer secara aman.
        /// </summary>
        public void SetBytes(byte[] data, int destIndex)
        {
            if (WriteHandler != null)
            {
                WriteHandler(destIndex, data);
                return;
            }
            if (data == null || data.Length == 0)
            {
                return;
            }

            lock (_lockObject)
            {
                if (destIndex < 0 || destIndex >= _buffer.Length)
                {
                    return;
                }

                int availableLength = _buffer.Length - destIndex;
                int copyLength = data.Length;

                if (copyLength > availableLength)
                {
                    copyLength = availableLength;
                }

                Array.Copy(data, 0, _buffer, destIndex, copyLength);
            }
        }

        /// <summary>
        /// Menyalin sebagian byte dari array sumber ke dalam buffer.
        /// </summary>
        public void SetBytes(byte[] source, int sourceIndex, int destIndex, int length)
        {
            if (source == null || length <= 0)
            {
                return;
            }

            lock (_lockObject)
            {
                if (sourceIndex < 0 || sourceIndex >= source.Length)
                {
                    return;
                }

                if (destIndex < 0 || destIndex >= _buffer.Length)
                {
                    return;
                }

                int validLength = length;
                if (sourceIndex + validLength > source.Length)
                {
                    validLength = source.Length - sourceIndex;
                }

                if (destIndex + validLength > _buffer.Length)
                {
                    validLength = _buffer.Length - destIndex;
                }

                if (validLength > 0)
                {
                    Array.Copy(source, sourceIndex, _buffer, destIndex, validLength);
                }
            }
        }

        #endregion

        #region Bit in Word Operations (untuk D100.1, dsb.)

        /// <summary>
        /// Menulis nilai boolean ke bit tertentu di dalam buffer word.
        /// </summary>
        public void SetBool(bool[] values, int bitStartIndex)
        {
            if (values == null || values.Length == 0)
            {
                return;
            }

            lock (_lockObject)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    int currentBitIndex = bitStartIndex + i;
                    int byteIndex = currentBitIndex / 8;
                    int bitOffset = currentBitIndex % 8;

                    if (byteIndex >= 0 && byteIndex < _buffer.Length)
                    {
                        byte mask = (byte)(1 << bitOffset);

                        if (values[i])
                        {
                            _buffer[byteIndex] = (byte)(_buffer[byteIndex] | mask);
                        }
                        else
                        {
                            _buffer[byteIndex] = (byte)(_buffer[byteIndex] & ~mask);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Membaca array boolean dari bit-bit di dalam buffer word.
        /// </summary>
        public bool[] GetBool(int bitStartIndex, int length)
        {
            if (length <= 0)
            {
                return new bool[0];
            }

            bool[] result = new bool[length];

            lock (_lockObject)
            {
                for (int i = 0; i < length; i++)
                {
                    int currentBitIndex = bitStartIndex + i;
                    int byteIndex = currentBitIndex / 8;
                    int bitOffset = currentBitIndex % 8;

                    if (byteIndex >= 0 && byteIndex < _buffer.Length)
                    {
                        byte mask = (byte)(1 << bitOffset);
                        result[i] = (_buffer[byteIndex] & mask) != 0;
                    }
                    else
                    {
                        result[i] = false;
                    }
                }
            }

            return result;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _buffer = null;
                _isDisposed = true;
            }
        }

        #endregion
    }
}

