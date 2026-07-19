using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Fh6Aftermarket.Input;

public sealed class WindowsMouseSender : IMouseSender
{
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;

    public void MoveTo(int screenX, int screenY)
    {
        if (!SetCursorPos(screenX, screenY))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"Could not move cursor to ({screenX},{screenY}).");
        }
    }

    public void ClickLeft()
    {
        mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
        try
        {
            Thread.Sleep(55);
        }
        finally
        {
            mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(
        uint flags,
        uint deltaX,
        uint deltaY,
        uint data,
        UIntPtr extraInfo);
}
