using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ChitChat.Core;
using ChitChat.Dictionary;

namespace ChitChat.App.Views;

/// <summary>Filters and edits entries in the local dictionary.</summary>
public sealed class DictionaryView : UserControl
{
    private readonly DictionaryFile _dictionary;
    private readonly TextBox _filter = ViewKit.Search("SearchDictionary", "Search dictionary");
    private readonly ContentControl _results = new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch,
    };
    private readonly TextBlock _total = ViewKit.Caption(string.Empty);
    private readonly TextBlock _feedback = ViewKit.Note(string.Empty);

    /// <summary>Creates an editor for an existing dictionary.</summary>
    /// <param name="file">The backing dictionary.</param>
    public DictionaryView(DictionaryFile file)
    {
        _dictionary = file;
        var add = ViewKit.Primary("Add entry", () => OpenEditor(null));
        add.Name = "AddDictionaryEntry";
        var toolbar = ViewKit.Split(_filter, add);
        var files = ViewKit.Row(ViewKit.Action("Open dictionary file", OpenFile), ViewKit.Action("Reload", Reload));
        _feedback.IsVisible = false;
        var footer = ViewKit.Column(_feedback, ViewKit.Split(_total, files));
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = ViewKit.Gap };
        Grid.SetRow(_results, 1);
        Grid.SetRow(footer, 2);
        layout.Children.AddRange([toolbar, _results, footer]);
        Content = layout;

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
        var matching = _dictionary.Search(_filter.Text ?? string.Empty);
        _total.Text = _dictionary.Entries.Count == 1 ? "1 entry" : $"{_dictionary.Entries.Count} entries";
        _results.Content = matching.Count switch
        {
            > 0 => ViewKit.Scroll(ViewKit.Column(matching.Select(CreateRow).ToArray())),
            _ when _dictionary.Entries.Count == 0 => ViewKit.Empty("Make it sound like you",
                "Add names, specialist terms, or words that need a correction."),
            _ => ViewKit.Empty("No matches", "Try a different search."),
        };
    }

    private Control CreateRow(DictionaryEntry entry)
    {
        var kind = ViewKit.Caption(entry.Kind == EntryKind.Correction ? "Correction" : "Term");
        var text = entry.Kind == EntryKind.Correction ? $"{entry.Hear} → {entry.Write}" : entry.Write;
        var value = new SelectableTextBlock
        {
            Text = text, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.Medium, FontSize = 15,
        };
        var preview = ViewKit.Column(kind, value);
        preview.Spacing = 4;
        preview.Opacity = entry.IsEnabled ? 1 : 0.65;

        var enabled = new ToggleSwitch { IsChecked = entry.IsEnabled, OnContent = null, OffContent = null };
        AutomationProperties.SetName(enabled, $"Enable {entry.Write}");
        enabled.IsCheckedChanged += (_, _) =>
            TryFileAction(() => _dictionary.Update(entry with { IsEnabled = enabled.IsChecked == true }),
                          "Couldn’t update this entry.");
        var edit = ViewKit.Action("Edit", () => OpenEditor(entry));
        AutomationProperties.SetName(edit, $"Edit {entry.Write}");
        var remove = ViewKit.Action("Delete", () =>
            TryFileAction(() => _dictionary.Remove(entry.Id), "Couldn’t delete this entry."));
        AutomationProperties.SetName(remove, $"Delete {entry.Write}");
        var actions = ViewKit.Row(enabled, edit, remove);
        actions.VerticalAlignment = VerticalAlignment.Center;
        return ViewKit.Card(ViewKit.Split(preview, actions));
    }

    private void OpenEditor(DictionaryEntry? current)
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

    private void Reload() => TryFileAction(_dictionary.Reload, "Couldn’t reload the dictionary.");

    private void OpenFile() => TryFileAction(() =>
    {
        var path = _dictionary.FilePath;
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using var empty = File.Open(path, FileMode.OpenOrCreate);
        }
        using var editor = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }, "Windows couldn’t open the dictionary file.");

    private void TryFileAction(Action apply, string failure)
    {
        try
        {
            apply();
            _feedback.IsVisible = false;
        }
        catch (Exception ex) when (ex is IOException or Win32Exception or UnauthorizedAccessException or InvalidOperationException)
        {
            _feedback.Text = failure;
            _feedback.IsVisible = true;
        }
    }
}

/// <summary>Edits one vocabulary term or substitution.</summary>
public sealed class DictionaryEditorWindow : Window
{
    private readonly DictionaryEntry? _original;
    private readonly ComboBox _kind = new() { ItemsSource = new[] { "Term", "Correction" } };
    private readonly TextBox _heard = new() { Watermark = "juniper lab" };
    private readonly TextBox _spelling = new() { Watermark = "Juniper Labs" };
    private readonly TextBlock _feedback = ViewKit.Note(string.Empty);
    private readonly Button _save;
    private readonly Control _triggerField;

    /// <summary>Emits a valid entry when saved.</summary>
    public event EventHandler<DictionaryEntry>? Saved;

    /// <summary>Opens an existing entry or a blank form.</summary>
    /// <param name="entry">Existing entry, if editing.</param>
    public DictionaryEditorWindow(DictionaryEntry? entry)
    {
        _original = entry;
        Title = entry is null ? "Add dictionary entry" : "Edit dictionary entry";
        Width = 470;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Classes.Add("workspace");

        _kind.SelectedIndex = entry?.Kind == EntryKind.Correction ? 1 : 0;
        _heard.Text = entry?.Hear;
        _spelling.Text = entry?.Write;
        _triggerField = ViewKit.Field("When you hear", _heard);
        _save = ViewKit.Primary("Save", Save);
        _save.IsDefault = true;
        _kind.SelectionChanged += (_, _) => UpdateForm();
        _heard.TextChanged += (_, _) => UpdateForm();
        _spelling.TextChanged += (_, _) => UpdateForm();

        var cancel = ViewKit.Action("Cancel", Close);
        cancel.IsCancel = true;
        var actions = ViewKit.Row(cancel, _save);
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        var form = ViewKit.Column(ViewKit.Heading(Title), ViewKit.Field("Entry type", _kind), _triggerField,
            ViewKit.Field("Preferred spelling", _spelling), _feedback, actions);
        form.Margin = new Thickness(ViewKit.Inset);
        Content = form;
        Opened += (_, _) =>
        {
            if (_kind.SelectedIndex == 1) _heard.Focus();
            else _spelling.Focus();
        };
        UpdateForm();
    }

    private DictionaryEntry Draft()
    {
        var entry = _original ?? new DictionaryEntry();
        var replaces = _kind.SelectedIndex == 1;
        return entry with
        {
            Kind = replaces ? EntryKind.Correction : EntryKind.Term,
            Hear = replaces ? _heard.Text?.Trim() ?? string.Empty : string.Empty,
            Write = _spelling.Text?.Trim() ?? string.Empty,
        };
    }

    private void Save()
    {
        try
        {
            Saved?.Invoke(this, Draft());
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _feedback.Text = "Couldn’t save this entry. Your changes are still here.";
            _feedback.IsVisible = true;
        }
    }

    private void UpdateForm()
    {
        var value = Draft();
        _triggerField.IsVisible = value.Kind == EntryKind.Correction;
        _save.IsEnabled = value.Write.Length > 0 && (!_triggerField.IsVisible || value.Hear.Length > 0);
        _feedback.Text = string.Join(Environment.NewLine, DictionaryWarning.Check(value).Select(warning => warning.Message));
        _feedback.IsVisible = _feedback.Text.Length > 0;
    }
}
