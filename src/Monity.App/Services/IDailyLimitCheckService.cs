namespace Monity.App.Services;

public interface IDailyLimitCheckService
{
    /// <summary>
    /// For each app in appIds, checks if daily limit is set and exceeded; shows tray notification once per app per day.
    /// </summary>
    Task CheckAndNotifyAsync(IReadOnlyList<int> appIds, CancellationToken ct = default);
}
