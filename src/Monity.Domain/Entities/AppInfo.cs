namespace Monity.Domain.Entities;

public class AppInfo
{
    public int Id { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string ExePath { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Category { get; set; }
    public bool IsIgnored { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// User-facing name: DisplayName if set, otherwise ProcessName.
    /// </summary>
    public string EffectiveName => !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName : ProcessName;
}
