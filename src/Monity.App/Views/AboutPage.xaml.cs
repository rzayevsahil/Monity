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

        private void EmailLink_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "mailto:sahilrzayev200d@gmail.com",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to open email: {ex.Message}");
            }
        }

        private void GitHubLink_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/rzayevsahil",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to open GitHub: {ex.Message}");
            }
        }

        private void LinkedInLink_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://linkedin.com/in/sahilrzayev",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to open LinkedIn: {ex.Message}");
            }
        }
    }
}