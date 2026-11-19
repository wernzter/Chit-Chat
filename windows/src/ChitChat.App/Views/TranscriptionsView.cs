using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;
using ChitChat.Core;

namespace ChitChat.App.Views;

/// <summary>Searchable history of local dictations.</summary>
public sealed class TranscriptionsView : UserControl
{
    private readonly TranscriptStore _history;
    private readonly StackPanel _results = ViewKit.Column();
    private readonly TextBox _search = new() { Watermark = "Search transcriptions", Name = "SearchTranscriptions" };

    /// <summary>Builds a view over the saved history.</summary>
    /// <param name="store">The transcript source.</param>
    public TranscriptionsView(TranscriptStore store)
    {
        _history = store;
        Content = ViewKit.Page(_search, new ScrollViewer { Content = _results });
        _search.TextChanged += (_, _) => Refresh();
        AttachedToVisualTree += (_, _) =>
        {
            _history.Changed += OnHistoryChanged;
            Refresh();
        };
        DetachedFromVisualTree += (_, _) => _history.Changed -= OnHistoryChanged;
        Refresh();
    }

    private void OnHistoryChanged(object? sender, EventArgs args) => Refresh();

    private void Refresh()
    {
        _results.Children.Clear();
        var records = _history.Search(_search.Text ?? string.Empty);
        foreach (var record in records) _results.Children.Add(CreateRow(record));
        if (records.Count == 0)
            _results.Children.Add(ViewKit.Note(string.IsNullOrWhiteSpace(_search.Text)
                ? "Your transcriptions will appear here." : "No matching transcriptions."));
    }

    private Border CreateRow(TranscriptRecord record)
    {
        var timestamp = record.At.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        var metadata = string.Create(CultureInfo.CurrentCulture,
            $"{timestamp}  ·  {record.AudioSeconds:0.#} s audio  ·  {record.ProcessingSeconds:0.##} s processing");

        var copy = new Button { Content = "Copy" };
        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(record.Text);
                copy.Content = "Copied";
            }
        };

        var column = ViewKit.Column(ViewKit.Split(ViewKit.Note(metadata), copy),
            new SelectableTextBlock { Text = record.Text, TextWrapping = TextWrapping.Wrap });
        if (record.Corrections is { Count: > 0 } corrections)
        {
            var applied = string.Join(", ", corrections.Select(rule => $"{rule.From} → {rule.To}"));
            column.Children.Add(ViewKit.Note(applied));
        }
        return ViewKit.Card(column);
    }
}
