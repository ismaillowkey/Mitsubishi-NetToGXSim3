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

            // Pengecekan proses ganda (mendeteksi juga jika ada instance NetToGXSim3 versi lama yang sedang berjalan)
            var currentProcess = Process.GetCurrentProcess();
            var runningInstances = Process.GetProcessesByName(currentProcess.ProcessName);
            bool isDuplicateProcess = false;

            foreach (var proc in runningInstances)
            {
                if (proc.Id != currentProcess.Id)
                {
                    isDuplicateProcess = true;
                    // Bawa window instance yang sedang berjalan ke layar depan
                    if (proc.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindowAsync(proc.MainWindowHandle, SW_RESTORE);
                        SetForegroundWindow(proc.MainWindowHandle);
                    }
                    break;
                }
            }

            if (!isMutexOwned || isDuplicateProcess)
            {
                MessageBox.Show(
                    "Aplikasi NetToGXSim3 sudah berjalan!\n\nHanya 1 instance yang diperbolehkan aktif pada saat yang sama.",
                    "NetToGXSim3 - Sudah Berjalan",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                if (_appMutex != null && isMutexOwned)
                {
                    try { _appMutex.ReleaseMutex(); } catch { }
                    _appMutex.Dispose();
                }
                _appMutex = null;

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
                    // Abaikan jika mutex sudah dilepas atau dihentikan paksa
                }
                _appMutex.Dispose();
                _appMutex = null;
            }

            base.OnExit(e);
        }
    }
}

