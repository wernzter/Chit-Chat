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
                                     string Text, IReadOnlyList<AppliedCorrection> Corrections)
{
    /// <summary>Whether this run used streaming speech recognition.</summary>
    public bool UsedStreamingSpeech { get; init; }
}

/// <summary>Optional behaviors supplied by the desktop app.</summary>
public sealed record DictationOptions
{
    /// <summary>Whether this recording supports tapping the shortcut to toggle recording.</summary>
    public Func<bool>? TapToToggle { get; init; }
    /// <summary>Captures a fixed destination at the start of the recording.</summary>
    public Func<ITextInjector>? CaptureOutput { get; init; }
    /// <summary>Owns playback settings for the lifetime of microphone capture.</summary>
    public Func<IDisposable?>? StartFeedback { get; init; }
    /// <summary>Plays the start cue on the first audio block and the finish cue after text delivery.</summary>
    public Action<bool>? PlayCue { get; init; }
    /// <summary>Receives the verified editor observer after text delivery.</summary>
    public Action<string, ITextEditObserver>? ObserveInsertedText { get; init; }
    /// <summary>Expires observation of a previous dictation when starting a new recording.</summary>
    public Action? RecordingStarted { get; init; }
    /// <summary>Verifies the selected speech model before microphone capture.</summary>
    public Func<bool>? IsSpeechReady { get; init; }
    /// <summary>Opens a streaming speech session when that engine is selected.</summary>
    public Func<IReadOnlyList<string>, IStreamingSpeechSession?>? StartStreamingSpeech { get; init; }
    /// <summary>Prepares recognized text before dictionary corrections.</summary>
    public Func<string, CancellationToken, Task<string>>? PrepareText { get; init; }
}

/// <summary>Coordinates an entire push-to-talk session through platform-neutral interfaces.</summary>
/// <remarks>Microphone buffers and recognition both belong to one worker, so release never races a second consumer.</remarks>
public sealed class DictationEngine : IAsyncDisposable
{
    private readonly IAudioCapture _capture;
    private readonly IHotkeySource _hotkey;
    private readonly ITranscriber _transcriber;
    private readonly ITextInjector _injector;
    private readonly Func<IReadOnlyList<DictionaryEntry>> _dictionary;
    private readonly DictationOptions? _options;
    private readonly Lock _sync = new();
    private readonly IClock _time;
    private readonly CancellationTokenSource _lifetime = new();
    private Session? _session;
    private Task _worker = Task.CompletedTask;
    private bool _disposed;
    private Action<bool>? _setupShortcut;
    private volatile DictationState _state;
    private volatile float _level;
    private volatile string? _lastError;
    private volatile bool _isStartingCapture;
    private volatile string? _previewText;

    /// <summary>Connects the audio, trigger, and text delivery services.</summary>
    /// <param name="capture">Microphone source.</param>
    /// <param name="hotkey">Push-to-talk control.</param>
    /// <param name="transcriber">Speech recognition.</param>
    /// <param name="injector">Delivery to the target application.</param>
    /// <param name="dictionary">Current dictionary lookup.</param>
    /// <param name="clock">Optional time source.</param>
    /// <param name="options">Optional desktop behaviors.</param>
    public DictationEngine(IAudioCapture capture, IHotkeySource hotkey, ITranscriber transcriber,
                           ITextInjector injector, Func<IReadOnlyList<DictionaryEntry>> dictionary,
                           IClock? clock = null, DictationOptions? options = null)
    {
        _capture = capture;
        _hotkey = hotkey;
        _transcriber = transcriber;
        _injector = injector;
        _dictionary = dictionary;
        _options = options;
        _time = clock ?? SystemClock.Instance;
        _hotkey.Pressed += TriggerDown;
        _hotkey.Released += TriggerUp;
    }

    private sealed class Session(ITextInjector output, bool fromShortcut, bool tapToToggle) : IDisposable
    {
        internal ITextInjector Output { get; } = output;
        internal long? PressedAt { get; } = fromShortcut ? Stopwatch.GetTimestamp() : null;
        internal bool TapToToggle { get; } = tapToToggle;
        internal bool Latched { get; set; }
        internal readonly CancellationTokenSource Capture = new();
        internal readonly TaskCompletionSource<DateTimeOffset> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Dispose() => Capture.Dispose();
    }

