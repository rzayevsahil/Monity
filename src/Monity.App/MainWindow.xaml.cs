using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Monity.App.Helpers;
using Monity.App.Services;
using Monity.App.Views;
using Monity.Infrastructure.Persistence;
using Monity.Infrastructure.Tracking;

namespace Monity.App;

public partial class MainWindow : Window
{
    private readonly UsageTrackingService _trackingService;
    private readonly UpdateService _updateService;
    private readonly IUsageRepository _repository;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private DispatcherTimer? _trayUsageTimer;
    private UpdateService.UpdateCheckResult? _pendingUpdate;

    public MainWindow()
    {
        InitializeComponent();
        var services = ((App)System.Windows.Application.Current).Services;
        _trackingService = services.GetRequiredService<UsageTrackingService>();
        _updateService = services.GetRequiredService<UpdateService>();
        _repository = services.GetRequiredService<IUsageRepository>();
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
        SetupFooter();
        _updateService.EnsureUpdaterInPlace();
        _ = CheckForUpdatesAsync();
    }

    private void SetupFooter()
    {
        TxtFooterVersion.Text = $"Monity v{AppVersion.Current}";
        LinkFooterDeveloper.Inlines.Clear();
        LinkFooterDeveloper.Inlines.Add(new Run(AboutConfig.DeveloperName));
        LinkFooterDeveloper.NavigateUri = new Uri(AboutConfig.DeveloperUrl);
    }

    private void LinkFooter_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { }
        e.Handled = true;
    }

    private async System.Threading.Tasks.Task CheckForUpdatesAsync()
    {
        var result = await _updateService.CheckForUpdateAsync();
        if (result == null) return;
        _pendingUpdate = result;
        await Dispatcher.InvokeAsync(() =>
        {
            TxtUpdateMessage.Text = $"Yeni sürüm mevcut ({result.Version})";
            UpdateBanner.Visibility = Visibility.Visible;
        });
    }

    private async void BtnUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate == null) return;
        BtnUpdate.IsEnabled = false;
        BtnUpdate.Content = "İndiriliyor...";
        var progress = new Progress<int>(pct => { BtnUpdate.Content = pct < 100 ? $"İndiriliyor... %{pct}" : "Kuruluyor..."; });
        var ok = await _updateService.DownloadAndApplyUpdateAsync(_pendingUpdate.Version, _pendingUpdate.DownloadUrl, progress);
        if (ok)
        {
            System.Windows.MessageBox.Show(
                Strings.Get("Msg_UpdateDownloaded"),
                Strings.Get("Msg_Update"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            await System.Threading.Tasks.Task.Delay(400);
            System.Windows.Application.Current.Shutdown();
        }
        else
        {
            BtnUpdate.IsEnabled = true;
            BtnUpdate.Content = Strings.Get("Main_Update");
            System.Windows.MessageBox.Show(Strings.Get("Msg_UpdateFailed"), Strings.Get("Msg_Update"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Monity - App Usage Tracker",
            Visible = true
        };
        _ = UpdateTrayTooltipAsync();

        _trayUsageTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMinutes(1)
        };
        _trayUsageTimer.Tick += async (_, _) => await UpdateTrayTooltipAsync();
        _trayUsageTimer.Start();
        _trayIcon.DoubleClick += (_, _) =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        };

        if (Services.GetRequiredService<Monity.App.Services.ITrayNotifier>() is Monity.App.Services.TrayNotifier holder)
        {
            holder.SetNotifier((title, text) =>
            {
                _trayIcon?.ShowBalloonTip(3000, title, text, System.Windows.Forms.ToolTipIcon.Info);
            });
        }

        var ctx = new System.Windows.Forms.ContextMenuStrip();
        var openItem = new System.Windows.Forms.ToolStripMenuItem(Strings.Get("Main_Open"));
        openItem.Click += (_, _) => { Show(); WindowState = WindowState.Normal; Activate(); };
        var exitItem = new System.Windows.Forms.ToolStripMenuItem(Strings.Get("Main_Exit"));
        exitItem.Click += (_, _) => System.Windows.Application.Current.Shutdown();
        ctx.Items.Add(openItem);
        ctx.Items.Add(exitItem);
        _trayIcon.ContextMenuStrip = ctx;
    }

    private void BtnStatistics_Click(object sender, RoutedEventArgs e)
    {
        MainFrame.Navigate(new StatisticsPage(Services));
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
        _trayUsageTimer?.Stop();
        _trayUsageTimer = null;
        _trayIcon?.Dispose();
        base.OnClosed(e);
    }

    private async System.Threading.Tasks.Task UpdateTrayTooltipAsync()
    {
        if (_trayIcon == null) return;
        try
        {
            var today = DateTime.Today.ToString("yyyy-MM-dd");
            var total = await _repository.GetDailyTotalAsync(today);
            var formatted = DurationAndPeriodHelper.FormatDuration(total.TotalSeconds);
            await Dispatcher.InvokeAsync(() =>
            {
                _trayIcon!.Text = $"Monity – Bugün: {formatted}";
            });
        }
        catch
        {
            // Ignore - tray text will stay as-is
        }
    }
}
