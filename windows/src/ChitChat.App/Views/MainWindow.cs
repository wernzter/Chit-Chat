using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ChitChat.Core;
using ChitChat.Speech;

namespace ChitChat.App.Views;

/// <summary>The Windows dictation workspace.</summary>
public sealed class MainWindow : Window
{
    private readonly Composition? _session;
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _elapsed = new() { FontFamily = new FontFamily("Consolas") };
    private readonly ProgressBar _microphone = new()
    {
        Minimum = 0, Maximum = 1, Width = 100, VerticalAlignment = VerticalAlignment.Center,
        Name = "InputLevel",
    };
    private readonly Button _record = new() { Name = "RecordButton", Foreground = Brushes.White };
    private readonly ContentControl _page = new();
    private readonly ToggleButton _historyTab = new() { Content = "Transcriptions", IsChecked = true };
    private readonly ToggleButton _dictionaryTab = new() { Content = "Dictionary" };
    private readonly Control _history;
    private readonly Control _dictionary;
    private readonly DispatcherTimer _displayTimer;
    private DateTimeOffset? _recordedSince;
    private DictationState _state;
    private bool _modelReady;
    private DateTimeOffset _nextModelCheck;
    private bool _shortcutUnavailable;

    /// <summary>Builds the workspace without desktop services for headless testing.</summary>
    public MainWindow() : this(null) { }

    /// <summary>Builds the workspace around the local dictation session.</summary>
    /// <param name="composition">Desktop services.</param>
    public MainWindow(Composition? composition)
    {
        _session = composition;
        Title = "Chit-Chat";
        Width = 940;
        Height = 680;
        MinWidth = 630;
        MinHeight = 430;

        _history = composition is null
            ? ViewKit.Note("No saved transcriptions.")
            : new TranscriptionsView(composition.Transcripts);
        _dictionary = composition is null
            ? ViewKit.Note("No dictionary entries.")
            : new DictionaryView(composition.Dictionary);
        _historyTab.Click += (_, _) => Navigate(showDictionary: false);
        _dictionaryTab.Click += (_, _) => Navigate(showDictionary: true);
        _record.Click += (_, _) => ToggleRecording();

        var preferences = ViewKit.Action("Settings", OpenSettings);
        var shortcut = ViewKit.Row(_status, _elapsed, _microphone, _record);
        shortcut.VerticalAlignment = VerticalAlignment.Center;
        var header = ViewKit.Split(ViewKit.Title("Chit-Chat"), shortcut);
        var navigation = ViewKit.Split(ViewKit.Row(_historyTab, _dictionaryTab), preferences);
        Content = ViewKit.Page(ViewKit.Column(header, navigation), _page);
        ((Control)Content).Margin = new Thickness(ViewKit.Inset);

        Navigate(showDictionary: false);
        SetState(DictationState.Idle);
        _displayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _displayTimer.Tick += (_, _) => RefreshStatus();
        _displayTimer.Start();
        if (_session?.Engine is { } engine && !engine.Start())
        {
            _shortcutUnavailable = true;
            SetState(DictationState.Idle);
        }
    }

    /// <summary>Whether microphone recording is active.</summary>
    public bool IsRecording => _state == DictationState.Recording;

    /// <summary>Starts or finishes a recording from the window.</summary>
    public void ToggleRecording()
    {
        if (_session?.Engine is { } engine)
        {
            engine.TogglePushToTalk();
            RefreshStatus();
        }
        else if (_session is null)
        {
            SetState(IsRecording ? DictationState.Idle : DictationState.Recording);
        }
    }

    /// <summary>Updates the recording controls to reflect the current stage.</summary>
    /// <param name="state">The dictation stage.</param>
    public void SetState(DictationState state)
    {
        if (_state != state)
        {
            _state = state;
            _recordedSince = state == DictationState.Recording ? DateTimeOffset.Now : null;
        }
        var canRecord = _session is null || _session.IsPlatformAvailable;
        var now = DateTimeOffset.Now;
        if (now >= _nextModelCheck)
        {
            _modelReady = _session is null || (_session.Settings.Data.ModelDirectory is { } path
                ? ParakeetTranscriber.IsComplete(path)
                : Composition.IsModelInstalled);
            _nextModelCheck = now.AddSeconds(1);
        }
        _record.Content = state == DictationState.Recording ? "Stop" : "Record";
        _record.Background = state == DictationState.Recording ? ViewKit.Recording : ViewKit.Accent;
        _record.IsEnabled = canRecord && _modelReady && state != DictationState.Transcribing;
        _status.Text = state switch
        {
            DictationState.Recording => "Recording",
            DictationState.Transcribing => "Transcribing…",
            _ when !canRecord => "Recording unavailable",
            _ when !_modelReady => "Set up the speech model in Settings",
            _ when _shortcutUnavailable => "Global shortcut unavailable",
            _ => "Ready",
        };
        var time = _recordedSince is { } started ? now - started : TimeSpan.Zero;
        _elapsed.Text = string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalMinutes:00}:{time.Seconds:00}");
    }

    /// <summary>Updates the input-level meter.</summary>
    /// <param name="level">Normalized amplitude.</param>
    public void SetLevel(float level) => _microphone.Value = Math.Clamp(level, 0, 1);

    internal void OpenSettings()
    {
        if (_session is not null) _ = new SettingsWindow(_session.Settings).ShowDialog(this);
    }

    private void Navigate(bool showDictionary)
    {
        _page.Content = showDictionary ? _dictionary : _history;
        _historyTab.IsChecked = !showDictionary;
        _dictionaryTab.IsChecked = showDictionary;
    }

    private void RefreshStatus()
    {
        if (!IsVisible) return;
        SetState(_session?.Engine?.State ?? _state);
        if (_session?.Engine is { } engine) SetLevel(engine.Level);
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        _displayTimer.Stop();
        base.OnClosed(e);
    }
}
