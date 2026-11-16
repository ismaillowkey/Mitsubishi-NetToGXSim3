using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using NetToGXSim3.Core;
using NetToGXSim3.Wpf.Services;

namespace NetToGXSim3.Wpf.Views
{
    public partial class MainWindow : Window
    {
        private readonly GxSimulatorEngine _simEngine;
        private readonly McProtocolServer _mcServer1;
        private readonly McProtocolServer _mcServer2;
        private readonly OpcDaServerEngine _opcDaServer;
        private readonly OpcUaServerEngine _opcUaServer;
        private readonly ModbusTcpServer _modbusServer;
        private readonly ModbusConfig _modbusConfig;
        private bool _isInitializingModbus = true;

        public ObservableCollection<ModbusItemModel> ModbusCoilItems { get; } = new ObservableCollection<ModbusItemModel>();
        public ObservableCollection<ModbusItemModel> ModbusDiscreteItems { get; } = new ObservableCollection<ModbusItemModel>();
        public ObservableCollection<ModbusItemModel> ModbusHoldingItems { get; } = new ObservableCollection<ModbusItemModel>();
        public ObservableCollection<ModbusItemModel> ModbusInputItems { get; } = new ObservableCollection<ModbusItemModel>();

        private ScrollViewer? _svModbusCoil;
        private ScrollViewer? _svModbusDiscrete;
        private ScrollViewer? _svModbusHolding;
        private ScrollViewer? _svModbusInput;

        private readonly DispatcherTimer _pollTimer;
        private readonly DispatcherTimer _modbusUiTimer;

        private static readonly string[] OctalDeviceIndices = new string[]
        {
            "0", "1", "2", "3", "4", "5", "6", "7",
            "10", "11", "12", "13", "14", "15", "16", "17"
        };

        // UI components for Inputs X0 to X17 (16 octal inputs)
        private readonly ToggleButton[] _inputToggles = new ToggleButton[16];
        private readonly Ellipse[] _inputLeds = new Ellipse[16];
        private bool _isUpdatingInputsFromPlc = false;

        // UI components for Outputs Y0 to Y17 (16 octal outputs)
        private readonly Ellipse[] _outputLamps = new Ellipse[16];
        private readonly DropShadowEffect[] _outputGlows = new DropShadowEffect[16];
        private readonly TextBlock[] _outputStateTexts = new TextBlock[16];

        private bool _isPollingBusy = false;
        private DateTime _lastConnectAttempt = DateTime.MinValue;
        private DateTime _lastModbusTableUpdateTime = DateTime.MinValue;

