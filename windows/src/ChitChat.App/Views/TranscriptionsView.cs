using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ChitChat.Core;

namespace ChitChat.App.Views;

/// <summary>Searchable history of local dictations.</summary>
public sealed class TranscriptionsView : UserControl
{
    private readonly TranscriptStore _history;
    private readonly Func<bool> _learnFromEdits;
    private readonly ContentControl _results = new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch,
    };
    private readonly TextBox _search = ViewKit.Search("SearchTranscriptions", "Search transcriptions");
    private readonly TextBlock _count = ViewKit.Caption(string.Empty);
    private readonly TextBlock _feedback = ViewKit.Note(string.Empty);
    private readonly Button _clear;

    /// <summary>Builds a view over the saved history.</summary>
    /// <param name="store">The transcript source.</param>
    /// <param name="learnFromEdits">Whether saved edits should be described as learning corrections.</param>
    public TranscriptionsView(TranscriptStore store, Func<bool>? learnFromEdits = null)
    {
        _history = store;
        _learnFromEdits = learnFromEdits ?? (() => true);
        _clear = ViewKit.Action("Clear history", ConfirmClear);
        _clear.Name = "ClearHistory";
        _feedback.IsVisible = false;

        var foot = ViewKit.Column(_feedback, ViewKit.Split(_count, _clear));
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = ViewKit.Gap };
        Grid.SetRow(_results, 1);
        Grid.SetRow(foot, 2);
        layout.Children.AddRange([_search, _results, foot]);
        Content = layout;

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
        var records = _history.Search(_search.Text ?? string.Empty);
        _count.Text = _history.Records.Count == 1 ? "1 transcription" : $"{_history.Records.Count} transcriptions";
        _clear.IsVisible = _history.Records.Count > 0;
        _results.Content = records.Count switch
        {
            > 0 => ViewKit.Scroll(ViewKit.Column(records.Select(CreateRow).ToArray())),
            _ when _history.Records.Count == 0 => ViewKit.Empty("Your transcriptions will appear here",
                "Press Record or use your recording shortcut to start dictating."),
            _ => ViewKit.Empty("No matches", "Try a different search."),
        };
    }

    private Control CreateRow(TranscriptRecord record)
    {
        var timestamp = record.At.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        var engine = record.UsedStreamingSpeech ? "Live speech" : "Parakeet";
        var metadata = string.Create(CultureInfo.CurrentCulture,
            $"{engine} · {record.ProcessingSeconds:0.##} s");
        var label = ViewKit.Caption(metadata);
        label.HorizontalAlignment = HorizontalAlignment.Right;
        var header = ViewKit.Split(ViewKit.Caption(timestamp), label);

        var copy = new Button { Content = "Copy" };
        AutomationProperties.SetName(copy, "Copy transcription");
        copy.Click += async (_, _) =>
        {
            try
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
                await clipboard.SetTextAsync(record.Text);
                copy.Content = "Copied";
                _ = DispatcherTimer.RunOnce(() => copy.Content = "Copy", TimeSpan.FromMilliseconds(1400));
            }
            catch (InvalidOperationException)
            {
                ShowError("Couldn’t copy the transcription. Try again.");
            }
        };

        var edit = ViewKit.Action("Edit", () => OpenEditor(record));
        AutomationProperties.SetName(edit, "Edit transcription");
        var remove = ViewKit.Action("Delete", () =>
            TryHistoryWrite(() => _history.Remove(record.Id), "Couldn’t delete this transcription."));
        AutomationProperties.SetName(remove, "Delete transcription");

        var corrections = record.Corrections is { Count: > 0 } applied
            ? ViewKit.Caption(string.Join(" · ", applied.Select(rule => $"{rule.From} → {rule.To}")))
            : ViewKit.Caption(string.Empty);
        corrections.IsVisible = !string.IsNullOrEmpty(corrections.Text);
        var actions = ViewKit.Split(corrections, ViewKit.Row(copy, edit, remove));
        return ViewKit.Card(ViewKit.Column(header, new SelectableTextBlock
        {
            Text = record.Text, TextWrapping = TextWrapping.Wrap, FontSize = 15,
        }, actions));
    }

    private void OpenEditor(TranscriptRecord record)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var learning = _learnFromEdits();
        var dialog = EditorWindow("Edit transcription", width: 620);

        var text = new TextBox
        {
            Name = "TranscriptEditorText", Text = record.Text, AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap, Height = 240,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(text, ScrollBarVisibility.Auto);
        AutomationProperties.SetName(text, "Transcript text");
        var feedback = ViewKit.Note(string.Empty);
        feedback.IsVisible = false;
        var save = ViewKit.Primary(learning ? "Save & learn" : "Save", () =>
        {
            try
            {
                if (_history.UpdateText(record.Id, text.Text ?? string.Empty))
                {
                    dialog.Close();
                    return;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Keep the editor open with the user's changes when persistence fails.
            }
            feedback.Text = "Couldn’t save this transcription. Your edits are still here.";
            feedback.IsVisible = true;
        });
        save.IsDefault = true;
        save.IsEnabled = false;
        text.TextChanged += (_, _) => save.IsEnabled =
            !string.IsNullOrWhiteSpace(text.Text) && !string.Equals(text.Text, record.Text, StringComparison.Ordinal);

        var cancel = ViewKit.Action("Cancel", dialog.Close);
        cancel.IsCancel = true;
        var buttons = ViewKit.Row(cancel, save);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        var form = ViewKit.Column(ViewKit.Heading("Edit transcription"),
            ViewKit.Note(learning
                ? "Saving updates this entry and can teach Chit-Chat a correction for future dictations."
                : "Saving updates this entry in your local history."),
            text, feedback, buttons);
        form.Margin = new Thickness(ViewKit.Inset);
        dialog.Content = form;
        dialog.Opened += (_, _) => text.Focus();
        _ = dialog.ShowDialog(owner);
    }

    private void ConfirmClear()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var dialog = EditorWindow("Clear history", width: 420);
        var feedback = ViewKit.Note(string.Empty);
        feedback.IsVisible = false;
        var cancel = ViewKit.Action("Cancel", dialog.Close);
        cancel.IsCancel = true;
        var clear = ViewKit.Action("Delete all", () =>
        {
            if (TryHistoryWrite(_history.Clear, "Couldn’t clear transcription history."))
                dialog.Close();
            else
            {
                feedback.Text = "Couldn’t clear transcription history. Try again.";
                feedback.IsVisible = true;
            }
        });
        var buttons = ViewKit.Row(cancel, clear);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        var form = ViewKit.Column(
            ViewKit.Heading($"Delete {_history.Records.Count} transcriptions?"),
            ViewKit.Note("This can’t be undone."), feedback, buttons);
        form.Margin = new Thickness(ViewKit.Inset);
        dialog.Content = form;
        _ = dialog.ShowDialog(owner);
    }

    private static Window EditorWindow(string title, double width)
    {
        var dialog = new Window
        {
            Title = title, Width = width, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        dialog.Classes.Add("workspace");
        return dialog;
    }

    private bool TryHistoryWrite(Action apply, string error)
    {
        try
        {
            apply();
            _feedback.IsVisible = false;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError(error);
            return false;
        }
    }

    private void ShowError(string message)
    {
        _feedback.Text = message;
        _feedback.IsVisible = true;
    }
}
