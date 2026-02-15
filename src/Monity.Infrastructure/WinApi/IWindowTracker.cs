using Monity.Domain;

namespace Monity.Infrastructure.WinApi;

/// <summary>
/// Abstracts foreground window / process detection for tracking.
/// </summary>
public interface IWindowTracker
{
    /// <summary>
    /// Gets the currently active foreground process info.
    /// Handles UWP (ApplicationFrameHost) by resolving real process via GetGUIThreadInfo.
    /// </summary>
    ForegroundProcessInfo? GetForegroundProcess();

    /// <summary>
    /// Gets the current idle time in milliseconds.
    /// </summary>
    uint GetIdleTimeMs();
}
