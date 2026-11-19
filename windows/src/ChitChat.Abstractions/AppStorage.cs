namespace ChitChat.Abstractions;

/// <summary>Storage destinations and upgrade compatibility shared by both app layers.</summary>
public static class AppStorage
{
    // Exact previous on-disk name. Only upgrade lookups may use it.
    private const string LegacyDirectory = "Murmur";

    /// <summary>Copies old data on first use; existing current data always wins.</summary>
    /// <param name="name">The data file name.</param>
    /// <param name="root">Optional application-data root for isolated tests.</param>
    /// <returns>The new path, or the original path if migration could not complete.</returns>
    public static string DataFile(string name, string? root = null)
    {
        root ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var destination = Path.Combine(root, "ChitChat", name);
        var previous = Path.Combine(root, LegacyDirectory, name);
        if (File.Exists(destination) || !File.Exists(previous)) return destination;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(previous, destination);
            return destination;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return previous;
        }
    }

    /// <summary>Reuses previously downloaded models without duplicating large weights.</summary>
    public static string LegacyModelDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        LegacyDirectory, "models", "parakeet-v2");
}
