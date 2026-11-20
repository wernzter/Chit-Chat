using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ChitChat.Abstractions;
using ChitChat.App.Views;
using ChitChat.Core;
using ChitChat.Speech;

namespace ChitChat.App;

/// <summary>A finished dictation waiting to be copied manually.</summary>
/// <param name="Id">The identifier of this waiting result.</param>
/// <param name="Text">The completed dictation.</param>
public sealed record WaitingDictation(Guid Id, string Text);

/// <summary>Owns the preferences, local files, and dictation services for a desktop session.</summary>
public sealed class Composition : IAsyncDisposable
{
    private readonly IPlatformServices? _platform;
    private readonly OpenAiEnhancement _openAi;
    private readonly LocalAiEnhancement _localAi;
    private readonly LiveCorrectionTracker _corrections;
    private readonly List<WaitingDictation> _waiting = [];
    private SpeechSettingsView? _speechSettings;
    private OpenAiSettingsView? _openAiSettings;
    private LocalAiSettingsView? _localAiSettings;
    private string? _unsavedLatestTranscript;

    private Composition(AppSettings settings, DictionaryFile dictionary, TranscriptStore history,
                        IPlatformServices? platform, DictationEngine? engine, OpenAiEnhancement openAi,
                        LocalAiEnhancement localAi, LiveCorrectionTracker corrections)
    {
        Settings = settings;
        Dictionary = dictionary;
        Transcripts = history;
        Engine = engine;
        _platform = platform;
        _openAi = openAi;
        _localAi = localAi;
        _corrections = corrections;
    }

    /// <summary>Preferences shared by the app windows.</summary>
    public AppSettings Settings { get; }
    /// <summary>The local correction dictionary.</summary>
    public DictionaryFile Dictionary { get; }
    /// <summary>Recognized text saved locally.</summary>
    public TranscriptStore Transcripts { get; }
    /// <summary>Recording control when the platform is available.</summary>
    public DictationEngine? Engine { get; }
    /// <summary>Whether native audio and keyboard support could be loaded.</summary>
    public bool IsPlatformAvailable => Engine is not null;
    /// <summary>The most recent transcript, even if history is disabled.</summary>
    public string? LatestTranscript => _unsavedLatestTranscript ??
        (Transcripts.Records.Count > 0 ? Transcripts.Records[0].Text : null);
    /// <summary>Transcript to offer for manual copying when automatic insertion could not deliver it.</summary>
    public WaitingDictation? Recovery => _waiting.Count > 0 ? _waiting[^1] : null;
    /// <summary>Number of waiting dictations.</summary>
    public int RecoveryCount => _waiting.Count;
    /// <summary>Emits changes to the last dictation, waiting queue, or cloud enhancement status.</summary>
    public event EventHandler? DictationAvailabilityChanged;
    /// <summary>Reports how the last OpenAI attempt finished.</summary>
    public string? OpenAiStatus => Settings.Data.OpenAi.Enabled ? _openAi.LastDictationStatus : null;
    /// <summary>A correction currently offered for an edit in the original editor.</summary>
    public LiveCorrectionSuggestion? CorrectionSuggestion => _corrections.Suggestion;
    /// <summary>Whether the selected speech model is installed.</summary>
    public bool IsSpeechReady => HasSpeechModel(Settings.Data);

    private static bool HasSpeechModel(SettingsData settings) => settings.UseStreamingSpeech
        ? ZipformerTranscriber.Locate() is not null
        : (settings.ModelDirectory ?? ParakeetTranscriber.Locate()) is { } path && ParakeetTranscriber.IsComplete(path);

