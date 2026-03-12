using Monity.App.Helpers;
using Monity.Infrastructure.Persistence;

namespace Monity.App.Services;

public sealed class PomodoroService : IPomodoroService
{
    private readonly IUsageRepository _repository;
    private readonly ITrayNotifier _trayNotifier;
    private System.Timers.Timer? _timer;
    private PomodoroPhase _phase = PomodoroPhase.Idle;
    private int _remainingSeconds;
    private bool _paused;
    private const string KeyWorkMinutes = "pomodoro_work_minutes";
    private const string KeyBreakMinutes = "pomodoro_break_minutes";

    public PomodoroService(IUsageRepository repository, ITrayNotifier trayNotifier)
    {
        _repository = repository;
        _trayNotifier = trayNotifier;
        WorkMinutes = 25;
        BreakMinutes = 5;
    }

    public PomodoroPhase Phase => _phase;
    public int RemainingSeconds => _remainingSeconds;
    public int WorkMinutes { get; set; }
    public int BreakMinutes { get; set; }
    public bool IsRunning => _timer?.Enabled == true;

    public event EventHandler<(PomodoroPhase Phase, int RemainingSeconds)>? Tick;
    public event EventHandler<PomodoroPhase>? PhaseEnded;

    public async Task LoadSettingsAsync()
    {
        var work = await _repository.GetSettingAsync(KeyWorkMinutes);
        var brk = await _repository.GetSettingAsync(KeyBreakMinutes);
        if (int.TryParse(work, out var w) && w >= 1 && w <= 120) WorkMinutes = w;
        if (int.TryParse(brk, out var b) && b >= 1 && b <= 60) BreakMinutes = b;
    }

    public async Task SaveSettingsAsync()
    {
        await _repository.SetSettingAsync(KeyWorkMinutes, WorkMinutes.ToString());
        await _repository.SetSettingAsync(KeyBreakMinutes, BreakMinutes.ToString());
    }

    public void StartWork()
    {
        Stop();
        _phase = PomodoroPhase.Work;
        _remainingSeconds = WorkMinutes * 60;
        _paused = false;
        StartTimer();
    }

    public void StartBreak()
    {
        Stop();
        _phase = PomodoroPhase.Break;
        _remainingSeconds = BreakMinutes * 60;
        _paused = false;
        StartTimer();
    }

    public void Pause()
    {
        _paused = true;
        _timer?.Stop();
    }

    public void Resume()
    {
        if (_phase == PomodoroPhase.Idle || _remainingSeconds <= 0) return;
        _paused = false;
        StartTimer();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer?.Dispose();
        _timer = null;
        _phase = PomodoroPhase.Idle;
        _remainingSeconds = 0;
    }

    private void StartTimer()
    {
        _timer?.Dispose();
        _timer = new System.Timers.Timer(1000) { AutoReset = true };
        _timer.Elapsed += OnTimerElapsed;
        _timer.Start();
        Tick?.Invoke(this, (_phase, _remainingSeconds));
    }

    private void OnTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (_paused) return;
        _remainingSeconds--;
        Tick?.Invoke(this, (_phase, _remainingSeconds));
        if (_remainingSeconds <= 0)
        {
            _timer?.Stop();
            var completed = _phase;
            _phase = PomodoroPhase.Idle;
            _remainingSeconds = 0;
            PhaseEnded?.Invoke(this, completed);
            NotifyPhaseEnded(completed);
        }
    }

    private void NotifyPhaseEnded(PomodoroPhase completed)
    {
        var title = Strings.Get("Pomodoro_NotificationTitle");
        var msg = completed == PomodoroPhase.Work
            ? Strings.Get("Pomodoro_WorkComplete")
            : Strings.Get("Pomodoro_BreakComplete");
        _trayNotifier.ShowBalloonTip(title, msg);
    }
}
