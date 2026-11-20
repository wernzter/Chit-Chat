using System.Diagnostics;
using System.Security.Cryptography;

namespace ChitChat.Speech;

/// <summary>The speech model to install for offline dictation.</summary>
public enum SpeechModelVariant
{
    /// <summary>Parakeet v2 for English speech.</summary>
    English,

    /// <summary>Parakeet v3 for multilingual speech.</summary>
    Multilingual,

    /// <summary>Streaming Zipformer for live English dictation.</summary>
    StreamingEnglish,
}

/// <summary>Aggregate progress for the current model installation.</summary>
/// <param name="DownloadedBytes">Bytes received across all files so far.</param>
/// <param name="TotalBytes">Total bytes expected.</param>
public readonly record struct SpeechModelProgress(long DownloadedBytes, long TotalBytes);

/// <summary>Installs verified speech model files in Chit-Chat's per-user model library.</summary>
public sealed class SpeechModelInstaller
{
    private const int CopyBufferSize = 128 * 1024;
    private static readonly TimeSpan StallLimit = TimeSpan.FromSeconds(60);
    private static readonly HttpClient DownloadClient = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        Timeout = StallLimit,
    };

    // The revisions and large-file hashes come from the Hugging Face model repository metadata.
    // The token hashes cover the raw vocabulary files at those revisions.
    private static readonly Dictionary<SpeechModelVariant, DownloadModel> Models = new()
        {
            [SpeechModelVariant.English] = new(
                "csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8",
                "84b478d541cdbf0c296a7ab5f1e20a6f8f998114", "parakeet-v2-84b478d5",
                [
                    new("encoder.int8.onnx", 652_282_309, "92a10bd9c23936ea0566c29f963bc57c2a5df4261b6a8921c39abea66557ca8b"),
                    new("decoder.int8.onnx", 7_257_753, "4d097bae3bac5c96018d5293ba39f1b657296102b142affda40378f38b020f41"),
                    new("joiner.int8.onnx", 1_739_080, "77a29f3e5ac7d37074e0af465391b80f314015d5b97f0f90c6c7767f1d56f428"),
                    new("tokens.txt", 9_384, "ec182b70dd42113aff6c5372c75cac58c952443eb22322f57bbd7f53977d497d"),
                ]),
            [SpeechModelVariant.Multilingual] = new(
                "csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8",
                "ed5d745623472f97cfc2e6c2916ee248798fc5a8", "parakeet-v3-ed5d7456",
                [
                    new("encoder.int8.onnx", 652_282_294, "1afbe69c1ffdabee94755f42e4daf1d93549131a3da11356bda84fcef0f2c479"),
                    new("decoder.int8.onnx", 11_845_274, "0222a86147299d4dee90edf95b6d13827edb42ad8b7c9cf0b2582f030937c0b5"),
                    new("joiner.int8.onnx", 6_355_277, "cbd02f3696e9944970111fb439d9c3fd365a80374565f13f2b9c7007d253fc98"),
                    new("tokens.txt", 93_939, "d58544679ea4bc6ac563d1f545eb7d474bd6cfa467f0a6e2c1dc1c7d37e3c35d"),
                ]),
            [SpeechModelVariant.StreamingEnglish] = new(
                "csukuangfj/sherpa-onnx-streaming-zipformer-en-2023-06-26",
                "672fbf1b30579d6585301139bb363f42a0ad4a24", "zipformer-en-672fbf1b",
                [
                    new(ZipformerTranscriber.Encoder, 71_083_163, "563fde436d16cf7607cf408cd6b30909819d03162652ef389c2450ced3f45ac1"),
                    new(ZipformerTranscriber.Decoder, 1_307_236, "98da299f471e38bb4e1a8df579b8cc9122d6039576a77e357b3c60f17dd83b02"),
                    new(ZipformerTranscriber.Joiner, 259_335, "d944208d660d67c8d72cd2acaeac971fa5ceb8c80e76c1968148846fedd6e297"),
                    new(ZipformerTranscriber.Vocabulary, 5_048, "49e3c2646595fd907228b3c6787069658f67b17377c60aeb8619c4551b2316fb"),
                    new(ZipformerTranscriber.Bpe, 12_590, "f191a4935f668fa8cd8e607bcd378404f948321cd3134a5ea13d324ba921673d",
                        "ChitChat.Speech.Resources.ZipformerEnglish.vocab"),
                    new("LICENSE.txt", 11_689, "5f7d282f87a90c1093a0b89a284d520458d13fb4ac35cf1426907e5d8f55195f",
                        "ChitChat.Speech.Resources.ZipformerEnglish.LICENSE.txt"),
                ]),
        };

    private readonly HttpClient _client;
    private readonly string _library;
    private readonly IReadOnlyDictionary<SpeechModelVariant, DownloadModel> _models;

    /// <summary>Creates an installer for the current user's model library.</summary>
    public SpeechModelInstaller() : this(DownloadClient, ManagedLibrary, Models) { }

    internal SpeechModelInstaller(HttpClient client, string library,
                                  IReadOnlyDictionary<SpeechModelVariant, DownloadModel> models)
    {
        _client = client;
        _library = library;
        _models = models;
    }

    internal sealed record ModelFile(string Name, long Bytes, string Sha256, string? EmbeddedResource = null);

    internal sealed record DownloadModel(string Repository, string Revision, string Folder, IReadOnlyList<ModelFile> Files)
    {
        internal long TotalBytes => Files.Where(file => file.EmbeddedResource is null).Sum(file => file.Bytes);
    }

    private static string ManagedLibrary => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChitChat", "models", "downloads");

    internal static string InstalledModelPath(SpeechModelVariant variant) => Path.Combine(ManagedLibrary, Models[variant].Folder);

    internal static bool IsComplete(string? directory, SpeechModelVariant variant)
    {
        if (string.IsNullOrWhiteSpace(directory)) return false;
        try
        {
            return HasExpectedFiles(directory, Models[variant]);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Returns the number of bytes needed to download the model.</summary>
    /// <param name="variant">The model variant.</param>
    public static long DownloadSize(SpeechModelVariant variant) => Models[variant].TotalBytes;

    /// <summary>Downloads the selected model, verifies its contents, and activates the complete folder.</summary>
    /// <param name="variant">The model variant to install.</param>
    /// <param name="progress">Optional progress observer.</param>
    /// <param name="cancellationToken">Cancels the download before the files are installed.</param>
    /// <returns>The complete model directory.</returns>
    public async Task<string> InstallAsync(SpeechModelVariant variant, IProgress<SpeechModelProgress>? progress,
                                           CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var model = _models[variant];
        var destination = Path.Combine(_library, model.Folder);
        if (HasExpectedFiles(destination, model))
        {
            await VerifyExistingAsync(destination, model, cancellationToken).ConfigureAwait(false);
            return destination;
        }
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException("A folder or file already occupies this download location.");

        Directory.CreateDirectory(_library);
        // Use a staging directory within the same volume so promotion is atomic.
        var work = Path.Combine(_library, $".{model.Folder}-{Guid.NewGuid():N}.partial");
        Directory.CreateDirectory(work);
        try
        {
            var total = model.TotalBytes;
            long received = 0;
            progress?.Report(new(0, total));
            foreach (var file in model.Files)
            {
                if (file.EmbeddedResource is { } resource)
                {
                    await InstallEmbeddedAsync(file, resource, work, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var uri = new Uri($"https://huggingface.co/{model.Repository}/resolve/{model.Revision}/{file.Name}");
                await DownloadAsync(uri, file, work, received, total, progress, cancellationToken).ConfigureAwait(false);
                received += file.Bytes;
            }

            cancellationToken.ThrowIfCancellationRequested();
            try { Directory.Move(work, destination); }
            catch (IOException) when (HasExpectedFiles(destination, model))
            {
                await VerifyExistingAsync(destination, model, cancellationToken).ConfigureAwait(false);
            }
            return destination;
        }
        finally
        {
            try { if (Directory.Exists(work)) Directory.Delete(work, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private static bool HasExpectedFiles(string folder, DownloadModel model)
    {
        return model.Files.All(file => new FileInfo(Path.Combine(folder, file.Name)) is { Exists: true } info &&
                                       info.Length == file.Bytes);
    }

    private static async Task VerifyExistingAsync(string folder, DownloadModel model, CancellationToken cancellationToken)
    {
        foreach (var file in model.Files)
        {
            await using var input = new FileStream(Path.Combine(folder, file.Name), FileMode.Open, FileAccess.Read,
                                                  FileShare.Read, CopyBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false);
            if (!MatchesHash(hash, file))
                throw new InvalidDataException("A model file in the installed folder failed integrity verification.");
        }
    }

    private static async Task InstallEmbeddedAsync(ModelFile file, string resource, string destination,
                                                    CancellationToken cancellationToken)
    {
        await using var input = typeof(SpeechModelInstaller).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidDataException("An included model file could not be read.");
        if (input.Length != file.Bytes ||
            !MatchesHash(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false), file))
            throw new InvalidDataException("An included model file failed integrity verification.");

        input.Position = 0;
        await using var output = new FileStream(Path.Combine(destination, file.Name), FileMode.CreateNew, FileAccess.Write,
                                               FileShare.None, CopyBufferSize, FileOptions.Asynchronous);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }

    private async Task DownloadAsync(Uri uri, ModelFile file, string destination, long previous, long total,
                                     IProgress<SpeechModelProgress>? progress, CancellationToken cancellationToken)
    {
        using var response = await OpenDownloadAsync(uri, cancellationToken).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength is { } length && length != file.Bytes)
            throw new InvalidDataException("The downloaded model file has an unexpected length.");

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(Path.Combine(destination, file.Name), FileMode.CreateNew, FileAccess.Write,
                                               FileShare.None, CopyBufferSize, FileOptions.Asynchronous);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var readBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var elapsed = Stopwatch.StartNew();
        var buffer = new byte[CopyBufferSize];
        long count = 0;
        while (true)
        {
            readBudget.CancelAfter(StallLimit);
            var size = await input.ReadAsync(buffer, readBudget.Token).ConfigureAwait(false);
            readBudget.CancelAfter(Timeout.InfiniteTimeSpan);
            if (size == 0) break;
            count += size;
            if (count > file.Bytes) throw new InvalidDataException("The downloaded model file is larger than expected.");

            hash.AppendData(buffer, 0, size);
            await output.WriteAsync(buffer.AsMemory(0, size), cancellationToken).ConfigureAwait(false);
            if (elapsed.ElapsedMilliseconds >= 100)
            {
                progress?.Report(new(previous + count, total));
                elapsed.Restart();
            }
        }

        if (count != file.Bytes || !MatchesHash(hash.GetHashAndReset(), file))
            throw new InvalidDataException("The downloaded model file failed integrity verification.");
        progress?.Report(new(previous + count, total));
    }

    private static bool MatchesHash(byte[] hash, ModelFile file) =>
        string.Equals(Convert.ToHexString(hash), file.Sha256, StringComparison.OrdinalIgnoreCase);

    private async Task<HttpResponseMessage> OpenDownloadAsync(Uri uri, CancellationToken cancellationToken)
    {
        for (var redirects = 0; redirects < 5; redirects++)
        {
            if (!IsTrustedDownloadUri(uri)) throw new HttpRequestException("An unexpected model download host was rejected.");
            var response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) return response;
            using (response)
            {
                if ((int)response.StatusCode < 300 || (int)response.StatusCode >= 400 || response.Headers.Location is not { } location)
                    response.EnsureSuccessStatusCode();
                else if (Uri.TryCreate(uri, location, out var redirected))
                    uri = redirected;
                else
                    throw new HttpRequestException("The model download received an invalid redirect.");
            }
        }
        throw new HttpRequestException("The model download was redirected too many times.");
    }

    private static bool IsTrustedDownloadUri(Uri uri) =>
        uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
        (uri.IdnHost.Equals("huggingface.co", StringComparison.OrdinalIgnoreCase) ||
         uri.IdnHost.EndsWith(".hf.co", StringComparison.OrdinalIgnoreCase) ||
         uri.IdnHost.EndsWith(".huggingface.co", StringComparison.OrdinalIgnoreCase));
}
