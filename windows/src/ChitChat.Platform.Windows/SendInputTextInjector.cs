using System.Runtime.InteropServices;
using ChitChat.Abstractions;

namespace ChitChat.Platform.Windows;

/// <summary>Delivers completed dictation to the foreground editor by synthesizing keystrokes.</summary>
public sealed class SendInputTextInjector : ITextInjector
{
    private const int Return = 0x0D;
    private const int Control = 0x11;
    private const uint Release = 0x0002;
    private const uint Unicode = 0x0004;
    private const int PageSize = 240;

    private static readonly int NativeSize = Marshal.SizeOf<Input>();

    /// <summary>Optional dispatcher-backed paste for editors that support the clipboard.</summary>
    public Func<string, CancellationToken, Task<bool>>? ClipboardPaste { get; init; }

    /// <summary>Time for the clipboard to settle before issuing a paste.</summary>
    public static TimeSpan ClipboardSettleDelay => TimeSpan.FromMilliseconds(60);
    /// <summary>Time for the target to read the clipboard before restoring it.</summary>
    public static TimeSpan PasteSettleDelay => TimeSpan.FromMilliseconds(500);

    /// <inheritdoc />
    public async ValueTask<bool> InjectAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (text.Length == 0) return true;

        var multiline = text.Contains('\n') || text.Contains('\r');
        if (ClipboardPaste is { } paste && (multiline || text.Length > 2000))
        {
            return await paste(text, cancellationToken).ConfigureAwait(false);
        }

        var target = GetForegroundWindow();
        if (target == 0) return false;

        var normalized = text.ReplaceLineEndings("\n");
        var strokes = new List<Input>(2 * PageSize);
        for (var index = 0; index < normalized.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var character = normalized[index];
            var down = character == '\n' ? Keyboard(Return, 0, 0) : Keyboard(0, character, Unicode);
            var up = down;
            up.Values.Keyboard.Flags |= Release;
            strokes.Add(down);
            strokes.Add(up);

            if (strokes.Count < 2 * PageSize ||
                (char.IsHighSurrogate(character) && index + 1 < normalized.Length)) continue;
            if (!SendTo(target, strokes.ToArray())) return false;
            strokes.Clear();
        }
        return strokes.Count == 0 || SendTo(target, strokes.ToArray());
    }

    /// <summary>Synthesizes Control+V for clipboard integrations.</summary>
    public static bool PressCtrlV()
    {
        var target = GetForegroundWindow();
        if (target == 0) return false;
        return SendTo(target, [Keyboard(Control, 0, 0), Keyboard('V', 0, 0),
                               Keyboard('V', 0, Release), Keyboard(Control, 0, Release)]);
    }

    private static bool SendTo(nint target, Input[] batch) =>
        target == GetForegroundWindow() && SendInput((uint)batch.Length, batch, NativeSize) == batch.Length;

    private static Input Keyboard(ushort key, ushort scan, uint flags) =>
        new()
        {
            Type = 1,
            Values = new InputValue
            {
                Keyboard = new Keystroke { Key = key, Scan = scan, Flags = flags, Marker = PushToTalkHook.InputMarker },
            },
        };

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        internal uint Type;
        internal InputValue Values;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputValue
    {
        [FieldOffset(0)] internal Keystroke Keyboard;
        [FieldOffset(0)] internal MouseStroke Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Keystroke
    {
        internal ushort Key;
        internal ushort Scan;
        internal uint Flags;
        internal uint Timestamp;
        internal nint Marker;
    }

    // The mouse member gives INPUT's union the size and alignment required by native SendInput.
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseStroke
    {
        internal int X;
        internal int Y;
        internal uint Data;
        internal uint Flags;
        internal uint Time;
        internal nint Marker;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
}
