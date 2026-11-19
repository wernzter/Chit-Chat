using System.Text.Json;
using System.Text.Json.Serialization;
using ChitChat.Abstractions;

namespace ChitChat.Core;

/// <summary>Preferences persisted between launches.</summary>
public sealed record SettingsData
{
    /// <summary>Virtual-key code to begin and end a recording. Defaults to right Control.</summary>
    public int PushToTalkKey { get; init; } = 0xA3;
    /// <summary>A custom speech model location, if selected.</summary>
    public string? ModelDirectory { get; init; }
    /// <summary>Whether to deliver dictation to the active application.</summary>
    public bool InjectText { get; init; } = true;
    /// <summary>Whether to keep completed recordings in local history.</summary>
    public bool KeepHistory { get; init; } = true;
}

/// <summary>Loads and updates local application preferences.</summary>
public sealed class AppSettings
{
    private readonly string _file;

    /// <summary>Loads preferences from a particular file.</summary>
    /// <param name="path">The JSON file to use.</param>
    public AppSettings(string path)
    {
        _file = path;
        try
        {
            Data = JsonSerializer.Deserialize(File.ReadAllText(_file), SettingsJsonContext.Default.SettingsData)
                   ?? new SettingsData();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Data = new SettingsData();
        }
    }

    /// <summary>The per-user preferences location.</summary>
    public static string DefaultPath => AppStorage.DataFile("settings.json");
    /// <summary>The last successfully loaded or saved preferences.</summary>
    public SettingsData Data { get; private set; }
    /// <summary>Notification that preferences were saved.</summary>
    public event EventHandler? Changed;

    /// <summary>Replaces the saved preferences.</summary>
    /// <param name="data">The new preferences.</param>
    public void Update(SettingsData data)
    {
        LocalFile.Write(_file, JsonSerializer.Serialize(data, SettingsJsonContext.Default.SettingsData));
        Data = data;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

[JsonSerializable(typeof(SettingsData))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
