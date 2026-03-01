using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Monity.Domain.Entities;
using Monity.Infrastructure.Persistence;

namespace Monity.Infrastructure.Browser;

public class BrowserTrackingService : IBrowserTrackingService
{
    private readonly ConcurrentDictionary<string, BrowserSession> _activeSessions = new();
    private readonly IUsageRepository _repository;

    public BrowserTrackingService(IUsageRepository repository)
    {
        _repository = repository;
    }

    public async Task StartSessionAsync(BrowserSessionStartRequest request, CancellationToken ct = default)
    {
        // End any existing session for this tab
        await EndSessionAsync(request.TabId, ct);

        var session = new BrowserSession
        {
            BrowserName = request.BrowserName,
            TabId = request.TabId,
            Url = request.Url,
            Domain = ExtractDomain(request.Url),
            Title = request.Title,
            StartedAt = DateTime.UtcNow,
            DurationSeconds = 0,
            IsActive = true,
            DayDate = DateTime.Today.ToString("yyyy-MM-dd"),
            CreatedAt = DateTime.UtcNow
        };

        var sessionId = await _repository.AddBrowserSessionAsync(session, ct);
        session.Id = sessionId;
        
        _activeSessions[request.TabId] = session;
    }

    public async Task EndSessionAsync(string tabId, CancellationToken ct = default)
    {
        if (_activeSessions.TryRemove(tabId, out var session))
        {
            var endTime = DateTime.UtcNow;
            var duration = (int)(endTime - session.StartedAt).TotalSeconds;
            
            await _repository.UpdateBrowserSessionAsync(session.Id, endTime, duration, ct);
            
            // Update daily summary
            await _repository.UpdateBrowserDailySummaryAsync(session.DayDate, ct);
        }
    }

    public async Task UpdateSessionAsync(string tabId, string url, string? title, CancellationToken ct = default)
    {
        if (_activeSessions.TryGetValue(tabId, out var session))
        {
            // If URL changed significantly, end current session and start new one
            var newDomain = ExtractDomain(url);
            if (newDomain != session.Domain)
            {
                await EndSessionAsync(tabId, ct);
                
                var request = new BrowserSessionStartRequest(session.BrowserName, tabId, url, title);
                await StartSessionAsync(request, ct);
            }
            else
            {
                // Just update the title and URL
                session.Url = url;
                session.Title = title;
            }
        }
    }

    public async Task FocusSessionAsync(string tabId, CancellationToken ct = default)
    {
        if (_activeSessions.TryGetValue(tabId, out var session))
        {
            session.IsActive = true;
            // Resume timing for this session
        }
    }

    public async Task BlurSessionAsync(string tabId, CancellationToken ct = default)
    {
        if (_activeSessions.TryGetValue(tabId, out var session))
        {
            session.IsActive = false;
            // Pause timing for this session
        }
    }

    public async Task<List<BrowserSession>> GetActiveSessionsAsync(CancellationToken ct = default)
    {
        return _activeSessions.Values.ToList();
    }

    public async Task CleanupInactiveSessionsAsync(CancellationToken ct = default)
    {
        var cutoffTime = DateTime.UtcNow.AddMinutes(-30); // Sessions inactive for 30+ minutes
        var inactiveSessions = _activeSessions.Values
            .Where(s => s.StartedAt < cutoffTime && !s.IsActive)
            .ToList();

        foreach (var session in inactiveSessions)
        {
            await EndSessionAsync(session.TabId);
        }
    }

    private static string ExtractDomain(string url)
    {
        try
        {
            if (string.IsNullOrEmpty(url))
                return "unknown";

            // Handle URLs without protocol
            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                url = "https://" + url;

            var uri = new Uri(url);
            var domain = uri.Host.ToLowerInvariant();

            // Remove www. prefix
            if (domain.StartsWith("www."))
                domain = domain[4..];

            return domain;
        }
        catch
        {
            // Fallback for invalid URLs
            var match = Regex.Match(url, @"(?:https?://)?(?:www\.)?([^/\s]+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.ToLowerInvariant() : "unknown";
        }
    }
}