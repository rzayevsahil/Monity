using System.Collections.Generic;
using System.Text.Json;
using Monity.Infrastructure.Persistence;

namespace Monity.App.Services;

public sealed class DailyLimitCheckService : IDailyLimitCheckService
{
    private readonly IUsageRepository _repository;
    private readonly ITrayNotifier _trayNotifier;
    private DateTime _notifiedDate = DateTime.MinValue;
    private readonly HashSet<string> _notifiedProcessNames = new();

    public DailyLimitCheckService(IUsageRepository repository, ITrayNotifier trayNotifier)
    {
        _repository = repository;
        _trayNotifier = trayNotifier;
    }

    public async Task CheckAndNotifyAsync(IReadOnlyList<int> appIds, CancellationToken ct = default)
    {
        if (appIds.Count == 0) return;

        var today = DateTime.Today;
        if (_notifiedDate != today)
        {
            _notifiedDate = today;
            _notifiedProcessNames.Clear();
        }

        var limitsJson = await _repository.GetSettingAsync("daily_limits", ct) ?? "{}";
        Dictionary<string, long>? limits = null;
        try
        {
            limits = JsonSerializer.Deserialize<Dictionary<string, long>>(limitsJson);
        }
        catch
        {
            // invalid JSON = no limits
        }

        if (limits == null || limits.Count == 0) return;

        foreach (var appId in appIds)
        {
            var processName = await _repository.GetProcessNameByAppIdAsync(appId, ct);
            if (string.IsNullOrEmpty(processName) || !limits.TryGetValue(processName, out var limitSeconds) || limitSeconds <= 0)
                continue;

            var totalSeconds = await _repository.GetTodayTotalSecondsForAppIdAsync(appId, ct);
            if (totalSeconds < limitSeconds) continue;
            if (_notifiedProcessNames.Contains(processName)) continue;

            _notifiedProcessNames.Add(processName);
            var limitText = FormatLimit(limitSeconds);
            var title = "Günlük kullanım süresi";
            var text = $"Günlük kullanım sürenizi tamamladınız: {processName} (limit: {limitText}).";
            _trayNotifier.ShowBalloonTip(title, text);
        }
    }

    private static string FormatLimit(long seconds)
    {
        if (seconds >= 3600 && seconds % 3600 == 0)
            return $"{seconds / 3600} saat";
        if (seconds >= 60)
            return $"{seconds / 60} dk";
        return $"{seconds} sn";
    }
}