    /// <summary>The current dictation stage.</summary>
    public DictationState State => _state;
    /// <summary>The latest input amplitude for display.</summary>
    public float Level => _level;
    /// <summary>Whether the worker is opening the selected speech engine or microphone.</summary>
    public bool IsStartingCapture => _isStartingCapture;
    /// <summary>The current provisional text in a streaming session.</summary>
    public string? PreviewText => _previewText;
    /// <summary>A user-facing error from the last unsuccessful capture or transcription.</summary>
    public string? LastError => _lastError;
    /// <summary>Emits each completed nonempty dictation.</summary>
    public event EventHandler<DictationResult>? Completed;
    /// <summary>Emits text that could not be delivered to its original editor.</summary>
    public event EventHandler<string>? DeliveryFailed;
    /// <summary>Emits changes to the stage or input amplitude.</summary>
    public event EventHandler? Changed;

    /// <summary>Arms the native hotkey.</summary>
    public bool Start() => _hotkey.Start();

    /// <summary>Updates the live shortcut as long as recording and setup are idle.</summary>
    /// <param name="virtualKey">The primary virtual key.</param>
    /// <param name="modifiers">Additional required modifiers.</param>
    public bool TryChangeShortcut(int virtualKey, HotkeyModifiers modifiers)
    {
        lock (_sync)
        {
            if (_disposed || _session is not null || _setupShortcut is not null
                || _hotkey is not IConfigurableHotkeySource configurable) return false;
            configurable.Configure(virtualKey, modifiers);
            return true;
        }
    }

    /// <summary>Routes physical shortcut events to diagnostics while recording is paused.</summary>
    /// <param name="onShortcut">Receives press and release events until the lease is disposed.</param>
    public IDisposable? TryBeginSetup(Action<bool> onShortcut)
    {
        ArgumentNullException.ThrowIfNull(onShortcut);
        lock (_sync)
        {
            if (_disposed || _session is not null || _setupShortcut is not null) return null;
            _setupShortcut = onShortcut;
            return new SetupLease(this, onShortcut);
        }
    }

    private sealed class SetupLease(DictationEngine engine, Action<bool> callback) : IDisposable
    {
        public void Dispose()
        {
            lock (engine._sync)
            {
                if (ReferenceEquals(engine._setupShortcut, callback)) engine._setupShortcut = null;
            }
        }
    }

    /// <summary>Equivalent to pressing or releasing from the app's control.</summary>
    public void TogglePushToTalk()
    {
        if (State == DictationState.Idle) Begin(fromShortcut: false);
        else if (State == DictationState.Recording) Release();
    }

    private void TriggerDown(object? sender, EventArgs e)
    {
        Action<bool>? setup;
        bool shouldStop;
        lock (_sync)
        {
            setup = _setupShortcut;
            shouldStop = _session is { } active && (active.Latched || active.PressedAt is null);
        }
        if (setup is not null) setup(true);
        else if (shouldStop) Release();
        else Begin(fromShortcut: true);
    }

    private void TriggerUp(object? sender, EventArgs e)
    {
        Action<bool>? setup;
        bool shouldStop = false;
        lock (_sync)
        {
            setup = _setupShortcut;
            if (setup is null && _session is { PressedAt: long pressed, Latched: false } active)
            {
                active.Latched = active.TapToToggle && Stopwatch.GetElapsedTime(pressed) < TimeSpan.FromMilliseconds(350);
                shouldStop = !active.Latched;
            }
        }
        if (setup is not null) setup(false);
        else if (shouldStop) Release();
    }