    /// <summary>Loads local files and opens the platform integration.</summary>
    public static Composition Create()
    {
        var preferences = new AppSettings(AppSettings.DefaultPath);
        var dictionary = new DictionaryFile(DictionaryFile.DefaultPath);
        var history = new TranscriptStore(TranscriptStore.DefaultPath);

        var services = PlatformFactory.Current;
        var openAi = new OpenAiEnhancement(services?.CreateOpenAiKeyStore());
        var localAi = new LocalAiEnhancement();
        var corrections = new LiveCorrectionTracker(preferences, dictionary);
        var engine = services is null ? null : new DictationEngine(services.CreateCapture(),
            services.CreateTrigger(preferences.Data.PushToTalkKey, preferences.Data.ShortcutModifiers),
            new ConfiguredModel(preferences), new OptionalOutput(preferences, services.CreateOutput()),
            () => dictionary.Entries, options: new DictationOptions
            {
                TapToToggle = () => preferences.Data.TapToToggle,
                CaptureOutput = () => new OptionalOutput(preferences, services.CaptureOutputTarget(
                    preferences.Data.InjectText && preferences.Data.LearnFromEdits &&
                    preferences.Data.CorrectionPopupPosition != RecordingIndicatorPosition.Hidden)),
                StartFeedback = () => preferences.Data.MuteAudioWhileRecording ? services.MutePlayback() : null,
                PlayCue = starting => { if (preferences.Data.PlayRecordingSounds) services.PlayRecordingCue(starting); },
                RecordingStarted = () => Dispatcher.UIThread.Post(corrections.Stop),
                IsSpeechReady = () => HasSpeechModel(preferences.Data),
                StartStreamingSpeech = hints => preferences.Data.UseStreamingSpeech
                    ? new ZipformerTranscriber().Start(hints) : null,
                ObserveInsertedText = (text, observer) => Dispatcher.UIThread.Post(() => corrections.Watch(text, observer)),
                PrepareText = async (original, token) =>
                {
                    var current = preferences.Data;
                    var cleaned = current.CleanupEnabled ? TranscriptCleanup.Format(original) : original;
                    if (string.IsNullOrWhiteSpace(cleaned)) cleaned = original;
                    if (current.CleanupEnabled && current.LocalAi.Enabled)
                        cleaned = await localAi.EnhanceAsync(cleaned, original, current.LocalAi,
                            current.OpenAi.MinimumWords, token).ConfigureAwait(false);
                    return await openAi.EnhanceAsync(cleaned, original, current.OpenAi, token).ConfigureAwait(false);
                },
            });
        var app = new Composition(preferences, dictionary, history, services, engine, openAi, localAi, corrections);
        history.Edited += (_, edit) => app.LearnCorrection(edit);
        history.Changed += (_, _) => app.NotifyDictationAvailability();
        if (engine is null) return app;
        openAi.DictationStatusChanged += (_, _) => Dispatcher.UIThread.Post(app.NotifyDictationAvailability);
        engine.DeliveryFailed += (_, text) => Dispatcher.UIThread.Post(() =>
        {
            app._waiting.Add(new WaitingDictation(Guid.NewGuid(), text));
            app.NotifyDictationAvailability();
        });
        engine.Completed += (_, utterance) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                app._unsavedLatestTranscript = null;
                try
                {
                    if (preferences.Data.KeepHistory)
                    {
                        history.Add(new TranscriptRecord
                        {
                            Text = utterance.Text,
                            At = utterance.At,
                            AudioSeconds = utterance.AudioDuration.TotalSeconds,
                            ProcessingSeconds = utterance.ProcessingTime.TotalSeconds,
                            UsedStreamingSpeech = utterance.UsedStreamingSpeech,
                            Corrections = utterance.Corrections.Count == 0 ? null : utterance.Corrections,
                        });
                        return;
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    // The completed text remains available to copy for this session.
                }
                app._unsavedLatestTranscript = utterance.Text;
                app.NotifyDictationAvailability();
            });
        };
        return app;
    }

    /// <summary>Dismisses the identified waiting dictation.</summary>
    /// <param name="id">The exact result the user copied or dismissed.</param>
    public void DismissRecovery(Guid id)
    {
        if (_waiting.RemoveAll(item => item.Id == id) > 0) NotifyDictationAvailability();
    }

    private void NotifyDictationAvailability() => DictationAvailabilityChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Opts out of the proposed dictionary correction.</summary>
    public void DismissCorrection() => _corrections.Dismiss();

    /// <summary>Applies the Windows styles for a recording or correction overlay.</summary>
    /// <param name="window">Handle of the overlay window.</param>
    /// <param name="clickThrough">Whether mouse input should pass to the editor below the overlay.</param>
    internal bool ConfigureRecordingOverlay(nint window, bool clickThrough) =>
        _platform?.ConfigureRecordingOverlay(window, clickThrough) == true;

    internal uint RecordingOverlayStyles(uint current, bool clickThrough) =>
        _platform?.RecordingOverlayStyles(current, clickThrough) ?? current;

    internal PixelPoint? ActiveDisplayPoint() =>
        _platform?.ActiveDisplayPoint() is { } point ? new PixelPoint(point.X, point.Y) : null;

    /// <summary>Saves a shortcut and applies it immediately when the recording listener is idle.</summary>
    /// <param name="virtualKey">The primary key.</param>
    /// <param name="modifiers">Additional required modifiers.</param>
    public bool TryChangeShortcut(int virtualKey, HotkeyModifiers modifiers = HotkeyModifiers.None)
    {
        if (virtualKey is < 1 or > 254 || (Engine is not null && !Engine.TryChangeShortcut(virtualKey, modifiers)))
            return false;
        Settings.Update(Settings.Data with { PushToTalkKey = virtualKey, ShortcutModifiers = modifiers });
        return true;
    }

    /// <summary>The speech configuration page, retained while a model downloads.</summary>
    public Control CreateSpeechSettingsView() => _speechSettings ??= new SpeechSettingsView(Settings);

    /// <summary>The OpenAI configuration page, including connection and prompt tests.</summary>
    public Control CreateOpenAiSettingsView() => _openAiSettings ??= new OpenAiSettingsView(Settings, _openAi);

    /// <summary>Controls for optional local AI cleanup.</summary>
    public Control CreateLocalAiSettingsView() => _localAiSettings ??= new LocalAiSettingsView(Settings, _localAi);

    /// <summary>The interactive diagnostic page for Windows integrations.</summary>
    public Control CreateSetupCheckView() => new SetupCheckView(Engine, _platform);

    private void LearnCorrection(TranscriptEdit edit)
    {
        if (!Settings.Data.LearnFromEdits) return;
        foreach (var rule in TranscriptCleanup.LearnFrom(edit)) _corrections.Save(rule);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _corrections.Dispose();
        return Engine?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    private sealed class OptionalOutput(AppSettings preferences, ITextInjector output) : ITextInjector, ITextEditObserver
    {
        private bool _delivered;
        public bool CanObserveEdits => _delivered && output is ITextEditObserver { CanObserveEdits: true };

        public async ValueTask<bool> InjectAsync(string text, CancellationToken cancellationToken)
        {
            if (!preferences.Data.InjectText) return true;
            _delivered = await output.InjectAsync(text, cancellationToken).ConfigureAwait(false);
            return _delivered;
        }

        public bool TryReadEditedText(string inserted, out string? edited)
        {
            edited = null;
            return CanObserveEdits && ((ITextEditObserver)output).TryReadEditedText(inserted, out edited);
        }
    }

    private sealed class ConfiguredModel(AppSettings preferences) : ITranscriber
    {
        private ParakeetTranscriber? _current;
        private string? _directory;

        public bool IsReady => _current?.IsReady == true;

        private async ValueTask<ParakeetTranscriber?> PrepareAsync()
        {
            var path = preferences.Data.ModelDirectory ?? ParakeetTranscriber.Locate();
            if (string.Equals(path, _directory, StringComparison.Ordinal)) return _current;

            await DisposeAsync().ConfigureAwait(false);
            _directory = path;
            _current = path is null ? null : new ParakeetTranscriber(path);
            return _current;
        }

        public async ValueTask<bool> LoadAsync(CancellationToken cancellationToken)
        {
            var model = await PrepareAsync().ConfigureAwait(false);
            return model is not null && await model.LoadAsync(cancellationToken).ConfigureAwait(false);
        }

        public ValueTask<string> TranscribeAsync(ReadOnlyMemory<float> samples, IReadOnlyList<string> biasPhrases,
                                                  CancellationToken cancellationToken) => _current is { } model
            ? model.TranscribeAsync(samples, biasPhrases, cancellationToken)
            : ValueTask.FromResult(string.Empty);

        public async ValueTask DisposeAsync()
        {
            if (_current is { } current) await current.DisposeAsync().ConfigureAwait(false);
            _current = null;
            _directory = null;
        }
    }
}
