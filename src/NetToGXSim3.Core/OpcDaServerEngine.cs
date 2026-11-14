#nullable enable
#pragma warning disable CS8600, CS8601, CS8604, CS8767, CS8625
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;

namespace NetToGXSim3.Core
{
    public class OpcDaServerEngine : IClassFactory, IOPCServer, IOPCCommon, IOPCBrowseServerAddressSpace, IOPCItemProperties, IDisposable
    {
        private static OpcDaServerEngine? _instance;
        public static OpcDaServerEngine? Instance => _instance;

        private readonly GxSimulatorEngine? _engine;
        private int _comCookie = 0;
        private Timer? _scanTimer;
        private bool _isRunning = false;
        private DateTime _startTime = DateTime.UtcNow;
        private readonly List<OpcGroupInstance> _groups = new List<OpcGroupInstance>();
        private readonly object _lock = new object();
        private int _nextGroupHandle = 1;
        private int _nextItemHandle = 100;

        public ObservableCollection<OpcTagItem> Tags { get; } = new ObservableCollection<OpcTagItem>();

        public bool IsRunning => _isRunning;
        public int ClientCount => _groups.Count;
        public string StoragePath { get; private set; } = "opc_tags.json";

        public event Action<string>? OnLog;
        public event Action? OnStateChanged;

        public OpcDaServerEngine(GxSimulatorEngine? engine = null)
        {
            _instance = this;
            _engine = engine;
            DetermineStoragePath();
            LoadTags();
        }

        private void DetermineStoragePath()
        {
            try
            {
                string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "opc_tags.json");
                // Test write permissions
                using (var fs = File.Open(localPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite)) { }
                StoragePath = localPath;
            }
            catch
            {
                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetToGXSim3");
                if (!Directory.Exists(appData)) Directory.CreateDirectory(appData);
                StoragePath = Path.Combine(appData, "opc_tags.json");
            }
        }

        public void StartServer()
        {
            if (_isRunning) return;

            try
            {
                // Ensure COM registration exists in Windows
                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
                OpcRegistryHelper.RegisterServer(exePath, out _);

                // Register COM Class Factory
                Guid clsid = OpcDaConstants.CLSID_NetToGXSim3OpcDa;
                int hr = OpcRegistryHelper.CoRegisterClassObject(
                    ref clsid,
                    this,
                    OpcRegistryHelper.CLSCTX_LOCAL_SERVER | OpcRegistryHelper.CLSCTX_REMOTE_SERVER,
                    OpcRegistryHelper.REGCLS_MULTIPLEUSE,
                    out _comCookie);

                _startTime = DateTime.UtcNow;
                _isRunning = true;

                // Start live memory scan cycler (every 40ms for high responsiveness)
                _scanTimer = new Timer(ScanLoop, null, 40, 40);

                Log($"OPC DA Server started. ProgID: {OpcDaConstants.ProgId} (HR: 0x{hr:X8})");
                OnStateChanged?.Invoke();
            }
            catch (Exception ex)
            {
                Log($"Failed to start OPC DA Server: {ex.Message}");
            }
        }

        public void StopServer()
        {
            if (!_isRunning) return;

            try
            {
                _scanTimer?.Dispose();
                _scanTimer = null;

                if (_comCookie != 0)
                {
                    OpcRegistryHelper.CoRevokeClassObject(_comCookie);
                    _comCookie = 0;
                }

                _isRunning = false;
                Log("OPC DA Server stopped.");
                OnStateChanged?.Invoke();
            }
            catch (Exception ex)
            {
                Log($"Error stopping OPC Server: {ex.Message}");
            }
        }

        private void ScanLoop(object? state)
        {
            try
            {
                bool isPlcConnected = _engine != null && _engine.IsConnected;

                lock (_lock)
                {
                    DateTime now = DateTime.Now;

                    foreach (var tag in Tags)
                    {
                        if (!tag.IsActive) continue;

                        if (!isPlcConnected)
                        {
                            tag.Quality = "Offline";
                            tag.QualityCode = OpcDaConstants.OPC_QUALITY_BAD;
                            tag.Value = "---";
                            tag.Timestamp = now;
                        }
                        else
                        {
                            try
                            {
                                object? liveVal = ReadTagFromPlc(tag);
                                if (liveVal != null)
                                {
                                    tag.Value = liveVal;
                                    tag.Quality = "Good";
                                    tag.QualityCode = OpcDaConstants.OPC_QUALITY_GOOD;
                                    tag.Timestamp = now;
                                }
                                else
                                {
                                    tag.Quality = "Offline";
                                    tag.QualityCode = OpcDaConstants.OPC_QUALITY_BAD;
                                    tag.Timestamp = now;
                                }
                            }
                            catch
                            {
                                tag.Quality = "Bad";
                                tag.QualityCode = OpcDaConstants.OPC_QUALITY_BAD;
                                tag.Timestamp = now;
                            }
                        }
                    }

                    // Trigger live callbacks to active OPC client groups
                    DateTime nowUtc = DateTime.UtcNow;
                    foreach (var group in _groups)
                    {
                        if (group.IsActive && (nowUtc - group.LastCallbackTime).TotalMilliseconds >= Math.Max(50, group.UpdateRate))
                        {
                            group.LastCallbackTime = nowUtc;
                            var g = group;
                            ThreadPool.QueueUserWorkItem(_ => g.SendNotification(0, isRefresh: false));
                        }
                    }
                }
            }
            catch { }
        }

        #region Mitsubishi Address Parsing & Direct Memory Access

        public static bool TryParseMitsubishiAddress(string rawAddress, out string deviceType, out int address, out int bitIndex)
        {
            deviceType = "D";
            address = 0;
            bitIndex = -1;

            if (string.IsNullOrWhiteSpace(rawAddress)) return false;

            string s = rawAddress.Trim().ToUpperInvariant();

            // Strip client prefix hierarchy if present
            if (s.StartsWith("MITSUBISHI.", StringComparison.OrdinalIgnoreCase)) s = s.Substring(11);
            else if (s.StartsWith("MITSUBISHI/", StringComparison.OrdinalIgnoreCase)) s = s.Substring(11);
            else if (s.StartsWith("MITSUBISHI\\", StringComparison.OrdinalIgnoreCase)) s = s.Substring(11);
            else if (s.StartsWith("FX.", StringComparison.OrdinalIgnoreCase)) s = s.Substring(3);
            else if (s.StartsWith("NETTOGXSIM.", StringComparison.OrdinalIgnoreCase)) s = s.Substring(11);

            // Check for bit-of-word dot notation (e.g., D0.0, D10.15, R0.5)
            int dotIdx = s.IndexOf('.');
            if (dotIdx > 0 && dotIdx < s.Length - 1)
            {
                string wordPart = s.Substring(0, dotIdx);
                string bitPart = s.Substring(dotIdx + 1);

                if (!ParseBaseDeviceAndNumber(wordPart, out deviceType, out address)) return false;

                // Bit devices cannot have dot-bit notation
                if (deviceType == "X" || deviceType == "Y" || deviceType == "PY" ||
                    deviceType == "M" || deviceType == "PM" || deviceType == "S" ||
                    deviceType == "SM" || deviceType == "PSM" ||
                    deviceType == "TS" || deviceType == "TC" || deviceType == "TI" || deviceType == "TO" ||
                    deviceType == "CS" || deviceType == "CC" || deviceType == "CI" || deviceType == "CO" ||
                    deviceType == "B")
                {
                    return false;
                }

                if (int.TryParse(bitPart, out int bIdx) && bIdx >= 0 && bIdx <= 15)
                {
                    bitIndex = bIdx;
                    return true;
                }
                // Also allow hex bit index 0-F
                try
                {
                    int hexBit = Convert.ToInt32(bitPart, 16);
                    if (hexBit >= 0 && hexBit <= 15)
                    {
                        bitIndex = hexBit;
                        return true;
                    }
                }
                catch { }

                return false;
            }

            return ParseBaseDeviceAndNumber(s, out deviceType, out address);
        }

