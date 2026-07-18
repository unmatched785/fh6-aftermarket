using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Fh6Aftermarket.Input;

public sealed class WindowsKeySender : IKeySender
{
    private const uint InputKeyboard = 1;
    private const uint KeyUp = 0x0002;

    private static readonly IReadOnlyDictionary<string, ushort> VirtualKeys =
        new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            ["Escape"] = 0x1B,
            ["Enter"] = 0x0D,
            ["Up"] = 0x26,
            ["Down"] = 0x28,
            ["Left"] = 0x25,
            ["Right"] = 0x27,
            ["F1"] = 0x70,
            ["F2"] = 0x71
        };

    public void Send(string key)
    {
        var virtualKey = Resolve(key);
        var inputs = new[]
        {
            CreateInput(virtualKey, 0),
            CreateInput(virtualKey, KeyUp)
        };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>());
        if (sent != inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not send key: {key}");
        }
    }

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

    private static NativeInput CreateInput(ushort virtualKey, uint flags)
    {
        return new NativeInput
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = virtualKey,
                    Flags = flags
                }
            }
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(
        uint inputCount,
        [In] NativeInput[] inputs,
        int inputSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }
}
