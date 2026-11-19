namespace ChitChat.Dictionary;

/// <summary>Whether an entry teaches recognition spelling or substitutes a phrase.</summary>
public enum EntryKind
{
    /// <summary>A preferred term.</summary>
    Term,
    /// <summary>A rule with a trigger and a replacement.</summary>
    Correction,
}

/// <summary>A single saved dictionary entry.</summary>
public sealed record DictionaryEntry
{
    /// <summary>Identifier used when editing a loaded entry.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The entry's purpose.</summary>
    public EntryKind Kind { get; init; }

    /// <summary>The preferred spelling.</summary>
    public string Write { get; init; } = string.Empty;

    /// <summary>The recognized phrase to replace.</summary>
    public string Hear { get; init; } = string.Empty;

    /// <summary>Whether the entry participates in recognition and cleanup.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>Creates a preferred term.</summary>
    /// <param name="word">Its spelling.</param>
    public static DictionaryEntry Term(string word) => new() { Write = word.Trim() };

    /// <summary>Creates a replacement rule.</summary>
    /// <param name="hear">The trigger.</param>
    /// <param name="write">The replacement.</param>
    public static DictionaryEntry Correction(string hear, string write) =>
        new() { Kind = EntryKind.Correction, Hear = hear.Trim(), Write = write.Trim() };

    /// <summary>Encodes this entry for the editable text file.</summary>
    public string ToFileLine()
    {
        var value = Kind == EntryKind.Term ? Write : $"{Hear} -> {Write}";
        return IsEnabled ? value : "# off: " + value;
    }
}

/// <summary>Advises the user about how a rule might behave.</summary>
/// <param name="Message">Text to show alongside the editor.</param>
public sealed record DictionaryWarning(string Message)
{
    private static readonly HashSet<string> CommonWords = new(
        """
        about after against again also bank before both call case change check cloud code come copy
        data date day down edit end file find first folder form from group home into item last
        left level line link list mail make move name near next note open over page part place
        point read report right save send some start stop team test text that them then this time
        today turn under want when where which while with work write year
        """.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);

    private static readonly char[] WordSeparators = [' ', '\t', '-'];

    /// <summary>Finds short, common, or redundant correction triggers.</summary>
    /// <param name="entry">The proposed dictionary entry.</param>
    public static IReadOnlyList<DictionaryWarning> Check(DictionaryEntry entry)
    {
        var phrase = entry.Hear.Trim();
        if (entry.Kind != EntryKind.Correction || phrase.Length == 0) return [];

        var notices = new List<DictionaryWarning>();
        var words = phrase.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 1 && (words[0].Length <= 3 || CommonWords.Contains(words[0])))
        {
            notices.Add(new($"“{phrase}” can match often. Add neighboring words to narrow this rule."));
        }

        if (phrase.Equals(entry.Write.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            notices.Add(new("The trigger and preferred spelling contain the same words."));
        }
        return notices;
    }
}
