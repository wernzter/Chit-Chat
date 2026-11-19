using System.Text.Json;
using ChitChat.Dictionary;
using Shouldly;
using Xunit;

namespace ChitChat.DictionaryTests;

public sealed class VectorTests
{
    private static readonly JsonElement[] Examples = ReadCases();

    public static TheoryData<int> Indexes()
    {
        var rows = new TheoryData<int>();
        for (var i = 0; i < Examples.Length; i++) rows.Add(i);
        return rows;
    }

    [Fact]
    public void FixtureContainsNamedCases()
    {
        Examples.ShouldNotBeEmpty();
        Examples.Select(row => row.GetProperty("name").GetString()).Distinct().Count().ShouldBe(Examples.Length);
    }

    [Theory]
    [MemberData(nameof(Indexes))]
    public void SharedExampleMatchesTextAndCounts(int index)
    {
        var row = Examples[index];
        var dictionary = row.GetProperty("entries").EnumerateArray().Select(ReadEntry);
        var corrector = new DictionaryCorrector(dictionary);
        var (text, corrections) = corrector.Apply(row.GetProperty("input").GetString()!);
        text.ShouldBe(row.GetProperty("expected").GetString(), row.GetProperty("name").GetString());

        var actual = corrections.Select(hit => (hit.To, hit.Count)).ToArray();
        var expected = row.GetProperty("expectedCorrections").EnumerateArray()
            .Select(hit => (hit.GetProperty("to").GetString()!, hit.GetProperty("count").GetInt32()))
            .ToArray();
        actual.ShouldBe(expected, ignoreOrder: true);
    }

    [Fact]
    public void BiasUsesDistinctTrimmedSpellingsInOrder()
    {
        var source = Enumerable.Range(1, 100).Select(i => DictionaryEntry.Term($"Phrase {i}"))
            .Prepend(DictionaryEntry.Term("juniper"))
            .Prepend(DictionaryEntry.Term("  Juniper  "));
        var expected = Enumerable.Range(1, 39).Select(i => $"Phrase {i}").Prepend("Juniper");
        DictionaryCorrector.BiasPhrases(source).ShouldBe(expected);
    }

    [Fact]
    public void BiasSkipsDisabledAndBlankEntries()
    {
        DictionaryEntry[] input =
        [
            DictionaryEntry.Term("Sleeping") with { IsEnabled = false },
            DictionaryEntry.Correction("juniper lab", "Juniper Labs"),
            DictionaryEntry.Term(" "),
        ];
        DictionaryCorrector.BiasPhrases(input).ShouldBe(["Juniper Labs"]);
    }

    [Theory]
    [InlineData("about", true)]
    [InlineData("kestrel shuttle", false)]
    public void WarningDistinguishesOrdinaryTriggersFromSpecificPhrases(string trigger, bool warned)
    {
        var warnings = DictionaryWarning.Check(DictionaryEntry.Correction(trigger, "Kestrel"));
        (warnings.Count > 0).ShouldBe(warned);
    }

    private static DictionaryEntry ReadEntry(JsonElement data)
    {
        var spelling = data.GetProperty("write").GetString()!;
        var entry = data.GetProperty("kind").GetString() == "correction"
            ? DictionaryEntry.Correction(data.GetProperty("hear").GetString()!, spelling)
            : DictionaryEntry.Term(spelling);
        return entry with { IsEnabled = !data.TryGetProperty("isEnabled", out var value) || value.GetBoolean() };
    }

    private static JsonElement[] ReadCases()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "dictionary-test-vectors.json");
        using var json = JsonDocument.Parse(File.ReadAllBytes(file));
        json.RootElement.GetProperty("version").GetInt32().ShouldBe(1);
        return json.RootElement.GetProperty("cases").EnumerateArray().Select(element => element.Clone()).ToArray();
    }
}
