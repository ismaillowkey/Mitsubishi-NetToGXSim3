using System;
using System.Diagnostics;
using System.Windows;
using NetToGXSim3.Wpf.Services;

namespace NetToGXSim3.Wpf.Views
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            TxtVersionBadge.Text = AppVersion.DisplayVersion;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnGithub_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/ismaillowkey/Mitsubishi-NetToGXSim3",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void BtnWhatsNew_Click(object sender, RoutedEventArgs e)
        {
            var whatsNew = new WhatsNewWindow
            {
                Owner = this
            };
            whatsNew.ShowDialog();
        }

        private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            var result = await UpdateCheckerService.CheckForUpdatesAsync();
            if (result.Success && result.HasUpdate)
            {
                var answer = MessageBox.Show(
                    $"A newer version of NetToGXSim3 is available!\n\n" +
                    $"Current Version: v{UpdateCheckerService.CurrentVersion}\n" +
                    $"Latest Version: {result.LatestVersion}\n\n" +
                    $"Would you like to open the GitHub Releases page to download?",
                    "Update Available",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (answer == MessageBoxResult.Yes)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
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
                MessageBox.Show(
                    $"You are using the latest version of NetToGXSim3 (v{UpdateCheckerService.CurrentVersion})!",
                    "Up to Date",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
    }
}
