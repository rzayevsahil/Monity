using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Monity.App.Helpers;
using Monity.App.Services;

namespace Monity.App.Views;

public partial class PomodoroPage : Page
{
    private readonly IPomodoroService _pomodoro;
    private readonly DispatcherTimer _uiTimer;

    public PomodoroPage(IServiceProvider services)
    {
        InitializeComponent();
        _pomodoro = services.GetRequiredService<IPomodoroService>();

        _uiTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _uiTimer.Tick += (_, _) => UpdateFromService();

        Loaded += async (_, _) =>
        {
            await _pomodoro.LoadSettingsAsync();
            TxtWorkMinutes.Text = _pomodoro.WorkMinutes.ToString();
            TxtBreakMinutes.Text = _pomodoro.BreakMinutes.ToString();
            UpdateFromService();
        };
        Unloaded += (_, _) => _uiTimer.Stop();
    }

    private void UpdateFromService()
    {
        var phase = _pomodoro.Phase;
        var remaining = _pomodoro.RemainingSeconds;
        var running = _pomodoro.IsRunning;

        TxtCountdown.Text = $"{remaining / 60}:{(remaining % 60):D2}";
        TxtPhase.Text = phase switch
        {
            PomodoroPhase.Work => Strings.Get("Pomodoro_Work"),
            PomodoroPhase.Break => Strings.Get("Pomodoro_Break"),
            _ => ""
        };

        var idle = phase == PomodoroPhase.Idle;
        BtnStartWork.Visibility = idle ? Visibility.Visible : Visibility.Collapsed;
        BtnStartBreak.Visibility = idle ? Visibility.Visible : Visibility.Collapsed;
        BtnStop.Visibility = !idle ? Visibility.Visible : Visibility.Collapsed;
        var paused = !idle && !running;
        BtnPause.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        BtnResume.Visibility = paused ? Visibility.Visible : Visibility.Collapsed;

        TxtWorkMinutes.IsEnabled = idle;
        TxtBreakMinutes.IsEnabled = idle;
        if (idle)
            _uiTimer.Stop();
    }

    private void ApplyDurations()
    {
        if (int.TryParse(TxtWorkMinutes.Text, out var w) && w >= 1 && w <= 120)
            _pomodoro.WorkMinutes = w;
        if (int.TryParse(TxtBreakMinutes.Text, out var b) && b >= 1 && b <= 60)
            _pomodoro.BreakMinutes = b;
        _ = _pomodoro.SaveSettingsAsync();
    }

    private void BtnStartWork_Click(object sender, RoutedEventArgs e)
    {
        ApplyDurations();
        _pomodoro.StartWork();
        _uiTimer.Start();
        UpdateFromService();
    }

    private void BtnStartBreak_Click(object sender, RoutedEventArgs e)
    {
        ApplyDurations();
        _pomodoro.StartBreak();
        _uiTimer.Start();
        UpdateFromService();
    }

    private void BtnPause_Click(object sender, RoutedEventArgs e)
    {
        _pomodoro.Pause();
        UpdateFromService();
    }

    private void BtnResume_Click(object sender, RoutedEventArgs e)
    {
        _pomodoro.Resume();
        _uiTimer.Start();
        UpdateFromService();
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        _pomodoro.Stop();
        _uiTimer.Stop();
        UpdateFromService();
    }
}
