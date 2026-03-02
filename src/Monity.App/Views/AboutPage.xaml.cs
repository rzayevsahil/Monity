using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace Monity.App.Views
{
    public partial class AboutPage : Page
    {
        public AboutPage()
        {
            InitializeComponent();
            LoadDynamicContent();
        }

        private void LoadDynamicContent()
        {
            // Load version information dynamically
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version != null)
            {
                TxtVersion.Text = $"v{version.Major}.{version.Minor}.{version.Build}";
            }
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            // Navigate back to previous page
            if (NavigationService?.CanGoBack == true)
            {
                NavigationService.GoBack();
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            // Open URL in default browser
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                // Log error if needed
                System.Diagnostics.Debug.WriteLine($"Failed to open URL: {ex.Message}");
            }
        }
    }
}