using ChitChat.Abstractions;
using ChitChat.Dictionary;

namespace ChitChat.Core;

/// <summary>Stores dictionary entries in the shared editable text format.</summary>
public sealed class DictionaryFile
{
    private const string Disabled = "off:";
    private DictionaryEntry[] _entries = [];

    /// <summary>Opens the dictionary at a specific location.</summary>
    /// <param name="path">The text file to manage.</param>
    public DictionaryFile(string path)
    {
        FilePath = path;
        Reload();
    }

    /// <summary>The usual per-user dictionary location.</summary>
    public static string DefaultPath => AppStorage.DataFile("dictionary.txt");
    /// <summary>The file backing this instance.</summary>
    public string FilePath { get; }
    /// <summary>Entries in their original file order.</summary>
    public IReadOnlyList<DictionaryEntry> Entries => _entries;
    /// <summary>Indicates that the loaded dictionary changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Reads the current file, including any manual edits.</summary>
    public void Reload()
    {
        _entries = File.Exists(FilePath) ? [..Parse(File.ReadAllText(FilePath))] : [];
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reads terms and replacement rules from dictionary text.</summary>
    /// <param name="text">Dictionary content.</param>
    public static List<DictionaryEntry> Parse(string text)
    {
        using var reader = new StringReader(text);
        var entries = new List<DictionaryEntry>();
        while (reader.ReadLine() is { } row)
        {
            var line = row.Trim();
            var enabled = true;
            if (line.StartsWith('#'))
            {
                var comment = line[1..].TrimStart();
                if (!comment.StartsWith(Disabled, StringComparison.OrdinalIgnoreCase)) continue;
                line = comment[Disabled.Length..].Trim();
                enabled = false;
            }

            var divider = line.IndexOf("->", StringComparison.Ordinal);
            DictionaryEntry entry = divider < 0
                ? DictionaryEntry.Term(line)
                : DictionaryEntry.Correction(line[..divider], line[(divider + 2)..]);
            if (entry.Write.Length == 0 || (divider >= 0 && entry.Hear.Length == 0)) continue;
            entries.Add(entry with { IsEnabled = enabled });
        }
        return entries;
    }

    /// <summary>Adds and saves a new entry.</summary>
    /// <param name="entry">The entry to add.</param>
    public void Add(DictionaryEntry entry) => Save([.._entries, entry]);

    /// <summary>Updates an existing entry by its identifier.</summary>
    /// <param name="entry">The edited entry.</param>
    public void Update(DictionaryEntry entry)
    {
        var index = Array.FindIndex(_entries, candidate => candidate.Id == entry.Id);
        if (index < 0) return;
        DictionaryEntry[] revised = [.._entries];
        revised[index] = entry;
        Save(revised);
    }

    /// <summary>Deletes a named entry.</summary>
    /// <param name="id">The identifier to delete.</param>
    public void Remove(Guid id) => Save(_entries.Where(entry => entry.Id != id).ToArray());

    /// <summary>Filters entries by text in either field.</summary>
    /// <param name="query">Case-insensitive filter text.</param>
    public IReadOnlyList<DictionaryEntry> Search(string query)
    {
        var filter = query.Trim();
        if (filter.Length == 0) return _entries;

        return _entries.Where(entry => entry.Hear.Contains(filter, StringComparison.OrdinalIgnoreCase)
                                    || entry.Write.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private void Save(DictionaryEntry[] entries)
    {
        const string heading = "# Chit-Chat dictionary\n# One term or 'heard phrase -> preferred spelling' per line.\n\n";
        var contents = string.Join('\n', entries.Select(entry => entry.ToFileLine()));
        LocalFile.Write(FilePath, heading + contents + "\n");
        _entries = entries;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
