using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace NetToGXSim3.Wpf
{
    public partial class App : Application
    {
        private const string AppMutexName = @"Global\NetToGXSim3_SingleInstance_Mutex";
        private static Mutex? _appMutex;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        protected override void OnStartup(StartupEventArgs e)
        {
            bool isMutexOwned = false;

            try
            {
                _appMutex = new Mutex(true, AppMutexName, out isMutexOwned);
            }
            catch
            {
                isMutexOwned = false;
            }

            // Single-instance verification using Named Mutex
            if (!isMutexOwned)
            {
                // Find existing NetToGXSim3 instance and bring it to the foreground
                try
                {
                    var runningInstances = Process.GetProcessesByName("NetToGXSim3.Wpf");
                    foreach (var proc in runningInstances)
                    {
                        if (proc.Id != Process.GetCurrentProcess().Id && proc.MainWindowHandle != IntPtr.Zero)
                        {
                            ShowWindowAsync(proc.MainWindowHandle, SW_RESTORE);
                            SetForegroundWindow(proc.MainWindowHandle);
                            break;
                        }
                    }
                }
                catch { }

                MessageBox.Show(
                    "NetToGXSim3 is already running!\n\nOnly one active instance is allowed at a time.",
                    "NetToGXSim3 - Already Running",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                Shutdown();
                return;
            }

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_appMutex != null)
            {
                try
                {
                    _appMutex.ReleaseMutex();
                }
                catch
                {
                    // Ignore if mutex was already released or terminated
                }
                _appMutex.Dispose();
                _appMutex = null;
            }

            base.OnExit(e);
        }
    }
}

