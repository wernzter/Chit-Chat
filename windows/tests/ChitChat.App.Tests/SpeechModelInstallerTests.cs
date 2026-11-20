using System.Net;
using System.Security.Cryptography;
using System.Text;
using ChitChat.Speech;
using Xunit;

namespace ChitChat.App.Tests;

public sealed class SpeechModelInstallerTests : IDisposable
{
    private static readonly byte[] ModelBytes = Encoding.UTF8.GetBytes("sample model");
    private static readonly byte[] DifferentBytes = Encoding.UTF8.GetBytes("tamper model");
    private static readonly string[] ExpectedHosts = ["huggingface.co", "us.aws.cdn.hf.co"];
    private static readonly SpeechModelInstaller.DownloadModel TestModel = new(
        "test/models", "test-revision", "local-model",
        [new("tokens.txt", ModelBytes.Length, Convert.ToHexString(SHA256.HashData(ModelBytes)))]);
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("chitchat-model-test-");

    [Theory]
    [InlineData(SpeechModelVariant.English)]
    [InlineData(SpeechModelVariant.StreamingEnglish)]
    public async Task FollowsTrustedRedirectAndOnlyPublishesAfterVerification(SpeechModelVariant variant)
    {
        var attempts = new List<string>();
        using var client = new HttpClient(new RespondingHandler(uri =>
        {
            attempts.Add(uri.Host);
            Assert.False(Directory.Exists(Destination));
            return uri.Host == "huggingface.co"
                ? RedirectTo("https://us.aws.cdn.hf.co/file")
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(ModelBytes) };
        }));
        var progress = new RecordedProgress();
        var installer = Installer(client, variant);

        var installed = await installer.InstallAsync(variant, progress, CancellationToken.None);
        Assert.Equal(Destination, installed);
        Assert.Equal(ModelBytes, await File.ReadAllBytesAsync(Path.Combine(installed, "tokens.txt")));
        Assert.Equal(ExpectedHosts, attempts);
        Assert.Equal(new SpeechModelProgress(ModelBytes.Length, ModelBytes.Length), progress.Values[^1]);
        Assert.Single(_root.EnumerateDirectories());

        Assert.Equal(installed, await installer.InstallAsync(variant, null, CancellationToken.None));
        Assert.Equal(2, attempts.Count);
    }

    [Fact]
    public async Task RejectsCorruptResponseAndRemovesPartialFiles()
    {
        Assert.Equal(ModelBytes.Length, DifferentBytes.Length);
        using var client = new HttpClient(new RespondingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(DifferentBytes) }));

        await Assert.ThrowsAsync<InvalidDataException>(() => Installer(client)
            .InstallAsync(SpeechModelVariant.English, null, CancellationToken.None));
        Assert.Empty(_root.EnumerateFileSystemInfos());
    }

    [Theory]
    [InlineData("http://huggingface.co/path")]
    [InlineData("https://huggingface.co.example.test/path")]
    public async Task RejectsRedirectToNonHttpsOrUnrelatedHost(string redirect)
    {
        var attempts = 0;
        using var client = new HttpClient(new RespondingHandler(_ =>
        {
            attempts++;
            return RedirectTo(redirect);
        }));

        await Assert.ThrowsAsync<HttpRequestException>(() => Installer(client)
            .InstallAsync(SpeechModelVariant.English, null, CancellationToken.None));
        Assert.Equal(1, attempts);
        Assert.Empty(_root.EnumerateFileSystemInfos());
    }

    [Fact]
    public async Task CancellationDoesNotLeaveAnInstalledOrPartialDirectory()
    {
        using var cancellation = new CancellationTokenSource();
        using var client = new HttpClient(new RespondingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(ModelBytes) }));
        var progress = new RecordedProgress(cancellation.Cancel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Installer(client)
            .InstallAsync(SpeechModelVariant.English, progress, cancellation.Token));
        Assert.Empty(_root.EnumerateFileSystemInfos());
    }

    [Fact]
    public async Task LeavesExistingFolderUntouchedWhenTheFileIsCorrupt()
    {
        Directory.CreateDirectory(Destination);
        var vocabulary = Path.Combine(Destination, "tokens.txt");
        await File.WriteAllBytesAsync(vocabulary, DifferentBytes);
        using var client = new HttpClient(new RespondingHandler(_ => throw new InvalidOperationException("Unexpected download.")));

        await Assert.ThrowsAsync<InvalidDataException>(() => Installer(client)
            .InstallAsync(SpeechModelVariant.English, null, CancellationToken.None));
        Assert.Equal(DifferentBytes, await File.ReadAllBytesAsync(vocabulary));
    }

    [Fact]
    public async Task StagesAndVerifiesTheEmbeddedVocabularyAlongsideDownloads()
    {
        var model = TestModel with
        {
            Files =
            [
                ..TestModel.Files,
                new("bpe.vocab", 12_590, "f191a4935f668fa8cd8e607bcd378404f948321cd3134a5ea13d324ba921673d",
                    "ChitChat.Speech.Resources.ZipformerEnglish.vocab"),
                new("LICENSE.txt", 11_689, "5f7d282f87a90c1093a0b89a284d520458d13fb4ac35cf1426907e5d8f55195f",
                    "ChitChat.Speech.Resources.ZipformerEnglish.LICENSE.txt"),
            ],
        };
        var attempts = 0;
        using var client = new HttpClient(new RespondingHandler(uri =>
        {
            attempts++;
            Assert.EndsWith("/tokens.txt", uri.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(ModelBytes) };
        }));
        var models = new Dictionary<SpeechModelVariant, SpeechModelInstaller.DownloadModel>
        {
            [SpeechModelVariant.StreamingEnglish] = model,
        };
        var installer = new SpeechModelInstaller(client, _root.FullName, models);
        var progress = new RecordedProgress();

        var path = await installer.InstallAsync(SpeechModelVariant.StreamingEnglish, progress, CancellationToken.None);

        Assert.Equal(1, attempts);
        Assert.Equal(Destination, path);
        Assert.Equal(new SpeechModelProgress(ModelBytes.Length, ModelBytes.Length), progress.Values[^1]);
        Assert.Equal(12_590, new FileInfo(Path.Combine(path, "bpe.vocab")).Length);
        Assert.Contains("Apache License", await File.ReadAllTextAsync(Path.Combine(path, "LICENSE.txt")));
    }

    private string Destination => Path.Combine(_root.FullName, TestModel.Folder);

    private SpeechModelInstaller Installer(HttpClient client, SpeechModelVariant variant = SpeechModelVariant.English) =>
        new(client, _root.FullName,
            new Dictionary<SpeechModelVariant, SpeechModelInstaller.DownloadModel> { [variant] = TestModel });

    private static HttpResponseMessage RedirectTo(string location) => new(HttpStatusCode.Redirect)
    {
        Headers = { Location = new Uri(location) },
    };

    public void Dispose() => _root.Delete(recursive: true);

    private sealed class RespondingHandler(Func<Uri, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(response(request.RequestUri!));
        }
    }

    private sealed class RecordedProgress(Action? onReport = null) : IProgress<SpeechModelProgress>
    {
        internal List<SpeechModelProgress> Values { get; } = [];

        public void Report(SpeechModelProgress value)
        {
            Values.Add(value);
            onReport?.Invoke();
        }
    }
}
