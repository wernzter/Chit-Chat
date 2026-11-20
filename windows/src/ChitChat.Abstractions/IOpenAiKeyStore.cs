namespace ChitChat.Abstractions;

/// <summary>Access to the OpenAI key for the current desktop account.</summary>
public interface IOpenAiKeyStore
{
    /// <summary>Reads the saved key, or returns null if none exists.</summary>
    string? Read();

    /// <summary>Saves or replaces the key.</summary>
    /// <param name="key">The API credential.</param>
    void Save(string key);

    /// <summary>Removes the saved key if present.</summary>
    void Remove();
}