    private void Begin(bool fromShortcut)
    {
        lock (_sync)
        {
            if (_disposed || _session is not null || _setupShortcut is not null) return;
            var session = new Session(_options?.CaptureOutput?.Invoke() ?? _injector,
                fromShortcut, _options?.TapToToggle?.Invoke() == true);
            _session = session;
            _lastError = null;
            _isStartingCapture = true;
            _state = DictationState.Recording;
            _options?.RecordingStarted?.Invoke();
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
            _previewText = null;
            _isStartingCapture = false;
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
            if (_options?.IsSpeechReady?.Invoke() == false)
            {
                _lastError = "Open Speech engine to download or select a speech model.";
                return;
            }
            var entries = _dictionary();
            var hints = DictionaryCorrector.BiasPhrases(entries);
            using var streaming = _options?.StartStreamingSpeech?.Invoke(hints);
            if (_options?.IsSpeechReady is not null && streaming is null &&
                !await _transcriber.LoadAsync(session.Capture.Token).ConfigureAwait(false))
            {
                _lastError = "The selected speech model could not be prepared. Open Speech engine to check the model.";
                return;
            }
            if (session.Capture.IsCancellationRequested) return;
            var samples = streaming is null ? new List<float>() : null;
            var sampleCount = 0;
            try
            {
                using var feedback = _options?.StartFeedback?.Invoke();
                await foreach (var block in _capture.CaptureAsync(session.Capture.Token).ConfigureAwait(false))
                {
                    var first = sampleCount == 0;
                    lock (_sync)
                    {
                        if (_state != DictationState.Recording) break;
                        sampleCount += block.Samples.Length;
                        _isStartingCapture = false;
                        _level = block.Rms();
                    }
                    if (first && sampleCount > 0) _options?.PlayCue?.Invoke(true);
                    if (streaming is not null)
                    {
                        var preview = streaming.Feed(block.Samples);
                        lock (_sync)
                        {
                            if (_state == DictationState.Recording) _previewText = preview;
                        }
                    }
                    else samples!.AddRange(block.Samples.Span);
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (OperationCanceledException) when (session.Capture.IsCancellationRequested)
            {
            }

            var released = await session.Release.Task.WaitAsync(_lifetime.Token).ConfigureAwait(false);
            if (sampleCount == 0) return;
            _lifetime.Token.ThrowIfCancellationRequested();
            var raw = streaming is not null ? streaming.Finish() :
                await RecognizeAsync(samples!.ToArray(), hints).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(raw))
                await FinishAsync(session, entries, raw, sampleCount, released, streaming is not null).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested || session.Capture.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            _lastError = "Dictation could not finish. Check your microphone and speech model.";
            Trace.TraceWarning("The dictation session could not be completed.");
        }
        finally
        {
            lock (_sync)
            {
                _session = null;
                _level = 0;
                _isStartingCapture = false;
                _previewText = null;
                _state = DictationState.Idle;
                session.Dispose();
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task<string> RecognizeAsync(float[] samples, IReadOnlyList<string> hints)
    {
        var lines = new List<string>();
        foreach (var segment in AudioSegmenter.Split(samples))
        {
            var speech = (await _transcriber.TranscribeAsync(segment, hints, _lifetime.Token)
                                           .ConfigureAwait(false)).Trim();
            if (speech.Length > 0) lines.Add(speech);
        }
        return string.Join(' ', lines);
    }

    private async Task FinishAsync(Session session, IReadOnlyList<DictionaryEntry> entries, string raw, int sampleCount,
                                   DateTimeOffset released, bool usedStreamingSpeech)
    {
        var prepared = _options?.PrepareText is { } prepare
            ? await prepare(raw, _lifetime.Token).ConfigureAwait(false)
            : raw;
        if (string.IsNullOrWhiteSpace(prepared)) prepared = raw;
        var (text, changes) = new DictionaryCorrector(entries).Apply(prepared);
        var duration = TimeSpan.FromSeconds((double)sampleCount / AudioChunk.SampleRate);
        Completed?.Invoke(this, new DictationResult(released, duration, _time.Now - released, text, changes)
        {
            UsedStreamingSpeech = usedStreamingSpeech,
        });
        if (!await session.Output.InjectAsync(text, _lifetime.Token).ConfigureAwait(false))
            DeliveryFailed?.Invoke(this, text);
        else if (session.Output is ITextEditObserver { CanObserveEdits: true } observer)
            _options?.ObserveInsertedText?.Invoke(text, observer);
        _options?.PlayCue?.Invoke(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Task completion;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _setupShortcut = null;
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
