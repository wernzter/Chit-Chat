using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using ChitChat.Abstractions;

namespace ChitChat.Core;

/// <summary>A safe OpenAI preview and its measured network and model latency.</summary>
/// <param name="Text">Edited sample text.</param>
/// <param name="Model">The model that served the request.</param>
/// <param name="Effort">The selected reasoning setting.</param>
/// <param name="Elapsed">Wall-clock time spent on the request.</param>
public sealed record OpenAiSampleResult(string Text, string Model, OpenAiReasoningEffort Effort, TimeSpan Elapsed);

/// <summary>Handles credentials and optional enhancement while keeping local dictation usable.</summary>
/// <param name="keyStore">Windows credential store, or null on a headless/non-Windows host.</param>
/// <param name="client">Optional OpenAI client for tests.</param>
public sealed class OpenAiEnhancement(IOpenAiKeyStore? keyStore, OpenAiClient? client = null)
{
    /// <summary>Public sample used by connection checks, prompt previews, and benchmarks.</summary>
    public const string Sample = "um please send the updated agenda to sam before friday and include the budget notes thanks";

    private readonly OpenAiClient _client = client ?? new();
    private string? _lastDictationStatus;

    /// <summary>Whether this host provides a protected credential store.</summary>
    public bool HasCredentialStore => keyStore is not null;

    /// <summary>Summary of the last OpenAI enhancement attempt or skip.</summary>
    public string? LastDictationStatus => Volatile.Read(ref _lastDictationStatus);

    /// <summary>Emitted from the dictation thread after an enhancement attempt or threshold skip.</summary>
    public event EventHandler? DictationStatusChanged;


    /// <summary>Saves a new key without checking it over the network.</summary>
    /// <param name="value">Text entered into the API key field.</param>
    /// <param name="message">Feedback suitable for display.</param>
    public bool SaveKey(string value, out string message)
    {
        var key = value.Trim();
        if (!OpenAiClient.IsValidKey(key))
        {
            message = "Enter a valid API key without spaces or line breaks.";
            return false;
        }

        if (keyStore is null)
        {
            message = "Windows Credential Manager is unavailable.";
            return false;
        }

        try
        {
            keyStore.Save(key);
            message = "API key saved in Windows Credential Manager.";
            return true;
        }
        catch (Exception error) when (IsStorageError(error))
        {
            message = "Windows Credential Manager could not save the API key.";
            return false;
        }
    }

    /// <summary>Removes any saved key from Windows Credential Manager.</summary>
    /// <param name="message">Feedback suitable for display.</param>
    public bool RemoveKey(out string message)
    {
        if (keyStore is null)
        {
            message = "Windows Credential Manager is unavailable.";
            return false;
        }

        try
        {
            keyStore.Remove();
            message = "The saved API key was removed.";
            return true;
        }
        catch (Exception error) when (IsStorageError(error))
        {
            message = "Windows Credential Manager could not remove the API key.";
            return false;
        }
    }

    /// <summary>Fetches the saved account's text model candidates.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    public Task<IReadOnlyList<string>> RefreshModelsAsync(CancellationToken cancellationToken = default) =>
        _client.ModelsAsync(RequireKey(), cancellationToken);

    /// <summary>Verifies credentials and model access using the public sample and standard prompt.</summary>
    /// <param name="settings">Selected model and effort.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public async Task<string> TestConnectionAsync(OpenAiSettings settings, CancellationToken cancellationToken = default)
    {
        var result = await TestModelAsync(settings, speedOnly: true, cancellationToken).ConfigureAwait(false);
        return $"Connection verified for {result.Model} ({OpenAiReasoning.Label(result.Effort)}).";
    }

    /// <summary>Runs a preview or timed measurement against the public sample.</summary>
    /// <param name="settings">Selected model, effort, and custom prompt.</param>
    /// <param name="speedOnly">Use the standard prompt for comparable speed measurements.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public async Task<OpenAiSampleResult> TestModelAsync(OpenAiSettings settings, bool speedOnly,
                                                        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = RequireKey();
        var effort = settings.EffortFor(settings.Model);
        var start = Stopwatch.GetTimestamp();
        var result = await _client.EnhanceAsync(Sample, key, settings.Model,
                speedOnly ? OpenAiSettings.DefaultPrompt : settings.Prompt, effort, cancellationToken)
            .ConfigureAwait(false);
        return new(result, settings.Model, effort, Stopwatch.GetElapsedTime(start));
    }

    /// <summary>Uses the locally processed text on API failure or if enhancement is ineligible.</summary>
    /// <param name="localText">Text after local cleanup and before dictionary replacements.</param>
    /// <param name="originalText">Text from the recognizer, used for the threshold.</param>
    /// <param name="settings">Snapshot of the current enhancement preferences.</param>
    /// <param name="cancellationToken">Cancels the dictation, including any network request.</param>
    public async Task<string> EnhanceAsync(string localText, string originalText, OpenAiSettings settings,
                                           CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!settings.Enabled) return localText;

        var minimum = Math.Max(0, settings.MinimumWords);
        var words = CountWords(originalText);
        if (words < minimum)
        {
            SetDictationStatus($"Used local text. {words} words were below the {minimum}-word AI minimum.");
            return localText;
        }

        var effort = settings.EffortFor(settings.Model);
        try
        {
            var result = await _client.EnhanceAsync(localText, RequireKey(), settings.Model, settings.Prompt,
                                                    effort, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            SetDictationStatus($"Last dictation enhanced with {settings.Model} ({OpenAiReasoning.Label(effort)}).");
            return result;
        }
        catch (OpenAiRequestException error)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetDictationStatus($"Used local text. {error.Message}");
            return localText;
        }
    }

    private void SetDictationStatus(string status)
    {
        Volatile.Write(ref _lastDictationStatus, status);
        DictationStatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private string RequireKey()
    {
        try
        {
            var key = keyStore?.Read();
            if (!string.IsNullOrWhiteSpace(key)) return key;
        }
        catch (Exception error) when (IsStorageError(error))
        {
            throw new OpenAiRequestException("Windows Credential Manager could not read the saved API key.");
        }

        throw new OpenAiRequestException("Save an API key in Settings → OpenAI.");
    }

    private static bool IsStorageError(Exception error) =>
        error is Win32Exception or UnauthorizedAccessException or InvalidOperationException or IOException or PlatformNotSupportedException;

    private static int CountWords(string text)
    {
        var words = 0;
        var meaningful = false;
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                if (meaningful) words++;
                meaningful = false;
            }
            else if (Rune.IsLetter(rune) || Rune.IsNumber(rune))
            {
                meaningful = true;
            }
        }
        return meaningful ? words + 1 : words;
    }
}
