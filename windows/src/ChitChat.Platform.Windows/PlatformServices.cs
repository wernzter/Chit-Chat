using ChitChat.Abstractions;

namespace ChitChat.Platform.Windows;

/// <summary>The Windows integrations shipped with Chit-Chat.</summary>
public sealed class PlatformServices : IPlatformServices
{
    /// <inheritdoc />
    public IAudioCapture CreateCapture() => new WasapiAudioCapture();

    /// <inheritdoc />
    public IHotkeySource CreateTrigger(int virtualKey) => new PushToTalkHook { Key = (PushToTalkKey)virtualKey };

    /// <inheritdoc />
    public ITextInjector CreateOutput() => new SendInputTextInjector();
}
