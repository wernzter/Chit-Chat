using System.Runtime.InteropServices;
using ChitChat.Abstractions;

namespace ChitChat.Platform.Windows;

/// <summary>Delivers completed dictation to the original foreground editor.</summary>
public sealed class SendInputTextInjector : ITextInjector, ITextEditObserver
{
    private const uint Release = 0x0002;
    private const uint Unicode = 0x0004;
    private const int PageSize = 240;

    private static readonly int NativeSize = Marshal.SizeOf<Input>();
    private IFocusedEditObservation? _editObservation;
    private bool _requiresSafeEditor;

    /// <summary>Foreground window that must still be active when this injector runs.</summary>
    public nint? ExpectedWindow { get; init; }

    /// <summary>Native editor control that must still have focus when available.</summary>
    public nint? ExpectedFocus { get; init; }

    /// <inheritdoc />
    public bool CanObserveEdits => _editObservation?.IsAvailable == true;

    /// <inheritdoc />
    public bool TryReadEditedText(string inserted, out string? edited)
    {
        edited = null;
        return _editObservation is { } observation && observation.TryRead(inserted, out edited);
    }

    /// <inheritdoc />
    public ValueTask<bool> InjectAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (text.Length == 0) return ValueTask.FromResult(true);

        // The setup check supplies its own window. An otherwise unbound injector must still
        // verify a foreground editor before reading or sending keys to it.
        var target = _requiresSafeEditor || ExpectedWindow is not null ? this : CaptureForeground();
        return ValueTask.FromResult(target.Inject(text, cancellationToken));
    }

    private bool Inject(string text, CancellationToken cancellationToken)
    {
        var target = GetForegroundWindow();
        if (!IsTarget(target)) return false;

        var delivered = false;
        try
        {
            if (_requiresSafeEditor && _editObservation?.Prepare(text) != true) return false;
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsTarget(target)) return false;

            delivered = SendText(text, target, cancellationToken);
            return delivered;
        }
        finally
        {
            _editObservation?.Complete(delivered);
        }
    }

    private bool SendText(string text, nint target, CancellationToken cancellationToken)
    {
        var normalized = text.ReplaceLineEndings("\n");
        if (normalized.Contains('\n'))
        {
            // Chat editors can treat a synthetic Return key as Send. Only use direct replacement
            // on native multiline controls. The caller offers recovery when delivery is unavailable.
            if (_editObservation is not FocusedEditObservation native || normalized.Contains('\0') ||
                cancellationToken.IsCancellationRequested || !IsTarget(target)) return false;
            return native.TryReplaceMultiline(normalized.ReplaceLineEndings("\r\n"));
        }

        var strokes = new List<Input>(2 * PageSize);
        for (var index = 0; index < normalized.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var character = normalized[index];
            var down = Keyboard(0, character, Unicode);
            var up = down;
            up.Values.Keyboard.Flags |= Release;
            strokes.Add(down);
            strokes.Add(up);

            if (strokes.Count < 2 * PageSize ||
                (char.IsHighSurrogate(character) && index + 1 < normalized.Length)) continue;
            if (!IsTarget(target) || !SendTo(target, strokes.ToArray())) return false;
            strokes.Clear();
        }
        return strokes.Count == 0 || (IsTarget(target) && SendTo(target, strokes.ToArray()));
    }

    private bool IsTarget(nint target) => target != 0 && target == GetForegroundWindow()
        && (ExpectedWindow is null || ExpectedWindow == target)
        && (ExpectedFocus is null || ExpectedFocus == FocusedControl());

    /// <summary>Captures the active window and native focus before recording.</summary>
    /// <param name="watchEdits">Allows a supported control to verify and observe the delivered span.</param>
    public static SendInputTextInjector CaptureForeground(bool watchEdits = false)
    {
        var window = GetForegroundWindow();
        _ = GetWindowThreadProcessId(window, out var owner);
        var focus = FocusedControl();
        var injector = new SendInputTextInjector
        {
            ExpectedWindow = owner == (uint)Environment.ProcessId ? 0 : window,
            ExpectedFocus = focus,
            _requiresSafeEditor = true,
        };
        if (owner != 0 && focus is { } editor && injector.IsTarget(window))
        {
            bool IsOriginalTarget() => injector.IsTarget(window);
            var native = FocusedEditObservation.ForTarget(editor, owner, IsOriginalTarget, watchEdits, out var knownNativeEditor);
            injector._editObservation = native;
            if (native is null && !knownNativeEditor)
                injector._editObservation = new AutomationEditObservation(owner, IsOriginalTarget, watchEdits);
        }
        return injector;
    }

    private static nint? FocusedControl()
    {
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        return GetGUIThreadInfo(0, ref info) && info.Focus != 0 ? info.Focus : null;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        internal uint Size;
        internal uint Flags;
        internal nint Active;
        internal nint Focus;
        internal nint Capture;
        internal nint MenuOwner;
        internal nint MoveSize;
        internal nint Caret;
        internal int CaretLeft;
        internal int CaretTop;
        internal int CaretRight;
        internal int CaretBottom;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
}
