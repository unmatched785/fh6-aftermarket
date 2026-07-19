using System.Runtime.InteropServices;

namespace Fh6Aftermarket.Input;

public sealed class WindowsKeySender : IKeySender
{
    private const uint KeyEventKeyUp = 0x0002;
    private const int KeyHoldMilliseconds = 55;

    private static readonly IReadOnlyDictionary<string, ushort> VirtualKeys =
        new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            ["Escape"] = 0x1B,
            ["Enter"] = 0x0D,
            ["Up"] = 0x26,
            ["Down"] = 0x28,
            ["Left"] = 0x25,
            ["Right"] = 0x27,
            ["PageDown"] = 0x22,
            ["A"] = 0x41,
            ["D"] = 0x44,
            ["M"] = 0x4D,
            ["W"] = 0x57,
            ["X"] = 0x58,
            ["F1"] = 0x70,
            ["F2"] = 0x71
        };

    public void Send(string key)
        => Hold(key, KeyHoldMilliseconds);

    public void Hold(string key, int milliseconds)
    {
        if (milliseconds is < 1 or > 30_000)
        {
            throw new ArgumentOutOfRangeException(nameof(milliseconds));
        }

        KeyDown(key);
        try
        {
            Thread.Sleep(milliseconds);
        }
        finally
        {
            KeyUp(key);
        }
    }

    public void KeyDown(string key) => SendKeyEvent(key, 0);

    public void KeyUp(string key) => SendKeyEvent(key, KeyEventKeyUp);

    public bool IsDown(string key)
    {
        var state = GetAsyncKeyState(Resolve(key));
        return (state & 0x8000) != 0;
    }

    private static ushort Resolve(string key)
    {
        return VirtualKeys.TryGetValue(key, out var virtualKey)
            ? virtualKey
            : throw new InvalidOperationException($"Unsupported key: {key}");
    }

    private static void SendKeyEvent(string key, uint flags)
    {
        var virtualKey = Resolve(key);
        var scanCode = (byte)MapVirtualKey(virtualKey, 0);
        keybd_event((byte)virtualKey, scanCode, flags, UIntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll")]
    private static extern void keybd_event(
        byte virtualKey,
        byte scanCode,
        uint flags,
        UIntPtr extraInfo);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

}
