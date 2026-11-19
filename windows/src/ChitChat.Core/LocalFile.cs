namespace ChitChat.Core;

internal static class LocalFile
{
    internal static void Write(string path, string text)
    {
        var destination = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".chitchat-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, text);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
