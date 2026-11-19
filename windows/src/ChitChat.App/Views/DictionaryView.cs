using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using ChitChat.Core;
using ChitChat.Dictionary;

namespace ChitChat.App.Views;

/// <summary>Filters and edits entries in the local dictionary.</summary>
public sealed class DictionaryView : UserControl
{
    private readonly DictionaryFile _dictionary;
    private readonly TextBox _filter = new() { Watermark = "Search dictionary", Name = "SearchDictionary" };
    private readonly StackPanel _entries = ViewKit.Column();

    /// <summary>Creates an editor for an existing dictionary.</summary>
    /// <param name="file">The backing dictionary.</param>
    public DictionaryView(DictionaryFile file)
    {
        _dictionary = file;
        var toolbar = ViewKit.Split(_filter, ViewKit.Row(
            ViewKit.Action("Open file", OpenFile),
            ViewKit.Action("Reload", Reload),
            ViewKit.Action("Add entry", () => Editor(null))));
        Content = ViewKit.Page(toolbar, new ScrollViewer { Content = _entries });
        _filter.TextChanged += (_, _) => Populate();
        AttachedToVisualTree += (_, _) =>
        {
            _dictionary.Changed += OnDictionaryChanged;
            Populate();
        };
        DetachedFromVisualTree += (_, _) => _dictionary.Changed -= OnDictionaryChanged;
        Populate();
    }

    private void OnDictionaryChanged(object? sender, EventArgs args) => Populate();

    private void Populate()
    {
        _entries.Children.Clear();
        foreach (var entry in _dictionary.Search(_filter.Text ?? string.Empty))
        {
            var caption = entry.Kind == EntryKind.Correction ? $"{entry.Hear} → {entry.Write}" : entry.Write;
            var enabled = new CheckBox { Content = "Enabled", IsChecked = entry.IsEnabled };
            enabled.IsCheckedChanged += (_, _) => _dictionary.Update(entry with { IsEnabled = enabled.IsChecked == true });
            var actions = ViewKit.Row(enabled,
                                      ViewKit.Action("Edit", () => Editor(entry)),
                                      ViewKit.Action("Delete", () => _dictionary.Remove(entry.Id)));
            _entries.Children.Add(ViewKit.Card(ViewKit.Split(ViewKit.Note(caption), actions)));
        }
        if (_entries.Children.Count == 0) _entries.Children.Add(ViewKit.Note("No dictionary entries match."));
    }

    private void Editor(DictionaryEntry? current)
    {
        if (TopLevel.GetTopLevel(this) is not Window parent) return;
        var dialog = new DictionaryEditorWindow(current);
        dialog.Saved += (_, edited) =>
        {
            if (current is null) _dictionary.Add(edited);
            else _dictionary.Update(edited);
        };
        _ = dialog.ShowDialog(parent);
    }

    private void Reload() => _dictionary.Reload();

    private void OpenFile()
    {
        if (!File.Exists(_dictionary.FilePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_dictionary.FilePath))!);
            using var empty = File.Open(_dictionary.FilePath, FileMode.OpenOrCreate);
        }
        using var editor = Process.Start(new ProcessStartInfo(_dictionary.FilePath) { UseShellExecute = true });
    }
}

/// <summary>Edits one vocabulary term or substitution.</summary>
public sealed class DictionaryEditorWindow : Window
{
    private readonly DictionaryEntry? _original;
    private readonly CheckBox _replace = new() { Content = "Replace a recognized phrase" };
    private readonly TextBox _heard = new() { Watermark = "Recognized phrase" };
    private readonly TextBox _spelling = new() { Watermark = "Preferred spelling" };
    private readonly TextBlock _feedback = ViewKit.Note(string.Empty);
    private readonly Button _save = new() { Content = "Save", IsDefault = true };

    /// <summary>Emits a valid entry when saved.</summary>
    public event EventHandler<DictionaryEntry>? Saved;

    /// <summary>Opens an existing entry or a blank form.</summary>
    /// <param name="entry">Existing entry, if editing.</param>
    public DictionaryEditorWindow(DictionaryEntry? entry)
    {
        _original = entry;
        Title = entry is null ? "New dictionary entry" : "Edit dictionary entry";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _replace.IsChecked = entry?.Kind == EntryKind.Correction;
        _heard.Text = entry?.Hear;
        _spelling.Text = entry?.Write;
        _replace.IsCheckedChanged += (_, _) => UpdateForm();
        _heard.TextChanged += (_, _) => UpdateForm();
        _spelling.TextChanged += (_, _) => UpdateForm();
        _save.Click += (_, _) =>
        {
            Saved?.Invoke(this, Draft());
            Close();
        };

        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close();
        var form = ViewKit.Column(_replace, _heard, _spelling, _feedback, ViewKit.Row(_save, cancel));
        form.Margin = new Thickness(ViewKit.Inset);
        Content = form;
        UpdateForm();
    }

    private DictionaryEntry Draft()
    {
        var entry = _original ?? new DictionaryEntry();
        return entry with
        {
            Kind = _replace.IsChecked == true ? EntryKind.Correction : EntryKind.Term,
            Hear = _replace.IsChecked == true ? _heard.Text?.Trim() ?? string.Empty : string.Empty,
            Write = _spelling.Text?.Trim() ?? string.Empty,
        };
    }

    private void UpdateForm()
    {
        var value = Draft();
        _heard.IsVisible = value.Kind == EntryKind.Correction;
        _save.IsEnabled = value.Write.Length > 0 && (!_heard.IsVisible || value.Hear.Length > 0);
        _feedback.Text = string.Join(Environment.NewLine, DictionaryWarning.Check(value).Select(warning => warning.Message));
        _feedback.IsVisible = _feedback.Text.Length > 0;
    }
}
