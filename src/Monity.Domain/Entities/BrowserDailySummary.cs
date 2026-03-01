namespace Monity.Domain.Entities;

public class BrowserDailySummary
{
    public int Id { get; set; }
    public string Domain { get; set; } = string.Empty;
    public string BrowserName { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty; // YYYY-MM-DD
    public int TotalSeconds { get; set; }
    public int SessionCount { get; set; }
    public int PageViews { get; set; }
}