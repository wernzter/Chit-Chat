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
public sealed class PushToTalkHook : IHotkeySource
{
    internal static readonly nint InputMarker = 0x43484354;
    private const uint Quit = 0x0012;
    private const int LowLevelKeyboard = 13;
    private readonly Lock _gate = new();
    private Listener? _listener;

    /// <summary>The configured trigger.</summary>
    public PushToTalkKey Key { get; set; } = PushToTalkKey.RightControl;

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

            var listener = new Listener(Key);
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

    private sealed class Listener
    {
        private readonly PushToTalkKey _key;
        private bool _down;
        internal Thread Thread { get; }
        internal Channel<bool> Transitions { get; } = Channel.CreateUnbounded<bool>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        internal TaskCompletionSource<uint> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Listener(PushToTalkKey key)
        {
            _key = key;
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
            if (code == 0)
            {
                var input = Marshal.PtrToStructure<KeyEvent>(data);
                if (input.KeyCode == (uint)_key && input.Tag != InputMarker)
                {
                    var down = message is 0x0100 or 0x0104;
                    if (down != _down)
                    {
                        _down = down;
                        Transitions.Writer.TryWrite(down);
                    }
                }
            }
            return CallNextHookEx(0, code, message, data);
        }
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
