namespace Monity.App.Services;

public enum PomodoroPhase
{
    Idle,
    Work,
    Break
}

public interface IPomodoroService
{
    PomodoroPhase Phase { get; }
    int RemainingSeconds { get; }
    int WorkMinutes { get; set; }
    int BreakMinutes { get; set; }
    bool IsRunning { get; }
    void StartWork();
    void StartBreak();
    void Pause();
    void Resume();
    void Stop();
    event EventHandler<(PomodoroPhase Phase, int RemainingSeconds)>? Tick;
    event EventHandler<PomodoroPhase>? PhaseEnded;
    Task LoadSettingsAsync();
    Task SaveSettingsAsync();
}
