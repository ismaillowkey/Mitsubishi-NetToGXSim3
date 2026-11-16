#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Server;

namespace NetToGXSim3.Core
{
    /// <summary>
    /// Engine OPC UA Server modern untuk NetToGXSim3.
    /// Mengimplementasikan standard Opc.Ua.Server.StandardServer dengan auto-start,
    /// dynamic port conflict resolution, anonymous authentication, dan sinkronisasi tag
    /// dua arah (mirror) dengan tag OPC DA Classic.
    /// </summary>
    public sealed class OpcUaServerEngine : StandardServer, IDisposable
    {
        private static OpcUaServerEngine? _instance;
        public static OpcUaServerEngine? Instance => _instance;

        private readonly OpcDaServerEngine? _daEngine;
        private ApplicationInstance? _appInstance;
        private ApplicationConfiguration? _appConfig;
        private NetToGX3NodeManager? _nodeManager;

        private volatile bool _isRunning = false;
        private int _activePort = 48400;
        private string? _portCollisionNotice = null;
        private DateTime _startedAt = DateTime.MinValue;

        public event Action<string>? OnLog;
        public event Action? OnStateChanged;

        public bool IsRunning => _isRunning;
        public int ActivePort => _activePort;
        public string? PortCollisionNotice => _portCollisionNotice;
        public DateTime StartedAt => _startedAt;

        public string ActiveEndpoint
        {
            get
            {
                return "opc.tcp://localhost:" + _activePort + "/nettogxsim3";
            }
        }

        public string NetworkEndpoint
        {
            get
            {
                string host = Dns.GetHostName();
                if (string.IsNullOrWhiteSpace(host))
                {
                    host = "localhost";
                }
                return "opc.tcp://" + host + ":" + _activePort + "/nettogxsim3";
            }
        }

        public int SessionCount
        {
            get
            {
                if (!_isRunning)
                {
                    return 0;
                }

                try
                {
                    if (CurrentInstance != null && CurrentInstance.SessionManager != null)
                    {
                        return CurrentInstance.SessionManager.GetSessions().Count;
                    }
                }
                catch
                {
                    // Abaikan kesalahan saat mengakses session
                }

                return 0;
            }
        }

        public ObservableCollection<OpcTagItem> Tags
        {
            get
            {
                if (_daEngine != null)
                {
                    return _daEngine.Tags;
                }
                return new ObservableCollection<OpcTagItem>();
            }
        }

        public OpcUaServerEngine(OpcDaServerEngine? daEngine = null)
        {
            _instance = this;
            _daEngine = daEngine;

            // Sambungkan event listener untuk sinkronisasi nilai live tag ke node OPC UA
            if (_daEngine != null)
            {
                _daEngine.Tags.CollectionChanged += OnDaTagsCollectionChanged;
            }
        }

