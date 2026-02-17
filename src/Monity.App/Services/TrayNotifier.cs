namespace Monity.App.Services;

/// <summary>
/// Holder for the actual tray notifier implementation. MainWindow sets the callback when it loads.
/// </summary>
public sealed class TrayNotifier : ITrayNotifier
{
    private Action<string, string>? _showBalloonTip;

    public void SetNotifier(Action<string, string> showBalloonTip)
    {
        _showBalloonTip = showBalloonTip;
    }

    public void ShowBalloonTip(string title, string text)
    {
        _showBalloonTip?.Invoke(title, text);
    }
}
