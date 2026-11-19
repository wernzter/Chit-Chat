using System.Diagnostics;
using ChitChat.Abstractions;
using ChitChat.Dictionary;

namespace ChitChat.Core;

/// <summary>The current stage of a push-to-talk interaction.</summary>
public enum DictationState
{
    /// <summary>Ready for another recording.</summary>
    Idle,
    /// <summary>Collecting input from the microphone.</summary>
    Recording,
    /// <summary>Preparing the completed text.</summary>
    Transcribing,
}

/// <summary>The finished text and measurements for one recording.</summary>
/// <param name="At">Time of release.</param>
/// <param name="AudioDuration">Duration of the captured audio.</param>
/// <param name="ProcessingTime">Time spent finalizing the text.</param>
/// <param name="Text">Corrected text.</param>
/// <param name="Corrections">Rules that matched.</param>
public sealed record DictationResult(DateTimeOffset At, TimeSpan AudioDuration, TimeSpan ProcessingTime,
                                     string Text, IReadOnlyList<AppliedCorrection> Corrections);

/// <summary>Coordinates an entire push-to-talk session through platform-neutral interfaces.</summary>
/// <remarks>Microphone buffers and recognition both belong to one worker, so release never races a second consumer.</remarks>
public sealed class DictationEngine : IAsyncDisposable
{
    private readonly IAudioCapture _capture;
    private readonly IHotkeySource _hotkey;
    private readonly ITranscriber _transcriber;
    private readonly ITextInjector _injector;
    private readonly Func<IReadOnlyList<DictionaryEntry>> _dictionary;
    private readonly Lock _sync = new();
    private readonly IClock _time;
    private readonly CancellationTokenSource _lifetime = new();
    private Session? _session;
    private Task _worker = Task.CompletedTask;
    private bool _disposed;
    private volatile DictationState _state;
    private volatile float _level;

    /// <summary>Connects the audio, trigger, and text delivery services.</summary>
    /// <param name="capture">Microphone source.</param>
    /// <param name="hotkey">Push-to-talk control.</param>
    /// <param name="transcriber">Speech recognition.</param>
    /// <param name="injector">Delivery to the target application.</param>
    /// <param name="dictionary">Current dictionary lookup.</param>
    /// <param name="clock">Optional time source.</param>
    public DictationEngine(IAudioCapture capture, IHotkeySource hotkey, ITranscriber transcriber,
                           ITextInjector injector, Func<IReadOnlyList<DictionaryEntry>> dictionary,
                           IClock? clock = null)
    {
        _capture = capture;
        _hotkey = hotkey;
        _transcriber = transcriber;
        _injector = injector;
        _dictionary = dictionary;
        _time = clock ?? SystemClock.Instance;
        _hotkey.Pressed += TriggerDown;
        _hotkey.Released += TriggerUp;
    }

    private sealed class Session : IDisposable
    {
        internal readonly CancellationTokenSource Capture = new();
        internal readonly TaskCompletionSource<DateTimeOffset> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Dispose() => Capture.Dispose();
    }

    /// <summary>The current dictation stage.</summary>
    public DictationState State => _state;
    /// <summary>The latest input amplitude for display.</summary>
    public float Level => _level;
    /// <summary>Emits each completed nonempty dictation.</summary>
    public event EventHandler<DictationResult>? Completed;
    /// <summary>Emits changes to the stage or input amplitude.</summary>
    public event EventHandler? Changed;

    /// <summary>Arms the native hotkey.</summary>
    public bool Start() => _hotkey.Start();

    /// <summary>Equivalent to pressing or releasing from the app's control.</summary>
    public void TogglePushToTalk()
    {
        if (State == DictationState.Idle) Begin();
        else if (State == DictationState.Recording) Release();
    }

    private void TriggerDown(object? sender, EventArgs e) => Begin();
    private void TriggerUp(object? sender, EventArgs e) => Release();

    private void Begin()
    {
        lock (_sync)
        {
            if (_disposed || _session is not null) return;
            var session = new Session();
            _session = session;
            _state = DictationState.Recording;
            _worker = Task.Run(() => RunAsync(session));
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Release()
    {
        lock (_sync)
        {
            if (_state != DictationState.Recording || _session is not { } session) return;
            _level = 0;
            _state = DictationState.Transcribing;
            session.Capture.Cancel();
            session.Release.TrySetResult(_time.Now);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task RunAsync(Session session)
    {
        try
        {
            var samples = new List<float>();
            try
            {
                await foreach (var block in _capture.CaptureAsync(session.Capture.Token).ConfigureAwait(false))
                {
                    lock (_sync)
                    {
                        if (_state != DictationState.Recording) break;
                        samples.AddRange(block.Samples.Span);
                        _level = block.Rms();
                    }
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (OperationCanceledException) when (session.Capture.IsCancellationRequested)
            {
            }

            var released = await session.Release.Task.WaitAsync(_lifetime.Token).ConfigureAwait(false);
            if (samples.Count > 0) await FinishAsync(samples.ToArray(), released).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            Trace.TraceWarning("The dictation session could not be completed.");
        }
        finally
        {
            lock (_sync)
            {
                _session = null;
                _level = 0;
                _state = DictationState.Idle;
                session.Dispose();
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task FinishAsync(float[] samples, DateTimeOffset released)
    {
        var entries = _dictionary();
        var hints = DictionaryCorrector.BiasPhrases(entries);
        var lines = new List<string>();
        foreach (var segment in AudioSegmenter.Split(samples))
        {
            var speech = (await _transcriber.TranscribeAsync(segment, hints, _lifetime.Token)
                                           .ConfigureAwait(false)).Trim();
            if (speech.Length > 0) lines.Add(speech);
        }
        if (lines.Count == 0) return;

        var (text, changes) = new DictionaryCorrector(entries).Apply(string.Join(' ', lines));
        var duration = TimeSpan.FromSeconds((double)samples.Length / AudioChunk.SampleRate);
        Completed?.Invoke(this, new DictationResult(released, duration, _time.Now - released, text, changes));
        await _injector.InjectAsync(text, _lifetime.Token).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Task completion;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _session?.Capture.Cancel();
            _lifetime.Cancel();
            completion = _worker;
        }
        _hotkey.Pressed -= TriggerDown;
        _hotkey.Released -= TriggerUp;
        _hotkey.Dispose();
        await completion.ConfigureAwait(false);
        await _capture.DisposeAsync().ConfigureAwait(false);
        await _transcriber.DisposeAsync().ConfigureAwait(false);
        _lifetime.Dispose();
    }
}
