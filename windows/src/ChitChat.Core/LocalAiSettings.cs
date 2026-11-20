namespace ChitChat.Core;

/// <summary>Optional on-device text enhancement preferences.</summary>
public sealed record LocalAiSettings
{
    /// <summary>Whether to use an installed Ollama model for dictation.</summary>
    public bool Enabled { get; init; }

    /// <summary>The name of an installed local text model.</summary>
    public string Model { get; init; } = string.Empty;
}
