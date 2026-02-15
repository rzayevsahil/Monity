using System.Collections.Concurrent;
using Monity.Domain;
using Monity.Infrastructure.WinApi;

namespace Monity.Infrastructure.Tracking;

/// <summary>
/// Timer-based tracking engine. Polls every second for foreground window changes.
/// </summary>
public sealed class TrackingEngine : ITrackingEngine
{
    private readonly IWindowTracker _windowTracker;
    private readonly HashSet<string> _ignoredProcesses;
    private System.Timers.Timer? _timer;
    private DateTime _currentSessionStart;
    private ForegroundProcessInfo? _currentProcess;
    private readonly object _lock = new();

    /// <summary>
    /// Idle threshold in milliseconds. Default 60 seconds.
    /// </summary>
    public uint IdleThresholdMs { get; set; } = 60_000;

    /// <summary>
    /// Polling interval in milliseconds. Default 1000 (1 second).
    /// </summary>
    public int PollIntervalMs { get; set; } = 1000;

    public event EventHandler<SessionEndedEventArgs>? SessionEnded;
    public event EventHandler<ForegroundChangedEventArgs>? ForegroundChanged;

    public TrackingEngine(IWindowTracker windowTracker)
    {
        _windowTracker = windowTracker;
        _ignoredProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Monity.App",
            "explorer"
        };
    }

    /// <summary>
    /// Add process names to ignore (e.g. "SearchHost", "StartMenuExperienceHost").
    /// </summary>
    public void IgnoreProcess(string processName)
    {
        lock (_lock)
        {
            _ignoredProcesses.Add(processName);
        }
    }

    public void SetIgnoredProcesses(IEnumerable<string> baseProcesses, IEnumerable<string> userProcesses)
    {
        lock (_lock)
        {
            _ignoredProcesses.Clear();
            foreach (var p in baseProcesses)
                _ignoredProcesses.Add(p);
            foreach (var p in userProcesses)
                if (!string.IsNullOrWhiteSpace(p))
                    _ignoredProcesses.Add(p.Trim());
        }
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_timer != null)
                return;

            _timer = new System.Timers.Timer(PollIntervalMs)
            {
                AutoReset = true
            };
            _timer.Elapsed += OnTimerElapsed;
            _timer.Start();
        }
    }

    public async Task StopAsync()
    {
        System.Timers.Timer? t;
        lock (_lock)
        {
            t = _timer;
            _timer = null;
        }

        if (t != null)
        {
            t.Stop();
            t.Elapsed -= OnTimerElapsed;
            t.Dispose();
        }

        lock (_lock)
        {
            FlushCurrentSession(isIdle: false);
        }

        await Task.CompletedTask;
    }

    public void HandlePowerSuspend()
    {
        lock (_lock)
        {
            FlushCurrentSession(isIdle: false);
            Serilog.Log.Information("Power suspend - flushed current session");
        }
    }

    public void HandlePowerResume()
    {
        lock (_lock)
        {
            _currentProcess = null;
            Serilog.Log.Information("Power resume - reset tracking state");
        }
    }

    private void OnTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        lock (_lock)
        {
            var idleMs = _windowTracker.GetIdleTimeMs();
            var isIdle = idleMs >= IdleThresholdMs;

            if (isIdle)
            {
                FlushCurrentSession(isIdle: true);
                _currentProcess = null;
                return;
            }

            var fg = _windowTracker.GetForegroundProcess();
            if (fg == null)
                return;

            if (_ignoredProcesses.Contains(fg.ProcessName))
                return;

            var key = $"{fg.ProcessId}:{fg.ProcessName}";

            if (_currentProcess != null)
            {
                var currentKey = $"{_currentProcess.ProcessId}:{_currentProcess.ProcessName}";
                if (currentKey == key)
                    return; // Same process, no change
            }

            FlushCurrentSession(isIdle: false);

            _currentProcess = fg;
            _currentSessionStart = DateTime.UtcNow;
            ForegroundChanged?.Invoke(this, new ForegroundChangedEventArgs(fg.ProcessName, fg.ExePath));
        }
    }

    private void FlushCurrentSession(bool isIdle)
    {
        if (_currentProcess == null)
            return;

        var endedAt = DateTime.UtcNow;
        var duration = (int)(endedAt - _currentSessionStart).TotalSeconds;

        if (duration > 0)
        {
            var args = new SessionEndedEventArgs(
                _currentProcess.ProcessName,
                _currentProcess.ExePath,
                _currentSessionStart,
                endedAt,
                isIdle,
                _currentProcess.WindowTitle);

            try
            {
                SessionEnded?.Invoke(this, args);
            }
            catch { /* Don't let handler exceptions stop tracking */ }
        }

        _currentProcess = null;
    }
}
