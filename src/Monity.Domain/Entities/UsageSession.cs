namespace Monity.Domain.Entities;

public class UsageSession
{
    public int Id { get; set; }
    public int AppId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime EndedAt { get; set; }
    public int DurationSeconds { get; set; }
    public bool IsIdle { get; set; }
    public string? WindowTitle { get; set; }
    public string DayDate { get; set; } = string.Empty; // YYYY-MM-DD

    public AppInfo? App { get; set; }
}
