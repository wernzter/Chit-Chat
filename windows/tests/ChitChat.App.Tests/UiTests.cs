using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ChitChat.Abstractions;
using ChitChat.App.Views;
using ChitChat.Core;
using ChitChat.Dictionary;
using Shouldly;

[assembly: AvaloniaTestApplication(typeof(ChitChat.AppTests.HeadlessChitChat))]

namespace ChitChat.AppTests;

public static class HeadlessChitChat
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<ChitChat.App.App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
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
    public void SidebarShowsEachWorkspaceSectionWithoutReplacingTheRecordControl()
    {
        var shell = new MainWindow();
        shell.Show();
        try
        {
            var header = shell.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "WorkspaceTitle");
            var record = shell.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "RecordButton");
            var routes = new[]
            {
                ("NavDictionary", "Dictionary"), ("NavRecording", "Recording"), ("NavSpeech", "Speech engine"),
                ("NavText", "Text & AI"), ("NavOpenAI", "OpenAI"), ("NavAccess", "Access & data"),
                ("NavSetup", "Check my setup"), ("NavHistory", "History"),
            };
            foreach (var (name, title) in routes)
            {
                var nav = shell.GetVisualDescendants().OfType<ToggleButton>().Single(button => button.Name == name);
                nav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                header.Text.ShouldBe(title);
                nav.IsChecked.ShouldBe(true);
                record.IsEnabled.ShouldBe(name != "NavSetup");
            }
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
    public void HistoryEditorPersistsTheUpdatedTranscript()
    {
        using var data = new TempData();
        var path = data.PathFor("history.jsonl");
        var history = new TranscriptStore(path);
        history.Add(new TranscriptRecord { Text = "juniper lab" });
        var contents = new TranscriptionsView(history);
        var host = Open(contents);
        try
        {
            var edit = contents.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Edit"));
            edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var dialog = host.OwnedWindows.Single();
            var editor = dialog.GetVisualDescendants().OfType<TextBox>().Single();
            var save = dialog.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Save & learn"));
            save.IsEnabled.ShouldBeFalse();
            editor.Text = "Juniper Labs";
            Dispatcher.UIThread.RunJobs();
            save.IsEnabled.ShouldBeTrue();
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            new TranscriptStore(path).Records.Single().Text.ShouldBe("Juniper Labs");
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
    public void SpeechSettingsShowTheSavedModelDirectory()
    {
        using var data = new TempData();
        var settings = new AppSettings(data.PathFor("settings.json"));
        var directory = data.PathFor("speech-model");
        settings.Update(settings.Data with { ModelDirectory = directory });
        var window = Open(new SpeechSettingsView(settings));
        try
        {
            window.GetVisualDescendants().OfType<TextBox>().Select(control => control.Text).ShouldContain(directory);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void OpenAISettingsLetTheUserSaveAProtectedKeyAndTestTheConnection()
    {
        using var data = new TempData();
        var path = data.PathFor("settings.json");
        var settings = new AppSettings(path);
        settings.Update(settings.Data);
        var keyStore = new MemoryKeyStore();
        var view = new OpenAiSettingsView(settings, new OpenAiEnhancement(keyStore));
        var window = Open(view);
        try
        {
            var input = view.GetVisualDescendants().OfType<TextBox>().Single(box => box.PasswordChar != '\0');
            var test = view.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Test connection"));
            var save = view.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Save key"));
            input.IsEnabled.ShouldBeTrue();
            test.IsEnabled.ShouldBeTrue();
            save.IsEnabled.ShouldBeFalse();

            const string sample = "sample-key-for-ui-test";
            input.Text = sample;
            Dispatcher.UIThread.RunJobs();
            test.Content.ShouldBe("Save & test connection");
            save.IsEnabled.ShouldBeTrue();
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            keyStore.Read().ShouldBe(sample);
            File.ReadAllText(path).ShouldNotContain(sample);
            string.IsNullOrEmpty(input.Text).ShouldBeTrue();
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

    private sealed class MemoryKeyStore : IOpenAiKeyStore
    {
        private string? _key;
        public string? Read() => _key;
        public void Save(string key) => _key = key;
        public void Remove() => _key = null;
    }
}
