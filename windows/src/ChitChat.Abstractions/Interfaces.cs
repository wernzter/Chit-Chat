namespace ChitChat.Abstractions;

/// <summary>A buffer of mono microphone samples at the recognizer sample rate.</summary>
/// <param name="Samples">A buffer whose contents remain valid after delivery.</param>
public readonly record struct AudioChunk(ReadOnlyMemory<float> Samples)
{
    /// <summary>Sample rate in Hz.</summary>
    public const int SampleRate = 16_000;

    /// <summary>Time represented by the buffer.</summary>
    public TimeSpan Duration => TimeSpan.FromSeconds(Samples.Length / (double)SampleRate);

    /// <summary>Mean signal amplitude for display.</summary>
    public float Rms()
    {
        if (Samples.IsEmpty) return 0f;
        double energy = 0;
        var remaining = Samples.Span;
        foreach (var value in remaining) energy += value * (double)value;
        return (float)Math.Sqrt(energy / remaining.Length);
    }
}

/// <summary>Opens a microphone and normalizes its output.</summary>
public interface IAudioCapture : IAsyncDisposable
{
    /// <summary>Whether the microphone is delivering audio.</summary>
    bool IsCapturing { get; }
    /// <summary>Starts the device for the lifetime of an enumeration.</summary>
    /// <param name="cancellationToken">Stops acquisition.</param>
    IAsyncEnumerable<AudioChunk> CaptureAsync(CancellationToken cancellationToken);
}

/// <summary>Produces ordered transitions for push-to-talk.</summary>
public interface IHotkeySource : IDisposable
{
    /// <summary>Signals the start of a key hold.</summary>
    event EventHandler? Pressed;
    /// <summary>Signals the end of a key hold.</summary>
    event EventHandler? Released;
    /// <summary>Enables listening and returns whether it succeeded.</summary>
    bool Start();
    /// <summary>Disables listening.</summary>
    void StopListening();
}

/// <summary>Delivers final text to the active editor.</summary>
public interface ITextInjector
{
    /// <summary>Attempts to insert the dictation at the current input position.</summary>
    /// <param name="text">Final dictation.</param>
    /// <param name="cancellationToken">Stops pending delivery.</param>
    ValueTask<bool> InjectAsync(string text, CancellationToken cancellationToken);
}

/// <summary>Recognizes one recorded utterance at a time.</summary>
public interface ITranscriber : IAsyncDisposable
{
    /// <summary>Whether the recognition model has been opened.</summary>
    bool IsReady { get; }
    /// <summary>Prepares the speech model and returns whether it is available.</summary>
    /// <param name="cancellationToken">Stops model preparation if supported.</param>
    ValueTask<bool> LoadAsync(CancellationToken cancellationToken);
    /// <summary>Performs recognition for one segment of audio.</summary>
    /// <param name="samples">Mono input at the capture sample rate.</param>
    /// <param name="biasPhrases">Preferred transcriptions if the recognizer accepts hints.</param>
    /// <param name="cancellationToken">Stops recognition if supported.</param>
    ValueTask<string> TranscribeAsync(ReadOnlyMemory<float> samples, IReadOnlyList<string> biasPhrases,
                                     CancellationToken cancellationToken);
}

/// <summary>Provides the current time to the dictation engine.</summary>
public interface IClock
{
    /// <summary>Current wall-clock time.</summary>
    DateTimeOffset Now { get; }
}

/// <summary>The wall clock used by the app.</summary>
public sealed class SystemClock : IClock
{
    /// <summary>Shared clock to use in production.</summary>
    public static SystemClock Instance { get; } = new();
    /// <inheritdoc />
    public DateTimeOffset Now => DateTimeOffset.Now;
}

/// <summary>Factory shared by the desktop app and its Windows-specific assembly.</summary>
public interface IPlatformServices
{
    /// <summary>Builds the microphone input.</summary>
    IAudioCapture CreateCapture();
    /// <summary>Builds a push-to-talk listener.</summary>
    /// <param name="virtualKey">Configured Win32 virtual-key code.</param>
    IHotkeySource CreateTrigger(int virtualKey);
    /// <summary>Builds a text delivery service.</summary>
    ITextInjector CreateOutput();
}
