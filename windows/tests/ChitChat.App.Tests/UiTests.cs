using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ChitChat.App.Views;
using ChitChat.Core;
using ChitChat.Dictionary;
using Shouldly;

[assembly: AvaloniaTestApplication(typeof(ChitChat.AppTests.HeadlessChitChat))]

namespace ChitChat.AppTests;

public sealed class HeadlessChitChat : Application
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<HeadlessChitChat>().UseHeadless(new AvaloniaHeadlessPlatformOptions());

    public override void Initialize() => Styles.Add(new FluentTheme());
}

public sealed class UiTests
{
    [AvaloniaFact]
    public void LaunchingTheMainWindowCreatesAUsableLayout()
    {
        var shell = new MainWindow();
        shell.Show();
        try
        {
            shell.Title.ShouldNotBeNull().ShouldContain("Chit-Chat");
            shell.Bounds.Width.ShouldBeGreaterThanOrEqualTo(shell.MinWidth);
            shell.Bounds.Height.ShouldBeGreaterThanOrEqualTo(shell.MinHeight);
            shell.GetVisualDescendants().OfType<Button>().ShouldNotBeEmpty();
        }
        finally { shell.Close(); }
    }

    [AvaloniaFact]
    public void ARecordToggleStartsAndThenStopsTheRecordingIndicator()
    {
        var shell = new MainWindow();
        shell.Show();
        try
        {
            shell.IsRecording.ShouldBeFalse();
            var record = shell.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "RecordButton");
            var meter = shell.GetVisualDescendants().OfType<ProgressBar>().Single(control => control.Name == "InputLevel");
            record.Content.ShouldBe("Record");
            shell.ToggleRecording();
            shell.IsRecording.ShouldBeTrue();
            record.Content.ShouldBe("Stop");
            shell.SetLevel(0.75f);
            meter.Value.ShouldBe(0.75);
            shell.ToggleRecording();
            shell.IsRecording.ShouldBeFalse();
            record.Content.ShouldBe("Record");
            shell.SetState(DictationState.Transcribing);
            record.IsEnabled.ShouldBeFalse();
        }
        finally { shell.Close(); }
    }

    [AvaloniaFact]
    public void HistorySearchFiltersByTranscriptText()
    {
        using var data = new TempData();
        var history = new TranscriptStore(data.PathFor("history.jsonl"));
        history.Add(new TranscriptRecord { Text = "Harbourview booking" });
        history.Add(new TranscriptRecord { Text = "Juniper appointment" });
        var contents = new TranscriptionsView(history);
        var host = Open(contents);
        try
        {
            Search(contents, "JUNIPER");
            VisibleLabels(contents).ShouldContain("Juniper appointment");
            VisibleLabels(contents).ShouldNotContain("Harbourview booking");
        }
        finally { host.Close(); }
    }

    [AvaloniaFact]
    public void DictionarySearchFindsAnEntryByItsSpokenForm()
    {
        using var data = new TempData();
        var rules = new DictionaryFile(data.PathFor("dictionary.txt"));
        rules.Add(DictionaryEntry.Correction("hawk ridge", "Hawk Ridge Reserve"));
        rules.Add(DictionaryEntry.Term("Juniper Meadows"));
        var contents = new DictionaryView(rules);
        var host = Open(contents);
        try
        {
            Search(contents, "HAWK");
            VisibleLabels(contents).Any(text => text.Contains("Hawk Ridge Reserve", StringComparison.Ordinal)).ShouldBeTrue();
            VisibleLabels(contents).ShouldNotContain("Juniper Meadows");
        }
        finally { host.Close(); }
    }

    [AvaloniaFact]
    public void SettingsWindowShowsTheSavedModelDirectory()
    {
        using var data = new TempData();
        var settings = new AppSettings(data.PathFor("settings.json"));
        var directory = data.PathFor("speech-model");
        settings.Update(settings.Data with { ModelDirectory = directory });
        var window = new SettingsWindow(settings);
        window.Show();
        try
        {
            window.GetVisualDescendants().OfType<TextBox>().Select(control => control.Text).ShouldContain(directory);
        }
        finally { window.Close(); }
    }

    private static Window Open(Control child)
    {
        var host = new Window { Content = child, Width = 840, Height = 600 };
        host.Show();
        Dispatcher.UIThread.RunJobs();
        return host;
    }

    private static void Search(Control root, string value)
    {
        root.GetVisualDescendants().OfType<TextBox>().Single().Text = value;
        Dispatcher.UIThread.RunJobs();
        root.UpdateLayout();
    }

    private static IEnumerable<string> VisibleLabels(Control root) =>
        root.GetVisualDescendants().Where(node => node.IsVisible).OfType<TextBlock>()
            .Select(control => control.Text ?? string.Empty);

    private sealed class TempData : IDisposable
    {
        private readonly DirectoryInfo _storage = Directory.CreateTempSubdirectory("chitchat-ui-");

        public string PathFor(string file) => Path.Combine(_storage.FullName, file);

        public void Dispose() => _storage.Delete(recursive: true);
    }
}