        public MainWindow()
        {
            InitializeComponent();

            Title = $"NetToGXSim3 {AppVersion.DisplayVersion} - Mitsubishi GX Works 3 Simulator Network Bridge by Ismail Lowkey";
            TxtHeaderVersion.Text = AppVersion.DisplayVersion;
            TxtFooterVersion.Text = $"NetToGXSim3 {AppVersion.DisplayVersion}";

            _simEngine = new GxSimulatorEngine(1);
            _simEngine.LogMessage += (msg) => Dispatcher.InvokeAsync(() => Log(msg));

            InitializeInputRack();
            InitializeOutputRack();

            // Server 1 (Top): Port 5001 (Started by default, auto-increment if in use) -> MC TCP Binary Server
            int s1Port = McProtocolServer.GetNextAvailablePort(5001, ServerTransportMode.TcpOnly);
            _mcServer1 = new McProtocolServer(_simEngine, "MC TCP Server", s1Port, ServerTransportMode.TcpOnly, McCpuMode.FX5);
            _mcServer1.LogMessage += (msg) => Dispatcher.InvokeAsync(() => Log(msg));
            _mcServer1.Start(s1Port);
            UpdateServer1Ui();

            // Server 2 (Bottom): Port 5002 (Stopped by default, check available port) -> MC UDP Binary Server
            int s2Port = McProtocolServer.GetNextAvailablePort(5002, ServerTransportMode.UdpOnly);
            _mcServer2 = new McProtocolServer(_simEngine, "MC UDP Server", s2Port, ServerTransportMode.UdpOnly, McCpuMode.FX5);
            _mcServer2.LogMessage += (msg) => Dispatcher.InvokeAsync(() => Log(msg));
            _simEngine.ConnectionStateChanged += isConnected =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    UpdateSimStatusDisplay(isConnected);
                    if (!isConnected) ResetUiInputsAndOutputs();
                });
            };

            UpdateServer2Ui();

            // Initialize OPC DA Classic Server (Auto-start on app launch & auto-save enabled)
            _opcDaServer = new OpcDaServerEngine(_simEngine);
            _opcDaServer.OnLog += (msg) => Dispatcher.InvokeAsync(() => Log(msg));
            _opcDaServer.OnStateChanged += () => Dispatcher.InvokeAsync(UpdateOpcServerUI);
            DgOpcTags.ItemsSource = _opcDaServer.Tags;
            TxtTagStoragePath.Text = $"Storage: {System.IO.Path.GetFileName(_opcDaServer.StoragePath)}";
            TxtTagStoragePath.ToolTip = _opcDaServer.StoragePath;
            _opcDaServer.StartServer();
            UpdateOpcServerUI();

            // Initialize OPC UA Server (Auto-start on port 48400, anonymous allowed, mirrors OPC DA tags)
            _opcUaServer = new OpcUaServerEngine(_opcDaServer);
            _opcUaServer.OnLog += (msg) => Dispatcher.InvokeAsync(() => Log(msg));
            _opcUaServer.OnStateChanged += () => Dispatcher.InvokeAsync(UpdateOpcUaServerUI);
            DgOpcUaTags.ItemsSource = _opcDaServer.Tags;
            UpdateOpcUaServerUI();

            Task.Run(async () =>
            {
                await _opcUaServer.StartServerAsync(48400);
            });

            // Initialize Modbus TCP Server (Default port 5020, auto-start, auto-save modbus_config.json)
            _modbusConfig = ModbusConfig.Load();
            _modbusServer = new ModbusTcpServer(_simEngine, _modbusConfig);
            _modbusServer.LogMessage += (msg) => Dispatcher.InvokeAsync(() => Log(msg));
            _modbusServer.StateChanged += (running) => Dispatcher.InvokeAsync(UpdateModbusServerUI);
            _modbusServer.PortCollisionDetected += (oldPort, newPort) => Dispatcher.InvokeAsync(() =>
            {
                TxtModbusPort.Text = newPort.ToString();
                TxtModbusPortHeader.Text = $"(Port {newPort})";
            });
            _modbusServer.DeviceWritten += (device, val) => Dispatcher.InvokeAsync(() =>
            {
                if (_modbusServer != null)
                {
                    TxtModbusRequests.Text = _modbusServer.RequestsProcessed.ToString("N0");
                    TxtModbusClients.Text = _modbusServer.ConnectedClientsCount.ToString();
                }
                OnModbusDeviceWritten(device, val);
            });

            InitializeModbusTables();
            _isInitializingModbus = false;

            if (!System.IO.File.Exists(_modbusConfig.StoragePath))
            {
                _modbusConfig.Save();
            }

            if (_modbusConfig.AutoStart)
            {
                _modbusServer.Start();
            }
            UpdateModbusServerUI();

            // Auto-register OPC DA Server in Windows Registry on app startup so SCADA / OPC clients immediately detect it
            try
            {
                string currentExe = OpcRegistryHelper.ResolveCurrentExe(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "");
                if (!string.IsNullOrEmpty(currentExe))
                {
                    OpcRegistryHelper.RegisterServer(currentExe, out _);
                }
            }
            catch { }

            Task.Run(() =>
            {
                try
                {
                    _lastConnectAttempt = DateTime.UtcNow;
                    bool ok = _simEngine.Connect(1);
                    Dispatcher.InvokeAsync(() => UpdateSimStatusDisplay(ok));
                }
                catch (Exception ex)
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        UpdateSimStatusDisplay(false);
                        Log($"[WARNING] Initial connection attempt: {ex.Message}");
                    });
                }
            });

            // High-speed polling timer (30ms for 33 FPS real-time responsiveness)
            _pollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(30)
            };
            _pollTimer.Tick += PollTimer_Tick;
            _pollTimer.Start();

            // Dedicated 1-second Modbus UI timer (lightweight, isolated from PLC hardware polling)
            _modbusUiTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _modbusUiTimer.Tick += (s, e) =>
            {
                if (_modbusServer == null)
                {
                    return;
                }

                TxtModbusRequests.Text = _modbusServer.RequestsProcessed.ToString("N0");
                TxtModbusClients.Text = _modbusServer.ConnectedClientsCount.ToString();

                if (TabModbusTcpServer.IsSelected && _simEngine.IsConnected)
                {
                    UpdateModbusVisibleLiveValues();
                }
            };
            _modbusUiTimer.Start();

            // Auto-check for updates in background on startup
            Task.Run(async () =>
            {
                await Task.Delay(2000);
                var result = await UpdateCheckerService.CheckForUpdatesAsync();
                Dispatcher.Invoke(() =>
                {
                    if (result.Success)
                    {
                        if (result.HasUpdate)
                        {
                            TxtUpdateStatus.Text = $"Update available ({result.LatestVersion})";
                            TxtUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(2, 132, 199));
                            UpdateStatusLed.Fill = new SolidColorBrush(Color.FromRgb(2, 132, 199));

                            var answer = MessageBox.Show(
                                $"A new version of NetToGXSim3 is available!\n\n" +
                                $"Current Version: v{UpdateCheckerService.CurrentVersion}\n" +
                                $"Latest Version: {result.LatestVersion}\n\n" +
                                $"Would you like to open the download page to update now?",
                                "Update Available - NetToGXSim3",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Information);

                            if (answer == MessageBoxResult.Yes)
                            {
                                try
                                {
                                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                    {
                                        FileName = result.ReleaseUrl,
                                        UseShellExecute = true
                                    });
                                }
                                catch { }
                            }
                        }
                        else
                        {
                            TxtUpdateStatus.Text = "No update available";
                            TxtUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
                            UpdateStatusLed.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                            Log($"[UPDATE] Application is up to date (v{UpdateCheckerService.CurrentVersion}). No update available.");
                        }
                    }
                    else
                    {
                        TxtUpdateStatus.Text = "No update available";
                        TxtUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
                        UpdateStatusLed.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                    }
                });
            });

            Log($"NetToGXSim3 ready. MC TCP Server listening on port {_mcServer1.Port}.");
        }

        private void InitializeInputRack()
        {
            InputGrid.Children.Clear();

            for (int i = 0; i < 16; i++)
            {
                int bitIndex = i;
                string octalNum = OctalDeviceIndices[i];
                string devName = $"X{octalNum}";

                var card = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                    CornerRadius = new CornerRadius(6),
                    Margin = new Thickness(2),
                    Padding = new Thickness(4, 5, 4, 5),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                    BorderThickness = new Thickness(1)
                };

                var sp = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                var txtBit = new TextBlock
                {
                    Text = devName,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 3)
                };

                var led = new Ellipse
                {
                    Width = 7,
                    Height = 7,
                    Fill = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 4),
                    Effect = new DropShadowEffect
                    {
                        Color = Color.FromRgb(2, 132, 199),
                        BlurRadius = 6,
                        ShadowDepth = 0,
                        Opacity = 0
                    }
                };
                _inputLeds[bitIndex] = led;

                var btn = new ToggleButton
                {
                    Content = "OFF",
                    FontSize = 9.5,
                    FontWeight = FontWeights.Bold,
                    Padding = new Thickness(4, 2, 4, 2),
                    Height = 22,
                    MinWidth = 42,
                    Style = (Style)FindResource("ToggleInputStyle")
                };

                btn.Click += (s, e) =>
                {
                    if (_isUpdatingInputsFromPlc) return;

                    bool isChecked = btn.IsChecked ?? false;
                    btn.Content = isChecked ? "ON" : "OFF";
                    int val = isChecked ? 1 : 0;

                    // Immediately update local UI LED
                    _inputLeds[bitIndex].Fill = new SolidColorBrush(isChecked ? Color.FromRgb(37, 99, 235) : Color.FromRgb(203, 213, 225));
                    ((DropShadowEffect)_inputLeds[bitIndex].Effect).Opacity = isChecked ? 1 : 0;

                    Task.Run(() =>
                    {
                        _simEngine.WriteDevice(devName, val);
                        // Instant readback of Outputs after PLC ladder scan
                        System.Threading.Thread.Sleep(40);
                        byte[] yBits;
                        if (_simEngine.ReadDeviceBlockBits("Y0", 16, out yBits) == 0)
                        {
                            Dispatcher.InvokeAsync(() => UpdateOutputsUi(yBits));
                        }
                    });

                    Log($"[INPUT] Set {devName} = {val}");
                };

                _inputToggles[bitIndex] = btn;

                sp.Children.Add(txtBit);
                sp.Children.Add(led);
                sp.Children.Add(btn);
                card.Child = sp;
                InputGrid.Children.Add(card);
            }
        }

        private void UpdateOutputsUi(byte[] yBits)
        {
            if (yBits == null) return;
            for (int i = 0; i < Math.Min(16, yBits.Length); i++)
            {
                bool isOn = yBits[i] != 0;
                _outputLamps[i].Fill = new SolidColorBrush(isOn ? Color.FromRgb(34, 197, 94) : Color.FromRgb(203, 213, 225));
                _outputLamps[i].Stroke = new SolidColorBrush(isOn ? Color.FromRgb(22, 163, 74) : Color.FromRgb(148, 163, 184));
                _outputGlows[i].Opacity = isOn ? 1 : 0;
                _outputStateTexts[i].Text = isOn ? "ON" : "OFF";
                _outputStateTexts[i].Foreground = new SolidColorBrush(isOn ? Color.FromRgb(22, 163, 74) : Color.FromRgb(148, 163, 184));
            }
        }

        private void InitializeOutputRack()
        {
            OutputGrid.Children.Clear();

            for (int i = 0; i < 16; i++)
            {
                int bitIndex = i;
                string octalNum = OctalDeviceIndices[i];
                string devName = $"Y{octalNum}";

                var card = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                    CornerRadius = new CornerRadius(6),
                    Margin = new Thickness(2),
                    Padding = new Thickness(4, 5, 4, 5),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                    BorderThickness = new Thickness(1)
                };

                var sp = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                var txtBit = new TextBlock
                {
                    Text = devName,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 3)
                };

                var glow = new DropShadowEffect
                {
                    Color = Color.FromRgb(34, 197, 94),
                    BlurRadius = 8,
                    ShadowDepth = 0,
                    Opacity = 0
                };
                _outputGlows[bitIndex] = glow;

                var lamp = new Ellipse
                {
                    Width = 16,
                    Height = 16,
                    Fill = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                    Stroke = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    StrokeThickness = 1.2,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 3),
                    Effect = glow
                };
                _outputLamps[bitIndex] = lamp;

                var txtState = new TextBlock
                {
                    Text = "OFF",
                    FontSize = 9.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                _outputStateTexts[bitIndex] = txtState;

                sp.Children.Add(txtBit);
                sp.Children.Add(lamp);
                sp.Children.Add(txtState);
                card.Child = sp;
                OutputGrid.Children.Add(card);
            }
        }

        private async void PollTimer_Tick(object? sender, EventArgs e)
        {
            if (_isPollingBusy) return;
            _isPollingBusy = true;

            await Task.Run(() =>
            {
                try
                {
                    if (_simEngine.IsConnected)
                    {
                        // Proactive heartbeat: auto-detect if GX Simulator process closed or stopped (OFF)
                        if (!_simEngine.CheckConnectionAlive())
                        {
                            Dispatcher.InvokeAsync(() =>
                            {
                                UpdateSimStatusDisplay(false);
                                ResetUiInputsAndOutputs();
                            });
                            return;
                        }
                    }
                    else
                    {
                        // Proactive auto-detection when GX Simulator starts (ON)
                        if ((DateTime.UtcNow - _lastConnectAttempt).TotalSeconds < 1.5) return;
                        _lastConnectAttempt = DateTime.UtcNow;

                        bool reconnected = _simEngine.Connect(1);
                        Dispatcher.InvokeAsync(() => UpdateSimStatusDisplay(reconnected));
                        if (!reconnected) return;
                    }

                    // Batch read Inputs X0 to X17 (16 octal inputs)
                    byte[] xBits;
                    if (_simEngine.ReadDeviceBlockBits("X0", 16, out xBits) == 0)
                    {
                        Dispatcher.InvokeAsync(() =>
                        {
                            _isUpdatingInputsFromPlc = true;
                            try
                            {
                                for (int i = 0; i < Math.Min(16, xBits.Length); i++)
                                {
                                    bool isOn = xBits[i] != 0;
                                    if (_inputToggles[i].IsChecked != isOn)
                                    {
                                        _inputToggles[i].IsChecked = isOn;
                                        _inputToggles[i].Content = isOn ? "ON" : "OFF";
                                    }
                                    _inputLeds[i].Fill = new SolidColorBrush(isOn ? Color.FromRgb(37, 99, 235) : Color.FromRgb(203, 213, 225));
                                    ((DropShadowEffect)_inputLeds[i].Effect).Opacity = isOn ? 1 : 0;
                                }
                            }
                            finally
                            {
                                _isUpdatingInputsFromPlc = false;
                            }
                        });
                    }

                    // Batch read Outputs Y0 to Y17 (16 octal outputs)
                    byte[] yBits;
                    if (_simEngine.ReadDeviceBlockBits("Y0", 16, out yBits) == 0)
                    {
                        Dispatcher.InvokeAsync(() => UpdateOutputsUi(yBits));
                    }
                }
                catch { }
                finally
                {
                    _isPollingBusy = false;
                }
            });
        }

        private void UpdateSimStatusDisplay(bool isConnected)
        {
            SimStatusLed.Fill = new SolidColorBrush(isConnected ? Color.FromRgb(34, 197, 94) : Color.FromRgb(239, 68, 68));
            TxtSimStatus.Text = isConnected ? "GX Sim 3: Connected" : "GX Sim 3: Offline";
            TxtSimStatus.ToolTip = isConnected
                ? $"Status: Connected\nEngine: {_simEngine.ConnectedEngineName}\nProtocol: Real-time Memory Bridge"
                : "Status: Offline\nWaiting for GX Works 3 simulation (Debug -> Start Simulation)...";
            TxtSimStatus.Foreground = new SolidColorBrush(isConnected ? Color.FromRgb(22, 163, 74) : Color.FromRgb(220, 38, 38));
        }

        private void ResetUiInputsAndOutputs()
        {
            _isUpdatingInputsFromPlc = true;
            try
            {
                for (int i = 0; i < 16; i++)
                {
                    if (_inputToggles[i] != null)
                    {
                        _inputToggles[i].IsChecked = false;
                        _inputToggles[i].Content = "OFF";
                    }
                    if (_inputLeds[i] != null)
                    {
                        _inputLeds[i].Fill = new SolidColorBrush(Color.FromRgb(203, 213, 225));
                        if (_inputLeds[i].Effect is DropShadowEffect effect) effect.Opacity = 0;
                    }
                    if (_outputLamps[i] != null)
                    {
                        _outputLamps[i].Fill = new SolidColorBrush(Color.FromRgb(203, 213, 225));
                        _outputLamps[i].Stroke = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                    }
                    if (_outputGlows[i] != null) _outputGlows[i].Opacity = 0;
                    if (_outputStateTexts[i] != null)
                    {
                        _outputStateTexts[i].Text = "OFF";
                        _outputStateTexts[i].Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                    }
                }
            }
            finally
            {
                _isUpdatingInputsFromPlc = false;
            }
        }

        private void BtnReadCustom_Click(object sender, RoutedEventArgs e)
        {
            string addr = TxtCustomAddress.Text.Trim();
            if (string.IsNullOrEmpty(addr)) return;

            Task.Run(() =>
            {
                if (_simEngine.ReadDevice(addr, out int val) == 0)
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        TxtCustomResult.Text = $"Bit: [{(val != 0 ? "ON" : "OFF")}] | Value: {val} (0x{val:X4})";
                        Log($"[INSPECTOR] Read {addr} = {val}");
                    });
                }
                else
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        TxtCustomResult.Text = $"Error reading {addr}";
                        Log($"[INSPECTOR ERROR] Failed to read {addr}");
                    });
                }
            });
        }

        private void BtnToggleCustom_Click(object sender, RoutedEventArgs e)
        {
            string addr = TxtCustomAddress.Text.Trim();
            if (string.IsNullOrEmpty(addr)) return;

            Task.Run(() =>
            {
                if (_simEngine.ReadDevice(addr, out int val) == 0)
                {
                    int newVal = val != 0 ? 0 : 1;
                    _simEngine.WriteDevice(addr, newVal);
                    Dispatcher.InvokeAsync(() =>
                    {
                        TxtCustomResult.Text = $"Bit: [{(newVal != 0 ? "ON" : "OFF")}] | Value: {newVal}";
                        Log($"[INSPECTOR] Toggled {addr} -> {newVal}");
                    });
                }
            });
        }

        private void BtnWriteCustomWord_Click(object sender, RoutedEventArgs e)
        {
            string addr = TxtCustomAddress.Text.Trim();
            if (string.IsNullOrEmpty(addr)) return;

            if (int.TryParse(TxtWriteWordValue.Text.Trim(), out int val))
            {
                Task.Run(() =>
                {
                    if (_simEngine.WriteDevice(addr, val) == 0)
                    {
                        Dispatcher.InvokeAsync(() =>
                        {
                            TxtCustomResult.Text = $"Value Written: {val}";
                            Log($"[INSPECTOR] Wrote {addr} = {val}");
                        });
                    }
                });
            }
        }

        private const int MaxLogLines = 500;
        private int _logLineCount = 0;

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            TxtLog.Clear();
            _logLineCount = 0;
        }

        private void Log(string message)
        {
            string time = DateTime.Now.ToString("HH:mm:ss.fff");
            string entry = $"[{time}] {message}\n";

            if (_logLineCount >= MaxLogLines)
            {
                string text = TxtLog.Text;
                int newlineIndex = text.IndexOf('\n');
                if (newlineIndex >= 0)
                {
                    TxtLog.Text = text.Substring(newlineIndex + 1);
                }
                else
                {
                    TxtLog.Clear();
                    _logLineCount = 0;
                }
            }
            else
            {
                _logLineCount++;
            }

            TxtLog.AppendText(entry);
            TxtLog.ScrollToEnd();
        }

        private void BtnToggleServer1_Click(object sender, RoutedEventArgs e)
        {
            if (_mcServer1.IsRunning)
            {
                _mcServer1.Stop();
            }
            else
            {
                int port = 5001;
                if (int.TryParse(TxtPortServer1.Text.Trim(), out int p)) port = p;

                int availablePort = McProtocolServer.GetNextAvailablePort(port, ServerTransportMode.TcpOnly);
                if (availablePort != port)
                {
                    Log($"[MC TCP Server] Port {port} is occupied! Automatically switched to port {availablePort}.");
                }
                _mcServer1.CpuMode = McCpuMode.FX5;
                Log($"[MC TCP Server] CPU mode: FX5U (MC protocol: {_mcServer1.CpuModel}).");
                _mcServer1.Start(availablePort);
            }
            UpdateServer1Ui();
        }

        private void BtnToggleServer2_Click(object sender, RoutedEventArgs e)
        {
            if (_mcServer2.IsRunning)
            {
                _mcServer2.Stop();
            }
            else
            {
                int port = 5002;
                if (int.TryParse(TxtPortServer2.Text.Trim(), out int p)) port = p;

                int availablePort = McProtocolServer.GetNextAvailablePort(port, ServerTransportMode.UdpOnly);
                if (availablePort != port)
                {
                    Log($"[MC UDP Server] Port {port} is occupied! Automatically switched to port {availablePort}.");
                }
                _mcServer2.CpuMode = McCpuMode.FX5;
                Log($"[MC UDP Server] CPU mode: FX5U (MC protocol: {_mcServer2.CpuModel}).");
                _mcServer2.Start(availablePort);
            }
            UpdateServer2Ui();
        }

        private void UpdateServer1Ui()
        {
            TxtPortServer1.Text = _mcServer1.Port.ToString();
            TxtPortServer1.IsEnabled = !_mcServer1.IsRunning;
            BtnToggleServer1.Content = _mcServer1.IsRunning ? "Stop" : "Start";
            Server1Led.Fill = new SolidColorBrush(_mcServer1.IsRunning ? Color.FromRgb(34, 197, 94) : Color.FromRgb(148, 163, 184));
        }

        private void UpdateServer2Ui()
        {
            TxtPortServer2.Text = _mcServer2.Port.ToString();
            TxtPortServer2.IsEnabled = !_mcServer2.IsRunning;
            BtnToggleServer2.Content = _mcServer2.IsRunning ? "Stop" : "Start";
            Server2Led.Fill = new SolidColorBrush(_mcServer2.IsRunning ? Color.FromRgb(34, 197, 94) : Color.FromRgb(148, 163, 184));
        }

        private async void BtnExitGxSim_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Log("[ACTION] Terminating GX Simulator 3 (FSim3Dlg, FSimRun3, Sim3Dlg, IOSystem)...");

                // Disconnect COM engine first
                _simEngine.Disconnect();
                UpdateSimStatusDisplay(false);

                await Task.Run(() =>
                {
                    string[] targetProcesses = new[] { "FSim3Dlg", "FSimRun3", "Sim3Dlg", "RSimRun3", "LSimRun3", "GXS3SysSim", "GXS3IOSystem", "SimManager", "IOSystem" };
                    int killedCount = 0;

                    foreach (var procName in targetProcesses)
                    {
                        try
                        {
                            var processes = System.Diagnostics.Process.GetProcessesByName(procName);
                            foreach (var p in processes)
                            {
                                try
                                {
                                    p.Kill();
                                    p.WaitForExit(1000);
                                    killedCount++;
                                }
                                catch { }
                            }
                        }
                        catch { }

                        // Fallback using taskkill
                        try
                        {
                            var psi = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "taskkill",
                                Arguments = $"/F /IM {procName}.exe",
                                CreateNoWindow = true,
                                UseShellExecute = false
                            };
                            System.Diagnostics.Process.Start(psi)?.WaitForExit(1000);
                        }
                        catch { }
                    }

                    Dispatcher.InvokeAsync(() =>
                    {
                        if (killedCount > 0)
                        {
                            Log($"[SUCCESS] GX Simulator 3 terminated ({killedCount} process(es) closed).");
                        }
                        else
                        {
                            Log("[INFO] GX Simulator 3 closed.");
                        }
                        UpdateSimStatusDisplay(false);
                    });
                });
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Failed to exit GX Simulator 3: {ex.Message}");
            }
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void MenuToggleLog_Click(object sender, RoutedEventArgs e)
        {
            TabAdvancedInspector.Focus();
        }

        private void MenuGuide_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "NetToGXSim3 Guide:\n\n" +
                "1. Inputs X0-X7 can be toggled using switches in Tab 1.\n" +
                "2. Outputs Y0-Y7 display real-time PLC output states.\n" +
                "3. MC Protocol Servers: Server 1 (port 5001) and Server 2 (port 5002).\n" +
                "4. Ports auto-increment if conflict detected (e.g. 5001 -> 5003).\n",
                "User Guide", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnExampleHmi_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Ambil path folder Public Windows (default fallback: C:\Users\Public)
                string publicFolder = Environment.GetEnvironmentVariable("PUBLIC");
                if (string.IsNullOrEmpty(publicFolder))
                {
                    publicFolder = @"C:\Users\Public";
                }

                // Path target utama yang dituju di folder Public
                string publicTargetDir = System.IO.Path.Combine(publicFolder, "NetToGXSim3", "Example HMI");

                // Cari folder sumber example program di direktori aplikasi atau folder project
                string sourceDir = FindSourceExampleDirectory();

                // Jika folder Public belum ada, buat foldernya secara otomatis
                if (!System.IO.Directory.Exists(publicTargetDir))
                {
                    try
                    {
                        System.IO.Directory.CreateDirectory(publicTargetDir);
                    }
                    catch
                    {
                        // Jika gagal membuat direktori public karena izin tertentu, abaikan di sini
                    }
                }

                // Jika folder Public berhasil dibuat/ada dan isinya masih kosong, salin dari folder sumber
                if (System.IO.Directory.Exists(publicTargetDir))
                {
                    bool isTargetEmpty = false;
                    try
                    {
                        string[] entries = System.IO.Directory.GetFileSystemEntries(publicTargetDir);
                        if (entries.Length == 0)
                        {
                            isTargetEmpty = true;
                        }
                    }
                    catch
                    {
                        isTargetEmpty = false;
                    }

                    if (isTargetEmpty && !string.IsNullOrEmpty(sourceDir) && System.IO.Directory.Exists(sourceDir))
                    {
                        try
                        {
                            CopyDirectoryRecursive(sourceDir, publicTargetDir);
                        }
                        catch
                        {
                            // Abaikan error penyalinan, buka folder yang ada
                        }
                    }
                }

                // Tentukan folder yang akan dibuka oleh Explorer
                string targetToOpen = null;
                if (System.IO.Directory.Exists(publicTargetDir))
                {
                    targetToOpen = publicTargetDir;
                }
                else if (!string.IsNullOrEmpty(sourceDir) && System.IO.Directory.Exists(sourceDir))
                {
                    // Fallback ke folder sumber jika folder Public sama sekali tidak dapat diakses
                    targetToOpen = sourceDir;
                }

                if (!string.IsNullOrEmpty(targetToOpen) && System.IO.Directory.Exists(targetToOpen))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = targetToOpen,
                        UseShellExecute = true
                    });
                }
                else
                {
                    MessageBox.Show(
                        $"Example HMI folder was not found at:\n{publicTargetDir}",
                        "Folder Not Found",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to open Example HMI folder:\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static string FindSourceExampleDirectory()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // 1. Cek langsung di dalam base directory (mode portable)
            string candidateApp1 = System.IO.Path.Combine(baseDir, "Example HMI");
            if (System.IO.Directory.Exists(candidateApp1))
            {
                return candidateApp1;
            }

            string candidateApp2 = System.IO.Path.Combine(baseDir, "Example Program HMI");
            if (System.IO.Directory.Exists(candidateApp2))
            {
                return candidateApp2;
            }

            // 2. Cek di parent directories (saat running dev / dotnet run)
            string current = baseDir;
            for (int i = 0; i < 6; i++)
            {
                string parent = System.IO.Path.GetFullPath(System.IO.Path.Combine(current, ".."));
                if (parent == current)
                {
                    break;
                }
                current = parent;

                string candidateParent1 = System.IO.Path.Combine(current, "Example HMI");
                if (System.IO.Directory.Exists(candidateParent1))
                {
                    return candidateParent1;
                }

                string candidateParent2 = System.IO.Path.Combine(current, "Example Program HMI");
                if (System.IO.Directory.Exists(candidateParent2))
                {
                    return candidateParent2;
                }
            }

            return null;
        }

        private static void CopyDirectoryRecursive(string sourcePath, string targetPath)
        {
            if (!System.IO.Directory.Exists(targetPath))
            {
                System.IO.Directory.CreateDirectory(targetPath);
            }

            foreach (string file in System.IO.Directory.GetFiles(sourcePath))
            {
                string fileName = System.IO.Path.GetFileName(file);
                string destFile = System.IO.Path.Combine(targetPath, fileName);
                System.IO.File.Copy(file, destFile, true);
            }

            foreach (string dir in System.IO.Directory.GetDirectories(sourcePath))
            {
                string dirName = System.IO.Path.GetFileName(dir);
                string destDir = System.IO.Path.Combine(targetPath, dirName);
                CopyDirectoryRecursive(dir, destDir);
            }
        }

        private async void MenuCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            Mouse.OverrideCursor = Cursors.Wait;
            TxtUpdateStatus.Text = "Checking for updates...";
            TxtUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            UpdateStatusLed.Fill = new SolidColorBrush(Color.FromRgb(148, 163, 184));

            try
            {
                var result = await UpdateCheckerService.CheckForUpdatesAsync();
                if (result.Success)
                {
                    if (result.HasUpdate)
                    {
                        TxtUpdateStatus.Text = $"Update available ({result.LatestVersion})";
                        TxtUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(2, 132, 199));
                        UpdateStatusLed.Fill = new SolidColorBrush(Color.FromRgb(2, 132, 199));

                        var answer = MessageBox.Show(
                            $"A new version of NetToGXSim3 is available!\n\n" +
                            $"Current Version: v{UpdateCheckerService.CurrentVersion}\n" +
                            $"Latest Version: {result.LatestVersion}\n\n" +
                            $"Would you like to open the download page to update now?",
                            "Update Available - NetToGXSim3",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Information);

                        if (answer == MessageBoxResult.Yes)
                        {
                            try
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = result.ReleaseUrl,
                                    UseShellExecute = true
                                });
                            }
                            catch { }
                        }
                    }
                    else
                    {
                        TxtUpdateStatus.Text = "No update available";
                        TxtUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
                        UpdateStatusLed.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));

                        MessageBox.Show(
                            $"You are using the latest version of NetToGXSim3 (v{UpdateCheckerService.CurrentVersion}).",
                            "Check for Updates",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }
                else
                {
                    var answer = MessageBox.Show(
                        $"Unable to check for updates at this time:\n{result.ErrorMessage}\n\nWould you like to open the GitHub releases page manually?",
                        "Check for Updates",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (answer == MessageBoxResult.Yes)
                    {
                        try
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = UpdateCheckerService.ReleasesPageUrl,
                                UseShellExecute = true
                            });
                        }
                        catch { }
                    }
                }
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void MenuWhatsNew_Click(object sender, RoutedEventArgs e)
        {
            var popup = new WhatsNewWindow
            {
                Owner = this
            };
            popup.ShowDialog();
        }

        private void BtnViewReleasesGithub_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = UpdateCheckerService.ReleasesPageUrl,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private static void OpenUrl(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void MenuApp_NetToGXSim2_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://github.com/ismaillowkey/Mitsubishi-NetToGXSim2");
        }

        private void MenuApp_NetToGXSim3_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://github.com/ismaillowkey/Mitsubishi-NetToGXSim3");
        }

        private void MenuApp_OmronNetToCXSim_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://github.com/ismaillowkey/OmronNetToCXSim");
        }

        private void MenuApp_MPSPneumaticSimulator_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://github.com/ismaillowkey/MPSPneumaticSimulator");
        }

        private void MenuAbout_Click(object sender, RoutedEventArgs e)
        {
            var about = new AboutWindow
            {
                Owner = this
            };
            about.ShowDialog();
        }

        #region OPC DA Classic Server Handlers

        private void UpdateOpcServerUI()
        {
            if (_opcDaServer == null) return;

            if (_opcDaServer.IsRunning)
            {
                TxtOpcServerStatus.Text = "OPC DA SERVER: RUNNING (AUTO-STARTED)";
                TxtOpcServerStatus.Foreground = new SolidColorBrush(Color.FromRgb(4, 120, 87));
                OpcStatusLed.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                BtnStartOpcServer.Visibility = Visibility.Collapsed;
                BtnStopOpcServer.Visibility = Visibility.Visible;
            }
            else
            {
                TxtOpcServerStatus.Text = "OPC DA SERVER: STOPPED";
                TxtOpcServerStatus.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                OpcStatusLed.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                BtnStartOpcServer.Visibility = Visibility.Visible;
                BtnStopOpcServer.Visibility = Visibility.Collapsed;
            }

            TxtOpcClientsCount.Text = _opcDaServer.ClientCount.ToString();
            TxtOpcTotalTags.Text = _opcDaServer.Tags.Count.ToString();
            if (TxtOpcUaTotalTags != null)
            {
                TxtOpcUaTotalTags.Text = _opcDaServer.Tags.Count.ToString();
            }
        }

        private void BtnStartOpcServer_Click(object sender, RoutedEventArgs e)
        {
            _opcDaServer.StartServer();
            UpdateOpcServerUI();
        }

        private void BtnStopOpcServer_Click(object sender, RoutedEventArgs e)
        {
            _opcDaServer.StopServer();
            UpdateOpcServerUI();
        }

        private void BtnRegisterOpcServer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string currentExe = OpcRegistryHelper.ResolveCurrentExe(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "");
                if (string.IsNullOrEmpty(currentExe) || !System.IO.File.Exists(currentExe))
                {
                    MessageBox.Show("Could not find application executable path for registration.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Check if running as Administrator
                bool isAdmin = false;
                try
                {
                    using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                    {
                        var principal = new System.Security.Principal.WindowsPrincipal(identity);
                        isAdmin = principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                    }
                }
                catch { }

                if (!isAdmin)
                {
                    // Trigger UAC prompt to elevate registration
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = currentExe,
                            Arguments = "/regserver",
                            Verb = "runas",
                            UseShellExecute = true
                        };
                        var proc = System.Diagnostics.Process.Start(psi);
                        proc?.WaitForExit();

                        if (OpcRegistryHelper.IsRegisteredInHklm())
                        {
                            MessageBox.Show("NetToGXSim3 OPC DA Server has been registered successfully with Administrator privileges in Windows System Registry (HKLM)!\n\nProgID: NetToGXSim3.OPCServer.DA\n\nOPC clients (OPC Expert, Kepware, Matrikon) will now detect the server.",
                                            "OPC Registration Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                            return;
                        }
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        MessageBox.Show("Administrator privileges are required to register OPC DA Server system-wide for OPC clients (OpcEnum).\n\nPlease approve the Windows UAC prompt or run NetToGXSim3 as Administrator.",
                                        "UAC Permission Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                if (OpcRegistryHelper.RegisterServer(currentExe, out string error))
                {
                    MessageBox.Show("NetToGXSim3 OPC DA Server has been registered successfully in Windows COM Registry!\n\nProgID: NetToGXSim3.OPCServer.DA",
                                    "OPC Registration Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Failed to register server: {error}\n\nTip: Run NetToGXSim3 as Administrator to write to Windows HKLM.",
                                    "OPC Registration Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error registering OPC Server: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnAddOpcTag_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new TagEditWindow();
            dlg.Owner = this;
            if (dlg.ShowDialog() == true)
            {
                _opcDaServer.AddTag(dlg.TagItem);
                UpdateOpcServerUI();
                Log($"Tag added: {dlg.TagItem.TagName} -> {dlg.TagItem.Address} ({dlg.TagItem.DataType})");
            }
        }

        private void BtnRowEditTag_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            OpcTagItem? tag = null;
            if (btn != null && btn.Tag is OpcTagItem tagFromBtn)
            {
                tag = tagFromBtn;
            }
            else if (DgOpcTags.SelectedItem is OpcTagItem tagFromGrid)
            {
                tag = tagFromGrid;
            }

            if (tag == null) return;

            var dlg = new TagEditWindow(tag);
            dlg.Owner = this;
            if (dlg.ShowDialog() == true)
            {
                tag.TagName = dlg.TagItem.TagName;
                tag.Address = dlg.TagItem.Address;
                tag.DataType = dlg.TagItem.DataType;
                tag.Description = dlg.TagItem.Description;
                tag.IsActive = dlg.TagItem.IsActive;

                _opcDaServer.SaveTags();
                UpdateOpcServerUI();
                Log($"Tag updated: {tag.TagName} -> {tag.Address}");
            }
        }

        private void BtnRowDeleteTag_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            OpcTagItem? tag = null;
            if (btn != null && btn.Tag is OpcTagItem tagFromBtn)
            {
                tag = tagFromBtn;
            }
            else if (DgOpcTags.SelectedItem is OpcTagItem tagFromGrid)
            {
                tag = tagFromGrid;
            }

            if (tag == null) return;

            var r = MessageBox.Show($"Are you sure you want to delete tag '{tag.TagName}' ({tag.Address})?", "Confirm Delete Tag", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r == MessageBoxResult.Yes)
            {
                _opcDaServer.RemoveTag(tag);
                UpdateOpcServerUI();
                Log($"Tag deleted: {tag.TagName}");
            }
        }

        private void BtnResetOpcPresets_Click(object sender, RoutedEventArgs e)
        {
            var r = MessageBox.Show("Reset the tag list to default Mitsubishi FX5U / iQ-R presets (X0..X17, Y0..Y17, M0..M7, D0..D3, R0, W0)?\nCurrent changes will be overwritten.", "Reset Presets", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r == MessageBoxResult.Yes)
            {
                _opcDaServer.LoadDefaultPresets();
                UpdateOpcServerUI();
                Log("Tags reset to default Mitsubishi FX5U / iQ-R presets.");
            }
        }

        private void BtnExportOpcCsv_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV File (*.csv)|*.csv",
                FileName = "NetToGXSim3_OpcTags.csv"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    string csv = _opcDaServer.ExportCsv();
                    System.IO.File.WriteAllText(sfd.FileName, csv);
                    MessageBox.Show("Tags exported to CSV successfully!", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnImportOpcCsv_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "CSV File (*.csv)|*.csv"
            };

            if (ofd.ShowDialog() == true)
            {
                try
                {
                    string csv = System.IO.File.ReadAllText(ofd.FileName);
                    _opcDaServer.ImportCsv(csv);
                    UpdateOpcServerUI();
                    MessageBox.Show("Tags imported from CSV successfully!", "Import Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Import failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void DgOpcTags_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = DgOpcTags.SelectedItem as OpcTagItem;
            if (selected != null)
            {
                TxtOpcSelectedTag.Text = $"{selected.TagName} ({selected.Address}, {selected.DataType})";
                if (selected.Value != null)
                {
                    TxtOpcWriteValue.Text = selected.Value.ToString();
                }
                else
                {
                    TxtOpcWriteValue.Text = "0";
                }
            }
            else
            {
                TxtOpcSelectedTag.Text = "None (select a row)";
            }
        }

        private void DgOpcTags_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            BtnRowEditTag_Click(sender, e);
        }

        private void BtnOpcWrite_Click(object sender, RoutedEventArgs e)
        {
            var selected = DgOpcTags.SelectedItem as OpcTagItem;
            if (selected == null)
            {
                MessageBox.Show("Please select a tag from the table first!", "Warning", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string valStr = "";
            if (TxtOpcWriteValue.Text != null)
            {
                valStr = TxtOpcWriteValue.Text.Trim();
            }

            if (string.IsNullOrEmpty(valStr)) return;

            bool ok = _opcDaServer.WriteTagToPlc(selected, valStr);
            if (ok)
            {
                Log($"[OPC Test Write] Wrote '{valStr}' to tag '{selected.TagName}' ({selected.Address})");
            }
            else
            {
                MessageBox.Show("Failed to write to GX Simulator 3!\nMake sure GX Simulator 3 is running and connected.", "Write Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnOpcToggle_Click(object sender, RoutedEventArgs e)
        {
            var selected = DgOpcTags.SelectedItem as OpcTagItem;
            if (selected == null)
            {
                MessageBox.Show("Please select a tag from the table first!", "Warning", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (selected.DataType != OpcDataType.Bool)
            {
                MessageBox.Show("Toggle only works for Bool tags!", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool current = false;
            if (selected.Value is bool b)
            {
                current = b;
            }
            else if (selected.Value != null && bool.TryParse(selected.Value.ToString(), out bool pb))
            {
                current = pb;
            }

            bool next = !current;
            bool ok = _opcDaServer.WriteTagToPlc(selected, next);
            if (ok)
            {
                TxtOpcWriteValue.Text = next.ToString();
                Log($"[OPC Test Toggle] Toggled '{selected.TagName}' ({selected.Address}) to {next}");
            }
            else
            {
                MessageBox.Show("Failed to toggle tag on GX Simulator 3!\nMake sure GX Simulator 3 is running and connected.", "Toggle Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        #endregion

        #region OPC UA Server Handlers

        private void UpdateOpcUaServerUI()
        {
            if (_opcUaServer == null)
            {
                return;
            }

            if (_opcUaServer.IsRunning)
            {
                TxtOpcUaServerStatus.Text = "OPC UA SERVER: RUNNING (AUTO-STARTED)";
                TxtOpcUaServerStatus.Foreground = new SolidColorBrush(Color.FromRgb(4, 120, 87));
                OpcUaStatusLed.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                BtnStartOpcUaServer.Visibility = Visibility.Collapsed;
                BtnStopOpcUaServer.Visibility = Visibility.Visible;
            }
            else
            {
                TxtOpcUaServerStatus.Text = "OPC UA SERVER: STOPPED";
                TxtOpcUaServerStatus.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                OpcUaStatusLed.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                BtnStartOpcUaServer.Visibility = Visibility.Visible;
                BtnStopOpcUaServer.Visibility = Visibility.Collapsed;
            }

            TxtOpcUaEndpoint.Text = _opcUaServer.ActiveEndpoint;
            TxtOpcUaNetworkEndpoint.Text = _opcUaServer.NetworkEndpoint;
            if (!TxtOpcUaPort.IsFocused)
            {
                TxtOpcUaPort.Text = _opcUaServer.ActivePort.ToString();
            }
            TxtOpcUaSessionsCount.Text = _opcUaServer.SessionCount.ToString();
            TxtOpcUaTotalTags.Text = _opcDaServer.Tags.Count.ToString();

            if (!string.IsNullOrEmpty(_opcUaServer.PortCollisionNotice))
            {
                BorderOpcUaPortNotice.Visibility = Visibility.Visible;
                TxtOpcUaPortNotice.Text = _opcUaServer.PortCollisionNotice;
            }
            else
            {
                BorderOpcUaPortNotice.Visibility = Visibility.Collapsed;
            }
        }

        private async void BtnStartOpcUaServer_Click(object sender, RoutedEventArgs e)
        {
            int port = _opcUaServer.ActivePort;
            if (int.TryParse(TxtOpcUaPort.Text.Trim(), out int customPort))
            {
                if (customPort >= 1024 && customPort <= 65535)
                {
                    port = customPort;
                }
            }
            await _opcUaServer.StartServerAsync(port);
            UpdateOpcUaServerUI();
        }

        private void BtnOpcUaPortUp_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtOpcUaPort.Text.Trim(), out int port))
            {
                if (port < 65535)
                {
                    port++;
                    TxtOpcUaPort.Text = port.ToString();
                    if (_opcUaServer.IsRunning)
                    {
                        Task.Run(async () =>
                        {
                            await _opcUaServer.RestartServerAsync(port);
                            Dispatcher.Invoke(UpdateOpcUaServerUI);
                        });
                    }
                }
            }
        }

        private void BtnOpcUaPortDown_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtOpcUaPort.Text.Trim(), out int port))
            {
                if (port > 1024)
                {
                    port--;
                    TxtOpcUaPort.Text = port.ToString();
                    if (_opcUaServer.IsRunning)
                    {
                        Task.Run(async () =>
                        {
                            await _opcUaServer.RestartServerAsync(port);
                            Dispatcher.Invoke(UpdateOpcUaServerUI);
                        });
                    }
                }
            }
        }

        private void BtnStopOpcUaServer_Click(object sender, RoutedEventArgs e)
        {
            _opcUaServer.StopServer();
            UpdateOpcUaServerUI();
        }

        private void BtnCopyUaEndpoint_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(_opcUaServer.ActiveEndpoint);
                MessageBox.Show($"OPC UA Endpoint URL copied to clipboard:\n{_opcUaServer.ActiveEndpoint}",
                                "Endpoint Copied", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to copy endpoint:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSwitchToOpcDaTab_Click(object sender, RoutedEventArgs e)
        {
            TabOpcDaServer.IsSelected = true;
        }

        #endregion

        #region Modbus TCP Server Management & Data Mapping

        /// <summary>
        /// Inisialisasi awal seluruh tabel pemetaan Modbus TCP (Coil, Discrete, Holding, Input).
        /// </summary>
        private void InitializeModbusTables()
        {
            TxtModbusPort.Text = _modbusConfig.Port.ToString();
            TxtModbusPortHeader.Text = $"(Port {_modbusConfig.Port})";

            if (_modbusConfig.IsZeroBased)
            {
                ChkZeroBasedAddressing.IsChecked = true;
                BadgeZeroBased.Text = "Zero-Based Addressing: ON";
            }
            else
            {
                ChkZeroBasedAddressing.IsChecked = false;
                BadgeZeroBased.Text = "Zero-Based Addressing: OFF (1-Based)";
            }

            // 1. Inisialisasi Coil (0x): Y0 s/d Y1999 (2000 titik) + M0 s/d M7679 (7680 titik) = 9680 titik
            ModbusCoilItems.Clear();
            for (int i = 0; i < 2000; i++)
            {
                int modbusAddr = i;
                if (!_modbusConfig.IsZeroBased)
                {
                    modbusAddr = i + 1;
                }

                string octalStr = Convert.ToString(i, 8);
                string plcDevice = "Y" + octalStr;
                string description = string.Empty;
                if (_modbusConfig.Descriptions.TryGetValue(plcDevice, out string? d))
                {
                    description = d;
                }

                ModbusCoilItems.Add(new ModbusItemModel
                {
                    No = i + 1,
                    ModbusAddress = modbusAddr,
                    PlcDevice = plcDevice,
                    Description = description,
                    ValueText = "0 (OFF)"
                });
            }

            for (int i = 0; i < 7680; i++)
            {
                int modbusAddr = 2000 + i;
                if (!_modbusConfig.IsZeroBased)
                {
                    modbusAddr = 2000 + i + 1;
                }

                string plcDevice = "M" + i;
                string description = string.Empty;
                if (_modbusConfig.Descriptions.TryGetValue(plcDevice, out string? d))
                {
                    description = d;
                }

                ModbusCoilItems.Add(new ModbusItemModel
                {
                    No = 2000 + i + 1,
                    ModbusAddress = modbusAddr,
                    PlcDevice = plcDevice,
                    Description = description,
                    ValueText = "0 (OFF)"
                });
            }

            // 2. Inisialisasi Discrete Input (1x): X0 s/d X2999 (3000 titik, READ ONLY)
            ModbusDiscreteItems.Clear();
            for (int i = 0; i < 3000; i++)
            {
                int modbusAddr = i;
                if (!_modbusConfig.IsZeroBased)
                {
                    modbusAddr = i + 1;
                }

                string octalStr = Convert.ToString(i, 8);
                string plcDevice = "X" + octalStr;
                string description = string.Empty;
                if (_modbusConfig.Descriptions.TryGetValue(plcDevice, out string? d))
                {
                    description = d;
                }

                ModbusDiscreteItems.Add(new ModbusItemModel
                {
                    No = i + 1,
                    ModbusAddress = modbusAddr,
                    PlcDevice = plcDevice,
                    Description = description,
                    ValueText = "0 (OFF)"
                });
            }

            // 3. Inisialisasi Holding Register (4x): D0 s/d D5999 (6000 register, READ/WRITE)
            ModbusHoldingItems.Clear();
            for (int i = 0; i < 6000; i++)
            {
                int modbusAddr = i;
                if (!_modbusConfig.IsZeroBased)
                {
                    modbusAddr = i + 1;
                }

                string plcDevice = "D" + i;
                string description = string.Empty;
                if (_modbusConfig.Descriptions.TryGetValue("Holding_" + plcDevice, out string? d))
                {
                    description = d;
                }
                else if (_modbusConfig.Descriptions.TryGetValue(plcDevice, out string? d2))
                {
                    description = d2;
                }

                ModbusHoldingItems.Add(new ModbusItemModel
                {
                    No = i + 1,
                    ModbusAddress = modbusAddr,
                    PlcDevice = plcDevice,
                    Description = description,
                    ValueText = "0"
                });
            }

            // 4. Inisialisasi Input Register (3x): D0 s/d D5999 (6000 register, READ ONLY)
            ModbusInputItems.Clear();
            for (int i = 0; i < 6000; i++)
            {
                int modbusAddr = i;
                if (!_modbusConfig.IsZeroBased)
                {
                    modbusAddr = i + 1;
                }

                string plcDevice = "D" + i;
                string description = string.Empty;
                if (_modbusConfig.Descriptions.TryGetValue("Input_" + plcDevice, out string? d))
                {
                    description = d;
                }
                else if (_modbusConfig.Descriptions.TryGetValue(plcDevice, out string? d2))
                {
                    description = d2;
                }

                ModbusInputItems.Add(new ModbusItemModel
                {
                    No = i + 1,
                    ModbusAddress = modbusAddr,
                    PlcDevice = plcDevice,
                    Description = description,
                    ValueText = "0"
                });
            }

            // Hubungkan koleksi data ke DataGrid masing-masing
            DgModbusCoil.ItemsSource = ModbusCoilItems;
            DgModbusDiscrete.ItemsSource = ModbusDiscreteItems;
            DgModbusHolding.ItemsSource = ModbusHoldingItems;
            DgModbusInput.ItemsSource = ModbusInputItems;

            // Pasang event LoadingRow untuk auto-refresh live value saat baris muncul di layar
            DgModbusCoil.LoadingRow += DgModbus_LoadingRow;
            DgModbusDiscrete.LoadingRow += DgModbus_LoadingRow;
            DgModbusHolding.LoadingRow += DgModbus_LoadingRow;
            DgModbusInput.LoadingRow += DgModbus_LoadingRow;
        }

        /// <summary>
        /// Membaca nilai aktual dari GX Simulator 3 secara instan saat baris dimuat di layar UI.
        /// </summary>
        private void DgModbus_LoadingRow(object? sender, DataGridRowEventArgs e)
        {
            if (e.Row.Item is ModbusItemModel item)
            {
                RefreshItemLiveValue(item);
            }
        }

        /// <summary>
        /// Memperbarui nilai live item individual dari memori GX Simulator 3.
        /// </summary>
        private void RefreshItemLiveValue(ModbusItemModel item)
        {
            if (!_simEngine.IsConnected || string.IsNullOrEmpty(item.PlcDevice))
            {
                return;
            }

            try
            {
                int res = _simEngine.ReadDevice(item.PlcDevice, out int val);
                if (res == 0)
                {
                    if (item.PlcDevice.StartsWith("X") || item.PlcDevice.StartsWith("Y") || item.PlcDevice.StartsWith("M"))
                    {
                        if (val != 0)
                        {
                            item.ValueText = "1 (ON)";
                        }
                        else
                        {
                            item.ValueText = "0 (OFF)";
                        }
                    }
                    else
                    {
                        item.ValueText = ((short)val).ToString();
                    }
                }
            }
            catch
            {
                // Abaikan kesalahan pembacaan jika simulator sedang transisi offline
            }
        }

        /// <summary>
        /// Handler pembaruan instan saat ada penulisan device dari client Modbus TCP (Modbus Poll / SCADA).
        /// Memperbarui nilai pada baris tabel secara seketika (zero-latency feedback).
        /// </summary>
        private void OnModbusDeviceWritten(string device, int val)
        {
            if (string.IsNullOrEmpty(device))
            {
                return;
            }

            if (device.StartsWith("M"))
            {
                // Relay M berada pada index coil mulai dari 2000
                if (int.TryParse(device.Substring(1), out int mIdx))
                {
                    int coilIdx = 2000 + mIdx;
                    if (coilIdx >= 0 && coilIdx < ModbusCoilItems.Count)
                    {
                        if (val != 0)
                        {
                            ModbusCoilItems[coilIdx].ValueText = "1 (ON)";
                        }
                        else
                        {
                            ModbusCoilItems[coilIdx].ValueText = "0 (OFF)";
                        }
                    }
                }
            }
            else if (device.StartsWith("Y"))
            {
                // Relay Y menggunakan penomoran oktal pada PLC Mitsubishi
                try
                {
                    int yDec = Convert.ToInt32(device.Substring(1), 8);
                    if (yDec >= 0 && yDec < 2000 && yDec < ModbusCoilItems.Count)
                    {
                        if (val != 0)
                        {
                            ModbusCoilItems[yDec].ValueText = "1 (ON)";
                        }
                        else
                        {
                            ModbusCoilItems[yDec].ValueText = "0 (OFF)";
                        }
                    }
                }
                catch
                {
                    // Alamat oktal tidak valid
                }
            }
            else if (device.StartsWith("D"))
            {
                if (int.TryParse(device.Substring(1), out int dIdx))
                {
                    string textVal = ((short)val).ToString();
                    if (dIdx >= 0 && dIdx < ModbusHoldingItems.Count)
                    {
                        ModbusHoldingItems[dIdx].ValueText = textVal;
                    }
                    if (dIdx >= 0 && dIdx < ModbusInputItems.Count)
                    {
                        ModbusInputItems[dIdx].ValueText = textVal;
                    }
                }
            }
        }

        /// <summary>
        /// Mencari ScrollViewer internal dari kontrol DataGrid untuk mendeteksi baris mana yang sedang terlihat di layar.
        /// </summary>
        private static ScrollViewer? GetScrollViewer(DependencyObject? depObj)
        {
            if (depObj == null)
            {
                return null;
            }

            if (depObj is ScrollViewer sv)
            {
                return sv;
            }

            int childrenCount = VisualTreeHelper.GetChildrenCount(depObj);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                var result = GetScrollViewer(child);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        /// <summary>
        /// Memperbarui nilai baris yang sedang aktif dibuka pengguna di layar (misal M0 s/d M20).
        /// Dijalankan setiap 1 detik sekali sehingga sangat ringan dan tidak membebani proses.
        /// </summary>
        private void RefreshVisibleItems(DataGrid dg, ObservableCollection<ModbusItemModel> items, ref ScrollViewer? cachedSv)
        {
            if (items == null || items.Count == 0)
            {
                return;
            }

            if (cachedSv == null)
            {
                cachedSv = GetScrollViewer(dg);
            }

            int startIndex = 0;
            int count = Math.Min(25, items.Count);

            if (cachedSv != null)
            {
                // Pada DataGrid dengan ScrollViewer.CanContentScroll="True", VerticalOffset adalah indeks item baris teratas
                int offset = (int)cachedSv.VerticalOffset;
                int viewport = (int)Math.Ceiling(cachedSv.ViewportHeight);
                if (viewport <= 0)
                {
                    viewport = 25;
                }

                // Cukup perbarui baris yang sedang tampak di layar (misal 20-25 baris aktif)
                startIndex = Math.Max(0, offset);
                int endIndex = Math.Min(items.Count, offset + viewport);
                count = endIndex - startIndex;
            }

            for (int i = 0; i < count; i++)
            {
                int idx = startIndex + i;
                if (idx >= 0 && idx < items.Count)
                {
                    RefreshItemLiveValue(items[idx]);
                }
            }
        }

        /// <summary>
        /// Sinkronisasi berkala untuk nilai live baris yang sedang aktif ditampilkan di sub-tab Modbus.
        /// Mendeteksi posisi scroll pengguna sehingga baris berapapun (termasuk M0 di baris 2000) selalu terbarui.
        /// </summary>
        private void UpdateModbusVisibleLiveValues()
        {
            if (!TabModbusTcpServer.IsSelected)
            {
                return;
            }

            if (_modbusServer != null)
            {
                TxtModbusRequests.Text = _modbusServer.RequestsProcessed.ToString("N0");
                TxtModbusClients.Text = _modbusServer.ConnectedClientsCount.ToString();
            }

            if (!_simEngine.IsConnected)
            {
                return;
            }

            int subIndex = TabControlModbusSub.SelectedIndex;

            if (subIndex == 0)
            {
                // Sub-tab Coil (Y & M)
                RefreshVisibleItems(DgModbusCoil, ModbusCoilItems, ref _svModbusCoil);
            }
            else if (subIndex == 1)
            {
                // Sub-tab Discrete Input (X)
                RefreshVisibleItems(DgModbusDiscrete, ModbusDiscreteItems, ref _svModbusDiscrete);
            }
            else if (subIndex == 2)
            {
                // Sub-tab Holding Register (D)
                RefreshVisibleItems(DgModbusHolding, ModbusHoldingItems, ref _svModbusHolding);
            }
            else if (subIndex == 3)
            {
                // Sub-tab Input Register (D)
                RefreshVisibleItems(DgModbusInput, ModbusInputItems, ref _svModbusInput);
            }
        }

        /// <summary>
        /// Handler perubahan pengaturan Zero-Based Addressing.
        /// Menyimpan konfigurasi dan memperbarui kolom alamat Modbus di seluruh 4 tabel.
        /// </summary>
        private void ChkZeroBasedAddressing_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializingModbus)
            {
                return;
            }

            bool isZeroBased = false;
            if (ChkZeroBasedAddressing.IsChecked.HasValue && ChkZeroBasedAddressing.IsChecked.Value)
            {
                isZeroBased = true;
            }

            _modbusConfig.IsZeroBased = isZeroBased;

            if (isZeroBased)
            {
                BadgeZeroBased.Text = "Zero-Based Addressing: ON";
            }
            else
            {
                BadgeZeroBased.Text = "Zero-Based Addressing: OFF (1-Based)";
            }

            RecalculateModbusAddresses(isZeroBased);
            _modbusConfig.Save();
        }

        /// <summary>
        /// Menghitung ulang seluruh alamat Modbus berdasarkan mode Zero-Based atau 1-Based.
        /// </summary>
        private void RecalculateModbusAddresses(bool isZeroBased)
        {
            // Coil
            for (int i = 0; i < ModbusCoilItems.Count; i++)
            {
                if (isZeroBased)
                {
                    ModbusCoilItems[i].ModbusAddress = ModbusCoilItems[i].No - 1;
                }
                else
                {
                    ModbusCoilItems[i].ModbusAddress = ModbusCoilItems[i].No;
                }
            }

            // Discrete Input
            for (int i = 0; i < ModbusDiscreteItems.Count; i++)
            {
                if (isZeroBased)
                {
                    ModbusDiscreteItems[i].ModbusAddress = ModbusDiscreteItems[i].No - 1;
                }
                else
                {
                    ModbusDiscreteItems[i].ModbusAddress = ModbusDiscreteItems[i].No;
                }
            }

            // Holding Register
            for (int i = 0; i < ModbusHoldingItems.Count; i++)
            {
                if (isZeroBased)
                {
                    ModbusHoldingItems[i].ModbusAddress = ModbusHoldingItems[i].No - 1;
                }
                else
                {
                    ModbusHoldingItems[i].ModbusAddress = ModbusHoldingItems[i].No;
                }
            }

            // Input Register
            for (int i = 0; i < ModbusInputItems.Count; i++)
            {
                if (isZeroBased)
                {
                    ModbusInputItems[i].ModbusAddress = ModbusInputItems[i].No - 1;
                }
                else
                {
                    ModbusInputItems[i].ModbusAddress = ModbusInputItems[i].No;
                }
            }
        }

        /// <summary>
        /// Handler saat pengguna mengedit kolom Keterangan di tabel Modbus.
        /// Hasil edit langsung disimpan secara otomatis ke file modbus_config.json.
        /// </summary>
        private void DgModbus_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.Row.Item is ModbusItemModel item)
            {
                Dispatcher.InvokeAsync(async () =>
                {
                    await Task.Delay(50);
                    if (!string.IsNullOrEmpty(item.PlcDevice))
                    {
                        string key = item.PlcDevice;
                        if (sender == DgModbusInput)
                        {
                            key = "Input_" + item.PlcDevice;
                        }
                        else if (sender == DgModbusHolding)
                        {
                            key = "Holding_" + item.PlcDevice;
                        }

                        _modbusConfig.Descriptions[key] = item.Description ?? string.Empty;
                        _modbusConfig.Save();
                    }
                });
            }
        }

        /// <summary>
        /// Memperbarui tampilan kartu kontrol Modbus TCP Server di UI.
        /// </summary>
        private void UpdateModbusServerUI()
        {
            if (_modbusServer == null)
            {
                return;
            }

            if (_modbusServer.IsRunning)
            {
                TxtModbusServerStatus.Text = "MODBUS TCP SERVER: RUNNING (AUTO-STARTED)";
                TxtModbusServerStatus.Foreground = new SolidColorBrush(Color.FromRgb(4, 120, 87));
                ModbusStatusLed.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                BtnStartModbusServer.Visibility = Visibility.Collapsed;
                BtnStopModbusServer.Visibility = Visibility.Visible;
            }
            else
            {
                TxtModbusServerStatus.Text = "MODBUS TCP SERVER: STOPPED";
                TxtModbusServerStatus.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                ModbusStatusLed.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                BtnStartModbusServer.Visibility = Visibility.Visible;
                BtnStopModbusServer.Visibility = Visibility.Collapsed;
            }

            TxtModbusRequests.Text = _modbusServer.RequestsProcessed.ToString("N0");
            TxtModbusClients.Text = _modbusServer.ConnectedClientsCount.ToString();
            TxtModbusPortHeader.Text = $"(Port {_modbusServer.ActivePort})";
        }

        private void BtnStartModbusServer_Click(object sender, RoutedEventArgs e)
        {
            _modbusServer.Start();
            UpdateModbusServerUI();
        }

        private void BtnStopModbusServer_Click(object sender, RoutedEventArgs e)
        {
            _modbusServer.Stop();
            UpdateModbusServerUI();
        }

        private void BtnModbusPortUp_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtModbusPort.Text, out int port))
            {
                if (port < 65535)
                {
                    port++;
                    TxtModbusPort.Text = port.ToString();
                }
            }
        }

        private void BtnModbusPortDown_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtModbusPort.Text, out int port))
            {
                if (port > 1)
                {
                    port--;
                    TxtModbusPort.Text = port.ToString();
                }
            }
        }

        private void TxtModbusPort_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializingModbus)
            {
                return;
            }

            if (int.TryParse(TxtModbusPort.Text, out int port))
            {
                if (port > 0 && port <= 65535)
                {
                    _modbusConfig.Port = port;
                    TxtModbusPortHeader.Text = $"(Port {port})";
                    _modbusConfig.Save();
                }
            }
        }

        #endregion

        protected override void OnClosed(EventArgs e)
        {
            _pollTimer.Stop();
            _modbusUiTimer.Stop();
            _mcServer1?.Stop();
            _mcServer2?.Stop();
            _opcDaServer?.Dispose();
            _opcUaServer?.Dispose();
            _modbusServer?.Dispose();
            _simEngine?.Dispose();
            base.OnClosed(e);
        }
    }
}
