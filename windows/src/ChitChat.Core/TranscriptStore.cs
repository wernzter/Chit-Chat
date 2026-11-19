using System.Text.Json;
using System.Text.Json.Serialization;
using ChitChat.Abstractions;
using ChitChat.Dictionary;

namespace ChitChat.Core;

/// <summary>A finished utterance stored in history.</summary>
public sealed record TranscriptRecord
{
    /// <summary>Stable identifier for editing or removal.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();
    /// <summary>When the recording ended.</summary>
    public DateTimeOffset At { get; init; }
    /// <summary>The duration of the captured audio.</summary>
    public double AudioSeconds { get; init; }
    /// <summary>The time spent preparing the final text.</summary>
    public double ProcessingSeconds { get; init; }
    /// <summary>The final dictation text.</summary>
    public string Text { get; init; } = string.Empty;
    /// <summary>Dictionary corrections applied to the dictation.</summary>
    public IReadOnlyList<AppliedCorrection>? Corrections { get; init; }
}

/// <summary>Maintains a newest-first view backed by a newline-delimited JSON log.</summary>
public sealed class TranscriptStore
{
    private readonly string _file;
    private readonly List<TranscriptRecord> _chronological = [];
    private TranscriptRecord[] _newest = [];

    /// <summary>Opens the transcript history for a file.</summary>
    /// <param name="path">Path of the history file.</param>
    public TranscriptStore(string path)
    {
        _file = Path.GetFullPath(path);
        Reload();
    }

    /// <summary>The usual transcript history location.</summary>
    public static string DefaultPath => AppStorage.DataFile("transcripts.jsonl");
    /// <summary>The loaded history with the newest entry first.</summary>
    public IReadOnlyList<TranscriptRecord> Records => _newest;
    /// <summary>Notification that the view changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Reloads the log and skips individual damaged lines.</summary>
    public void Reload()
    {
        var loaded = new List<TranscriptRecord>();
        if (File.Exists(_file))
        {
            foreach (var line in File.ReadLines(_file))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var record = JsonSerializer.Deserialize(line, TranscriptJsonContext.Default.TranscriptRecord);
                    if (record is not null) loaded.Add(record);
                }
                catch (JsonException)
                {
                    // An interrupted append can leave a partial record.
                }
            }
        }

        ReplaceView(loaded);
    }

    /// <summary>Appends a completed dictation.</summary>
    /// <param name="record">The record to save.</param>
    public void Add(TranscriptRecord record)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        using (var stream = new FileStream(_file, FileMode.OpenOrCreate, FileAccess.ReadWrite))
        {
            if (stream.Length != 0)
            {
                stream.Seek(-1, SeekOrigin.End);
                if (stream.ReadByte() != '\n') stream.WriteByte((byte)'\n');
            }
            using var writer = new StreamWriter(stream);
            writer.WriteLine(Encode(record));
        }
        _chronological.Add(record);
        RefreshView();
    }

    /// <summary>Removes a record by identifier.</summary>
    /// <param name="id">The identifier to remove.</param>
    public void Remove(Guid id) => Save(_chronological.Where(record => record.Id != id).ToArray());

    /// <summary>Removes all history.</summary>
    public void Clear() => Save([]);

    /// <summary>Finds transcripts containing the supplied text.</summary>
    /// <param name="query">The search string.</param>
    public IReadOnlyList<TranscriptRecord> Search(string query)
    {
        var filter = query.Trim();
        return filter.Length == 0 ? _newest
            : _newest.Where(record => record.Text.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private void Save(IReadOnlyList<TranscriptRecord> records)
    {
        var content = string.Concat(records.Select(record => Encode(record) + "\n"));
        LocalFile.Write(_file, content);
        ReplaceView(records);
    }

    private void ReplaceView(IEnumerable<TranscriptRecord> records)
    {
        _chronological.Clear();
        _chronological.AddRange(records);
        RefreshView();
    }

    private void RefreshView()
    {
        _newest = _chronological.AsEnumerable().Reverse().ToArray();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string Encode(TranscriptRecord record) =>
        JsonSerializer.Serialize(record, TranscriptJsonContext.Default.TranscriptRecord);
}

[JsonSerializable(typeof(TranscriptRecord))]
internal sealed partial class TranscriptJsonContext : JsonSerializerContext;
