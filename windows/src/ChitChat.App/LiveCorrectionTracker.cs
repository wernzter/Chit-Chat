using System.Diagnostics;
using Avalonia.Threading;
using ChitChat.Abstractions;
using ChitChat.Core;
using ChitChat.Dictionary;

namespace ChitChat.App;

/// <summary>A proposed dictionary entry the user can dismiss before it is saved.</summary>
/// <param name="Heard">The originally transcribed word.</param>
/// <param name="Written">The spelling the user substituted.</param>
/// <param name="SecondsRemaining">Time left to dismiss the suggestion.</param>
public sealed record LiveCorrectionSuggestion(string Heard, string Written, int SecondsRemaining);

internal sealed class LiveCorrectionTracker(AppSettings settings, DictionaryFile dictionary) : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan SettleInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DismissInterval = TimeSpan.FromSeconds(10);
    private readonly HashSet<(string Heard, string Written)> _handled = [];
    private CancellationTokenSource? _observation;
    private bool _disposed;

    internal LiveCorrectionSuggestion? Suggestion { get; private set; }

    internal void Watch(string original, ITextEditObserver observer)
    {
        Stop();
        if (_disposed || !settings.Data.LearnFromEdits ||
            settings.Data.CorrectionPopupPosition == RecordingIndicatorPosition.Hidden) return;
        _observation = new CancellationTokenSource();
        _ = WatchAsync(original, observer, _observation);
    }

    internal void Dismiss()
    {
        if (Suggestion is not { } suggestion) return;
        _handled.Add((suggestion.Heard, suggestion.Written));
        Suggestion = null;
    }

    internal void Stop()
    {
        _observation?.Cancel();
        _observation = null;
        _handled.Clear();
        Suggestion = null;
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
    }

    internal bool CanLearn(DictionaryEntry entry) =>
        !dictionary.Entries.Any(existing => existing.Hear.Equals(entry.Hear, StringComparison.OrdinalIgnoreCase));

    internal void Save(DictionaryEntry entry)
    {
        if (!CanLearn(entry)) return;
        try { dictionary.Add(entry); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Dictation and history editing remain usable when the dictionary cannot be written.
        }
    }

    private async Task WatchAsync(string original, ITextEditObserver observer, CancellationTokenSource observation)
    {
        var token = observation.Token;
        var idleWindow = TimeSpan.FromSeconds(Math.Clamp(settings.Data.EditWatchSeconds, 15, 600));
        var clock = Stopwatch.StartNew();
        var lastEdit = TimeSpan.Zero;
        var deadline = TimeSpan.FromSeconds(3);
        var countdown = TimeSpan.Zero;
        string? previous = null;
        IReadOnlyList<DictionaryEntry> candidates = [];
        try
        {
            while (!token.IsCancellationRequested && settings.Data.LearnFromEdits &&
                   settings.Data.CorrectionPopupPosition != RecordingIndicatorPosition.Hidden && clock.Elapsed < deadline)
            {
                var (focused, edited) = await Task.Run(
                    () => (observer.TryReadEditedText(original, out var span), span), token);
                token.ThrowIfCancellationRequested();
                if (!focused) return;
                var now = clock.Elapsed;
                if (edited is not null && !string.Equals(edited, previous, StringComparison.Ordinal))
                {
                    var edit = new TranscriptEdit(original, edited);
                    candidates = await Task.Run(() => TranscriptCleanup.LearnFrom(edit), token);
                    token.ThrowIfCancellationRequested();
                    previous = edited;
                    lastEdit = now;
                    deadline = now + idleWindow;
                }
                else if (edited is null && previous is null)
                {
                    // The editor still needs to confirm the insertion. Native observation bounds this wait.
                    await Task.Delay(PollInterval, token);
                    continue;
                }

                if (Suggestion is { } active && !candidates.Any(entry => Matches(active, entry) && CanLearn(entry)))
                    Suggestion = null;
                if (now - lastEdit >= SettleInterval)
                {
                    if (Suggestion is { } pending)
                    {
                        if (now >= countdown)
                        {
                            Save(candidates.First(entry => Matches(pending, entry)));
                            Dismiss();
                        }
                        else
                        {
                            Suggestion = pending with { SecondsRemaining = (int)Math.Ceiling((countdown - now).TotalSeconds) };
                        }
                    }
                    else if (candidates.FirstOrDefault(entry => CanLearn(entry) &&
                             !_handled.Contains((entry.Hear, entry.Write))) is { } next)
                    {
                        countdown = now + DismissInterval;
                        deadline = deadline > countdown + SettleInterval ? deadline : countdown + SettleInterval;
                        Suggestion = new LiveCorrectionSuggestion(next.Hear, next.Write, (int)DismissInterval.TotalSeconds);
                    }
                }
                await Task.Delay(PollInterval, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_observation, observation)) Stop();
            observation.Dispose();
        }
    }

    private static bool Matches(LiveCorrectionSuggestion suggestion, DictionaryEntry entry) =>
        suggestion.Heard == entry.Hear && suggestion.Written == entry.Write;
}
