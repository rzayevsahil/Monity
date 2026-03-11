namespace Monity.App.Services;

/// <summary>
/// Context for generating a share card (period and optional options).
/// </summary>
public enum SharePeriod
{
    Today,
    Week,
    Month
}

public record ShareContext(SharePeriod Period);
