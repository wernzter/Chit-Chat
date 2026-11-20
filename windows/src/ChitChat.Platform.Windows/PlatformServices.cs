using System.Diagnostics;
using System.Runtime.InteropServices;
using ChitChat.Abstractions;

namespace ChitChat.Platform.Windows;

/// <summary>The Windows integrations shipped with Chit-Chat.</summary>
public sealed class PlatformServices : IPlatformServices
{
    /// <inheritdoc />
    public IAudioCapture CreateCapture() => new WasapiAudioCapture();

    /// <inheritdoc />
    public IHotkeySource CreateTrigger(int virtualKey, HotkeyModifiers modifiers = HotkeyModifiers.None)
    {
        var hook = new PushToTalkHook();
        hook.Configure(virtualKey, modifiers);
        return hook;
    }

    /// <inheritdoc />
    public ITextInjector CreateOutput() => new SendInputTextInjector();

    /// <inheritdoc />
    public ITextInjector CaptureOutputTarget(bool watchEdits = false) => SendInputTextInjector.CaptureForeground(watchEdits);

    /// <inheritdoc />
    public ITextInjector CreateTestOutput(nint window) => new SendInputTextInjector { ExpectedWindow = window };

    /// <inheritdoc />
    public IOpenAiKeyStore CreateOpenAiKeyStore() => new CredentialManagerOpenAiKeyStore();

    /// <inheritdoc />
    public IDisposable MutePlayback() => new RecordingFeedback();

    /// <inheritdoc />
    public void PlayRecordingCue(bool starting) => RecordingCues.Play(starting);

    /// <inheritdoc />
    public bool ConfigureRecordingOverlay(nint window, bool clickThrough)
    {
        const int extendedStyle = -20;
        if (window == 0) return false;
        var current = (uint)GetWindowLongPtr(window, extendedStyle);
        var updated = RecordingOverlayStyles(current, clickThrough);
        if (current == updated) return true;
        _ = SetWindowLongPtr(window, extendedStyle, (nint)updated);
        return Marshal.GetLastPInvokeError() == 0;
    }

    /// <inheritdoc />
    public uint RecordingOverlayStyles(uint current, bool clickThrough)
    {
        const uint noActivate = 0x08000000;
        const uint toolWindow = 0x00000080;
        const uint transparent = 0x00000020;
        var updated = current | noActivate | toolWindow;
        return clickThrough ? updated | transparent : updated & ~transparent;
    }

    /// <inheritdoc />
    public (int X, int Y)? ActiveDisplayPoint()
    {
        var window = GetForegroundWindow();
        if (window == 0) return null;
        var monitor = MonitorFromWindow(window, 2);
        var info = new MonitorInformation { Size = Marshal.SizeOf<MonitorInformation>() };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info)) return null;
        var bounds = info.Display;
        return (bounds.Left + (bounds.Right - bounds.Left) / 2, bounds.Top + (bounds.Bottom - bounds.Top) / 2);
    }

    /// <inheritdoc />
    public void OpenMicrophoneSettings()
    {
        using var process = Process.Start(new ProcessStartInfo("ms-settings:privacy-microphone") { UseShellExecute = true });
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInformation information);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInformation
    {
        internal int Size;
        internal Rect Display;
        internal Rect WorkArea;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }
}
