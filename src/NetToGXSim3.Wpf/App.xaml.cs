using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace NetToGXSim3.Wpf
{
    public partial class App : Application
    {
        private const string AppMutexName = @"Local\NetToGXSim3_SingleInstance_Mutex";
        private static Mutex? _appMutex;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                try
                {
                    System.IO.File.WriteAllText("app_error.log", args.ExceptionObject?.ToString() ?? "Unknown exception");
                }
                catch { }
            };

            DispatcherUnhandledException += (s, args) =>
            {
                try
                {
                    System.IO.File.WriteAllText("app_error.log", args.Exception?.ToString() ?? "Unknown dispatcher exception");
                }
                catch { }
            };

            bool isMutexOwned = false;

            try
            {
                _appMutex = new Mutex(true, AppMutexName, out isMutexOwned);
            }
            catch (AbandonedMutexException)
            {
                // Previous instance was closed or killed; mutex ownership is now granted to us
                isMutexOwned = true;
            }
            catch
            {
                // Fallback: Izinkan aplikasi tetap berjalan normal jika terjadi batasan hak akses mutex OS
                isMutexOwned = true;
            }

            // Single-instance verification: only block if another NetToGXSim3 process is actually alive
            var currentProc = Process.GetCurrentProcess();
            var runningInstances = Process.GetProcessesByName("NetToGXSim3.Wpf");
            bool anotherInstanceExists = false;
            foreach (var proc in runningInstances)
            {
                if (proc.Id != currentProc.Id)
                {
                    anotherInstanceExists = true;
                    try
                    {
                        if (proc.MainWindowHandle != IntPtr.Zero)
                        {
                            ShowWindowAsync(proc.MainWindowHandle, SW_RESTORE);
                            SetForegroundWindow(proc.MainWindowHandle);
                        }
                    }
                    catch { }
                    break;
                }
            }

            if (!isMutexOwned && anotherInstanceExists)
            {
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

