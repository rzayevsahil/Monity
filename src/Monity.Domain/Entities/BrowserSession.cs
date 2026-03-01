namespace Monity.Domain.Entities;

public class BrowserSession
{
    public int Id { get; set; }
    public string BrowserName { get; set; } = string.Empty; // 'chrome', 'firefox', 'edge'
    public string TabId { get; set; } = string.Empty; // Extension'dan gelen tab ID
    public string Url { get; set; } = string.Empty; // Tam URL
    public string Domain { get; set; } = string.Empty; // Extracted domain (google.com)
    public string? Title { get; set; } // Sayfa başlığı
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; } // NULL if still active
    public int DurationSeconds { get; set; }
    public bool IsActive { get; set; } = true; // Active tab flag
    public string DayDate { get; set; } = string.Empty; // YYYY-MM-DD for indexing
    public DateTime CreatedAt { get; set; }
}