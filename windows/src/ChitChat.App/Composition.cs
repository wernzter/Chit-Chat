using Avalonia.Threading;
using ChitChat.Abstractions;
using ChitChat.Core;
using ChitChat.Speech;

namespace ChitChat.App;

/// <summary>Owns the preferences, local files, and dictation services for a desktop session.</summary>
public sealed class Composition : IAsyncDisposable
{
    private Composition(AppSettings settings, DictionaryFile dictionary, TranscriptStore history,
                        DictationEngine? engine)
    {
        Settings = settings;
        Dictionary = dictionary;
        Transcripts = history;
        Engine = engine;
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
    /// <summary>Whether a complete model is installed in a default directory.</summary>
    public static bool IsModelInstalled => ParakeetTranscriber.Locate() is not null;

    /// <summary>Loads local files and opens the platform integration.</summary>
    public static Composition Create()
    {
        var preferences = new AppSettings(AppSettings.DefaultPath);
        var dictionary = new DictionaryFile(DictionaryFile.DefaultPath);
        var history = new TranscriptStore(TranscriptStore.DefaultPath);

        var services = PlatformFactory.Current;
        if (services is null) return new(preferences, dictionary, history, null);

        var output = new OptionalOutput(preferences, services.CreateOutput());
        var engine = new DictationEngine(services.CreateCapture(),
                                         services.CreateTrigger(preferences.Data.PushToTalkKey),
                                         new ConfiguredModel(preferences),
                                         output, () => dictionary.Entries);
        engine.Completed += (_, utterance) =>
        {
            if (!preferences.Data.KeepHistory) return;

            Dispatcher.UIThread.Post(() =>
            {
                history.Add(new TranscriptRecord
                {
                    Text = utterance.Text,
                    At = utterance.At,
                    AudioSeconds = utterance.AudioDuration.TotalSeconds,
                    ProcessingSeconds = utterance.ProcessingTime.TotalSeconds,
                    Corrections = utterance.Corrections.Count == 0 ? null : utterance.Corrections,
                });
            });
        };
        return new(preferences, dictionary, history, engine);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => Engine?.DisposeAsync() ?? ValueTask.CompletedTask;

    private sealed class OptionalOutput(AppSettings preferences, ITextInjector output) : ITextInjector
    {
        public ValueTask<bool> InjectAsync(string text, CancellationToken cancellationToken) =>
            preferences.Data.InjectText ? output.InjectAsync(text, cancellationToken) : ValueTask.FromResult(true);
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

        public async ValueTask<string> TranscribeAsync(ReadOnlyMemory<float> samples, IReadOnlyList<string> biasPhrases,
                                                       CancellationToken cancellationToken)
        {
            var model = await PrepareAsync().ConfigureAwait(false);
            return model is null ? string.Empty :
                await model.TranscribeAsync(samples, biasPhrases, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            if (_current is { } current) await current.DisposeAsync().ConfigureAwait(false);
            _current = null;
            _directory = null;
        }
    }
}
