using System;
using System.Diagnostics;
using System.Windows;
using NetToGXSim3.Wpf.Services;

namespace NetToGXSim3.Wpf.Views
{
    /// <summary>
    /// Interaction logic for WhatsNewWindow.xaml
    /// </summary>
    public partial class WhatsNewWindow : Window
    {
        public WhatsNewWindow()
        {
            InitializeComponent();
            TxtHeaderVersion.Text = AppVersion.DisplayVersion;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnViewGithub_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = UpdateCheckerService.ReleasesPageUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open browser: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
