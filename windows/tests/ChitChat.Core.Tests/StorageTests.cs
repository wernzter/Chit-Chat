using ChitChat.Core;
using ChitChat.Dictionary;
using Shouldly;
using Xunit;

namespace ChitChat.CoreTests;

public sealed class StorageTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("chitchat-tests-");

    public void Dispose() => _directory.Delete(recursive: true);

    [Fact]
    public void DictionaryReaderRecoversTermsAndRulesFromHandEditedFiles()
    {
        const string document = """
            # Notes
            Juniper
            harbor view -> Harbourview
            # off: mesa ridge -> Mesa Ridge
            -> incomplete
            unfinished ->
            Riverbend
            """;
        var entries = DictionaryFile.Parse(document);

        entries.Select(entry => entry.Write).ShouldBe(["Juniper", "Harbourview", "Mesa Ridge", "Riverbend"]);
        entries[0].Kind.ShouldBe(EntryKind.Term);
        entries[1].Kind.ShouldBe(EntryKind.Correction);
        entries[1].Hear.ShouldBe("harbor view");
        entries[2].IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public void DictionaryUpdatesAndDeletionsPersist()
    {
        var path = Item("dictionary.txt");
        var dictionary = new DictionaryFile(path);
        var term = DictionaryEntry.Term("Junper");
        dictionary.Add(term);
        dictionary.Add(DictionaryEntry.Correction("river bend", "Riverbend") with { IsEnabled = false });
        dictionary.Update(term with { Write = "Juniper" });

        var saved = new DictionaryFile(path);
        saved.Entries[0].Write.ShouldBe("Juniper");
        saved.Entries[1].IsEnabled.ShouldBeFalse();

        saved.Remove(saved.Entries[0].Id);
        var remaining = new DictionaryFile(path).Entries.ShouldHaveSingleItem();
        remaining.Hear.ShouldBe("river bend");
    }

    [Theory]
    [InlineData("HAWBOR", "Harbourview")]
    [InlineData("HARBOUR", "Harbourview")]
    [InlineData("juNIper", "Juniper")]
    public void DictionaryQueriesMatchEitherSideWithoutCase(string query, string match)
    {
        var dictionary = new DictionaryFile(Item("rules.txt"));
        dictionary.Add(DictionaryEntry.Correction("hawbor view", "Harbourview"));
        dictionary.Add(DictionaryEntry.Term("Juniper"));
        dictionary.Search(query).ShouldHaveSingleItem().Write.ShouldBe(match);
        dictionary.Search("").Count.ShouldBe(2);
    }

    [Fact]
    public void TranscriptHistoryKeepsOrderAndCorrectionDetailsAcrossReopen()
    {
        var path = Item("history.jsonl");
        var history = new TranscriptStore(path);
        history.Add(Entry("First"));
        var latest = Entry("Meet at Riverbend") with { Corrections = [new AppliedCorrection("river bend", "Riverbend", 2)] };
        history.Add(latest);

        var rows = new TranscriptStore(path).Records;
        rows.Select(row => row.Text).ShouldBe([latest.Text, "First"]);
        rows[0].Corrections!.ShouldHaveSingleItem().ShouldBe(latest.Corrections[0]);
    }

    [Fact]
    public void TranscriptReaderSkipsAMalformedLineAndKeepsOtherRecords()
    {
        var path = Item("corrupt.jsonl");
        var history = new TranscriptStore(path);
        history.Add(Entry("Before"));
        File.AppendAllLines(path, ["{unfinished"]);
        history.Add(Entry("After"));

        new TranscriptStore(path).Records.Select(row => row.Text).ShouldBe(["After", "Before"]);
    }

    [Fact]
    public void TranscriptDeleteAndClearSurviveLaterAppends()
    {
        var path = Item("history.jsonl");
        var history = new TranscriptStore(path);
        var deleted = Entry("Temporary");
        history.Add(deleted);
        history.Add(Entry("Keep"));
        history.Remove(deleted.Id);
        history.Add(Entry("New"));
        new TranscriptStore(path).Records.Select(row => row.Text).ShouldBe(["New", "Keep"]);

        history.Clear();
        new TranscriptStore(path).Records.ShouldBeEmpty();
    }

    [Fact]
    public void SavedSettingsOverrideRightControlAndDefaults()
    {
        var path = Item("prefs.json");
        var settings = new AppSettings(path);
        settings.Data.PushToTalkKey.ShouldBe(0xA3);

        var replacement = settings.Data with { PushToTalkKey = 0x7C, InjectText = false, KeepHistory = false };
        settings.Update(replacement);
        new AppSettings(path).Data.ShouldBe(replacement);
    }

    [Fact]
    public void BrokenSettingsFileDoesNotPreventStartup()
    {
        var path = Item("broken.json");
        File.WriteAllText(path, "broken");
        new AppSettings(path).Data.ShouldBe(new SettingsData());
    }

    private string Item(string name) => Path.Combine(_directory.FullName, name);

    private static TranscriptRecord Entry(string text) => new()
    {
        At = new DateTimeOffset(2026, 6, 10, 9, 30, 0, TimeSpan.Zero),
        Text = text,
        AudioSeconds = 1.6,
        ProcessingSeconds = 0.3,
    };
}
