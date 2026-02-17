namespace Monity.App.Services;

/// <summary>
/// Shows a balloon tip in the system tray. Implemented by the main window; used by e.g. daily limit notifications.
/// </summary>
public interface ITrayNotifier
{
    void ShowBalloonTip(string title, string text);
}
