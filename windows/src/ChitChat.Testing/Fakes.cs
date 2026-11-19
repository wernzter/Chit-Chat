using System.Runtime.CompilerServices;
using ChitChat.Abstractions;

namespace ChitChat.Testing;

/// <summary>Provides replayable input for tests without a microphone.</summary>
/// <param name="samples">The 16 kHz mono recording to replay.</param>
/// <param name="chunkSize">Maximum samples per emitted block.</param>
public sealed class FakeAudioCapture(float[] samples, int chunkSize = AudioChunk.SampleRate / 50) : IAudioCapture
{
    /// <inheritdoc />
    public bool IsCapturing { get; private set; }

    /// <summary>Creates a recording loud enough to drive the input meter.</summary>
    /// <param name="seconds">Duration of the signal.</param>
    /// <param name="amplitude">Peak between zero and one.</param>
    /// <returns>A capture with regularly alternating sample values.</returns>
    public static FakeAudioCapture Tone(double seconds, float amplitude = 0.5f)
    {
        var wave = new float[(int)(seconds * AudioChunk.SampleRate)];
        for (var index = 0; index < wave.Length; index++)
            wave[index] = index % 2 == 0 ? amplitude : -amplitude;
        return new FakeAudioCapture(wave);
    }

    /// <summary>Creates a recording with no signal.</summary>
    /// <param name="seconds">Duration in seconds.</param>
    /// <returns>A capture that emits zeros.</returns>
    public static FakeAudioCapture Silence(double seconds) =>
        new(new float[(int)(seconds * AudioChunk.SampleRate)]);

    /// <inheritdoc />
    public async IAsyncEnumerable<AudioChunk> CaptureAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IsCapturing = true;
        try
        {
            var count = Math.Max(1, chunkSize);
            await Task.Yield();
            foreach (var block in samples.Chunk(count))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new AudioChunk(block);
            }
        }
        finally { IsCapturing = false; }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Lets a test send shortcut events directly.</summary>
public sealed class FakeHotkeySource : IHotkeySource
{
    /// <inheritdoc />
    public event EventHandler? Pressed;
    /// <inheritdoc />
    public event EventHandler? Released;
    /// <inheritdoc />
    public bool IsRunning { get; private set; }

    /// <inheritdoc />
    public bool Start() => IsRunning = true;
    /// <inheritdoc />
    public void StopListening() => IsRunning = false;

    /// <summary>Emits a key-press event.</summary>
    public void Press() => Pressed?.Invoke(this, EventArgs.Empty);
    /// <summary>Emits a key-release event.</summary>
    public void Release() => Released?.Invoke(this, EventArgs.Empty);

    /// <inheritdoc />
    public void Dispose() => StopListening();
}

/// <summary>Supplies predefined recognition results while recording the inference requests.</summary>
/// <param name="responses">Replies in order. The final value repeats if needed.</param>
public sealed class FakeTranscriber(params string[] responses) : ITranscriber
{
    private int _nextResponse;

    /// <inheritdoc />
    public bool IsReady { get; private set; }
    /// <summary>Lengths of the submitted audio segments.</summary>
    public List<int> SegmentLengths { get; } = [];
    /// <summary>Bias phrases from the last recognition call.</summary>
    public IReadOnlyList<string> LastBias { get; private set; } = [];

    /// <inheritdoc />
    public ValueTask<bool> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(IsReady = true);
    }

    /// <inheritdoc />
    public ValueTask<string> TranscribeAsync(ReadOnlyMemory<float> samples, IReadOnlyList<string> biasPhrases,
                                           CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SegmentLengths.Add(samples.Length);
        LastBias = biasPhrases.ToArray();
        if (responses.Length == 0) return ValueTask.FromResult(string.Empty);
        var reply = responses[_nextResponse];
        _nextResponse = Math.Min(_nextResponse + 1, responses.Length - 1);
        return ValueTask.FromResult(reply);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Collects text that would be sent to the editor.</summary>
public sealed class RecordingTextInjector : ITextInjector
{
    /// <summary>The attempted insertions in call order.</summary>
    public List<string> Injected { get; } = [];

    /// <inheritdoc />
    public ValueTask<bool> InjectAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Injected.Add(text);
        return ValueTask.FromResult(true);
    }
}

/// <summary>A deterministic clock for tests that need to advance time.</summary>
public sealed class FakeClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset Now { get; private set; } = DateTimeOffset.UnixEpoch;

    /// <summary>Moves the wall clock forward.</summary>
    /// <param name="by">Elapsed time to add.</param>
    public void Advance(TimeSpan by) => Now = Now.Add(by);
}
