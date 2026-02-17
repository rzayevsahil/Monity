using Monity.Domain.Entities;
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
    private bool _started;

    public UsageTrackingService(
        ITrackingEngine engine,
        IUsageRepository repository,
        SessionBuffer buffer,
        IWindowTracker windowTracker)
    {
        _engine = engine;
        _repository = repository;
        _buffer = buffer;
        _windowTracker = windowTracker;

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

    private async void OnSessionEnded(object? sender, SessionEndedEventArgs e)
    {
        try
        {
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
            SessionEnded?.Invoke(this, e);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to persist session for {Process}", e.ProcessName);
        }
    }
}
