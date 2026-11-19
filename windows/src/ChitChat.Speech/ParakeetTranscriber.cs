using ChitChat.Abstractions;
using SherpaOnnx;

namespace ChitChat.Speech;

/// <summary>Runs the locally installed Parakeet model on the CPU.</summary>
/// <param name="modelDirectory">The directory containing the model and vocabulary.</param>
public sealed class ParakeetTranscriber(string modelDirectory) : ITranscriber
{
    private const string ModelFolder = "parakeet-v2";
    private const string Encoder = "encoder.int8.onnx";
    private const string Decoder = "decoder.int8.onnx";
    private const string Joiner = "joiner.int8.onnx";
    private const string Vocabulary = "tokens.txt";

    private readonly Lock _gate = new();
    private OfflineRecognizer? _model;
    private bool _disposed;

    /// <summary>The files expected in a model directory.</summary>
    public static IReadOnlyList<string> RequiredFiles { get; } = Array.AsReadOnly([Encoder, Decoder, Joiner, Vocabulary]);

    /// <summary>Returns the standard per-user, migration, and portable model directories.</summary>
    public static IEnumerable<string> DefaultSearchPaths() =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChitChat", "models", ModelFolder),
        AppStorage.LegacyModelDirectory,
        Path.Combine(AppContext.BaseDirectory, "models", ModelFolder),
    ];

    /// <summary>Finds the first complete default model location.</summary>
    public static string? Locate() => DefaultSearchPaths().FirstOrDefault(IsComplete);

    /// <summary>Checks whether a directory contains all four expected files.</summary>
    /// <param name="directory">The model location.</param>
    public static bool IsComplete(string directory) => RequiredFiles.All(file => File.Exists(Path.Combine(directory, file)));

    /// <inheritdoc />
    public bool IsReady
    {
        get
        {
            lock (_gate) return _model is not null;
        }
    }

    /// <inheritdoc />
    public ValueTask<bool> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return ValueTask.FromResult(Open() is not null);
    }

    /// <inheritdoc />
    public ValueTask<string> TranscribeAsync(ReadOnlyMemory<float> samples, IReadOnlyList<string> biasPhrases,
                                             CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (samples.IsEmpty) return ValueTask.FromResult(string.Empty);

        lock (_gate)
        {
            var model = Open();
            if (model is null) return ValueTask.FromResult(string.Empty);

            using var input = model.CreateStream();
            input.AcceptWaveform(AudioChunk.SampleRate, samples.ToArray());
            model.Decode(input);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(input.Result.Text?.Trim() ?? string.Empty);
        }
    }

    private OfflineRecognizer? Open()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_model is not null || !IsComplete(modelDirectory)) return _model;

        var settings = new OfflineRecognizerConfig { DecodingMethod = "greedy_search" };
        settings.FeatConfig.FeatureDim = 128;
        settings.FeatConfig.SampleRate = AudioChunk.SampleRate;
        settings.ModelConfig.Provider = "cpu";
        settings.ModelConfig.NumThreads = 4;
        settings.ModelConfig.ModelType = "nemo_transducer";
        settings.ModelConfig.Tokens = Path.Combine(modelDirectory, Vocabulary);
        settings.ModelConfig.Transducer.Encoder = Path.Combine(modelDirectory, Encoder);
        settings.ModelConfig.Transducer.Decoder = Path.Combine(modelDirectory, Decoder);
        settings.ModelConfig.Transducer.Joiner = Path.Combine(modelDirectory, Joiner);
        return _model = new OfflineRecognizer(settings);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _disposed = true;
            _model?.Dispose();
            _model = null;
        }
        return ValueTask.CompletedTask;
    }
}
