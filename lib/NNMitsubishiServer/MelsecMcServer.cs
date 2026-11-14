using System;

namespace NNMitsubishiServer
{
    /// <summary>
    /// Virtual Server MC Protocol Mitsubishi (Alias kelas untuk <see cref="Fx5uServer"/>).
    /// </summary>
    public class MelsecMcServer : Fx5uServer
    {
        /// <summary>
        /// Inisialisasi virtual server MC Protocol Mitsubishi baru.
        /// </summary>
        /// <param name="isBinary">True untuk format Binary Qna-3E frame (standar FX5U), False untuk format ASCII.</param>
        public MelsecMcServer(bool isBinary = true) : base(isBinary)
        {
        }

        /// <summary>
        /// Inisialisasi virtual server MC Protocol Mitsubishi dengan port tertentu.
        /// </summary>
        /// <param name="port">Port TCP listening server (default: 6000 atau 5002).</param>
        /// <param name="isBinary">True untuk format Binary Qna-3E frame, False untuk format ASCII.</param>
        public MelsecMcServer(int port = 6000, bool isBinary = true) : base(port, isBinary)
        {
        }
    }
}
