using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Monity.App.Views;
using Monity.Infrastructure.Tracking;

namespace Monity.App;

public partial class MainWindow : Window
{
    private readonly UsageTrackingService _trackingService;
    private System.Windows.Forms.NotifyIcon? _trayIcon;

    public MainWindow()
    {
        InitializeComponent();
        _trackingService = ((App)System.Windows.Application.Current).Services.GetRequiredService<UsageTrackingService>();
        Loaded += MainWindow_Loaded;
        SetWindowIcon();
    }

    private void SetWindowIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Monity.App;component/Assets/Logo.png", UriKind.Absolute);
            Icon = BitmapFrame.Create(uri);
        }
        catch
        {
            // Logo yüklenemezse varsayılan simge kullanılır
        }
    }

    private IServiceProvider Services => ((App)System.Windows.Application.Current).Services;

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        MainFrame.Navigate(new DashboardPage(Services));
        SetupTrayIcon();
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Monity - App Usage Tracker",
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        };

        var ctx = new System.Windows.Forms.ContextMenuStrip();
        var openItem = new System.Windows.Forms.ToolStripMenuItem("Aç");
        openItem.Click += (_, _) => { Show(); WindowState = WindowState.Normal; Activate(); };
        var exitItem = new System.Windows.Forms.ToolStripMenuItem("Çıkış");
        exitItem.Click += (_, _) => System.Windows.Application.Current.Shutdown();
        ctx.Items.Add(openItem);
        ctx.Items.Add(exitItem);
        _trayIcon.ContextMenuStrip = ctx;
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        MainFrame.Navigate(new SettingsPage(Services));
    }

    private void BtnMinimizeToTray_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
        Hide();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    protected override void OnClosed(EventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnClosed(e);
    }
}
