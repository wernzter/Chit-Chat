using System.Text;
using System.Text.RegularExpressions;
using ChitChat.Abstractions;
using SherpaOnnx;

namespace ChitChat.Speech;

/// <summary>Runs the installed streaming English Zipformer model on the CPU.</summary>
/// <param name="modelDirectory">The complete model directory, or the managed download location by default.</param>
public sealed partial class ZipformerTranscriber(string? modelDirectory = null)
{
    internal const string Encoder = "encoder-epoch-99-avg-1-chunk-16-left-128.int8.onnx";
    internal const string Decoder = "decoder-epoch-99-avg-1-chunk-16-left-128.int8.onnx";
    internal const string Joiner = "joiner-epoch-99-avg-1-chunk-16-left-128.int8.onnx";
    internal const string Vocabulary = "tokens.txt";
    internal const string Bpe = "bpe.vocab";
    private static readonly float[] TailPadding = new float[(int)(AudioChunk.SampleRate * 0.6)];

    /// <summary>Finds the complete streaming model in the per-user download library.</summary>
    public static string? Locate()
    {
        var path = SpeechModelInstaller.InstalledModelPath(SpeechModelVariant.StreamingEnglish);
        return IsComplete(path) ? path : null;
    }

    /// <summary>Checks whether a folder contains the expected streaming model files and lengths.</summary>
    /// <param name="directory">The model directory to check.</param>
    public static bool IsComplete(string? directory) => SpeechModelInstaller.IsComplete(directory, SpeechModelVariant.StreamingEnglish);

    /// <summary>Starts a local streaming recording with optional English dictionary hints.</summary>
    /// <param name="hints">Preferred words and phrases for recognition.</param>
    public IStreamingSpeechSession Start(IReadOnlyList<string> hints)
    {
        ArgumentNullException.ThrowIfNull(hints);
        var folder = modelDirectory ?? Locate();
        if (folder is null || !IsComplete(folder)) throw new InvalidOperationException("The streaming English model is missing.");

        var hotwords = PrepareHotwords(hints);
        var config = new OnlineRecognizerConfig
        {
            DecodingMethod = hotwords.Length > 0 ? "modified_beam_search" : "greedy_search",
            EnableEndpoint = 1,
            Rule1MinTrailingSilence = 2.4f,
            Rule2MinTrailingSilence = 1.2f,
            HotwordsBuf = hotwords,
            HotwordsBufSize = hotwords.Length,
        };
        config.FeatConfig.SampleRate = AudioChunk.SampleRate;
        config.FeatConfig.FeatureDim = 80;
        config.ModelConfig.NumThreads = 2;
        config.ModelConfig.Provider = "cpu";
        config.ModelConfig.ModelingUnit = "bpe";
        config.ModelConfig.BpeVocab = Path.Combine(folder, Bpe);
        config.ModelConfig.Tokens = Path.Combine(folder, Vocabulary);
        config.ModelConfig.Transducer.Encoder = Path.Combine(folder, Encoder);
        config.ModelConfig.Transducer.Decoder = Path.Combine(folder, Decoder);
        config.ModelConfig.Transducer.Joiner = Path.Combine(folder, Joiner);

        var recognizer = new OnlineRecognizer(config);
        try
        {
            var stream = recognizer.CreateStream();
            if (stream.Handle != IntPtr.Zero) return new Session(recognizer, stream);
            stream.Dispose();
            throw new InvalidOperationException("The streaming speech recognizer could not start.");
        }
        catch
        {
            recognizer.Dispose();
            throw;
        }
    }

    internal static string PrepareHotwords(IEnumerable<string> hints) =>
        string.Join('\n', hints.Select(hint => hint.Trim())
            .Where(hint => hint.Length is > 0 and <= 64 && HintCharacters().IsMatch(hint))
            .Select(hint => hint.ToUpperInvariant()).Distinct(StringComparer.Ordinal).Take(64));

    private static string DisplayText(string text)
    {
        var normalized = Pronoun().Replace(text.ToLowerInvariant(), "I");
        return normalized.Length == 0 ? normalized : char.ToUpperInvariant(normalized[0]) + normalized[1..];
    }

    // Other characters can produce unknown tokens that the native decoder writes to its diagnostics.
    [GeneratedRegex(@"\A[A-Za-z][A-Za-z' ]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex HintCharacters();

    [GeneratedRegex(@"\bi\b", RegexOptions.CultureInvariant)]
    private static partial Regex Pronoun();

    private sealed class Session(OnlineRecognizer recognizer, OnlineStream stream) : IStreamingSpeechSession
    {
        private readonly StringBuilder _segments = new();
        private string _preview = string.Empty;
        private string? _final;
        private bool _disposed;

        public string Feed(ReadOnlyMemory<float> samples)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_final is not null) throw new InvalidOperationException("The speech session has ended.");
            if (samples.IsEmpty) return _preview;

            stream.AcceptWaveform(AudioChunk.SampleRate, samples.ToArray());
            if (!recognizer.IsReady(stream)) return _preview;
            Decode();
            var text = ReadResult();
            if (recognizer.IsEndpoint(stream))
            {
                AppendSegment(text);
                recognizer.Reset(stream);
                text = string.Empty;
            }
            return _preview = Combine(text);
        }

        public string Finish()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_final is not null) return _final;

            stream.AcceptWaveform(AudioChunk.SampleRate, TailPadding);
            stream.InputFinished();
            Decode();
            AppendSegment(ReadResult());
            return _final = _segments.ToString();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { stream.Dispose(); }
            finally { recognizer.Dispose(); }
        }

        private void Decode()
        {
            while (recognizer.IsReady(stream)) recognizer.Decode(stream);
        }

        private string ReadResult() => DisplayText(recognizer.GetResult(stream).Text?.Trim() ?? string.Empty);

        private void AppendSegment(string text)
        {
            if (text.Length == 0) return;
            if (_segments.Length > 0) _segments.Append(". ");
            _segments.Append(text);
        }

        private string Combine(string text)
        {
            if (_segments.Length == 0) return text;
            if (text.Length == 0) return _segments.ToString();
            return _segments.ToString() + ". " + text;
        }
    }
}
