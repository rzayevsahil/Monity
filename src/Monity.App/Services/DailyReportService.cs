using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Monity.App.Helpers;
using Monity.Infrastructure.Persistence;

namespace Monity.App.Services;

public sealed class DailyReportService : IDailyReportService
{
    private readonly IUsageRepository _repository;
    private readonly ITrayNotifier _trayNotifier;
    private readonly IInsightService _insightService;
    private DispatcherTimer? _timer;

    public DailyReportService(IUsageRepository repository, ITrayNotifier trayNotifier, IInsightService insightService)
    {
        _repository = repository;
        _trayNotifier = trayNotifier;
        _insightService = insightService;
    }

    public void Start()
    {
        if (_timer != null) return;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(1)
        };
        _timer.Tick += async (s, e) => await CheckAndSendReportAsync();
        _timer.Start();
        
        // Initial check
        _ = CheckAndSendReportAsync();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    private async Task CheckAndSendReportAsync()
    {
        try
        {
            var enabledStr = await _repository.GetSettingAsync("daily_report_enabled");
            if (enabledStr != "true") return;

            var today = DateTime.Today.ToString("yyyy-MM-dd");
            var lastSent = await _repository.GetSettingAsync("daily_report_last_sent_date");
            if (lastSent == today) return;

            var reportTimeStr = await _repository.GetSettingAsync("daily_report_time") ?? "21:00";
            if (!TimeSpan.TryParse(reportTimeStr, out var reportTime)) return;

            var now = DateTime.Now.TimeOfDay;
            // Send if current time is at or after report time
            if (now >= reportTime)
            {
                await SendReportAsync(today);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Error in DailyReportService");
        }
    }

    private async Task SendReportAsync(string date)
    {
        var (totalSeconds, _) = await _repository.GetDailyTotalAsync(date);
        if (totalSeconds <= 0) return;

        var apps = await _repository.GetDailyUsageAsync(date);
        var topApp = apps.FirstOrDefault();
        var topAppName = topApp?.DisplayName ?? topApp?.ProcessName ?? "---";

        var durationText = FormatDuration(totalSeconds);
        var title = Strings.Get("ReportNotification_Title");
        var message = string.Format(Strings.Get("ReportNotification_Text"), durationText, topAppName);

        var insightsEnabled = (await _repository.GetSettingAsync("smart_insights_enabled") ?? "true") == "true";
        if (insightsEnabled)
        {
            var insights = await _insightService.GetInsightsAsync(DateTime.Parse(date));
            var firstInsight = insights.FirstOrDefault();
            if (firstInsight != null)
                message += "\n\n" + firstInsight.Message;
        }

        _trayNotifier.ShowBalloonTip(title, message);

        await _repository.SetSettingAsync("daily_report_last_sent_date", date);
        Serilog.Log.Information("Daily report sent for {Date}", date);
    }

    private static string FormatDuration(long seconds)
    {
        var h = seconds / 3600;
        var m = (seconds % 3600) / 60;
        if (h > 0) return $"{h} sa {m} dk";
        return $"{m} dk";
    }
}
