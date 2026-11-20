using System.Text.Json;
using Avalonia.Threading;
using ChitChat.Core;

namespace ChitChat.App;

internal sealed class ReleaseUpdates(AppSettings settings) : IDisposable
{
    private readonly ReleaseUpdateClient _client = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _daily = new() { Interval = TimeSpan.FromDays(1) };
    private bool _disposed;
    private Version? _dismissedInSession;

    internal static Version Installed { get; } = InstalledVersion();
    internal Version? Available { get; private set; }
    internal string? Status { get; private set; }
    internal bool IsChecking { get; private set; }
    internal Version? Notice => Available is { } version && version != _dismissedInSession &&
        !string.Equals(settings.Data.DismissedUpdateVersion, version.ToString(3), StringComparison.Ordinal) ? version : null;
    internal event EventHandler? Changed;

    internal void Start()
    {
        _daily.Tick += (_, _) => _ = CheckAsync();
        _daily.Start();
        _ = CheckAsync();
    }

    internal async Task CheckAsync()
    {
        if (IsChecking || _disposed) return;
        var token = _lifetime.Token;
        IsChecking = true;
        Status = "Checking GitHub…";
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            var latest = await _client.LatestVersionAsync(token);
            if (token.IsCancellationRequested) return;
            Available = latest > Installed ? latest : null;
            Status = Available is { } version ? $"Chit-Chat {version.ToString(3)} is available." : "You’re up to date.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or
                                      JsonException or OperationCanceledException)
        {
            Status = "Couldn’t check for updates. Try again.";
        }
        finally
        {
            IsChecking = false;
            if (!_disposed) Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    internal void Dismiss(Version version)
    {
        _dismissedInSession = version;
        try { settings.Update(settings.Data with { DismissedUpdateVersion = version.ToString(3) }); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The notice is still dismissed for this session.
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _daily.Stop();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private static Version InstalledVersion()
    {
        var version = typeof(ReleaseUpdates).Assembly.GetName().Version!;
        return new Version(version.Major, version.Minor, version.Build);
    }
}
