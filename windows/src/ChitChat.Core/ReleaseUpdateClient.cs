using System.Text.Json;

namespace ChitChat.Core;

/// <summary>Reads the latest public Chit-Chat release from GitHub.</summary>
/// <param name="client">An optional HTTP client for testing the release response.</param>
public sealed class ReleaseUpdateClient(HttpClient? client = null)
{
    private const string Releases = "https://github.com/wernzter/Chit-Chat/releases";
    private static readonly Uri Latest = new("https://api.github.com/repos/wernzter/Chit-Chat/releases/latest");
    private static readonly HttpClient Shared = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        Timeout = TimeSpan.FromSeconds(10),
        MaxResponseContentBufferSize = 1_048_576,
    };

    /// <summary>Gets the latest stable release version, or throws if it cannot be verified.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    public async Task<Version> LatestVersionAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Latest);
        request.Headers.UserAgent.ParseAdd("Chit-Chat");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await (client ?? Shared).SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var data = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(data, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (json.RootElement.ValueKind == JsonValueKind.Object &&
            json.RootElement.TryGetProperty("tag_name", out var tag) && tag.ValueKind == JsonValueKind.String &&
            tag.GetString() is { Length: > 1 } name && name[0] == 'v' &&
            Version.TryParse(name[1..], out var version) && version.Build >= 0 && version.Revision < 0 &&
            string.Equals(name[1..], version.ToString(3), StringComparison.Ordinal))
            return version;
        throw new InvalidDataException("The release has no stable version.");
    }

    /// <summary>The official browser page for a verified release version.</summary>
    /// <param name="version">The version to open.</param>
    public static Uri ReleasePage(Version version) => new($"{Releases}/tag/v{version.ToString(3)}");
}
