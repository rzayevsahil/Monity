using Monity.Domain.Entities;
using Monity.Infrastructure.Browser;
using Monity.Infrastructure.Persistence;
using Monity.Infrastructure.WinApi;

namespace Monity.Infrastructure.Tracking;

/// <summary>
/// Orchestrates tracking engine, session buffer, and repository.
/// </summary>
public sealed class UsageTrackingService
{
    private readonly ITrackingEngine _engine;
    private readonly IUsageRepository _repository;
    private readonly SessionBuffer _buffer;
    private readonly IWindowTracker _windowTracker;
    private readonly IBrowserTrackingService? _browserTrackingService;
    private bool _started;

    public UsageTrackingService(
        ITrackingEngine engine,
        IUsageRepository repository,
        SessionBuffer buffer,
        IWindowTracker windowTracker,
        IBrowserTrackingService? browserTrackingService = null)
    {
        _engine = engine;
        _repository = repository;
        _buffer = buffer;
        _windowTracker = windowTracker;
        _browserTrackingService = browserTrackingService;

        _engine.SessionEnded += OnSessionEnded;
    }

    public void Start()
    {
        if (_started)
            return;

        _engine.Start();
        _started = true;
        Serilog.Log.Information("UsageTrackingService started");
    }

    public async Task StopAsync()
    {
        if (!_started)
            return;

        await _engine.StopAsync();
        await _buffer.FlushAsync();
        _started = false;
        Serilog.Log.Information("UsageTrackingService stopped");
    }

    /// <summary>
    /// Bekleyen oturumları hemen veritabanına yazar. Dashboard yenilemeden önce çağrılırsa güncel veri gösterilir.
    /// </summary>
    public Task FlushBufferAsync(CancellationToken ct = default) => _buffer.FlushAsync(ct);

    public event EventHandler<SessionEndedEventArgs>? SessionEnded;
    public event EventHandler<ForegroundChangedEventArgs>? ForegroundChanged
    {
        add => _engine.ForegroundChanged += value;
        remove => _engine.ForegroundChanged -= value;
    }

    public uint IdleThresholdMs
    {
        get => _engine.IdleThresholdMs;
        set => _engine.IdleThresholdMs = value;
    }

    /// <summary>
    /// Minimum session duration in seconds. Sessions shorter than this are not saved. 0 = disabled.
    /// </summary>
    public uint MinSessionSeconds { get; set; }

    private async void OnSessionEnded(object? sender, SessionEndedEventArgs e)
    {
        try
        {
            if (MinSessionSeconds > 0 && e.DurationSeconds < MinSessionSeconds)
                return;

            var appId = await _repository.GetOrCreateAppIdAsync(e.ProcessName, e.ExePath);

            var startedLocal = e.StartedAt.ToLocalTime();
            var endedLocal = e.EndedAt.ToLocalTime();
            var session = new UsageSession
            {
                AppId = appId,
                StartedAt = startedLocal,
                EndedAt = endedLocal,
                DurationSeconds = e.DurationSeconds,
                IsIdle = e.IsIdle,
                WindowTitle = e.WindowTitle,
                DayDate = startedLocal.ToString("yyyy-MM-dd")
            };

            _buffer.Add(session);

            // Browser tracking for detected browsers
            await TrackBrowserSessionIfApplicable(e, startedLocal, endedLocal);

            SessionEnded?.Invoke(this, e);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to persist session for {Process}", e.ProcessName);
        }
    }

    private async Task TrackBrowserSessionIfApplicable(SessionEndedEventArgs e, DateTime startedLocal, DateTime endedLocal)
    {
        if (_browserTrackingService == null || string.IsNullOrEmpty(e.WindowTitle))
            return;

        var browserType = BrowserDetector.DetectBrowserFromWindowTitle(e.WindowTitle, e.ProcessName, e.ExePath);
        if (browserType == "unknown")
        {
            Serilog.Log.Debug("Skipping browser session tracking - unknown browser type");
            return;
        }

        Serilog.Log.Debug("Tracking browser session for {BrowserType}", browserType);

        try
        {
            // Extract URL from window title
            var url = BrowserDetector.ExtractUrlFromWindowTitle(e.WindowTitle, browserType);
            Serilog.Log.Debug("URL extraction result: '{Url}'", url);
            
            if (string.IsNullOrEmpty(url))
            {
                // If no URL found, use the window title as a fallback
                url = e.WindowTitle;
                Serilog.Log.Debug("Using window title as fallback URL: '{Url}'", url);
            }

            // Create a browser session
            var tabId = $"{browserType}_{startedLocal:yyyyMMddHHmmss}_{e.ProcessName}";
            var domain = ExtractDomainFromUrl(url);
            Serilog.Log.Debug("Extracted domain: '{Domain}' from URL: '{Url}'", domain, url);
            
            var browserSession = new BrowserSession
            {
                BrowserName = browserType,
                TabId = tabId,
                Url = url,
                Domain = domain,
                Title = e.WindowTitle,
                StartedAt = startedLocal.ToUniversalTime(),
                EndedAt = endedLocal.ToUniversalTime(),
                DurationSeconds = e.DurationSeconds,
                IsActive = false, // Session has ended
                DayDate = startedLocal.ToString("yyyy-MM-dd"),
                CreatedAt = DateTime.UtcNow
            };

            // Add browser session directly to repository
            await _repository.AddBrowserSessionAsync(browserSession);
            
            // Update daily summary
            await _repository.UpdateBrowserDailySummaryAsync(browserSession.DayDate);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Error tracking browser session for {ProcessName}", e.ProcessName);
        }
    }

    private static string ExtractDomainFromUrl(string url)
    {
        try
        {
            if (string.IsNullOrEmpty(url))
                return "unknown";

            // Handle URLs without protocol
            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            {
                // If it looks like a domain, add https
                if (url.Contains(".") && !url.Contains(" "))
                    url = "https://" + url;
                else
                    return "local"; // Probably a local page or title
            }

            var uri = new Uri(url);
            var domain = uri.Host.ToLowerInvariant();

            // Remove www. prefix
            if (domain.StartsWith("www."))
                domain = domain[4..];

            return domain;
        }
        catch
        {
            // If URL parsing fails, try to extract domain from the string
            var parts = url.Split(new[] { '/', ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (part.Contains(".com") || part.Contains(".org") || part.Contains(".net"))
                {
                    return part.ToLowerInvariant();
                }
            }
            return "unknown";
        }
    }
}
