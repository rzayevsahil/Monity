namespace Monity.Domain;

/// <summary>
/// Represents the currently active foreground process info from Windows.
/// </summary>
public class ForegroundProcessInfo
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string ExePath { get; set; } = string.Empty;
    public string? WindowTitle { get; set; }
}
