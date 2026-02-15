using System.Collections.Concurrent;
using Monity.Domain.Entities;
using Monity.Infrastructure.Persistence;

namespace Monity.Infrastructure.Tracking;

/// <summary>
/// Buffers usage sessions and flushes to DB based on count (20) or time (5 min).
/// </summary>
public sealed class SessionBuffer
{
    private readonly IUsageRepository _repository;
    private readonly ConcurrentQueue<UsageSession> _buffer = new();
    private readonly SemaphoreSlim _flushLock = new(1, 1);
    private DateTime _lastFlush = DateTime.UtcNow;

    public int BufferSizeThreshold { get; set; } = 20;
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromMinutes(5);

    public SessionBuffer(IUsageRepository repository)
    {
        _repository = repository;
    }

    public void Add(UsageSession session)
    {
        _buffer.Enqueue(session);

        if (ShouldFlush())
            _ = FlushAsync();
    }

    public async Task FlushAsync(CancellationToken ct = default)
    {
        if (!await _flushLock.WaitAsync(0, ct))
            return;

        var items = new List<UsageSession>();
        try
        {
            while (_buffer.TryDequeue(out var s))
                items.Add(s);

            if (items.Count > 0)
            {
                await _repository.AddSessionsAsync(items, ct);
                Serilog.Log.Debug("SessionBuffer flushed {Count} sessions", items.Count);
            }

            _lastFlush = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "SessionBuffer flush failed");
            foreach (var s in items)
                _buffer.Enqueue(s);
        }
        finally
        {
            _flushLock.Release();
        }
    }

    private bool ShouldFlush()
    {
        return _buffer.Count >= BufferSizeThreshold ||
               (DateTime.UtcNow - _lastFlush) >= FlushInterval;
    }

}
