namespace Monity.Infrastructure.Persistence;

/// <summary>Dapper materialization: parameterless ctor + settable properties (SQLite INTEGER → long).</summary>
public class BrowserDomainUsage
{
    public string Domain { get; set; } = string.Empty;
    public string BrowserName { get; set; } = string.Empty;
    public long TotalSeconds { get; set; }
    public long SessionCount { get; set; }
    public long PageViews { get; set; }
}

/// <summary>Dapper materialization: parameterless ctor + settable properties (SQLite INTEGER → long).</summary>
public class BrowserHourlyUsage
{
    public long Hour { get; set; }
    public long TotalSeconds { get; set; }
}

public record BrowserSessionStartRequest(
    string BrowserName,
    string TabId,
    string Url,
    string? Title);

public record BrowserDomainUsageItem(
    int Rank,
    string Domain,
    string FormattedTime,
    long Visits,
    double Percentage);