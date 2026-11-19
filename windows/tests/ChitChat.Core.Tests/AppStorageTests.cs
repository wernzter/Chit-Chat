using ChitChat.Abstractions;
using Shouldly;
using Xunit;

namespace ChitChat.CoreTests;

/// <summary>Upgrade storage keeps existing data and survives failed copies.</summary>
public sealed class AppStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"chitchat-upgrade-{Guid.NewGuid():N}");

    /// <summary>Remove only the isolated test directory.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Fresh_install_uses_current_directory()
    {
        AppStorage.DataFile("settings.json", _root).ShouldBe(Path.Combine(_root, "ChitChat", "settings.json"));
        Directory.Exists(_root).ShouldBeFalse();
    }

    [Theory]
    [InlineData("dictionary.txt")]
    [InlineData("settings.json")]
    [InlineData("transcripts.jsonl")]
    public void Migration_copies_files_without_overwriting_current_data(string name)
    {
        // Resolve the legacy name through the compatibility helper to keep it centralized.
        var legacyName = new DirectoryInfo(AppStorage.LegacyModelDirectory).Parent!.Parent!.Name;
        var previous = Path.Combine(_root, legacyName, name);
        Directory.CreateDirectory(Path.GetDirectoryName(previous)!);
        File.WriteAllText(previous, "original");
        var destination = AppStorage.DataFile(name, _root);
        destination.ShouldBe(Path.Combine(_root, "ChitChat", name));
        File.ReadAllText(destination).ShouldBe("original");
        File.ReadAllText(previous).ShouldBe("original");
        File.WriteAllText(destination, "current");
        File.ReadAllText(AppStorage.DataFile(name, _root)).ShouldBe("current");
    }

    [Fact]
    public void Failed_copy_keeps_previous_file_accessible()
    {
        var legacyName = new DirectoryInfo(AppStorage.LegacyModelDirectory).Parent!.Parent!.Name;
        var previous = Path.Combine(_root, legacyName, "dictionary.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(previous)!);
        File.WriteAllText(previous, "original");
        File.WriteAllText(Path.Combine(_root, "ChitChat"), "blocks the new directory");
        AppStorage.DataFile("dictionary.txt", _root).ShouldBe(previous);
        File.ReadAllText(previous).ShouldBe("original");
    }
}
