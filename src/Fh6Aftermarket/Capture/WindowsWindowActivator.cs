using System.Runtime.InteropServices;
using System.Text;

namespace Fh6Aftermarket.Capture;

public static class WindowsWindowActivator
{
    private const int SwRestore = 9;

    public static bool TryActivateExactTitle(string title)
    {
        var target = FindVisibleWindow(title);
        if (target == IntPtr.Zero)
        {
            return false;
        }

        if (IsIconic(target))
        {
            _ = ShowWindowAsync(target, SwRestore);
        }

        var foreground = GetForegroundWindow();
        var currentThread = GetCurrentThreadId();
        var targetThread = GetWindowThreadProcessId(target, IntPtr.Zero);
        var foregroundThread = foreground == IntPtr.Zero
            ? 0
            : GetWindowThreadProcessId(foreground, IntPtr.Zero);

        var attachedTarget = targetThread != 0 && targetThread != currentThread &&
            AttachThreadInput(currentThread, targetThread, true);
        var attachedForeground = foregroundThread != 0 &&
            foregroundThread != currentThread &&
            foregroundThread != targetThread &&
            AttachThreadInput(currentThread, foregroundThread, true);

        try
        {
            _ = BringWindowToTop(target);
            _ = SetForegroundWindow(target);
            _ = SetFocus(target);
        }
        finally
        {
            if (attachedForeground)
            {
                _ = AttachThreadInput(currentThread, foregroundThread, false);
            }

            if (attachedTarget)
            {
                _ = AttachThreadInput(currentThread, targetThread, false);
            }
        }

        return GetForegroundWindow() == target;
    }

    private static IntPtr FindVisibleWindow(string title)
    {
        var result = IntPtr.Zero;
        _ = EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window) || !string.Equals(ReadTitle(window), title, StringComparison.Ordinal))
            {
                return true;
            }

            result = window;
            return false;
        }, IntPtr.Zero);
        return result;
    }

    private static string ReadTitle(IntPtr window)
    {
        var length = GetWindowTextLength(window);
        var builder = new StringBuilder(Math.Max(1, length + 1));
        _ = GetWindowText(window, builder, builder.Capacity);
        return builder.ToString();
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, bool attachState);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);
}
