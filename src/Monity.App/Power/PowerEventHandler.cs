using System.Windows;
using System.Windows.Interop;

namespace Monity.App.Power;

/// <summary>
/// Handles WM_POWERBROADCAST for sleep/wake events.
/// </summary>
public sealed class PowerEventHandler
{
    private const int WM_POWERBROADCAST = 0x0218;
    private const int PBT_APMSUSPEND = 0x0004;
    private const int PBT_APMRESUMESUSPEND = 0x0007;

    private HwndSource? _hwndSource;
    private readonly Action _onSuspend;
    private readonly Action _onResume;

    public PowerEventHandler(Action onSuspend, Action onResume)
    {
        _onSuspend = onSuspend;
        _onResume = onResume;
    }

    public void Attach(Window window)
    {
        window.Loaded += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            _hwndSource = HwndSource.FromHwnd(hwnd);
            _hwndSource?.AddHook(WndProc);
            Serilog.Log.Debug("PowerEventHandler attached");
        };

        window.Closed += (_, _) => Detach();
    }

    public void Detach()
    {
        _hwndSource?.RemoveHook(WndProc);
        _hwndSource = null;
        Serilog.Log.Debug("PowerEventHandler detached");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_POWERBROADCAST)
        {
            var eventType = wParam.ToInt32();
            switch (eventType)
            {
                case PBT_APMSUSPEND:
                    _onSuspend();
                    break;
                case PBT_APMRESUMESUSPEND:
                    _onResume();
                    break;
            }
        }

        return IntPtr.Zero;
    }
}
