using Monity.Domain.Entities;
using Monity.Infrastructure.Persistence;

namespace Monity.Infrastructure.Browser;

public interface IBrowserTrackingService
{
    Task StartSessionAsync(BrowserSessionStartRequest request, CancellationToken ct = default);
    Task EndSessionAsync(string tabId, CancellationToken ct = default);
    Task UpdateSessionAsync(string tabId, string url, string? title, CancellationToken ct = default);
    Task FocusSessionAsync(string tabId, CancellationToken ct = default);
    Task BlurSessionAsync(string tabId, CancellationToken ct = default);
    Task<List<BrowserSession>> GetActiveSessionsAsync(CancellationToken ct = default);
    Task CleanupInactiveSessionsAsync(CancellationToken ct = default);
}