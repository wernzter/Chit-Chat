using System.Net;
using ChitChat.Core;
using Xunit;

namespace ChitChat.Core.Tests;

public sealed class ReleaseUpdateTests
{
    [Fact]
    public async Task ReadsThePublicReleaseAndBuildsItsOfficialPage()
    {
        using var http = new HttpClient(new GitHubFixture("""{"tag_name":"v0.12.3","html_url":"https://example.com"}"""));
        var version = await new ReleaseUpdateClient(http).LatestVersionAsync();

        Assert.True(version > new Version(0, 2, 0));
        Assert.Equal("https://github.com/wernzter/Chit-Chat/releases/tag/v0.12.3",
            ReleaseUpdateClient.ReleasePage(version).AbsoluteUri);
    }

    [Theory]
    [InlineData("""{"tag_name":"v0.2.0-rc.1"}""")]
    [InlineData("""{"tag_name":"v0.2"}""")]
    [InlineData("""{"tag_name":3}""")]
    [InlineData("""{"html_url":"https://example.com"}""")]
    public async Task RejectsUnsupportedReleaseTags(string payload)
    {
        using var http = new HttpClient(new GitHubFixture(payload));
        var service = new ReleaseUpdateClient(http);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.LatestVersionAsync());
    }

    private sealed class GitHubFixture(string payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://api.github.com/repos/wernzter/Chit-Chat/releases/latest", request.RequestUri?.AbsoluteUri);
            Assert.Equal("Chit-Chat", request.Headers.UserAgent.ToString());
            Assert.Null(request.Headers.Authorization);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload) });
        }
    }
}
