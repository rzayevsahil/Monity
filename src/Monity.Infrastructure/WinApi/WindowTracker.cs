using System.Diagnostics;
using System.Runtime.InteropServices;
using Monity.Domain;

namespace Monity.Infrastructure.WinApi;

/// <summary>
/// Implementation of foreground window and idle detection using Win32 APIs.
/// Supports UWP apps via GetGUIThreadInfo(0) and EnumChildWindows fallback.
/// </summary>
public sealed class WindowTracker : IWindowTracker
{
    private const int MaxWindowTitleLength = 256;

    public ForegroundProcessInfo? GetForegroundProcess()
    {
        try
        {
            // Try GetGUIThreadInfo first - handles UWP and fullscreen better
            var gti = new User32Interop.GuiThreadInfo
            {
                cbSize = Marshal.SizeOf<User32Interop.GuiThreadInfo>()
            };

            if (User32Interop.GetGUIThreadInfo(0, ref gti))
            {
                var hwnd = gti.hwndFocus;
                if (hwnd == IntPtr.Zero)
                    hwnd = gti.hwndActive;

                if (hwnd != IntPtr.Zero && TryGetProcessFromHwnd(hwnd, out var info))
                    return info;
            }

            // Fallback: GetForegroundWindow
            var fgHwnd = User32Interop.GetForegroundWindow();
            if (fgHwnd == IntPtr.Zero)
                return null;

            if (TryGetProcessFromHwnd(fgHwnd, out var fgInfo))
            {
                // If ApplicationFrameHost, resolve real UWP process via EnumChildWindows
                if (string.Equals(fgInfo.ProcessName, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryResolveUwpProcess(fgHwnd, out var realInfo))
                        return realInfo;
                }
                return fgInfo;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public uint GetIdleTimeMs()
    {
        var lii = new User32Interop.LastInputInfo
        {
            cbSize = (uint)Marshal.SizeOf<User32Interop.LastInputInfo>()
        };

        if (!User32Interop.GetLastInputInfo(ref lii))
            return 0;

        // Use GetTickCount64 to avoid overflow (Environment.TickCount overflows ~49 days)
        var tickCount = User32Interop.GetTickCount64();
        var lastInput = lii.dwTime;
        return (uint)(tickCount - lastInput);
    }

    private static bool TryGetProcessFromHwnd(IntPtr hwnd, out ForegroundProcessInfo info)
    {
        info = null!;

        if (User32Interop.GetWindowThreadProcessId(hwnd, out var pid) == 0 || pid == 0)
            return false;

        try
        {
            using var process = Process.GetProcessById((int)pid);
            var exePath = GetProcessPath(process);
            var windowTitle = GetWindowTitle(hwnd);

            info = new ForegroundProcessInfo
            {
                ProcessId = (int)pid,
                ProcessName = process.ProcessName,
                ExePath = exePath ?? process.ProcessName,
                WindowTitle = string.IsNullOrWhiteSpace(windowTitle) ? null : windowTitle
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static ForegroundProcessInfo? _enumResult;

    private static bool TryResolveUwpProcess(IntPtr parentHwnd, out ForegroundProcessInfo info)
    {
        info = null!;
        _enumResult = null;

        User32Interop.EnumChildWindows(parentHwnd, EnumChildCallback, IntPtr.Zero);

        if (_enumResult != null)
        {
            info = _enumResult;
            return true;
        }
        return false;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static bool EnumChildCallback(IntPtr hwnd, IntPtr lParam)
    {
        if (TryGetProcessFromHwnd(hwnd, out var childInfo) &&
            !string.Equals(childInfo.ProcessName, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
        {
            _enumResult = childInfo;
            return false; // Stop enumeration
        }
        return true;
    }

    private static string? GetProcessPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetWindowTitle(IntPtr hwnd)
    {
        try
        {
            var buf = new char[MaxWindowTitleLength + 1];
            var len = User32Interop.GetWindowTextW(hwnd, buf, buf.Length);
            return len > 0 ? new string(buf, 0, len) : null;
        }
        catch
        {
            return null;
        }
    }
}
