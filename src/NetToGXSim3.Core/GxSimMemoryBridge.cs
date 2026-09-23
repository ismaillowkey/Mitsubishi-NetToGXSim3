using System;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace NetToGXSim3.Core
{
    /// <summary>
    /// High-Speed Direct Memory-Mapped Bridge for Mitsubishi GX Simulator 3 (FX5U / iQ-F / iQ-R).
    /// Connects directly to the live simulator shared memory (RX600 CPU RAM & QXBUS ASIC)
    /// with zero latency, zero DLL locks, and zero interference to GX Works 3 online monitoring.
    /// </summary>
    public class GxSimMemoryBridge : IDisposable
    {
        // Device memory offsets within GXS3.SYS01.CPU01.RX600.RAM
        private const long OFFSET_D = 0x061A50;      // Data Registers D (Word) - D0 to D7999 (16,000 bytes = 8,000 words)
        private const long OFFSET_X_RAM = 0x066000;  // Input Relays X (Octal Bit) Base Table
        private const long OFFSET_X_FORCE = 0x066184;// Input Relays X Simulation/Force Latch Table
        private const long OFFSET_Y_RAM = 0x066204;  // Output Relays Y (Octal Bit) Base Table
        private const long OFFSET_Y_FORCE = 0x066388;// Output Relays Y Simulation/Force Latch Table
        private const long OFFSET_M = 0x066408;      // Internal Relays M (Decimal Bit) - M0 to M4095 (512 bytes)

        // ASIC I/O physical simulation registers
        private const long OFFSET_X_ASIC = 0x001510; // Physical Input Image X

        private MemoryMappedFile? _ramMmf;
        private MemoryMappedViewAccessor? _ram;
        private MemoryMappedFile? _asicMmf;
        private MemoryMappedViewAccessor? _asic;

        private bool _disposed;
        private string _processName = "FSimRun3 (FX5U)";
        private int _trackedProcessId = 0;

        public bool IsAttached => _ram != null && _ram.CanRead;
        public string TargetProcessName => _processName;

        public event Action<string>? LogMessage;

        public bool Attach(int sys = 1, int cpu = 1)
        {
            if (IsAttached) return true;

            try
            {
                // Verify any active simulator process is running
                Process? targetProc = null;
                var fsimProcs = Process.GetProcessesByName("FSimRun3");
                if (fsimProcs.Length > 0)
                {
                    targetProc = fsimProcs[0];
                    _processName = "FSimRun3 (FX5U/FX5UJ/FX5S)";
                }
                else
                {
                    var rsimProcs = Process.GetProcessesByName("RSimRun3");
                    if (rsimProcs.Length > 0)
                    {
                        targetProc = rsimProcs[0];
                        _processName = "RSimRun3 (iQ-R)";
                    }
                    else
                    {
                        var lsimProcs = Process.GetProcessesByName("LSimRun3");
                        if (lsimProcs.Length > 0)
                        {
                            targetProc = lsimProcs[0];
                            _processName = "LSimRun3 (L-Series)";
                        }
                        else
                        {
                            var dlgProcs = Process.GetProcessesByName("FSim3Dlg");
                            if (dlgProcs.Length > 0)
                            {
                                targetProc = dlgProcs[0];
                                _processName = "FSim3Dlg (FX5U Simulator)";
                            }
                            else
                            {
                                var sim3Procs = Process.GetProcessesByName("Sim3Dlg");
                                if (sim3Procs.Length > 0)
                                {
                                    targetProc = sim3Procs[0];
                                    _processName = "Sim3Dlg (GX Simulator 3)";
                                }
                            }
                        }
                    }
                }

                if (targetProc == null)
                {
                    return false;
                }

                _trackedProcessId = targetProc.Id;

                // 1. Open CPU RAM shared memory section
                string ramSectionName = string.Format("GXS3.SYS{0:D2}.CPU{1:D2}.RX600.RAM", sys, cpu);
                try
                {
                    _ramMmf = MemoryMappedFile.OpenExisting(ramSectionName);
                    _ram = _ramMmf.CreateViewAccessor();
                }
                catch
                {
                    // Fallback try without RX600
                    try
                    {
                        ramSectionName = string.Format("GXS3.SYS{0:D2}.CPU{1:D2}.RAM", sys, cpu);
                        _ramMmf = MemoryMappedFile.OpenExisting(ramSectionName);
                        _ram = _ramMmf.CreateViewAccessor();
                    }
                    catch { }
                }

                // 2. Open ASIC physical I/O shared memory section
                string asicSectionName = string.Format("GXS3.SYS{0:D2}.QXBUS.PHS00.ASIC", sys);
                try
                {
                    _asicMmf = MemoryMappedFile.OpenExisting(asicSectionName);
                    _asic = _asicMmf.CreateViewAccessor();
                }
                catch { }

                if (_ram != null && _ram.CanRead)
                {
                    LogMessage?.Invoke($"[DIRECT ENGINE] Attached directly to GX Simulator 3 shared memory ({ramSectionName})");
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"[DIRECT ENGINE] Attach exception: {ex.Message}");
            }

            Detach();
            return false;
        }

        public bool CheckIsAlive()
        {
            if (!IsAttached || _ram == null) return false;

            try
            {
                // 1. Verify tracked process or any GX Simulator 3 process is still running
                if (_trackedProcessId > 0)
                {
                    try
                    {
                        var proc = Process.GetProcessById(_trackedProcessId);
                        if (proc.HasExited)
                        {
                            Detach();
                            return false;
                        }
                    }
                    catch
                    {
                        Detach();
                        return false;
                    }
                }
                else
                {
                    var fsim = Process.GetProcessesByName("FSimRun3");
                    var dlg = Process.GetProcessesByName("FSim3Dlg");
                    var rsim = Process.GetProcessesByName("RSimRun3");
                    var lsim = Process.GetProcessesByName("LSimRun3");
                    var sim3 = Process.GetProcessesByName("Sim3Dlg");

                    if (fsim.Length == 0 && dlg.Length == 0 && rsim.Length == 0 && lsim.Length == 0 && sim3.Length == 0)
                    {
                        Detach();
                        return false;
                    }
                }

                // 2. Heartbeat memory read test
                if (_ram != null && _ram.CanRead)
                {
                    _ = _ram.ReadByte(OFFSET_D);
                    return true;
                }
            }
            catch
            {
                Detach();
                return false;
            }

            Detach();
            return false;
        }

        public void Detach()
        {
            try { _ram?.Dispose(); } catch { }
            try { _ramMmf?.Dispose(); } catch { }
            try { _asic?.Dispose(); } catch { }
            try { _asicMmf?.Dispose(); } catch { }

            _ram = null;
            _ramMmf = null;
            _asic = null;
            _asicMmf = null;
            _trackedProcessId = 0;
        }

        public int ReadDevice(string deviceName, out int value)
        {
            value = 0;
            if (!IsAttached || _ram == null) return -1;

            ParseDevice(deviceName, out string prefix, out int wordOffset, out int bitOffset);

            try
            {
                switch (prefix)
                {
                    case "X":
                        {
                            int bytePos = (wordOffset * 16 + bitOffset) / 8;
                            int bitPos = (wordOffset * 16 + bitOffset) % 8;
                            byte b = (byte)(_ram.ReadByte(OFFSET_X_RAM + bytePos) | _ram.ReadByte(OFFSET_X_FORCE + bytePos));
                            if (_asic != null)
                            {
                                b |= _asic.ReadByte(OFFSET_X_ASIC + bytePos);
                            }
                            value = (b >> bitPos) & 0x01;
                            return 0;
                        }
                    case "Y":
                        {
                            int bytePos = (wordOffset * 16 + bitOffset) / 8;
                            int bitPos = (wordOffset * 16 + bitOffset) % 8;
                            byte b = (byte)(_ram.ReadByte(OFFSET_Y_RAM + bytePos) | _ram.ReadByte(OFFSET_Y_FORCE + bytePos));
                            value = (b >> bitPos) & 0x01;
                            return 0;
                        }
                    case "M":
                    case "L":
                    case "F":
                        {
                            int bytePos = (wordOffset * 16 + bitOffset) / 8;
                            int bitPos = (wordOffset * 16 + bitOffset) % 8;
                            byte b = _ram.ReadByte(OFFSET_M + bytePos);
                            value = (b >> bitPos) & 0x01;
                            return 0;
                        }
                    case "D":
                    case "W":
                    default:
                        {
                            value = _ram.ReadInt16(OFFSET_D + wordOffset * 2);
                            return 0;
                        }
                }
            }
            catch
            {
                return -1;
            }
        }

        public int WriteDevice(string deviceName, int value)
        {
            if (!IsAttached || _ram == null) return -1;

            ParseDevice(deviceName, out string prefix, out int wordOffset, out int bitOffset);

            try
            {
                switch (prefix)
                {
                    case "X":
                        {
                            int bytePos = (wordOffset * 16 + bitOffset) / 8;
                            int bitPos = (wordOffset * 16 + bitOffset) % 8;

                            // 1. Update Base RAM Input Table
                            byte curRam = _ram.ReadByte(OFFSET_X_RAM + bytePos);
                            if (value != 0) curRam |= (byte)(1 << bitPos);
                            else curRam &= (byte)~(1 << bitPos);
                            _ram.Write(OFFSET_X_RAM + bytePos, curRam);

                            // 2. Update Force / Simulation Input Latch Table (Overcomes GX Works 3 Force ON locks)
                            byte curForce = _ram.ReadByte(OFFSET_X_FORCE + bytePos);
                            if (value != 0) curForce |= (byte)(1 << bitPos);
                            else curForce &= (byte)~(1 << bitPos);
                            _ram.Write(OFFSET_X_FORCE + bytePos, curForce);

                            // 3. Update ASIC Physical Input Image
                            if (_asic != null)
                            {
                                byte curAsic = _asic.ReadByte(OFFSET_X_ASIC + bytePos);
                                if (value != 0) curAsic |= (byte)(1 << bitPos);
                                else curAsic &= (byte)~(1 << bitPos);
                                _asic.Write(OFFSET_X_ASIC + bytePos, curAsic);
                            }
                            return 0;
                        }
                    case "Y":
                        {
                            int bytePos = (wordOffset * 16 + bitOffset) / 8;
                            int bitPos = (wordOffset * 16 + bitOffset) % 8;

                            // Update Base RAM
                            byte curRam = _ram.ReadByte(OFFSET_Y_RAM + bytePos);
                            if (value != 0) curRam |= (byte)(1 << bitPos);
                            else curRam &= (byte)~(1 << bitPos);
                            _ram.Write(OFFSET_Y_RAM + bytePos, curRam);

                            // Update Force Latch Table
                            byte curForce = _ram.ReadByte(OFFSET_Y_FORCE + bytePos);
                            if (value != 0) curForce |= (byte)(1 << bitPos);
                            else curForce &= (byte)~(1 << bitPos);
                            _ram.Write(OFFSET_Y_FORCE + bytePos, curForce);
                            return 0;
                        }
                    case "M":
                    case "L":
                    case "F":
                        {
                            int bytePos = (wordOffset * 16 + bitOffset) / 8;
                            int bitPos = (wordOffset * 16 + bitOffset) % 8;
                            byte cur = _ram.ReadByte(OFFSET_M + bytePos);
                            if (value != 0) cur |= (byte)(1 << bitPos);
                            else cur &= (byte)~(1 << bitPos);
                            _ram.Write(OFFSET_M + bytePos, cur);
                            return 0;
                        }
                    case "D":
                    case "W":
                    default:
                        {
                            _ram.Write(OFFSET_D + wordOffset * 2, (short)value);
                            return 0;
                        }
                }
            }
            catch
            {
                return -1;
            }
        }

        public int ReadDeviceBlockWords(string deviceName, int count, out short[] data)
        {
            data = new short[count];
            if (!IsAttached || _ram == null) return -1;

            ParseDevice(deviceName, out string prefix, out int wordOffset, out _);

            try
            {
                switch (prefix)
                {
                    case "X":
                        {
                            for (int i = 0; i < count; i++)
                            {
                                int wOff = wordOffset + i;
                                byte b0 = (byte)(_ram.ReadByte(OFFSET_X_RAM + wOff * 2) | _ram.ReadByte(OFFSET_X_FORCE + wOff * 2));
                                byte b1 = (byte)(_ram.ReadByte(OFFSET_X_RAM + wOff * 2 + 1) | _ram.ReadByte(OFFSET_X_FORCE + wOff * 2 + 1));
                                if (_asic != null)
                                {
                                    b0 |= _asic.ReadByte(OFFSET_X_ASIC + wOff * 2);
                                    b1 |= _asic.ReadByte(OFFSET_X_ASIC + wOff * 2 + 1);
                                }
                                data[i] = (short)(b0 | (b1 << 8));
                            }
                            return 0;
                        }
                    case "Y":
                        {
                            for (int i = 0; i < count; i++)
                            {
                                int wOff = wordOffset + i;
                                byte b0 = (byte)(_ram.ReadByte(OFFSET_Y_RAM + wOff * 2) | _ram.ReadByte(OFFSET_Y_FORCE + wOff * 2));
                                byte b1 = (byte)(_ram.ReadByte(OFFSET_Y_RAM + wOff * 2 + 1) | _ram.ReadByte(OFFSET_Y_FORCE + wOff * 2 + 1));
                                data[i] = (short)(b0 | (b1 << 8));
                            }
                            return 0;
                        }
                    case "M":
                    case "L":
                    case "F":
                        {
                            for (int i = 0; i < count; i++)
                            {
                                int wOff = wordOffset + i;
                                byte b0 = _ram.ReadByte(OFFSET_M + wOff * 2);
                                byte b1 = _ram.ReadByte(OFFSET_M + wOff * 2 + 1);
                                data[i] = (short)(b0 | (b1 << 8));
                            }
                            return 0;
                        }
                    case "D":
                    case "W":
                    default:
                        {
                            for (int i = 0; i < count; i++)
                            {
                                data[i] = _ram.ReadInt16(OFFSET_D + (wordOffset + i) * 2);
                            }
                            return 0;
                        }
                }
            }
            catch
            {
                return -1;
            }
        }

        public int WriteDeviceBlockWords(string deviceName, int count, short[] data)
        {
            if (!IsAttached || _ram == null || data == null) return -1;

            ParseDevice(deviceName, out string prefix, out int wordOffset, out _);

            try
            {
                switch (prefix)
                {
                    case "X":
                        {
                            for (int i = 0; i < count; i++)
                            {
                                int wOff = wordOffset + i;
                                byte b0 = (byte)(data[i] & 0xFF);
                                byte b1 = (byte)((data[i] >> 8) & 0xFF);
                                _ram.Write(OFFSET_X_RAM + wOff * 2, b0);
                                _ram.Write(OFFSET_X_RAM + wOff * 2 + 1, b1);
                                _ram.Write(OFFSET_X_FORCE + wOff * 2, b0);
                                _ram.Write(OFFSET_X_FORCE + wOff * 2 + 1, b1);
                                if (_asic != null)
                                {
                                    _asic.Write(OFFSET_X_ASIC + wOff * 2, b0);
                                    _asic.Write(OFFSET_X_ASIC + wOff * 2 + 1, b1);
                                }
                            }
                            return 0;
                        }
                    case "Y":
                        {
                            for (int i = 0; i < count; i++)
                            {
                                int wOff = wordOffset + i;
                                byte b0 = (byte)(data[i] & 0xFF);
                                byte b1 = (byte)((data[i] >> 8) & 0xFF);
                                _ram.Write(OFFSET_Y_RAM + wOff * 2, b0);
                                _ram.Write(OFFSET_Y_RAM + wOff * 2 + 1, b1);
                                _ram.Write(OFFSET_Y_FORCE + wOff * 2, b0);
                                _ram.Write(OFFSET_Y_FORCE + wOff * 2 + 1, b1);
                            }
                            return 0;
                        }
                    case "M":
                    case "L":
                    case "F":
                        {
                            for (int i = 0; i < count; i++)
                            {
                                int wOff = wordOffset + i;
                                byte b0 = (byte)(data[i] & 0xFF);
                                byte b1 = (byte)((data[i] >> 8) & 0xFF);
                                _ram.Write(OFFSET_M + wOff * 2, b0);
                                _ram.Write(OFFSET_M + wOff * 2 + 1, b1);
                            }
                            return 0;
                        }
                    case "D":
                    case "W":
                    default:
                        {
                            for (int i = 0; i < count; i++)
                            {
                                _ram.Write(OFFSET_D + (wordOffset + i) * 2, data[i]);
                            }
                            return 0;
                        }
                }
            }
            catch
            {
                return -1;
            }
        }

        public int ReadDeviceBlockBits(string deviceName, int count, out byte[] bitValues)
        {
            bitValues = new byte[count];
            if (!IsAttached || _ram == null) return -1;

            ParseDevice(deviceName, out string prefix, out int wordOffset, out int bitOffset);

            int startBit = wordOffset * 16 + bitOffset;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    int totalBit = startBit + i;
                    int bytePos = totalBit / 8;
                    int bitPos = totalBit % 8;

                    byte b = 0;
                    if (prefix == "X")
                    {
                        b = (byte)(_ram.ReadByte(OFFSET_X_RAM + bytePos) | _ram.ReadByte(OFFSET_X_FORCE + bytePos));
                        if (_asic != null) b |= _asic.ReadByte(OFFSET_X_ASIC + bytePos);
                    }
                    else if (prefix == "Y")
                    {
                        b = (byte)(_ram.ReadByte(OFFSET_Y_RAM + bytePos) | _ram.ReadByte(OFFSET_Y_FORCE + bytePos));
                    }
                    else
                    {
                        b = _ram.ReadByte(OFFSET_M + bytePos);
                    }

                    bitValues[i] = (byte)((b >> bitPos) & 0x01);
                }
                return 0;
            }
            catch
            {
                return -1;
            }
        }

        public int WriteDeviceBlockBits(string deviceName, int count, byte[] bitValues)
        {
            if (!IsAttached || _ram == null || bitValues == null) return -1;

            ParseDevice(deviceName, out string prefix, out int wordOffset, out int bitOffset);

            int startBit = wordOffset * 16 + bitOffset;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    int totalBit = startBit + i;
                    int bytePos = totalBit / 8;
                    int bitPos = totalBit % 8;

                    if (prefix == "X")
                    {
                        // 1. Base RAM Input Table
                        byte bRam = _ram.ReadByte(OFFSET_X_RAM + bytePos);
                        if (bitValues[i] != 0) bRam |= (byte)(1 << bitPos);
                        else bRam &= (byte)~(1 << bitPos);
                        _ram.Write(OFFSET_X_RAM + bytePos, bRam);

                        // 2. Force Input Latch Table
                        byte bForce = _ram.ReadByte(OFFSET_X_FORCE + bytePos);
                        if (bitValues[i] != 0) bForce |= (byte)(1 << bitPos);
                        else bForce &= (byte)~(1 << bitPos);
                        _ram.Write(OFFSET_X_FORCE + bytePos, bForce);

                        // 3. ASIC Physical Input Image
                        if (_asic != null)
                        {
                            byte bAsic = _asic.ReadByte(OFFSET_X_ASIC + bytePos);
                            if (bitValues[i] != 0) bAsic |= (byte)(1 << bitPos);
                            else bAsic &= (byte)~(1 << bitPos);
                            _asic.Write(OFFSET_X_ASIC + bytePos, bAsic);
                        }
                    }
                    else if (prefix == "Y")
                    {
                        // 1. Base RAM Output Table
                        byte bRam = _ram.ReadByte(OFFSET_Y_RAM + bytePos);
                        if (bitValues[i] != 0) bRam |= (byte)(1 << bitPos);
                        else bRam &= (byte)~(1 << bitPos);
                        _ram.Write(OFFSET_Y_RAM + bytePos, bRam);

                        // 2. Force Output Latch Table
                        byte bForce = _ram.ReadByte(OFFSET_Y_FORCE + bytePos);
                        if (bitValues[i] != 0) bForce |= (byte)(1 << bitPos);
                        else bForce &= (byte)~(1 << bitPos);
                        _ram.Write(OFFSET_Y_FORCE + bytePos, bForce);
                    }
                    else
                    {
                        byte b = _ram.ReadByte(OFFSET_M + bytePos);
                        if (bitValues[i] != 0) b |= (byte)(1 << bitPos);
                        else b &= (byte)~(1 << bitPos);
                        _ram.Write(OFFSET_M + bytePos, b);
                    }
                }
                return 0;
            }
            catch
            {
                return -1;
            }
        }

        private static void ParseDevice(string deviceName, out string prefix, out int wordOffset, out int bitOffset)
        {
            prefix = string.Empty;
            wordOffset = 0;
            bitOffset = 0;

            if (string.IsNullOrEmpty(deviceName)) return;

            prefix = deviceName.Substring(0, 1).ToUpperInvariant();
            if (deviceName.Length > 2 && char.IsLetter(deviceName[1]))
            {
                prefix = deviceName.Substring(0, 2).ToUpperInvariant();
            }

            string numPart = deviceName.Substring(prefix.Length);
            int addr = 0;

            switch (prefix)
            {
                case "X":
                case "Y":
                    try { addr = Convert.ToInt32(numPart, 8); } catch { addr = 0; }
                    wordOffset = addr / 16;
                    bitOffset = addr % 16;
                    break;
                case "M":
                case "L":
                case "F":
                    int.TryParse(numPart, out addr);
                    wordOffset = addr / 16;
                    bitOffset = addr % 16;
                    break;
                case "D":
                case "W":
                case "TS":
                case "TN":
                case "CS":
                case "CN":
                    int.TryParse(numPart, out addr);
                    wordOffset = addr;
                    bitOffset = 0;
                    break;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Detach();
        }
    }
}