        private static bool ParseBaseDeviceAndNumber(string s, out string deviceType, out int address)
        {
            deviceType = "D";
            address = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;

            // Match known multi-character prefixes first
            if (s.StartsWith("CHN")) deviceType = "CHN";
            else if (s.StartsWith("PSM")) deviceType = "PSM";
            else if (s.StartsWith("PM")) deviceType = "PM";
            else if (s.StartsWith("PY")) deviceType = "PY";
            else if (s.StartsWith("SM")) deviceType = "SM";
            else if (s.StartsWith("SD")) deviceType = "SD";
            else if (s.StartsWith("TS")) deviceType = "TS";
            else if (s.StartsWith("TC")) deviceType = "TC";
            else if (s.StartsWith("TN")) deviceType = "TN";
            else if (s.StartsWith("TI")) deviceType = "TI";
            else if (s.StartsWith("TO")) deviceType = "TO";
            else if (s.StartsWith("CS")) deviceType = "CS";
            else if (s.StartsWith("CC")) deviceType = "CC";
            else if (s.StartsWith("CN")) deviceType = "CN";
            else if (s.StartsWith("CI")) deviceType = "CI";
            else if (s.StartsWith("CO")) deviceType = "CO";
            else if (s.StartsWith("ZR")) deviceType = "ZR";
            else if (s.StartsWith("X")) deviceType = "X";
            else if (s.StartsWith("Y")) deviceType = "Y";
            else if (s.StartsWith("M")) deviceType = "M";
            else if (s.StartsWith("S")) deviceType = "S";
            else if (s.StartsWith("D")) deviceType = "D";
            else if (s.StartsWith("R")) deviceType = "R";
            else if (s.StartsWith("T")) deviceType = "T";
            else if (s.StartsWith("C")) deviceType = "C";
            else if (s.StartsWith("W")) deviceType = "W";
            else if (s.StartsWith("B")) deviceType = "B";
            else return false;

            string rest = s.Substring(deviceType.Length);
            if (string.IsNullOrEmpty(rest)) return false;

            if (deviceType == "W" || deviceType == "B")
            {
                // Link registers W and Link relays B use Hexadecimal addressing
                foreach (char c in rest)
                {
                    if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f')))
                        return false;
                }
                try
                {
                    address = Convert.ToInt32(rest, 16);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            else if (deviceType == "X" || deviceType == "Y" || deviceType == "PY")
            {
                // Octal addressing (digits 0-7 only)
                foreach (char c in rest)
                {
                    if (c < '0' || c > '7') return false;
                }
                return int.TryParse(rest, out address);
            }
            else
            {
                // Decimal address string (digits 0-9 only)
                foreach (char c in rest)
                {
                    if (c < '0' || c > '9') return false;
                }
                return int.TryParse(rest, out address);
            }
        }

        public object? ReadTagFromPlc(OpcTagItem tag)
        {
            if (_engine == null || !_engine.IsConnected) return null;

            if (!TryParseMitsubishiAddress(tag.Address, out string devType, out int addr, out int bitIdx))
            {
                if (_engine.ReadDevice(tag.Address, out int rawVal) == 0) return rawVal;
                return null;
            }

            var bridge = _engine.MemoryBridge;

            switch (tag.DataType)
            {
                case OpcDataType.Bool:
                    if (bitIdx >= 0)
                    {
                        if (bridge.ReadWord(devType, addr, out ushort wVal))
                        {
                            return (wVal & (1 << bitIdx)) != 0;
                        }
                        return null;
                    }

                    string bitDev = devType;
                    if (devType == "T") bitDev = "TS";
                    else if (devType == "C") bitDev = "CS";

                    if (bridge.ReadBit(bitDev, addr, out bool bVal))
                    {
                        return bVal;
                    }
                    return null;

                case OpcDataType.Int16:
                    string wordDev = devType;
                    if (devType == "T") wordDev = "TN";
                    else if (devType == "C") wordDev = "CN";

                    if (bridge.ReadWord(wordDev, addr, out ushort w16))
                    {
                        return (short)w16;
                    }
                    return null;

                case OpcDataType.UInt16:
                    string uwordDev = devType;
                    if (devType == "T") uwordDev = "TN";
                    else if (devType == "C") uwordDev = "CN";

                    if (bridge.ReadWord(uwordDev, addr, out ushort uw16))
                    {
                        return uw16;
                    }
                    return null;

                case OpcDataType.Int32:
                    if ((devType == "C" || devType == "CN") && addr >= 200)
                    {
                        if (bridge.ReadDWord("C", addr, out uint dwVal)) return (int)dwVal;
                        return null;
                    }
                    if (devType == "CHN")
                    {
                        if (bridge.ReadDWord("CHN", addr, out uint dwVal)) return (int)dwVal;
                        return null;
                    }

                    if (bridge.ReadWord(devType, addr, out ushort low32) &&
                        bridge.ReadWord(devType, addr + 1, out ushort high32))
                    {
                        return (int)(low32 | (high32 << 16));
                    }
                    return null;

                case OpcDataType.UInt32:
                    if ((devType == "C" || devType == "CN") && addr >= 200)
                    {
                        if (bridge.ReadDWord("C", addr, out uint dwVal)) return dwVal;
                        return null;
                    }
                    if (devType == "CHN")
                    {
                        if (bridge.ReadDWord("CHN", addr, out uint dwVal)) return dwVal;
                        return null;
                    }

                    if (bridge.ReadWord(devType, addr, out ushort ulow32) &&
                        bridge.ReadWord(devType, addr + 1, out ushort uhigh32))
                    {
                        return (uint)(ulow32 | (uhigh32 << 16));
                    }
                    return null;

                case OpcDataType.Float:
                    if (bridge.ReadWord(devType, addr, out ushort fLow) &&
                        bridge.ReadWord(devType, addr + 1, out ushort fHigh))
                    {
                        byte[] bytes = new byte[4];
                        bytes[0] = (byte)(fLow & 0xFF);
                        bytes[1] = (byte)((fLow >> 8) & 0xFF);
                        bytes[2] = (byte)(fHigh & 0xFF);
                        bytes[3] = (byte)((fHigh >> 8) & 0xFF);
                        return BitConverter.ToSingle(bytes, 0);
                    }
                    return null;

                default:
                    if (bridge.ReadWord(devType, addr, out ushort defaultVal)) return defaultVal;
                    return null;
            }
        }

        public bool WriteTagToPlc(OpcTagItem tag, object value)
        {
            if (_engine == null || !_engine.IsConnected) return false;

            if (!TryParseMitsubishiAddress(tag.Address, out string devType, out int addr, out int bitIdx))
            {
                if (int.TryParse(value?.ToString(), out int numVal))
                {
                    return _engine.WriteDevice(tag.Address, numVal) == 0;
                }
                return false;
            }

            var bridge = _engine.MemoryBridge;

            try
            {
                switch (tag.DataType)
                {
                    case OpcDataType.Bool:
                        bool b = Convert.ToBoolean(value);
                        if (bitIdx >= 0)
                        {
                            if (bridge.ReadWord(devType, addr, out ushort currentWord))
                            {
                                if (b) currentWord |= (ushort)(1 << bitIdx);
                                else currentWord &= (ushort)~(1 << bitIdx);

                                bool ok = bridge.WriteWord(devType, addr, currentWord);
                                if (ok) tag.Value = b;
                                return ok;
                            }
                            return false;
                        }

                        string bitDev = devType;
                        if (devType == "T") bitDev = "TS";
                        else if (devType == "C") bitDev = "CS";

                        bool resB = bridge.WriteBit(bitDev, addr, b);
                        if (resB) tag.Value = b;
                        return resB;

                    case OpcDataType.Int16:
                        short s = Convert.ToInt16(value);
                        string wordDev = devType;
                        if (devType == "T") wordDev = "TN";
                        else if (devType == "C") wordDev = "CN";

                        bool resS = bridge.WriteWord(wordDev, addr, (ushort)s);
                        if (resS) tag.Value = s;
                        return resS;

                    case OpcDataType.UInt16:
                        ushort u = Convert.ToUInt16(value);
                        string uwordDev = devType;
                        if (devType == "T") uwordDev = "TN";
                        else if (devType == "C") uwordDev = "CN";

                        bool resU = bridge.WriteWord(uwordDev, addr, u);
                        if (resU) tag.Value = u;
                        return resU;

                    case OpcDataType.Int32:
                        int iVal = Convert.ToInt32(value);
                        if ((devType == "C" || devType == "CN") && addr >= 200)
                        {
                            bool okC = bridge.WriteDWord("C", addr, (uint)iVal);
                            if (okC) tag.Value = iVal;
                            return okC;
                        }
                        if (devType == "CHN")
                        {
                            bool okChn = bridge.WriteDWord("CHN", addr, (uint)iVal);
                            if (okChn) tag.Value = iVal;
                            return okChn;
                        }

                        ushort low16 = (ushort)(iVal & 0xFFFF);
                        ushort high16 = (ushort)((iVal >> 16) & 0xFFFF);
                        bool ok1 = bridge.WriteWord(devType, addr, low16);
                        bool ok2 = bridge.WriteWord(devType, addr + 1, high16);
                        if (ok1 && ok2) tag.Value = iVal;
                        return ok1 && ok2;

                    case OpcDataType.UInt32:
                        uint uVal = Convert.ToUInt32(value);
                        if ((devType == "C" || devType == "CN") && addr >= 200)
                        {
                            bool okC = bridge.WriteDWord("C", addr, uVal);
                            if (okC) tag.Value = uVal;
                            return okC;
                        }
                        if (devType == "CHN")
                        {
                            bool okChn = bridge.WriteDWord("CHN", addr, uVal);
                            if (okChn) tag.Value = uVal;
                            return okChn;
                        }

                        ushort ulow16 = (ushort)(uVal & 0xFFFF);
                        ushort uhigh16 = (ushort)((uVal >> 16) & 0xFFFF);
                        bool uok1 = bridge.WriteWord(devType, addr, ulow16);
                        bool uok2 = bridge.WriteWord(devType, addr + 1, uhigh16);
                        if (uok1 && uok2) tag.Value = uVal;
                        return uok1 && uok2;

                    case OpcDataType.Float:
                        float fVal = Convert.ToSingle(value);
                        byte[] fb = BitConverter.GetBytes(fVal);
                        ushort fLow = (ushort)(fb[0] | (fb[1] << 8));
                        ushort fHigh = (ushort)(fb[2] | (fb[3] << 8));

                        bool fok1 = bridge.WriteWord(devType, addr, fLow);
                        bool fok2 = bridge.WriteWord(devType, addr + 1, fHigh);
                        if (fok1 && fok2) tag.Value = fVal;
                        return fok1 && fok2;

                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                Log($"Write error to {tag.TagName}: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Tag Management & Auto-Save
        public void AddTag(OpcTagItem tag)
        {
            lock (_lock)
            {
                tag.ServerHandle = _nextItemHandle++;
                Tags.Add(tag);
            }
            SaveTags();
        }

        public void RemoveTag(OpcTagItem tag)
        {
            lock (_lock)
            {
                Tags.Remove(tag);
            }
            SaveTags();
        }

        public void LoadDefaultPresets()
        {
            lock (_lock)
            {
                Tags.Clear();

                // 1. Digital Inputs: X0 to X17 (Octal addressing: X0..X7, X10..X17) - Read & Write
                string[] xAddrs = new[] { "X0", "X1", "X2", "X3", "X4", "X5", "X6", "X7", "X10", "X11", "X12", "X13", "X14", "X15", "X16", "X17" };
                foreach (var x in xAddrs)
                {
                    Tags.Add(new OpcTagItem
                    {
                        ServerHandle = _nextItemHandle++,
                        TagName = x,
                        Address = x,
                        DataType = OpcDataType.Bool,
                        Description = $"Input Relay {x} (Read/Write)"
                    });
                }

                // 2. Digital Outputs: Y0 to Y17 (Octal addressing: Y0..Y7, Y10..Y17) - Read & Write
                string[] yAddrs = new[] { "Y0", "Y1", "Y2", "Y3", "Y4", "Y5", "Y6", "Y7", "Y10", "Y11", "Y12", "Y13", "Y14", "Y15", "Y16", "Y17" };
                foreach (var y in yAddrs)
                {
                    Tags.Add(new OpcTagItem
                    {
                        ServerHandle = _nextItemHandle++,
                        TagName = y,
                        Address = y,
                        DataType = OpcDataType.Bool,
                        Description = $"Output Relay {y} (Read/Write)"
                    });
                }

                // 3. Internal Relays: M0 to M7 - Read & Write
                string[] mAddrs = new[] { "M0", "M1", "M2", "M3", "M4", "M5", "M6", "M7" };
                foreach (var m in mAddrs)
                {
                    Tags.Add(new OpcTagItem
                    {
                        ServerHandle = _nextItemHandle++,
                        TagName = m,
                        Address = m,
                        DataType = OpcDataType.Bool,
                        Description = $"Internal Relay {m} (Read/Write)"
                    });
                }

                // 4. Data Registers: D0, D1, D10
                Tags.Add(new OpcTagItem
                {
                    ServerHandle = _nextItemHandle++,
                    TagName = "D0",
                    Address = "D0",
                    DataType = OpcDataType.Int16,
                    Description = "Data Register D0"
                });
                Tags.Add(new OpcTagItem
                {
                    ServerHandle = _nextItemHandle++,
                    TagName = "D1",
                    Address = "D1",
                    DataType = OpcDataType.Int16,
                    Description = "Data Register D1"
                });
                Tags.Add(new OpcTagItem
                {
                    ServerHandle = _nextItemHandle++,
                    TagName = "D10",
                    Address = "D10",
                    DataType = OpcDataType.Int16,
                    Description = "Data Register D10"
                });

                // 5. File / Extension Register: R0
                Tags.Add(new OpcTagItem
                {
                    ServerHandle = _nextItemHandle++,
                    TagName = "R0",
                    Address = "R0",
                    DataType = OpcDataType.Int16,
                    Description = "Extension Register R0"
                });

                // 6. Link Register: W0
                Tags.Add(new OpcTagItem
                {
                    ServerHandle = _nextItemHandle++,
                    TagName = "W0",
                    Address = "W0",
                    DataType = OpcDataType.Int16,
                    Description = "Link Register W0"
                });
            }
            SaveTags();
        }

        public void SaveTags()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("[");
                lock (_lock)
                {
                    for (int i = 0; i < Tags.Count; i++)
                    {
                        var t = Tags[i];
                        sb.Append("  {");
                        sb.Append($"\"TagName\": \"{EscapeJson(t.TagName)}\", ");
                        sb.Append($"\"Address\": \"{EscapeJson(t.Address)}\", ");
                        sb.Append($"\"DataType\": \"{t.DataType}\", ");
                        sb.Append($"\"Description\": \"{EscapeJson(t.Description)}\", ");
                        sb.Append($"\"IsActive\": {(t.IsActive ? "true" : "false")}");
                        sb.Append(i < Tags.Count - 1 ? "},\n" : "}\n");
                    }
                }
                sb.AppendLine("]");
                File.WriteAllText(StoragePath, sb.ToString());
            }
            catch (Exception ex)
            {
                Log($"Failed to auto-save tags: {ex.Message}");
            }
        }

        public void LoadTags()
        {
            try
            {
                if (!File.Exists(StoragePath))
                {
                    LoadDefaultPresets();
                    return;
                }

                string json = File.ReadAllText(StoragePath);
                var loaded = ParseTagsJson(json);
                if (loaded.Count == 0)
                {
                    LoadDefaultPresets();
                    return;
                }

                Tags.Clear();
                foreach (var tag in loaded)
                {
                    tag.ServerHandle = _nextItemHandle++;
                    Tags.Add(tag);
                }
            }
            catch
            {
                LoadDefaultPresets();
            }
        }

        private static string EscapeJson(string? s) => s?.Replace("\\", "\\\\")?.Replace("\"", "\\\"") ?? "";

        private static List<OpcTagItem> ParseTagsJson(string json)
        {
            var list = new List<OpcTagItem>();
            if (string.IsNullOrWhiteSpace(json)) return list;

            int idx = 0;
            while ((idx = json.IndexOf('{', idx)) != -1)
            {
                int end = json.IndexOf('}', idx);
                if (end == -1) break;

                string objStr = json.Substring(idx + 1, end - idx - 1);
                var tag = new OpcTagItem();

                string? name = ExtractJsonProp(objStr, "TagName");
                if (!string.IsNullOrEmpty(name)) tag.TagName = name!;

                string? addr = ExtractJsonProp(objStr, "Address");
                if (!string.IsNullOrEmpty(addr)) tag.Address = addr!;

                string? dtStr = ExtractJsonProp(objStr, "DataType");
                if (dtStr != null && Enum.TryParse(dtStr, true, out OpcDataType dt)) tag.DataType = dt;

                string? desc = ExtractJsonProp(objStr, "Description");
                if (desc != null) tag.Description = desc;

                string? actStr = ExtractJsonProp(objStr, "IsActive");
                if (actStr != null && bool.TryParse(actStr, out bool act)) tag.IsActive = act;

                list.Add(tag);
                idx = end + 1;
            }
            return list;
        }

        private static string? ExtractJsonProp(string objStr, string propName)
        {
            string needle = $"\"{propName}\":";
            int idx = objStr.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (idx == -1) return null;

            int valStart = idx + needle.Length;
            while (valStart < objStr.Length && (char.IsWhiteSpace(objStr[valStart]) || objStr[valStart] == '\"')) valStart++;

            int valEnd = valStart;
            bool insideQuotes = (valStart > 0 && objStr[valStart - 1] == '\"');

            if (insideQuotes)
            {
                while (valEnd < objStr.Length && objStr[valEnd] != '\"') valEnd++;
            }
            else
            {
                while (valEnd < objStr.Length && objStr[valEnd] != ',' && objStr[valEnd] != '}') valEnd++;
            }

            if (valEnd >= valStart)
            {
                return objStr.Substring(valStart, valEnd - valStart).Trim();
            }
            return null;
        }

        public string ExportCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("TagName,Address,DataType,Description,IsActive");
            lock (_lock)
            {
                foreach (var t in Tags)
                {
                    sb.AppendLine($"\"{t.TagName}\",\"{t.Address}\",\"{t.DataType}\",\"{t.Description}\",\"{t.IsActive}\"");
                }
            }
            return sb.ToString();
        }

        public void ImportCsv(string csvContent)
        {
            var lines = csvContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length <= 1) return;

            lock (_lock)
            {
                Tags.Clear();
                for (int i = 1; i < lines.Length; i++)
                {
                    var parts = lines[i].Split(',');
                    if (parts.Length < 3) continue;

                    string name = parts[0].Trim('"', ' ');
                    string addr = parts[1].Trim('"', ' ');
                    string dtStr = parts[2].Trim('"', ' ');
                    string desc = parts.Length > 3 ? parts[3].Trim('"', ' ') : "";
                    bool act = parts.Length <= 4 || !bool.TryParse(parts[4].Trim('"', ' '), out bool b) || b;

                    if (!Enum.TryParse(dtStr, true, out OpcDataType dt)) dt = OpcDataType.Bool;

                    Tags.Add(new OpcTagItem
                    {
                        ServerHandle = _nextItemHandle++,
                        TagName = name,
                        Address = addr,
                        DataType = dt,
                        Description = desc,
                        IsActive = act
                    });
                }
            }
            SaveTags();
        }
        #endregion

        #region IClassFactory Implementation
        public int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject)
        {
            ppvObject = IntPtr.Zero;
            if (pUnkOuter != IntPtr.Zero) return unchecked((int)0x80040110); // CLASS_E_NOAGGREGATION

            IntPtr pUnk = Marshal.GetIUnknownForObject(this);
            int hr = Marshal.QueryInterface(pUnk, ref riid, out ppvObject);
            Marshal.Release(pUnk);

            if (hr == 0)
            {
                Log($"COM Client connected with interface: {riid}");
                OnStateChanged?.Invoke();
            }
            return hr;
        }

        public int LockServer(bool fLock) => OpcDaConstants.S_OK;
        #endregion

        #region IOPCServer Implementation
        public int AddGroup(
            string szName,
            bool bActive,
            int dwRequestedUpdateRate,
            int hClientGroup,
            IntPtr pTimeBias,
            IntPtr pPercentDeadband,
            int dwLCID,
            out int phServerGroup,
            out int pRevisedUpdateRate,
            ref Guid riid,
            out object? ppUnk)
        {
            ppUnk = null;
            phServerGroup = 0;
            pRevisedUpdateRate = dwRequestedUpdateRate > 0 ? dwRequestedUpdateRate : 100;

            lock (_lock)
            {
                if (!string.IsNullOrEmpty(szName))
                {
                    foreach (var g in _groups)
                    {
                        if (g.Name.Equals(szName, StringComparison.OrdinalIgnoreCase))
                        {
                            return OpcDaConstants.OPC_E_DUPLICATENAME;
                        }
                    }
                }

                int hServer = _nextGroupHandle++;
                phServerGroup = hServer;

                var group = new OpcGroupInstance(this, szName, bActive, pRevisedUpdateRate, hClientGroup, hServer);
                _groups.Add(group);

                ppUnk = group;
                Log($"OPC Client created group: '{szName}' (Handle: {hServer})");
                OnStateChanged?.Invoke();
                return OpcDaConstants.S_OK;
            }
        }

        public int GetErrorString(int dwError, int dwLocale, out string ppString)
        {
            switch ((uint)dwError)
            {
                case 0:
                    ppString = "The operation completed successfully.";
                    return OpcDaConstants.S_OK;
                case 0xC0040001: // OPC_E_INVALIDHANDLE
                    ppString = "The value of the handle is invalid.";
                    return OpcDaConstants.S_OK;
                case 0xC0040004: // OPC_E_BADTYPE
                    ppString = "The server cannot convert the data between the specified format and the native type.";
                    return OpcDaConstants.S_OK;
                case 0xC0040007: // OPC_E_UNKNOWNITEMID
                    ppString = "The item is no longer available in the server address space.";
                    return OpcDaConstants.S_OK;
                case 0xC004000C: // OPC_E_DUPLICATENAME
                    ppString = "Duplicate name not allowed.";
                    return OpcDaConstants.S_OK;
                default:
                    ppString = $"OPC Status Code: 0x{dwError:X8}";
                    return OpcDaConstants.S_OK;
            }
        }

        public int GetGroupByName(string szName, ref Guid riid, out object? ppUnk)
        {
            lock (_lock)
            {
                foreach (var g in _groups)
                {
                    if (g.Name.Equals(szName, StringComparison.OrdinalIgnoreCase))
                    {
                        ppUnk = g;
                        return OpcDaConstants.S_OK;
                    }
                }
            }
            ppUnk = null;
            return OpcDaConstants.E_FAIL;
        }

        public int GetStatus(out IntPtr ppServerStatus)
        {
            ppServerStatus = IntPtr.Zero;
            try
            {
                OPCSERVERSTATUS status = new OPCSERVERSTATUS
                {
                    szVendorInfo = "NetToGXSim3 by Ismail Lowkey",
                    wMajorVersion = (short)AppVersionInfo.Major,
                    wMinorVersion = (short)AppVersionInfo.Minor,
                    wBuildNumber = (short)AppVersionInfo.Build,
                    dwServerState = _isRunning ? OPCSERVERSTATE.OPC_STATUS_RUNNING : OPCSERVERSTATE.OPC_STATUS_NOCONFIG,
                    dwGroupCount = _groups.Count,
                    dwBandWidth = 0
                };

                DateTime start = _startTime > DateTime.MinValue ? _startTime : DateTime.UtcNow;
                long startFt = start.ToFileTime();
                status.ftStartTime.dwLowDateTime = (int)(startFt & 0xFFFFFFFF);
                status.ftStartTime.dwHighDateTime = (int)(startFt >> 32);

                long curFt = DateTime.UtcNow.ToFileTime();
                status.ftCurrentTime.dwLowDateTime = (int)(curFt & 0xFFFFFFFF);
                status.ftCurrentTime.dwHighDateTime = (int)(curFt >> 32);
                status.ftLastUpdateTime = status.ftCurrentTime;

                ppServerStatus = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(OPCSERVERSTATUS)));
                Marshal.StructureToPtr(status, ppServerStatus, false);
                return OpcDaConstants.S_OK;
            }
            catch (Exception ex)
            {
                Log($"GetStatus exception: {ex.Message}");
                return OpcDaConstants.E_FAIL;
            }
        }

        public int RemoveGroup(int hServerGroup, bool bForce)
        {
            lock (_lock)
            {
                for (int i = 0; i < _groups.Count; i++)
                {
                    if (_groups[i].ServerHandle == hServerGroup)
                    {
                        _groups.RemoveAt(i);
                        Log($"OPC Group {hServerGroup} removed.");
                        OnStateChanged?.Invoke();
                        return OpcDaConstants.S_OK;
                    }
                }
            }
            return OpcDaConstants.OPC_E_INVALIDHANDLE;
        }

        public int CreateGroupEnumerator(int dwScope, ref Guid riid, out object? ppUnk)
        {
            ppUnk = null;
            return OpcDaConstants.E_NOTIMPL;
        }
        #endregion

        #region IOPCCommon Implementation
        public int SetLocaleID(int dwLcid) => OpcDaConstants.S_OK;
        public int GetLocaleID(out int pdwLcid) { pdwLcid = 0x0409; return OpcDaConstants.S_OK; }
        public int QueryAvailableLocaleIDs(out int pdwCount, out IntPtr pdwLcid)
        {
            pdwCount = 1;
            pdwLcid = Marshal.AllocCoTaskMem(4);
            Marshal.WriteInt32(pdwLcid, 0x0409);
            return OpcDaConstants.S_OK;
        }
        public int GetErrorString(int dwError, out string ppString) { ppString = "Error " + dwError; return OpcDaConstants.S_OK; }
        public int SetClientName(string szName) { Log($"OPC Client Name: {szName}"); return OpcDaConstants.S_OK; }
        #endregion

        #region IOPCBrowseServerAddressSpace Implementation
        public int QueryOrganization(out OPCNAMESPACETYPE pNameSpaceType)
        {
            pNameSpaceType = OPCNAMESPACETYPE.OPC_NS_FLAT;
            return OpcDaConstants.S_OK;
        }

        public int ChangeBrowsePosition(OPCBROWSEDIRECTION dwBrowseDirection, string szString)
        {
            switch (dwBrowseDirection)
            {
                case OPCBROWSEDIRECTION.OPC_BROWSE_UP:
                    return OpcDaConstants.E_FAIL;

                case OPCBROWSEDIRECTION.OPC_BROWSE_TO:
                    if (string.IsNullOrEmpty(szString)) return OpcDaConstants.S_OK;
                    return unchecked((int)0xC0040004); // OPC_E_UNKNOWNITEMID

                case OPCBROWSEDIRECTION.OPC_BROWSE_DOWN:
                default:
                    return unchecked((int)0xC0040004); // OPC_E_UNKNOWNITEMID
            }
        }

        public int BrowseOPCItemIDs(
            OPCBROWSETYPE dwBrowseFilterType,
            string szFilterCriteria,
            short vtDataTypeFilter,
            int dwAccessRightsFilter,
            out IEnumString ppIEnumString)
        {
            var names = new List<string>();

            if (dwBrowseFilterType != OPCBROWSETYPE.OPC_BRANCH)
            {
                lock (_lock)
                {
                    foreach (var tag in Tags)
                    {
                        if (!string.IsNullOrEmpty(szFilterCriteria) && szFilterCriteria != "*")
                        {
                            string filter = szFilterCriteria.Replace("*", "");
                            if (tag.TagName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                names.Add(tag.TagName);
                            }
                        }
                        else
                        {
                            names.Add(tag.TagName);
                        }
                    }
                }
            }

            ppIEnumString = new OpcEnumString(names);
            Log($"OPC Browse (Filter: {dwBrowseFilterType}, Criteria: '{szFilterCriteria}'). Returning {names.Count} tags.");
            return OpcDaConstants.S_OK;
        }

        public int GetItemID(string szItemDataID, out string szItemID)
        {
            szItemID = szItemDataID ?? "";
            return OpcDaConstants.S_OK;
        }

        public int BrowseAccessPaths(string szItemID, out IEnumString? ppIEnumString)
        {
            ppIEnumString = null;
            return OpcDaConstants.E_NOTIMPL;
        }
        #endregion

        #region IOPCItemProperties Implementation
        public int QueryAvailableProperties(
            string szItemID,
            out int pdwCount,
            out IntPtr ppPropertyIDs,
            out IntPtr ppDescriptions,
            out IntPtr ppvtDataTypes)
        {
            var tag = FindTag(szItemID);
            if (tag == null)
            {
                pdwCount = 0;
                ppPropertyIDs = IntPtr.Zero;
                ppDescriptions = IntPtr.Zero;
                ppvtDataTypes = IntPtr.Zero;
                return OpcDaConstants.OPC_E_UNKNOWNITEMID;
            }

            int[] propIDs = { 1, 2, 3, 4, 5, 6 };
            string[] propDescs = { "Item Canonical DataType", "Item Value", "Item Quality", "Item Timestamp", "Item Access Rights", "Server Scan Rate" };
            short[] propTypes = { 2, 12, 2, 7, 3, 4 };

            pdwCount = propIDs.Length;
            ppPropertyIDs = Marshal.AllocCoTaskMem(pdwCount * 4);
            ppDescriptions = Marshal.AllocCoTaskMem(pdwCount * IntPtr.Size);
            ppvtDataTypes = Marshal.AllocCoTaskMem(pdwCount * 2);

            for (int i = 0; i < pdwCount; i++)
            {
                Marshal.WriteInt32(ppPropertyIDs, i * 4, propIDs[i]);
                IntPtr strPtr = Marshal.StringToCoTaskMemUni(propDescs[i]);
                Marshal.WriteIntPtr(ppDescriptions, i * IntPtr.Size, strPtr);
                Marshal.WriteInt16(ppvtDataTypes, i * 2, propTypes[i]);
            }
            return OpcDaConstants.S_OK;
        }

        public int GetItemProperties(
            string szItemID,
            int dwCount,
            IntPtr pdwPropertyIDs,
            out IntPtr ppvData,
            out IntPtr ppErrors)
        {
            ppvData = Marshal.AllocCoTaskMem(dwCount * 16);
            ppErrors = Marshal.AllocCoTaskMem(dwCount * 4);

            var tag = FindTag(szItemID);

            for (int i = 0; i < dwCount; i++)
            {
                int propID = Marshal.ReadInt32(pdwPropertyIDs, i * 4);
                IntPtr pVar = (IntPtr)((long)ppvData + i * 16);

                if (tag == null)
                {
                    Marshal.GetNativeVariantForObject(null, pVar);
                    Marshal.WriteInt32(ppErrors, i * 4, OpcDaConstants.OPC_E_UNKNOWNITEMID);
                    continue;
                }

                object? val = null;
                switch (propID)
                {
                    case 1: val = (short)11; break;
                    case 2: val = tag.Value; break;
                    case 3: val = (short)tag.QualityCode; break;
                    case 4: val = tag.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"); break;
                    case 5: val = (int)3; break; // Read & Write
                    case 6: val = 100.0f; break;
                    default: break;
                }

                if (val != null)
                {
                    Marshal.GetNativeVariantForObject(val, pVar);
                    Marshal.WriteInt32(ppErrors, i * 4, OpcDaConstants.S_OK);
                }
                else
                {
                    Marshal.GetNativeVariantForObject(null, pVar);
                    Marshal.WriteInt32(ppErrors, i * 4, OpcDaConstants.E_INVALIDARG);
                }
            }
            return OpcDaConstants.S_OK;
        }

        public int LookupItemIDs(
            string szItemID,
            int dwCount,
            IntPtr pdwPropertyIDs,
            out IntPtr ppszNewItemIDs,
            out IntPtr ppErrors)
        {
            ppszNewItemIDs = IntPtr.Zero;
            ppErrors = IntPtr.Zero;
            return OpcDaConstants.E_NOTIMPL;
        }
        #endregion

        public OpcTagItem? FindTag(string? identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return null;

            string cleanId = identifier!.Trim();
            if (cleanId.StartsWith("Mitsubishi.", StringComparison.OrdinalIgnoreCase)) cleanId = cleanId.Substring(11);
            else if (cleanId.StartsWith("Mitsubishi/", StringComparison.OrdinalIgnoreCase)) cleanId = cleanId.Substring(11);
            else if (cleanId.StartsWith("Mitsubishi\\", StringComparison.OrdinalIgnoreCase)) cleanId = cleanId.Substring(11);
            else if (cleanId.StartsWith("FX.", StringComparison.OrdinalIgnoreCase)) cleanId = cleanId.Substring(3);
            else if (cleanId.StartsWith("NetToGXSim.", StringComparison.OrdinalIgnoreCase)) cleanId = cleanId.Substring(11);

            lock (_lock)
            {
                foreach (var t in Tags)
                {
                    if (t.TagName.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
                        t.Address.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
                        t.TagName.Equals(identifier, StringComparison.OrdinalIgnoreCase) ||
                        t.Address.Equals(identifier, StringComparison.OrdinalIgnoreCase))
                    {
                        return t;
                    }
                }
            }
            return null;
        }

        public OpcTagItem? FindTagByServerHandle(int handle)
        {
            lock (_lock)
            {
                foreach (var t in Tags)
                {
                    if (t.ServerHandle == handle) return t;
                }
            }
            return null;
        }

        private void Log(string msg)
        {
            OnLog?.Invoke($"[OPC DA] {DateTime.Now:HH:mm:ss} {msg}");
        }

        public void Dispose()
        {
            StopServer();
        }
    }

    #region OPC Group Implementation
    public class OpcGroupInstance : IOPCGroupStateMgt, IOPCItemMgt, IOPCSyncIO, IOPCAsyncIO2, IConnectionPointContainer, IConnectionPoint
    {
        private readonly OpcDaServerEngine _server;
        public string Name { get; private set; }
        public bool IsActive { get; private set; }
        public int UpdateRate { get; private set; }
        public int ClientHandle { get; }
        public int ServerHandle { get; }
        private bool _asyncEnabled = true;
        public DateTime LastCallbackTime { get; set; } = DateTime.MinValue;

        private readonly Dictionary<int, OpcGroupItemEntry> _items = new Dictionary<int, OpcGroupItemEntry>();
        private readonly object _lock = new object();
        private int _nextItemHandle = 1000;

        public class OpcGroupItemEntry
        {
            public int ServerHandle { get; set; }
            public int ClientHandle { get; set; }
            public OpcTagItem Tag { get; set; } = null!;
            public bool IsActive { get; set; }
        }

        public OpcGroupInstance(OpcDaServerEngine server, string? name, bool active, int updateRate, int clientHandle, int serverHandle)
        {
            _server = server;
            Name = name ?? $"Group_{serverHandle}";
            IsActive = active;
            UpdateRate = updateRate;
            ClientHandle = clientHandle;
            ServerHandle = serverHandle;
        }

        #region IOPCGroupStateMgt
        public int GetState(out int pUpdateRate, out bool pActive, out string ppName, out int pTimeBias, out float pPercentDeadband, out int pLCID, out int phClientGroup, out int phServerGroup)
        {
            pUpdateRate = UpdateRate;
            pActive = IsActive;
            ppName = Name;
            pTimeBias = 0;
            pPercentDeadband = 0.0f;
            pLCID = 0x0409;
            phClientGroup = ClientHandle;
            phServerGroup = ServerHandle;
            return OpcDaConstants.S_OK;
        }

        public int SetState(IntPtr pRequestedUpdateRate, out int pRevisedUpdateRate, IntPtr pActive, IntPtr pTimeBias, IntPtr pPercentDeadband, IntPtr pLCID, IntPtr phClientGroup)
        {
            if (pRequestedUpdateRate != IntPtr.Zero)
            {
                UpdateRate = Math.Max(100, Marshal.ReadInt32(pRequestedUpdateRate));
            }
            pRevisedUpdateRate = UpdateRate;
            if (pActive != IntPtr.Zero)
            {
                IsActive = Marshal.ReadInt32(pActive) != 0;
            }
            return OpcDaConstants.S_OK;
        }

        public int SetName(string szName) { Name = szName; return OpcDaConstants.S_OK; }
        public int CloneGroup(string szName, ref Guid riid, out object? ppUnk) { ppUnk = null; return OpcDaConstants.E_NOTIMPL; }
        #endregion

        #region IOPCItemMgt
        public int AddItems(int dwCount, IntPtr pItemArray, out IntPtr ppAddResults, out IntPtr ppErrors)
        {
            int structSize = Marshal.SizeOf(typeof(OPCITEMDEF));
            int resSize = Marshal.SizeOf(typeof(OPCITEMRESULT));

            ppAddResults = Marshal.AllocCoTaskMem(dwCount * resSize);
            ppErrors = Marshal.AllocCoTaskMem(dwCount * 4);

            lock (_lock)
            {
                for (int i = 0; i < dwCount; i++)
                {
                    IntPtr pCurrent = (IntPtr)((long)pItemArray + i * structSize);
                    OPCITEMDEF def = (OPCITEMDEF)Marshal.PtrToStructure(pCurrent, typeof(OPCITEMDEF))!;

                    OpcTagItem? tag = _server.FindTag(def.szItemID);
                    if (tag == null)
                    {
                        string rawAddr = def.szItemID ?? "";
                        if (rawAddr.StartsWith("Mitsubishi.", StringComparison.OrdinalIgnoreCase)) rawAddr = rawAddr.Substring(11);
                        else if (rawAddr.StartsWith("Mitsubishi/", StringComparison.OrdinalIgnoreCase)) rawAddr = rawAddr.Substring(11);
                        else if (rawAddr.StartsWith("Mitsubishi\\", StringComparison.OrdinalIgnoreCase)) rawAddr = rawAddr.Substring(11);
                        else if (rawAddr.StartsWith("FX.", StringComparison.OrdinalIgnoreCase)) rawAddr = rawAddr.Substring(3);

                        // If tag name not in config, see if it's a direct valid Mitsubishi address (e.g. "X0", "Y0", "M0", "D10")
                        if (OpcDaServerEngine.TryParseMitsubishiAddress(rawAddr, out string devType, out _, out int bitIdx))
                        {
                            bool isBit = bitIdx >= 0 || devType == "X" || devType == "Y" || devType == "M" || devType == "S" || devType == "SM" || devType == "B" || devType == "TS" || devType == "CS";
                            tag = new OpcTagItem
                            {
                                TagName = def.szItemID,
                                Address = rawAddr,
                                DataType = isBit ? OpcDataType.Bool : OpcDataType.Int16
                            };
                            _server.AddTag(tag);
                        }
                    }

                    OPCITEMRESULT res = new OPCITEMRESULT();
                    if (tag != null)
                    {
                        int hItem = _nextItemHandle++;
                        _items[hItem] = new OpcGroupItemEntry
                        {
                            ServerHandle = hItem,
                            ClientHandle = def.hClient,
                            Tag = tag,
                            IsActive = def.bActive != 0
                        };

                        res.hServer = hItem;
                        res.vtCanonicalDataType = GetVarType(tag.DataType);
                        res.dwAccessRights = 3; // Read & Write

                        Marshal.StructureToPtr(res, (IntPtr)((long)ppAddResults + i * resSize), false);
                        Marshal.WriteInt32(ppErrors, i * 4, OpcDaConstants.S_OK);
                    }
                    else
                    {
                        res.hServer = 0;
                        Marshal.StructureToPtr(res, (IntPtr)((long)ppAddResults + i * resSize), false);
                        Marshal.WriteInt32(ppErrors, i * 4, OpcDaConstants.OPC_E_UNKNOWNITEMID);
                    }
                }
            }

            ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.Sleep(30);
                SendNotification(0, isRefresh: true);
            });

            return OpcDaConstants.S_OK;
        }

        public int ValidateItems(int dwCount, IntPtr pItemArray, bool bBlobUpdate, out IntPtr ppValidationResults, out IntPtr ppErrors)
        {
            int structSize = Marshal.SizeOf(typeof(OPCITEMDEF));
            int resSize = Marshal.SizeOf(typeof(OPCITEMRESULT));

            ppValidationResults = Marshal.AllocCoTaskMem(dwCount * resSize);
            ppErrors = Marshal.AllocCoTaskMem(dwCount * 4);

            for (int i = 0; i < dwCount; i++)
            {
                IntPtr pCurrent = (IntPtr)((long)pItemArray + i * structSize);
                OPCITEMDEF def = (OPCITEMDEF)Marshal.PtrToStructure(pCurrent, typeof(OPCITEMDEF))!;

                OPCITEMRESULT res = new OPCITEMRESULT();
                var tag = _server.FindTag(def.szItemID);
                if (tag != null || OpcDaServerEngine.TryParseMitsubishiAddress(def.szItemID, out _, out _, out _))
                {
                    res.hServer = 0;
                    res.vtCanonicalDataType = tag != null ? GetVarType(tag.DataType) : (short)11;
                    res.dwAccessRights = 3;
                    Marshal.StructureToPtr(res, (IntPtr)((long)ppValidationResults + i * resSize), false);
                    Marshal.WriteInt32(ppErrors, i * 4, OpcDaConstants.S_OK);
                }
                else
                {
                    Marshal.StructureToPtr(res, (IntPtr)((long)ppValidationResults + i * resSize), false);
                    Marshal.WriteInt32(ppErrors, i * 4, OpcDaConstants.OPC_E_UNKNOWNITEMID);
                }
            }
            return OpcDaConstants.S_OK;
        }

        public int RemoveItems(int dwCount, IntPtr phServer, out IntPtr ppErrors)
        {
            ppErrors = Marshal.AllocCoTaskMem(dwCount * 4);
            lock (_lock)
            {
                for (int i = 0; i < dwCount; i++)
                {
                    int hServer = Marshal.ReadInt32(phServer, i * 4);
                    if (_items.Remove(hServer))
                    {
                        Marshal.WriteInt32(ppErrors, i * 4, OpcDaConstants.S_OK);
                    }
                    else
                    {
                        Marshal.WriteInt32(ppErrors, i * 4, OpcDaConstants.OPC_E_INVALIDHANDLE);
                    }
                }
            }
            return OpcDaConstants.S_OK;
        }

        public int SetActiveState(int dwCount, IntPtr phServer, bool bActive, out IntPtr ppErrors)
        {
            ppErrors = Marshal.AllocCoTaskMem(dwCount * 4);
            lock (_lock)
            {
                for (int i = 0; i < dwCount; i++)
                {
                    int hServer = Marshal.ReadInt32(phServer, i * 4);
                    if (_items.TryGetValue(hServer, out var entry))
                    {
                        entry.IsActive = bActive;
                        Marshal.WriteInt32(ppErrors, i * 4, OpcDaConstants.S_OK);
                    }
                    else
                    {
                        Marshal.WriteInt32(ppErrors, i * 4, OpcDaConstants.OPC_E_INVALIDHANDLE);
                    }
                }
            }
            return OpcDaConstants.S_OK;
        }

        public int SetClientHandles(int dwCount, IntPtr phServer, IntPtr phClient, out IntPtr ppErrors)
        {
            ppErrors = Marshal.AllocCoTaskMem(dwCount * 4);
            lock (_lock)
            {
                for (int i = 0; i < dwCount; i++)
                {
                    int hServer = Marshal.ReadInt32(phServer, i * 4);
                    int hClient = Marshal.ReadInt32(phClient, i * 4);
                    if (_items.TryGetValue(hServer, out var entry))
                    {
                        entry.ClientHandle = hClient;
                        Marshal.WriteInt32(ppErrors, i * 4, OpcDaConstants.S_OK);
                    }
                    else
                    {
                        Marshal.WriteInt32(ppErrors, i * 4, OpcDaConstants.OPC_E_INVALIDHANDLE);
                    }
                }
            }
            return OpcDaConstants.S_OK;
        }

        public int SetDatatypes(int dwCount, IntPtr phServer, IntPtr pRequestedDatatypes, out IntPtr ppErrors)
        {
            ppErrors = Marshal.AllocCoTaskMem(dwCount * 4);
            for (int i = 0; i < dwCount; i++)
            {
                Marshal.WriteInt32(ppErrors, i * 4, OpcDaConstants.S_OK);
            }
            return OpcDaConstants.S_OK;
        }

        public int CreateEnumerator(ref Guid riid, out object? ppUnk)
        {
            ppUnk = null;
            return OpcDaConstants.E_NOTIMPL;
        }
        #endregion

        #region IOPCSyncIO
        public int Read(OPCDATASOURCE dwSource, int dwCount, IntPtr phServer, out IntPtr ppItemValues, out IntPtr ppErrors)
        {
            int stateSize = Marshal.SizeOf(typeof(OPCITEMSTATE));
            ppItemValues = Marshal.AllocCoTaskMem(dwCount * stateSize);
            ppErrors = Marshal.AllocCoTaskMem(dwCount * 4);

            long ftNow = DateTime.UtcNow.ToFileTime();
            System.Runtime.InteropServices.ComTypes.FILETIME ft = new System.Runtime.InteropServices.ComTypes.FILETIME
            {
                dwLowDateTime = (int)(ftNow & 0xFFFFFFFF),
                dwHighDateTime = (int)(ftNow >> 32)
            };

            lock (_lock)
            {
                for (int i = 0; i < dwCount; i++)
                {
                    int hServer = Marshal.ReadInt32(phServer, i * 4);
                    IntPtr targetStatePtr = (IntPtr)((long)ppItemValues + i * stateSize);

                    if (_items.TryGetValue(hServer, out var entry))
                    {
                        var tag = entry.Tag;
                        object val = tag.QualityCode == OpcDaConstants.OPC_QUALITY_GOOD
                            ? ConvertForVariant(tag.Value, tag.DataType)
                            : GetDefaultVariant(tag.DataType);

                        OPCITEMSTATE state = new OPCITEMSTATE
                        {
                            hClient = entry.ClientHandle,
                            ftTimeStamp = ft,
                            wQuality = tag.QualityCode,
                            vDataValue = val
                        };

                        Marshal.StructureToPtr(state, targetStatePtr, false);
                        Marshal.WriteInt32(ppErrors, i * 4, OpcDaConstants.S_OK);
                    }
                    else
                    {
                        OPCITEMSTATE state = new OPCITEMSTATE
                        {
                            hClient = 0,
                            ftTimeStamp = ft,
                            wQuality = OpcDaConstants.OPC_QUALITY_BAD,
                            vDataValue = 0
                        };
                        Marshal.StructureToPtr(state, targetStatePtr, false);
                        Marshal.WriteInt32(ppErrors, i * 4, OpcDaConstants.OPC_E_INVALIDHANDLE);
                    }
                }
            }
            return OpcDaConstants.S_OK;
        }

        public int Write(int dwCount, IntPtr phServer, IntPtr pItemValues, out IntPtr ppErrors)
        {
            ppErrors = Marshal.AllocCoTaskMem(dwCount * 4);

            lock (_lock)
            {
                for (int i = 0; i < dwCount; i++)
                {
                    int hServer = Marshal.ReadInt32(phServer, i * 4);
                    IntPtr pVal = (IntPtr)((long)pItemValues + i * 16); // 16 bytes per VARIANT

                    if (_items.TryGetValue(hServer, out var entry))
                    {
                        try
                        {
                            object val = Marshal.GetObjectForNativeVariant(pVal);
                            bool ok = _server.WriteTagToPlc(entry.Tag, val);
                            Marshal.WriteInt32(ppErrors, i * 4, ok ? OpcDaConstants.S_OK : OpcDaConstants.E_FAIL);
                        }
                        catch
                        {
                            Marshal.WriteInt32(ppErrors, i * 4, OpcDaConstants.OPC_E_BADTYPE);
                        }
                    }
                    else
                    {
                        Marshal.WriteInt32(ppErrors, i * 4, OpcDaConstants.OPC_E_INVALIDHANDLE);
                    }
                }
            }
            return OpcDaConstants.S_OK;
        }
        #endregion

        #region IOPCAsyncIO2
        public int Read(int dwCount, IntPtr phServer, int dwTransactionID, out int pdwCancelID, out IntPtr ppErrors)
        {
            pdwCancelID = dwTransactionID != 0 ? dwTransactionID : 1;
            ppErrors = Marshal.AllocCoTaskMem(dwCount * 4);

            lock (_lock)
            {
                for (int i = 0; i < dwCount; i++)
                {
                    int hServer = Marshal.ReadInt32(phServer, i * 4);
                    Marshal.WriteInt32(ppErrors, i * 4, _items.ContainsKey(hServer) ? OpcDaConstants.S_OK : OpcDaConstants.OPC_E_INVALIDHANDLE);
                }
            }

            ThreadPool.QueueUserWorkItem(_ =>
            {
                SendNotification(dwTransactionID, isRefresh: true);
            });

            return OpcDaConstants.S_OK;
        }

        public int Write(int dwCount, IntPtr phServer, IntPtr pItemValues, int dwTransactionID, out int pdwCancelID, out IntPtr ppErrors)
        {
            pdwCancelID = dwTransactionID != 0 ? dwTransactionID : 1;
            ppErrors = Marshal.AllocCoTaskMem(dwCount * 4);

            lock (_lock)
            {
                for (int i = 0; i < dwCount; i++)
                {
                    int hServer = Marshal.ReadInt32(phServer, i * 4);
                    IntPtr pVal = (IntPtr)((long)pItemValues + i * 16);

                    if (_items.TryGetValue(hServer, out var entry))
                    {
                        try
                        {
                            object val = Marshal.GetObjectForNativeVariant(pVal);
                            bool ok = _server.WriteTagToPlc(entry.Tag, val);
                            Marshal.WriteInt32(ppErrors, i * 4, ok ? OpcDaConstants.S_OK : OpcDaConstants.E_FAIL);
                        }
                        catch
                        {
                            Marshal.WriteInt32(ppErrors, i * 4, OpcDaConstants.OPC_E_BADTYPE);
                        }
                    }
                    else
                    {
                        Marshal.WriteInt32(ppErrors, i * 4, OpcDaConstants.OPC_E_INVALIDHANDLE);
                    }
                }
            }

            return OpcDaConstants.S_OK;
        }

        public int Refresh2(OPCDATASOURCE dwSource, int dwTransactionID, out int pdwCancelID)
        {
            pdwCancelID = dwTransactionID != 0 ? dwTransactionID : 1;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                SendNotification(dwTransactionID, isRefresh: true);
            });
            return OpcDaConstants.S_OK;
        }

        public int Cancel2(int dwCancelID)
        {
            return OpcDaConstants.S_OK;
        }

        public int SetEnable(bool bEnable)
        {
            _asyncEnabled = bEnable;
            return OpcDaConstants.S_OK;
        }

        public int GetEnable(out bool pbEnable)
        {
            pbEnable = _asyncEnabled;
            return OpcDaConstants.S_OK;
        }
        #endregion

        #region IOPCDataCallback Dispatcher
        public void SendNotification(int dwTransid = 0, bool isRefresh = false)
        {
            if (!_asyncEnabled) return;
            if (!IsActive && !isRefresh) return;

            List<IOPCDataCallback> sinks = new List<IOPCDataCallback>();
            lock (_lock)
            {
                if (_callbacks.Count == 0) return;
                foreach (var sinkObj in _callbacks.Values)
                {
                    if (sinkObj is IOPCDataCallback cb)
                    {
                        sinks.Add(cb);
                    }
                }
            }

            if (sinks.Count == 0) return;

            List<OpcGroupItemEntry> activeItems = new List<OpcGroupItemEntry>();
            lock (_lock)
            {
                foreach (var entry in _items.Values)
                {
                    if (entry.IsActive || isRefresh)
                    {
                        activeItems.Add(entry);
                    }
                }
            }

            if (activeItems.Count == 0) return;

            int count = activeItems.Count;
            int variantSize = 16;
            int filetimeSize = 8;

            IntPtr pClients = Marshal.AllocCoTaskMem(count * 4);
            IntPtr pValues = Marshal.AllocCoTaskMem(count * variantSize);
            IntPtr pQualities = Marshal.AllocCoTaskMem(count * 2);
            IntPtr pTimeStamps = Marshal.AllocCoTaskMem(count * filetimeSize);
            IntPtr pErrors = Marshal.AllocCoTaskMem(count * 4);

            long ftNow = DateTime.UtcNow.ToFileTime();
            int hrMasterQuality = OpcDaConstants.S_OK;
            int hrMasterError = OpcDaConstants.S_OK;

            try
            {
                for (int i = 0; i < count; i++)
                {
                    var entry = activeItems[i];
                    var tag = entry.Tag;

                    Marshal.WriteInt32(pClients, i * 4, entry.ClientHandle);

                    IntPtr pVar = (IntPtr)((long)pValues + i * variantSize);
                    for (int b = 0; b < variantSize; b++) Marshal.WriteByte(pVar, b, 0);

                    object val = tag.QualityCode == OpcDaConstants.OPC_QUALITY_GOOD
                        ? ConvertForVariant(tag.Value, tag.DataType)
                        : GetDefaultVariant(tag.DataType);

                    Marshal.GetNativeVariantForObject(val, pVar);

                    short q = tag.QualityCode;
                    Marshal.WriteInt16(pQualities, i * 2, q);
                    if (q != OpcDaConstants.OPC_QUALITY_GOOD)
                    {
                        hrMasterQuality = OpcDaConstants.S_FALSE;
                    }

                    Marshal.WriteInt32(pTimeStamps, i * filetimeSize, (int)(ftNow & 0xFFFFFFFF));
                    Marshal.WriteInt32(pTimeStamps, i * filetimeSize + 4, (int)(ftNow >> 32));

                    Marshal.WriteInt32(pErrors, i * 4, OpcDaConstants.S_OK);
                }

                foreach (var sink in sinks)
                {
                    try
                    {
                        sink.OnDataChange(dwTransid, ClientHandle, hrMasterQuality, hrMasterError, count, pClients, pValues, pQualities, pTimeStamps, pErrors);
                    }
                    catch
                    {
                    }
                }
            }
            finally
            {
                for (int i = 0; i < count; i++)
                {
                    IntPtr pVar = (IntPtr)((long)pValues + i * variantSize);
                    OpcRegistryHelper.VariantClear(pVar);
                }
                Marshal.FreeCoTaskMem(pClients);
                Marshal.FreeCoTaskMem(pValues);
                Marshal.FreeCoTaskMem(pQualities);
                Marshal.FreeCoTaskMem(pTimeStamps);
                Marshal.FreeCoTaskMem(pErrors);
            }
        }
        #endregion

        #region IConnectionPointContainer
        public int EnumConnectionPoints(out IntPtr ppEnum)
        {
            ppEnum = IntPtr.Zero;
            return OpcDaConstants.E_NOTIMPL;
        }

        public int FindConnectionPoint(ref Guid riid, out IConnectionPoint? ppCP)
        {
            // IOPCDataCallback {39c13a70-011e-11d0-9675-0020afd8adb3}
            if (riid == new Guid("39c13a70-011e-11d0-9675-0020afd8adb3"))
            {
                ppCP = this;
                return OpcDaConstants.S_OK;
            }
            ppCP = null;
            return unchecked((int)0x80040200); // CONNECT_E_NOCONNECTION
        }
        #endregion

        #region IConnectionPoint
        private readonly Dictionary<int, object> _callbacks = new Dictionary<int, object>();
        private int _nextCallbackCookie = 1;

        public int GetConnectionInterface(out Guid pIID)
        {
            pIID = new Guid("39c13a70-011e-11d0-9675-0020afd8adb3");
            return OpcDaConstants.S_OK;
        }

        public int GetConnectionPointContainer(out IConnectionPointContainer ppCPC)
        {
            ppCPC = this;
            return OpcDaConstants.S_OK;
        }

        public int Advise(object pUnkSink, out int pdwCookie)
        {
            lock (_lock)
            {
                pdwCookie = _nextCallbackCookie++;
                _callbacks[pdwCookie] = pUnkSink;
            }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.Sleep(50);
                SendNotification(0, isRefresh: true);
            });
            return OpcDaConstants.S_OK;
        }

        public int Unadvise(int dwCookie)
        {
            lock (_lock)
            {
                _callbacks.Remove(dwCookie);
                return OpcDaConstants.S_OK;
            }
        }

        public int EnumConnections(out IntPtr ppEnum)
        {
            ppEnum = IntPtr.Zero;
            return OpcDaConstants.E_NOTIMPL;
        }
        #endregion

        private static short GetVarType(OpcDataType dt)
        {
            switch (dt)
            {
                case OpcDataType.Bool: return 11; // VT_BOOL
                case OpcDataType.Int16: return 2;  // VT_I2
                case OpcDataType.UInt16: return 18; // VT_UI2
                case OpcDataType.Int32: return 3;  // VT_I4
                case OpcDataType.UInt32: return 19; // VT_UI4
                case OpcDataType.Float: return 4;  // VT_R4
                default: return 8; // VT_BSTR
            }
        }

        private static object ConvertForVariant(object? val, OpcDataType dt)
        {
            if (val == null) return GetDefaultVariant(dt);
            try
            {
                switch (dt)
                {
                    case OpcDataType.Bool: return Convert.ToBoolean(val);
                    case OpcDataType.Int16: return Convert.ToInt16(val);
                    case OpcDataType.UInt16: return Convert.ToUInt16(val);
                    case OpcDataType.Int32: return Convert.ToInt32(val);
                    case OpcDataType.UInt32: return Convert.ToUInt32(val);
                    case OpcDataType.Float: return Convert.ToSingle(val);
                    default: return val.ToString() ?? "";
                }
            }
            catch
            {
                return GetDefaultVariant(dt);
            }
        }

        private static object GetDefaultVariant(OpcDataType dt)
        {
            switch (dt)
            {
                case OpcDataType.Bool: return false;
                case OpcDataType.Int16: return (short)0;
                case OpcDataType.UInt16: return (ushort)0;
                case OpcDataType.Int32: return 0;
                case OpcDataType.UInt32: return 0U;
                case OpcDataType.Float: return 0.0f;
                default: return "";
            }
        }
    }
    #endregion

    #region OPC String Enumerator for Browse
    public class OpcEnumString : IEnumString
    {
        private readonly List<string> _items;
        private int _index = 0;

        public OpcEnumString(List<string> items)
        {
            _items = items ?? new List<string>();
        }

        public int Next(int celt, IntPtr rgelt, IntPtr pceltFetched)
        {
            if (rgelt == IntPtr.Zero || celt <= 0) return OpcDaConstants.E_INVALIDARG;

            int fetched = 0;
            while (_index < _items.Count && fetched < celt)
            {
                IntPtr strPtr = Marshal.StringToCoTaskMemUni(_items[_index++]);
                Marshal.WriteIntPtr(rgelt, fetched * IntPtr.Size, strPtr);
                fetched++;
            }

            if (pceltFetched != IntPtr.Zero)
            {
                Marshal.WriteInt32(pceltFetched, fetched);
            }

            return fetched == celt ? OpcDaConstants.S_OK : OpcDaConstants.S_FALSE;
        }

        public int Skip(int celt)
        {
            _index = Math.Min(_items.Count, _index + celt);
            return OpcDaConstants.S_OK;
        }

        public int Reset()
        {
            _index = 0;
            return OpcDaConstants.S_OK;
        }

        public int Clone(out IEnumString ppenum)
        {
            ppenum = new OpcEnumString(new List<string>(_items));
            return OpcDaConstants.S_OK;
        }
    }
    #endregion
}
