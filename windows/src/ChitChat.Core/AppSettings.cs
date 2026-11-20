using System.Text.Json;
using System.Text.Json.Serialization;
using ChitChat.Abstractions;

namespace ChitChat.Core;

/// <summary>Placement for the compact indicator shown while dictating.</summary>
public enum RecordingIndicatorPosition
{
    /// <summary>At the top of the active screen.</summary>
    Top,
    /// <summary>At the bottom of the active screen.</summary>
    Bottom,
    /// <summary>Keep the indicator inside the main window.</summary>
    Hidden,
}

/// <summary>Preferences persisted between launches.</summary>
public sealed record SettingsData
{
    private OpenAiSettings _openAi = new();
    private LocalAiSettings _localAi = new();

    /// <summary>Virtual-key code to begin and end a recording. Defaults to right Control.</summary>
    public int PushToTalkKey { get; init; } = 0xA3;
    /// <summary>Additional keys required by the recording shortcut.</summary>
    public HotkeyModifiers ShortcutModifiers { get; init; }
    /// <summary>Whether a quick shortcut tap latches recording until the next tap.</summary>
    public bool TapToToggle { get; init; } = true;
    /// <summary>Whether capture and delivery play recording cues.</summary>
    public bool PlayRecordingSounds { get; init; } = true;
    /// <summary>Whether to temporarily mute audio outputs during capture.</summary>
    public bool MuteAudioWhileRecording { get; init; }
    /// <summary>Where the separate recording indicator appears.</summary>
    public RecordingIndicatorPosition IndicatorPosition { get; init; } = RecordingIndicatorPosition.Bottom;
    /// <summary>Where the independent correction popup appears.</summary>
    public RecordingIndicatorPosition CorrectionPopupPosition { get; init; } = RecordingIndicatorPosition.Bottom;
    /// <summary>How long an idle editor remains eligible for learning from a dictation.</summary>
    public int EditWatchSeconds { get; init; } = 120;
    /// <summary>Whether to apply deterministic cleanup before enhancement and dictionary rules.</summary>
    public bool CleanupEnabled { get; init; } = true;
    /// <summary>Whether edits to verified dictation text can add a speech correction.</summary>
    public bool LearnFromEdits { get; init; } = true;
    /// <summary>Optional on-device language model for smart cleanup.</summary>
    public LocalAiSettings LocalAi { get => _localAi; init => _localAi = value ?? new LocalAiSettings(); }
    /// <summary>Optional cloud enhancement preferences. The API key lives in Windows Credential Manager.</summary>
    public OpenAiSettings OpenAi { get => _openAi; init => _openAi = value ?? new OpenAiSettings(); }
    /// <summary>A custom speech model location, if selected.</summary>
    public string? ModelDirectory { get; init; }
    /// <summary>Whether to use the on-device streaming speech engine.</summary>
    public bool UseStreamingSpeech { get; init; }
    /// <summary>Whether to deliver dictation to the active application.</summary>
    public bool InjectText { get; init; } = true;
    /// <summary>Whether to keep completed recordings in local history.</summary>
    public bool KeepHistory { get; init; } = true;
    /// <summary>The release whose update notice the user dismissed.</summary>
    public string? DismissedUpdateVersion { get; init; }
}

/// <summary>Loads and updates local application preferences.</summary>
public sealed class AppSettings
{
    private readonly string _file;
    private volatile SettingsData _data;

    /// <summary>Loads preferences from a particular file.</summary>
    /// <param name="path">The JSON file to use.</param>
    public AppSettings(string path)
    {
        _file = path;
        try
        {
            _data = JsonSerializer.Deserialize(File.ReadAllText(_file), SettingsJsonContext.Default.SettingsData)
                   ?? new SettingsData();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _data = new SettingsData();
        }
    }

    /// <summary>The per-user preferences location.</summary>
    public static string DefaultPath => AppStorage.DataFile("settings.json");
    /// <summary>The last successfully loaded or saved preferences.</summary>
    public SettingsData Data => _data;
    /// <summary>Notification that preferences were saved.</summary>
    public event EventHandler? Changed;

    /// <summary>Replaces the saved preferences.</summary>
    /// <param name="data">The new preferences.</param>
    public void Update(SettingsData data)
    {
        LocalFile.Write(_file, JsonSerializer.Serialize(data, SettingsJsonContext.Default.SettingsData));
        _data = data;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

[JsonSerializable(typeof(SettingsData))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