        private void OnDaTagsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_nodeManager != null)
            {
                _nodeManager.RefreshTagNodes();
            }
        }

        /// <summary>
        /// Memperbarui nilai live node di OPC UA saat scanner OPC DA membaca data dari PLC.
        /// </summary>
        public void SyncTagValue(OpcTagItem tag)
        {
            if (_nodeManager != null && _isRunning)
            {
                _nodeManager.UpdateTagValue(tag);
            }
        }

        protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            List<INodeManager> nodeManagers = new List<INodeManager>();

            // Daftarkan Node Manager khusus NetToGXSim3
            _nodeManager = new NetToGX3NodeManager(server, configuration, _daEngine);
            nodeManagers.Add(_nodeManager);

            return new MasterNodeManager(server, configuration, null, nodeManagers.ToArray());
        }

        protected override ISessionManager CreateSessionManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            ISessionManager sessionManager = base.CreateSessionManager(server, configuration);
            if (sessionManager is SessionManager sm)
            {
                sm.ImpersonateUser += OnImpersonateUser;
            }
            return sessionManager;
        }

        private void OnImpersonateUser(Opc.Ua.Server.ISession session, ImpersonateEventArgs args)
        {
            // Izinkan token identitas Anonymous secara default
            if (args.NewIdentity is AnonymousIdentityToken)
            {
                args.Identity = new UserIdentity(new AnonymousIdentityToken());
                return;
            }

            // Izinkan juga jika user menyertakan username
            if (args.NewIdentity is UserNameIdentityToken userNameToken)
            {
                args.Identity = new UserIdentity(userNameToken);
                return;
            }
        }

        /// <summary>
        /// Memeriksa ketersediaan TCP port.
        /// </summary>
        public static bool IsPortAvailable(int port)
        {
            bool isAvailable = true;

            try
            {
                IPGlobalProperties ipGlobal = IPGlobalProperties.GetIPGlobalProperties();
                IPEndPoint[] tcpListeners = ipGlobal.GetActiveTcpListeners();

                for (int i = 0; i < tcpListeners.Length; i++)
                {
                    if (tcpListeners[i].Port == port)
                    {
                        isAvailable = false;
                        break;
                    }
                }

                if (isAvailable)
                {
                    TcpListener testListener = new TcpListener(IPAddress.Any, port);
                    try
                    {
                        testListener.Start();
                    }
                    finally
                    {
                        testListener.Stop();
                    }
                }
            }
            catch
            {
                isAvailable = false;
            }

            return isAvailable;
        }

        /// <summary>
        /// Memulai OPC UA Server dengan resolusi tabrakan port otomatis.
        /// </summary>
        public async Task<bool> StartServerAsync(int requestedPort = 48400)
        {
            if (_isRunning)
            {
                return true;
            }

            try
            {
                Log("Starting OPC UA Server on requested port " + requestedPort + "...");

                // 1. Resolusi Tabrakan Port (Port Collision Resolution)
                int resolvedPort = requestedPort;
                bool portCollisionDetected = false;

                while (!IsPortAvailable(resolvedPort) && resolvedPort < 65535)
                {
                    portCollisionDetected = true;
                    resolvedPort++;
                }

                _activePort = resolvedPort;

                if (portCollisionDetected)
                {
                    _portCollisionNotice = "Port " + requestedPort + " was occupied by another process. Automatically switched to port " + _activePort + ".";
                    Log("[WARNING] " + _portCollisionNotice);
                }
                else
                {
                    _portCollisionNotice = null;
                }

                // 2. Siapkan direktori PKI untuk penyimpanan sertifikat
                string pkiRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetToGXSim3", "pki");
                if (!Directory.Exists(pkiRoot))
                {
                    Directory.CreateDirectory(pkiRoot);
                }

                string baseAddress = "opc.tcp://0.0.0.0:" + _activePort + "/nettogxsim3";

                // 3. Konfigurasi User Token Policies (Default: Anonymous)
                UserTokenPolicyCollection userTokenPolicies = new UserTokenPolicyCollection();
                userTokenPolicies.Add(new UserTokenPolicy(UserTokenType.Anonymous)
                {
                    PolicyId = "Anonymous"
                });
                userTokenPolicies.Add(new UserTokenPolicy(UserTokenType.UserName)
                {
                    PolicyId = "UserName"
                });

                // 4. Bangun ApplicationConfiguration OPC UA
                ApplicationConfiguration appConfig = new ApplicationConfiguration
                {
                    ApplicationName = "NetToGXSim3 OPC UA Server",
                    ApplicationUri = "urn:" + Dns.GetHostName().ToLowerInvariant() + ":NetToGXSim3:OPCUAServer",
                    ApplicationType = ApplicationType.Server,
                    ServerConfiguration = new ServerConfiguration
                    {
                        BaseAddresses = { baseAddress },
                        MinRequestThreadCount = 4,
                        MaxRequestThreadCount = 50,
                        MaxQueuedRequestCount = 1000,
                        DiagnosticsEnabled = false,
                        SecurityPolicies =
                        {
                            new ServerSecurityPolicy
                            {
                                SecurityMode = MessageSecurityMode.None,
                                SecurityPolicyUri = SecurityPolicies.None
                            },
                            new ServerSecurityPolicy
                            {
                                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                            }
                        },
                        UserTokenPolicies = userTokenPolicies
                    },
                    SecurityConfiguration = new SecurityConfiguration
                    {
                        ApplicationCertificate = new CertificateIdentifier
                        {
                            StoreType = CertificateStoreType.Directory,
                            StorePath = Path.Combine(pkiRoot, "own"),
                            SubjectName = "CN=NetToGXSim3 OPC UA Server, O=Ismail Lowkey"
                        },
                        AutoAcceptUntrustedCertificates = true,
                        RejectSHA1SignedCertificates = false,
                        MinimumCertificateKeySize = 1024,
                        TrustedPeerCertificates = new CertificateTrustList
                        {
                            StoreType = CertificateStoreType.Directory,
                            StorePath = Path.Combine(pkiRoot, "trusted")
                        },
                        TrustedIssuerCertificates = new CertificateTrustList
                        {
                            StoreType = CertificateStoreType.Directory,
                            StorePath = Path.Combine(pkiRoot, "issuer")
                        },
                        RejectedCertificateStore = new CertificateTrustList
                        {
                            StoreType = CertificateStoreType.Directory,
                            StorePath = Path.Combine(pkiRoot, "rejected")
                        }
                    },
                    TransportQuotas = new TransportQuotas
                    {
                        OperationTimeout = 15000,
                        MaxStringLength = 1048576,
                        MaxByteStringLength = 1048576,
                        MaxArrayLength = 65535,
                        MaxMessageSize = 4194304,
                        MaxBufferSize = 65535,
                        ChannelLifetime = 300000,
                        SecurityTokenLifetime = 3600000
                    }
                };

                await appConfig.ValidateAsync(ApplicationType.Server);

                // Auto-accept sertifikat klien eksternal (misal UaExpert / Node-RED)
                if (appConfig.CertificateValidator != null)
                {
                    appConfig.CertificateValidator.CertificateValidation += (sender, eventArgs) =>
                    {
                        eventArgs.Accept = true;
                    };
                }

                _appConfig = appConfig;

                // 5. Inisialisasi ApplicationInstance dan cek/buat sertifikat
#pragma warning disable CS0618
                _appInstance = new ApplicationInstance
                {
                    ApplicationName = appConfig.ApplicationName,
                    ApplicationType = ApplicationType.Server,
                    ApplicationConfiguration = appConfig
                };
#pragma warning restore CS0618

                await _appInstance.CheckApplicationInstanceCertificatesAsync(silent: true);

                // 6. Jalankan server
                await _appInstance.StartAsync(this);

                _isRunning = true;
                _startedAt = DateTime.Now;

                Log("[SUCCESS] OPC UA Server started successfully at " + ActiveEndpoint);
                OnStateChanged?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                _isRunning = false;
                Log("[ERROR] Failed to start OPC UA Server: " + ex.Message);
                OnStateChanged?.Invoke();
                return false;
            }
        }

        /// <summary>
        /// Menghentikan OPC UA Server.
        /// </summary>
        public void StopServer()
        {
            if (!_isRunning)
            {
                return;
            }

            try
            {
                Log("Stopping OPC UA Server...");
                if (_appInstance != null)
                {
                    _appInstance.Stop();
                }
                _isRunning = false;
                Log("[OK] OPC UA Server stopped.");
                OnStateChanged?.Invoke();
            }
            catch (Exception ex)
            {
                Log("[ERROR] Error while stopping OPC UA Server: " + ex.Message);
            }
        }

        /// <summary>
        /// Me-restart server dengan opsi port baru.
        /// </summary>
        public async Task<bool> RestartServerAsync(int newPort = 48400)
        {
            StopServer();
            await Task.Delay(200);
            return await StartServerAsync(newPort);
        }

        private void Log(string message)
        {
            OnLog?.Invoke("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopServer();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// NodeManager khusus NetToGXSim3 yang meng-expose tag dari Central Tag Engine (OPC DA).
    /// </summary>
    public sealed class NetToGX3NodeManager : CustomNodeManager2
    {
        public const string NamespaceUri = "http://ismaillowkey.com/NetToGXSim3";

        private readonly OpcDaServerEngine? _daEngine;
        private readonly ConcurrentDictionary<string, BaseDataVariableState> _tagNodes = new ConcurrentDictionary<string, BaseDataVariableState>();
        private FolderState? _rootFolder;

        public NetToGX3NodeManager(IServerInternal server, ApplicationConfiguration configuration, OpcDaServerEngine? daEngine)
            : base(server, configuration, NamespaceUri)
        {
            _daEngine = daEngine;
            SystemContext.NodeIdFactory = this;
        }

        public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            lock (Lock)
            {
                base.CreateAddressSpace(externalReferences);

                // 1. Buat folder root "NetToGXSim3" di bawah folder Objects
                _rootFolder = CreateFolder(null, "NetToGXSim3", "NetToGXSim3", "NetToGXSim3");
                _rootFolder.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);

                IList<IReference>? references;
                if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out references))
                {
                    references = new List<IReference>();
                    externalReferences[ObjectIds.ObjectsFolder] = references;
                }
                references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, _rootFolder.NodeId));

                AddPredefinedNode(SystemContext, _rootFolder);

                // 2. Muat seluruh tag yang ada di OPC DA Server
                PopulateTagNodes();
            }
        }

        private void PopulateTagNodes()
        {
            if (_daEngine == null || _rootFolder == null)
            {
                return;
            }

            foreach (OpcTagItem tag in _daEngine.Tags)
            {
                CreateAndRegisterTagVariable(_rootFolder, tag);
            }
        }

        public void RefreshTagNodes()
        {
            lock (Lock)
            {
                if (_rootFolder == null || _daEngine == null)
                {
                    return;
                }

                // Cek tag yang belum terdaftar di OPC UA
                foreach (OpcTagItem tag in _daEngine.Tags)
                {
                    if (!_tagNodes.ContainsKey(tag.TagName))
                    {
                        CreateAndRegisterTagVariable(_rootFolder, tag);
                    }
                }
            }
        }

        private void CreateAndRegisterTagVariable(FolderState parent, OpcTagItem tag)
        {
            BaseDataVariableState variable = new BaseDataVariableState(parent);

            string nodeIdStr = "NetToGXSim3." + tag.TagName;
            variable.SymbolicName = tag.TagName;
            variable.ReferenceTypeId = ReferenceTypeIds.Organizes;
            variable.TypeDefinitionId = VariableTypeIds.BaseDataVariableType;
            variable.NodeId = new NodeId(nodeIdStr, NamespaceIndex);
            variable.BrowseName = new QualifiedName(tag.TagName, NamespaceIndex);
            variable.DisplayName = new LocalizedText("en", tag.TagName);
            variable.Description = new LocalizedText("en", "PLC Device: " + tag.Address + " (" + tag.DataType + ")");
            variable.WriteMask = AttributeWriteMask.DisplayName | AttributeWriteMask.Description;
            variable.UserWriteMask = AttributeWriteMask.DisplayName | AttributeWriteMask.Description;

            // Mapping DataType
            NodeId dataTypeId = MapDataTypeToOpcUa(tag.DataType);
            variable.DataType = dataTypeId;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.Historizing = false;

            // Nilai awal default
            variable.Value = ParseValueForOpcUa(tag.DataType, tag.Value?.ToString());
            variable.StatusCode = StatusCodes.Good;
            variable.Timestamp = DateTime.UtcNow;

            // Handler Write dari klien OPC UA: Langsung teruskan ke memory PLC
            variable.OnSimpleWriteValue = (ISystemContext context, NodeState node, ref object val) =>
            {
                if (_daEngine != null)
                {
                    string strVal = "";
                    if (val != null)
                    {
                        strVal = val.ToString() ?? "";
                    }

                    _daEngine.WriteTagToPlc(tag, strVal);
                }
                return ServiceResult.Good;
            };

            parent.AddChild(variable);
            parent.AddReference(ReferenceTypeIds.Organizes, false, variable.NodeId);
            variable.AddReference(ReferenceTypeIds.Organizes, true, parent.NodeId);

            AddPredefinedNode(SystemContext, variable);
            _tagNodes[tag.TagName] = variable;
        }

        public void UpdateTagValue(OpcTagItem tag)
        {
            if (_tagNodes.TryGetValue(tag.TagName, out BaseDataVariableState? variable))
            {
                lock (Lock)
                {
                    variable.Value = ParseValueForOpcUa(tag.DataType, tag.Value?.ToString());
                    
                    if (tag.Quality == "Good")
                    {
                        variable.StatusCode = StatusCodes.Good;
                    }
                    else
                    {
                        variable.StatusCode = StatusCodes.BadWaitingForInitialData;
                    }

                    variable.Timestamp = DateTime.UtcNow;
                    variable.ClearChangeMasks(SystemContext, true);
                }
            }
        }

        private FolderState CreateFolder(NodeState? parent, string browseName, string displayName, string customNodeId)
        {
            FolderState folder = new FolderState(parent);
            folder.SymbolicName = browseName;
            folder.ReferenceTypeId = ReferenceTypeIds.Organizes;
            folder.TypeDefinitionId = ObjectTypeIds.FolderType;

            folder.NodeId = new NodeId(customNodeId, NamespaceIndex);
            folder.BrowseName = new QualifiedName(browseName, NamespaceIndex);
            folder.DisplayName = new LocalizedText("en", displayName);
            folder.WriteMask = AttributeWriteMask.None;
            folder.UserWriteMask = AttributeWriteMask.None;
            folder.EventNotifier = EventNotifiers.None;

            if (parent != null)
            {
                parent.AddReference(ReferenceTypeIds.Organizes, false, folder.NodeId);
                folder.AddReference(ReferenceTypeIds.Organizes, true, parent.NodeId);
            }

            return folder;
        }

        private static NodeId MapDataTypeToOpcUa(OpcDataType dataType)
        {
            if (dataType == OpcDataType.Bool)
            {
                return DataTypeIds.Boolean;
            }
            else if (dataType == OpcDataType.Int16)
            {
                return DataTypeIds.Int16;
            }
            else if (dataType == OpcDataType.UInt16)
            {
                return DataTypeIds.UInt16;
            }
            else if (dataType == OpcDataType.Int32)
            {
                return DataTypeIds.Int32;
            }
            else if (dataType == OpcDataType.UInt32)
            {
                return DataTypeIds.UInt32;
            }
            else if (dataType == OpcDataType.Float)
            {
                return DataTypeIds.Float;
            }
            else
            {
                return DataTypeIds.String;
            }
        }

        private static object ParseValueForOpcUa(OpcDataType dataType, string? rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue) || rawValue == "---")
            {
                return GetDefaultValueForType(dataType);
            }

            string val = rawValue.Trim();

            if (dataType == OpcDataType.Bool)
            {
                if (val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase) || val.Equals("on", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                return false;
            }
            else if (dataType == OpcDataType.Int16)
            {
                if (short.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out short s))
                {
                    return s;
                }
            }
            else if (dataType == OpcDataType.UInt16)
            {
                if (ushort.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out ushort us))
                {
                    return us;
                }
            }
            else if (dataType == OpcDataType.Int32)
            {
                if (int.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out int i))
                {
                    return i;
                }
            }
            else if (dataType == OpcDataType.UInt32)
            {
                if (uint.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out uint ui))
                {
                    return ui;
                }
            }
            else if (dataType == OpcDataType.Float)
            {
                if (float.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out float f))
                {
                    return f;
                }
            }

            return val;
        }

        private static object GetDefaultValueForType(OpcDataType dataType)
        {
            if (dataType == OpcDataType.Bool)
            {
                return false;
            }
            else if (dataType == OpcDataType.Int16)
            {
                return (short)0;
            }
            else if (dataType == OpcDataType.UInt16)
            {
                return (ushort)0;
            }
            else if (dataType == OpcDataType.Int32)
            {
                return 0;
            }
            else if (dataType == OpcDataType.UInt32)
            {
                return (uint)0;
            }
            else if (dataType == OpcDataType.Float)
            {
                return 0.0f;
            }
            else
            {
                return string.Empty;
            }
        }
    }
}
