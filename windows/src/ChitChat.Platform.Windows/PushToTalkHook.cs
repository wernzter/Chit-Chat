using System.Runtime.InteropServices;
using System.Threading.Channels;
using ChitChat.Abstractions;

namespace ChitChat.Platform.Windows;

/// <summary>Virtual-key choices available for push-to-talk.</summary>
public enum PushToTalkKey
{
    /// <summary>Right-hand Control.</summary>
    RightControl = 0xA3,
    /// <summary>Right-hand Shift.</summary>
    RightShift = 0xA1,
    /// <summary>Right-hand Alt, often used as AltGr.</summary>
    RightAlt = 0xA5,
    /// <summary>Caps Lock.</summary>
    CapsLock = 0x14,
    /// <summary>Function key F13.</summary>
    F13 = 0x7C,
}

/// <summary>Observes a held key using a dedicated Win32 message thread.</summary>
public sealed class PushToTalkHook : IConfigurableHotkeySource
{
    internal static readonly nint InputMarker = 0x43484354;
    private const uint Quit = 0x0012;
    private const int LowLevelKeyboard = 13;
    private readonly Lock _gate = new();
    private Listener? _listener;
    private Shortcut _shortcut = new((int)PushToTalkKey.RightControl, HotkeyModifiers.None);

    /// <summary>The configured trigger.</summary>
    public PushToTalkKey Key
    {
        get => (PushToTalkKey)Volatile.Read(ref _shortcut).Key;
        set => Configure((int)value, HotkeyModifiers.None);
    }

    /// <inheritdoc />
    public void Configure(int virtualKey, HotkeyModifiers modifiers) =>
        Volatile.Write(ref _shortcut, new Shortcut(virtualKey, modifiers));

    /// <inheritdoc />
    public event EventHandler? Pressed;
    /// <inheritdoc />
    public event EventHandler? Released;

    /// <inheritdoc />
    public bool Start()
    {
        lock (_gate)
        {
            if (_listener is not null) return true;

            var listener = new Listener(() => Volatile.Read(ref _shortcut));
            listener.Thread.Start();
            var threadId = listener.Ready.Task.GetAwaiter().GetResult();
            if (threadId == 0)
            {
                listener.Thread.Join();
                return false;
            }

            _listener = listener;
            _ = DeliverAsync(listener);
            return true;
        }
    }

    /// <inheritdoc />
    public void StopListening()
    {
        Listener? listener;
        lock (_gate)
        {
            listener = _listener;
            _listener = null;
        }
        if (listener is null) return;

        _ = PostThreadMessage(listener.Ready.Task.Result, Quit, 0, 0);
        listener.Thread.Join();
    }

    private async Task DeliverAsync(Listener listener)
    {
        await foreach (var down in listener.Transitions.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            lock (_gate)
            {
                if (!ReferenceEquals(listener, _listener)) break;
            }
            var callback = down ? Pressed : Released;
            callback?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public void Dispose() => StopListening();

    private sealed record Shortcut(int Key, HotkeyModifiers Modifiers);

    private sealed class Listener
    {
        private readonly Func<Shortcut> _configuredShortcut;
        private Shortcut? _heldShortcut;
        private bool _down;
        internal Thread Thread { get; }
        internal Channel<bool> Transitions { get; } = Channel.CreateUnbounded<bool>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        internal TaskCompletionSource<uint> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Listener(Func<Shortcut> configuredShortcut)
        {
            _configuredShortcut = configuredShortcut;
            Thread = new Thread(Pump) { IsBackground = true, Name = "Chit-Chat keyboard" };
        }

        private void Pump()
        {
            // Create the queue before another thread can post a quit message.
            _ = PeekMessage(out _, 0, 0, 0, 0);
            KeyboardCallback handler = Observe;
            var hook = SetWindowsHookEx(LowLevelKeyboard, handler, GetModuleHandle(null), 0);
            Ready.SetResult(hook == 0 ? 0 : GetCurrentThreadId());
            try
            {
                if (hook == 0) return;
                while (GetMessage(out _, 0, 0, 0) > 0) { }
            }
            finally
            {
                if (hook != 0) _ = UnhookWindowsHookEx(hook);
                GC.KeepAlive(handler);
                Transitions.Writer.TryComplete();
            }
        }

        private nint Observe(int code, nuint message, nint data)
        {
            if (code == 0 && message is 0x0100 or 0x0101 or 0x0104 or 0x0105)
            {
                var input = Marshal.PtrToStructure<KeyEvent>(data);
                if (input.Tag != InputMarker)
                {
                    ObserveShortcut((int)input.KeyCode, message is 0x0100 or 0x0104);
                }
            }
            return CallNextHookEx(0, code, message, data);
        }

        private void ObserveShortcut(int key, bool pressed)
        {
            var current = _configuredShortcut();
            if (_down && _heldShortcut is { } held)
            {
                var modifierReleased = !pressed && (held.Modifiers & KeyModifier(key)) != HotkeyModifiers.None
                    && (ActiveModifiers(key, pressed) & held.Modifiers) != held.Modifiers;
                if (held != current || (!pressed && key == held.Key) || modifierReleased)
                {
                    _down = false;
                    _heldShortcut = null;
                    Transitions.Writer.TryWrite(false);
                }
                return;
            }
            if (!pressed || key != current.Key) return;
            var modifiers = ActiveModifiers(key, pressed) & ~KeyModifier(key);
            if (modifiers != current.Modifiers) return;
            _heldShortcut = current;
            _down = true;
            Transitions.Writer.TryWrite(true);
        }

        private static HotkeyModifiers KeyModifier(int key) => key switch
        {
            0x11 or 0xA2 or 0xA3 => HotkeyModifiers.Control,
            0x10 or 0xA0 or 0xA1 => HotkeyModifiers.Shift,
            0x12 or 0xA4 or 0xA5 => HotkeyModifiers.Alt,
            _ => HotkeyModifiers.None,
        };

        private static HotkeyModifiers ActiveModifiers(int key, bool pressed)
        {
            var result = HotkeyModifiers.None;
            if (IsModifierDown(key, pressed, 0x11, 0xA2, 0xA3)) result |= HotkeyModifiers.Control;
            if (IsModifierDown(key, pressed, 0x10, 0xA0, 0xA1)) result |= HotkeyModifiers.Shift;
            if (IsModifierDown(key, pressed, 0x12, 0xA4, 0xA5)) result |= HotkeyModifiers.Alt;
            return result;
        }

        private static bool IsModifierDown(int key, bool pressed, int generic, int left, int right) => key switch
        {
            _ when key == left => pressed || GetAsyncKeyState(right) < 0,
            _ when key == right => pressed || GetAsyncKeyState(left) < 0,
            _ when key == generic => pressed,
            _ => GetAsyncKeyState(generic) < 0,
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyEvent
    {
        internal uint KeyCode;
        internal uint ScanCode;
        internal uint Flags;
        internal uint Time;
        internal nint Tag;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        internal nint Window;
        internal uint Id;
        internal nuint WParam;
        internal nint LParam;
        internal uint Time;
        internal int X;
        internal int Y;
        internal uint Reserved;
    }

    private delegate nint KeyboardCallback(int code, nuint message, nint data);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW")]
    private static extern nint SetWindowsHookEx(int id, KeyboardCallback callback, nint module, uint thread);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint handle);
    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint handle, int code, nuint message, nint data);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("user32.dll", EntryPoint = "GetMessageW")]
    private static extern int GetMessage(out Message message, nint window, uint min, uint max);
    [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out Message message, nint window, uint min, uint max, uint remove);
    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint thread, uint message, nuint wParam, nint lParam);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);
}
