using Monity.App.Helpers;
using Monity.Infrastructure.Persistence;
using Monity.Infrastructure.Tracking;

namespace Monity.App.Services;

/// <summary>
/// When focus mode is enabled, shows a warning notification when the user switches to a blocked app.
/// </summary>
public sealed class FocusModeService
{
    private readonly UsageTrackingService _trackingService;
    private readonly ITrayNotifier _trayNotifier;
    private readonly IUsageRepository _repository;
    private bool _enabled;
    private HashSet<string> _blockedProcesses = new(StringComparer.OrdinalIgnoreCase);
    private bool _subscribed;

    public FocusModeService(UsageTrackingService trackingService, ITrayNotifier trayNotifier, IUsageRepository repository)
    {
        _trackingService = trackingService;
        _trayNotifier = trayNotifier;
        _repository = repository;
        _ = LoadSettingsAsync();
    }

    public bool Enabled => _enabled;
    public IReadOnlySet<string> BlockedProcesses => _blockedProcesses;

    public async Task LoadSettingsAsync()
    {
        var enabledStr = await _repository.GetSettingAsync("focus_mode_enabled") ?? "false";
        _enabled = enabledStr == "true";
        var blockedStr = await _repository.GetSettingAsync("focus_mode_blocked_processes") ?? "";
        _blockedProcesses = new HashSet<string>(
            blockedStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);
        UpdateSubscription();
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        _enabled = enabled;
        await _repository.SetSettingAsync("focus_mode_enabled", enabled ? "true" : "false");
        UpdateSubscription();
    }

    public async Task SetBlockedProcessesAsync(IEnumerable<string> processNames)
    {
        _blockedProcesses = new HashSet<string>(processNames.Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.OrdinalIgnoreCase);
        await _repository.SetSettingAsync("focus_mode_blocked_processes", string.Join(",", _blockedProcesses));
    }

    private void UpdateSubscription()
    {
        if (_enabled && !_subscribed)
        {
            _trackingService.ForegroundChanged += OnForegroundChanged;
            _subscribed = true;
        }
        else if (!_enabled && _subscribed)
        {
            _trackingService.ForegroundChanged -= OnForegroundChanged;
            _subscribed = false;
        }
    }

    private void OnForegroundChanged(object? sender, ForegroundChangedEventArgs e)
    {
        if (!_enabled || _blockedProcesses.Count == 0) return;
        if (!_blockedProcesses.Contains(e.ProcessName)) return;
        var title = Strings.Get("FocusMode_NotificationTitle");
        var msg = string.Format(Strings.Get("FocusMode_NotificationText"), e.ProcessName);
        _trayNotifier.ShowBalloonTip(title, msg);
    }
}
