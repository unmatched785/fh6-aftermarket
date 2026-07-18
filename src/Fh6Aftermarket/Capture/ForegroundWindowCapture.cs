using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace Fh6Aftermarket.Capture;

public sealed record CapturedWindow(string Title, Bitmap Image) : IDisposable
{
    public void Dispose() => Image.Dispose();
}

public static class ForegroundWindowCapture
{
    public static CapturedWindow Capture()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            throw new InvalidOperationException("No foreground window is available.");
        }

        if (IsIconic(window))
        {
            throw new InvalidOperationException("The foreground window is minimized.");
        }

        if (!GetClientRect(window, out var clientRect))
        {
            throw new InvalidOperationException("Could not read the foreground window client area.");
        }

        var origin = new NativePoint { X = 0, Y = 0 };
        if (!ClientToScreen(window, ref origin))
        {
            throw new InvalidOperationException("Could not resolve the foreground window screen position.");
        }

        var width = clientRect.Right - clientRect.Left;
        var height = clientRect.Bottom - clientRect.Top;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("The foreground window client area is empty.");
        }

        var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(
                origin.X,
                origin.Y,
                0,
                0,
                new Size(width, height),
                CopyPixelOperation.SourceCopy);
        }

        return new CapturedWindow(ReadWindowTitle(window), bitmap);
    }

    private static string ReadWindowTitle(IntPtr window)
    {
        var length = GetWindowTextLength(window);
        var builder = new StringBuilder(Math.Max(1, length + 1));
        _ = GetWindowText(window, builder, builder.Capacity);
        return builder.ToString();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
