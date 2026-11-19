using Avalonia;
using Avalonia.Controls;
using ChitChat.Core;
using ChitChat.Speech;

namespace ChitChat.App.Views;

/// <summary>Preferences for recording and local transcription.</summary>
public sealed class SettingsWindow : Window
{
    private static readonly KeyChoice[] Keys =
    [
        new("Right Ctrl", 0xA3), new("Right Shift", 0xA1), new("F13", 0x7C),
        new("Caps Lock", 0x14), new("Right Alt", 0xA5),
    ];

    private sealed record KeyChoice(string Label, int Code)
    {
        public override string ToString() => Label;
    }

    /// <summary>Opens preferences for the desktop session.</summary>
    /// <param name="settings">The preference store.</param>
    public SettingsWindow(AppSettings settings)
    {
        Title = "Chit-Chat settings";
        Width = 510;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        var trigger = new ComboBox
        {
            ItemsSource = Keys, SelectedItem = Keys.FirstOrDefault(key => key.Code == settings.Data.PushToTalkKey),
        };
        var keyboardHint = ViewKit.Note("Restart Chit-Chat after changing the shortcut.");
        trigger.SelectionChanged += (_, _) =>
        {
            if (trigger.SelectedItem is not KeyChoice key) return;
            settings.Update(settings.Data with { PushToTalkKey = key.Code });
            keyboardHint.Text = key.Code == 0xA5
                ? "Right Alt may act as AltGr. Restart Chit-Chat to apply the shortcut."
                : "Restart Chit-Chat after changing the shortcut.";
        };

        var injection = new CheckBox { Content = "Type transcripts into the focused app", IsChecked = settings.Data.InjectText };
        injection.IsCheckedChanged += (_, _) => settings.Update(settings.Data with { InjectText = injection.IsChecked == true });
        var history = new CheckBox { Content = "Keep transcription history", IsChecked = settings.Data.KeepHistory };
        history.IsCheckedChanged += (_, _) => settings.Update(settings.Data with { KeepHistory = history.IsChecked == true });

        var model = new TextBox { Text = settings.Data.ModelDirectory, Watermark = "Automatic model location" };
        var modelHint = ViewKit.Note(DescribeModel(settings.Data));
        var applyModel = ViewKit.Action("Save location", () =>
        {
            settings.Update(settings.Data with { ModelDirectory = string.IsNullOrWhiteSpace(model.Text) ? null : model.Text.Trim() });
            modelHint.Text = DescribeModel(settings.Data);
        });

        var form = ViewKit.Column(
            ViewKit.Title("Recording"), ViewKit.Note("Push-to-talk key"), trigger, keyboardHint, injection, history,
            ViewKit.Title("Local speech model"), modelHint, ViewKit.Split(model, applyModel),
            ViewKit.Action("Done", Close));
        form.Margin = new Thickness(ViewKit.Inset);
        Content = form;
    }

    private static string DescribeModel(SettingsData settings)
    {
        var location = settings.ModelDirectory ?? ParakeetTranscriber.Locate();
        return location is not null && ParakeetTranscriber.IsComplete(location)
            ? "Parakeet is ready."
            : "Install the four Parakeet model files in a model folder, then select it here.";
    }
}
