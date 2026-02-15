namespace Monity.Infrastructure.Tracking;

/// <summary>
/// Core tracking engine - polls foreground window and emits usage sessions.
/// </summary>
public interface ITrackingEngine
{
    uint IdleThresholdMs { get; set; }

    void IgnoreProcess(string processName);
    void SetIgnoredProcesses(IEnumerable<string> baseProcesses, IEnumerable<string> userProcesses);

    /// <summary>
    /// Starts the tracking engine (timer-based polling).
    /// </summary>
    void Start();

    /// <summary>
    /// Stops the tracking engine and flushes any pending sessions.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Call when system is about to sleep - flushes current session.
    /// </summary>
    void HandlePowerSuspend();

    /// <summary>
    /// Call when system resumes from sleep - resets state.
    /// </summary>
    void HandlePowerResume();

    /// <summary>
    /// Raised when a usage session ends (window changed or idle).
    /// </summary>
    event EventHandler<SessionEndedEventArgs>? SessionEnded;

    /// <summary>
    /// Raised when the current foreground app changes (for UI updates).
    /// </summary>
    event EventHandler<ForegroundChangedEventArgs>? ForegroundChanged;
}

public sealed class SessionEndedEventArgs : EventArgs
{
    public SessionEndedEventArgs(string processName, string exePath, DateTime startedAt, DateTime endedAt, bool isIdle, string? windowTitle)
    {
        ProcessName = processName;
        ExePath = exePath;
        StartedAt = startedAt;
        EndedAt = endedAt;
        IsIdle = isIdle;
        WindowTitle = windowTitle;
    }

    public string ProcessName { get; }
    public string ExePath { get; }
    public DateTime StartedAt { get; }
    public DateTime EndedAt { get; }
    public bool IsIdle { get; }
    public string? WindowTitle { get; }
    public int DurationSeconds => (int)(EndedAt - StartedAt).TotalSeconds;
}

public sealed class ForegroundChangedEventArgs : EventArgs
{
    public ForegroundChangedEventArgs(string processName, string exePath)
    {
        ProcessName = processName;
        ExePath = exePath;
    }

    public string ProcessName { get; }
    public string ExePath { get; }
}
