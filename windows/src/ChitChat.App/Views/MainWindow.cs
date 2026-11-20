using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ChitChat.Core;

namespace ChitChat.App.Views;

/// <summary>The Windows dictation workspace.</summary>
public sealed class MainWindow : Window
{
    private enum WorkspacePage { History, Dictionary, Recording, Speech, Text, OpenAI, Access, Setup }

    private static readonly Dictionary<WorkspacePage, (string Title, string Detail, string Icon)> Pages = new()
        {
            [WorkspacePage.History] = ("History", "Find, copy, or edit your transcriptions.",
                "M10 2 A8 8 0 1 0 18 10 A8 8 0 0 0 10 2 Z M10 4 A6 6 0 1 1 4 10 A6 6 0 0 1 10 4 Z M9 5 H11 V9 L14 12 L13 13 L9 10 Z"),
            [WorkspacePage.Dictionary] = ("Dictionary", "Save names, terms, and spelling corrections.",
                "M3 3 H10 L11 4 L12 3 H18 V16 H12 L11 17 L10 16 H3 Z M5 5 V14 H10 V6 L9 5 Z M12 6 V14 H16 V5 H13 Z"),
            [WorkspacePage.Recording] = ("Recording", "Choose how to start, stop, and deliver dictation.",
                "M2 5 H18 V15 H2 Z M4 7 V13 H16 V7 Z M5 8 H7 V10 H5 Z M9 8 H11 V10 H9 Z M13 8 H15 V10 H13 Z M7 11 H13 V12 H7 Z"),
            [WorkspacePage.Speech] = ("Speech engine", "Set up the on-device model that turns speech into text.",
                "M9 6 H11 V14 H9 Z M5 8 H7 V12 H5 Z M13 4 H15 V16 H13 Z M17 8 H19 V12 H17 Z M1 9 H3 V11 H1 Z"),
            [WorkspacePage.Text] = ("Text & AI", "Choose how transcripts are cleaned up before insertion.",
                "M3 4 H17 V6 H3 Z M3 9 H13 V11 H3 Z M3 14 H10 V16 H3 Z M12 15 L14 17 L18 12 L19 13 L14 20 L11 17 Z"),
            [WorkspacePage.OpenAI] = ("OpenAI", "Configure optional cloud enhancement for your transcripts.",
                "M10 1 L12 8 L19 10 L12 12 L10 19 L8 12 L1 10 L8 8 Z"),
            [WorkspacePage.Access] = ("Access & data", "Check access and find the files Chit-Chat stores locally.",
                "M10 1 L17 4 V10 C17 14 14 17 10 19 C6 17 3 14 3 10 V4 Z M10 4 L5 6 V10 C5 13 7 15 10 17 C13 15 15 13 15 10 V6 Z"),
            [WorkspacePage.Setup] = ("Check my setup", "Test your shortcut, microphone, and text insertion.",
                "M10 1 A9 9 0 1 0 19 10 A9 9 0 0 0 10 1 Z M10 3 A7 7 0 1 1 3 10 A7 7 0 0 1 10 3 Z M6 10 L9 13 L14 7 L15 8 L9 15 L5 11 Z"),
        };

    private readonly Composition? _session;
    private readonly TextBlock _title = ViewKit.Title(string.Empty);
    private readonly TextBlock _detail = ViewKit.Note(string.Empty);
    private readonly TextBlock _status = ViewKit.Caption(string.Empty);
    private readonly TextBlock _shortcut = ViewKit.Caption(string.Empty);
    private readonly TextBlock _engineLabel = ViewKit.Caption("Parakeet · On-device");
    private readonly TextBlock _waitingCount = ViewKit.Caption(string.Empty);
    private readonly TextBlock _recordingError = ViewKit.Note(string.Empty);
    private readonly TextBlock _liveText = new()
    {
        TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
        MaxLines = 3,
    };
    private readonly Border _livePreview;
    private readonly Border _recovery;
    private readonly ProgressBar _microphone = new()
    {
        Minimum = 0, Maximum = 1, Width = 34, Height = 6, VerticalAlignment = VerticalAlignment.Center,
        Name = "InputLevel",
    };
    private readonly Button _record = new()
    {
        Name = "RecordButton", Foreground = Brushes.White, MinWidth = 112,
        HorizontalAlignment = HorizontalAlignment.Right, HorizontalContentAlignment = HorizontalAlignment.Center,
        FontWeight = FontWeight.SemiBold,
    };
    private readonly ContentControl _content = new()
    {
        Name = "WorkspaceContent",
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch,
    };
    private readonly Dictionary<WorkspacePage, ToggleButton> _navigation = [];
    private readonly Dictionary<WorkspacePage, Control> _views = [];
    private readonly DispatcherTimer _clock;
    private readonly RecordingOverlay? _recordingOverlay;
    private readonly CorrectionPopup? _correctionPopup;
    private readonly RecoveryPopup? _recoveryPopup;
    private WaitingDictation? _inlineRecovery;
    private WorkspacePage _page;
    private WorkspacePage _settingsPage = WorkspacePage.Recording;
    private WorkspacePage _previousPage = WorkspacePage.History;
    private DateTimeOffset? _started;
    private DateTimeOffset _checkModelAfter;
    private DictationState _state;
    private bool _modelReady;
    private bool _shortcutUnavailable;

    /// <summary>Builds the workspace without desktop services for headless testing.</summary>
    public MainWindow() : this(null) { }

    /// <summary>Builds the workspace around the local dictation session.</summary>
    /// <param name="composition">Desktop services.</param>
    public MainWindow(Composition? composition)
    {
        _session = composition;
        if (OperatingSystem.IsWindows() && composition?.IsPlatformAvailable == true)
        {
            _recordingOverlay = new RecordingOverlay(this, composition);
            _correctionPopup = new CorrectionPopup(this, composition, composition.DismissCorrection);
            _recoveryPopup = new RecoveryPopup(this, composition, CopyWaitingAsync, DismissWaiting);
        }
        Title = "Chit-Chat";
        Width = 1060;
        Height = 760;
        MinWidth = 880;
        MinHeight = 640;
        Classes.Add("workspace");

        _record.Classes.Add("record");
        AutomationProperties.SetName(_microphone, "Microphone input level");
        _record.Click += (_, _) => ToggleRecording();

        _livePreview = ViewKit.Section("Live transcript", _liveText);
        _livePreview.IsVisible = false;
        _recovery = CreateRecoveryBanner();
        _recordingError.IsVisible = false;
        var sidebar = CreateSidebar();
        var body = ViewKit.Page(ViewKit.Column(CreateHeader(), _recordingError, _recovery, _livePreview), _content);
        body.Margin = new Thickness(ViewKit.Inset);
        var layout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Children = { sidebar, body },
        };
        Grid.SetColumn(body, 1);
        Content = layout;

        Navigate(WorkspacePage.History);
        if (_session?.Engine is { } engine)
            _shortcutUnavailable = !engine.Start();
        SetState(DictationState.Idle);

        _clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        _clock.Tick += (_, _) => RefreshStatus();
        _clock.Start();
    }

    /// <summary>Whether the microphone is listening.</summary>
    public bool IsRecording => _state == DictationState.Recording;

    /// <summary>Starts or finishes a recording from the window.</summary>
    public void ToggleRecording()
    {
        if (!_record.IsEnabled) return;
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

    /// <summary>Updates the presentation for a dictation stage.</summary>
    /// <param name="state">The dictation stage.</param>
    public void SetState(DictationState state)
    {
        var now = DateTimeOffset.Now;
        if (state != _state)
        {
            _state = state;
            _started = state == DictationState.Recording ? now : null;
        }

        if (now >= _checkModelAfter)
        {
            _modelReady = _session?.IsSpeechReady ?? true;
            _checkModelAfter = now.AddSeconds(1);
        }

        var engine = _session?.Engine;
        var preparing = engine?.IsStartingCapture == true;
        var platform = _session is null || _session.IsPlatformAvailable;
        var runningSetupCheck = _page == WorkspacePage.Setup;
        _record.Content = state switch
        {
            DictationState.Recording => "Stop",
            DictationState.Transcribing => "Processing…",
            _ => "Record",
        };
        _record.Background = IsRecording ? ViewKit.Recording : ViewKit.Accent;
        _record.IsEnabled = platform && (IsRecording || (_modelReady && !runningSetupCheck && state == DictationState.Idle));
        _microphone.IsVisible = IsRecording && !preparing;
        var elapsed = _started is { } start ? now - start : TimeSpan.Zero;
        _status.Text = state switch
        {
            DictationState.Recording when preparing => "Preparing speech engine…",
            DictationState.Recording => string.Create(CultureInfo.InvariantCulture,
                $"Listening · {(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}"),
            DictationState.Transcribing => "Transcribing…",
            _ when runningSetupCheck => "Setup check",
            _ when !platform => "Recording unavailable",
            _ when !_modelReady => "Speech model required",
            _ when _shortcutUnavailable => "Shortcut unavailable",
            _ => string.Empty,
        };
        _status.IsVisible = _status.Text.Length != 0;
        _shortcut.Text = _session is null ? "Right Ctrl" : WorkspacePreferences.DescribeShortcut(
            _session.Settings.Data.PushToTalkKey, _session.Settings.Data.ShortcutModifiers);
        _engineLabel.Text = _session?.Settings.Data.UseStreamingSpeech == true
            ? "Streaming speech · On-device" : "Parakeet · On-device";
        _inlineRecovery = _session?.Recovery;
        _waitingCount.Text = _session is { RecoveryCount: > 0 } desktop ? $"{desktop.RecoveryCount} waiting" : string.Empty;
        _recovery.IsVisible = _inlineRecovery is not null;
        _recordingError.Text = engine?.LastError;
        _recordingError.IsVisible = !string.IsNullOrWhiteSpace(_recordingError.Text);
        _liveText.Text = IsRecording ? engine?.PreviewText : null;
        _livePreview.IsVisible = !string.IsNullOrWhiteSpace(_liveText.Text);
    }

    /// <summary>Updates the input level on the recording meter.</summary>
    /// <param name="level">Normalized amplitude.</param>
    public void SetLevel(float level) => _microphone.Value = float.IsFinite(level) ? Math.Clamp(level, 0, 1) : 0;

    internal void OpenSettings() => Navigate(_settingsPage);

    internal void OpenSetup() => Navigate(WorkspacePage.Setup);

    internal void HideToTray()
    {
        if (_page == WorkspacePage.Setup) Navigate(_previousPage);
        Hide();
    }

    internal async Task CopyLastTranscriptAsync()
    {
        var latest = _session?.LatestTranscript;
        if (latest is null && _session?.Transcripts.Records is { Count: > 0 } history)
            latest = history[0].Text;
        if (latest is not null) await CopyAsync(latest);
    }

    private Border CreateSidebar()
    {
        var mark = new Border
        {
            Width = 38, Height = 38, Child = ViewKit.Icon(Pages[WorkspacePage.Speech].Icon, 21),
        };
        mark.Classes.Add("brand");

        var brand = ViewKit.Row(mark, new TextBlock
        {
            Text = "Chit-Chat", FontSize = 19, FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        brand.Margin = new Thickness(4, 11, 4, 17);

        var paths = new StackPanel { Spacing = 3 };
        paths.Children.Add(GroupLabel("Workspace"));
        paths.Children.Add(CreateNavigation(WorkspacePage.History));
        paths.Children.Add(CreateNavigation(WorkspacePage.Dictionary));

        var settingsLabel = GroupLabel("Settings");
        settingsLabel.Margin = new Thickness(10, 16, 0, 4);
        paths.Children.Add(settingsLabel);
        foreach (var setting in new[] { WorkspacePage.Recording, WorkspacePage.Speech, WorkspacePage.Text, WorkspacePage.OpenAI, WorkspacePage.Access })
            paths.Children.Add(CreateNavigation(setting));

        var footer = new StackPanel { Spacing = 6 };
        footer.Children.Add(ViewKit.Rule());
        footer.Children.Add(CreateNavigation(WorkspacePage.Setup));
        _engineLabel.Margin = new Thickness(10, 4, 0, 0);
        footer.Children.Add(_engineLabel);

        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        Grid.SetRow(paths, 1);
        Grid.SetRow(footer, 2);
        layout.Children.AddRange([brand, paths, footer]);

        var sidebar = new Border { Width = ViewKit.SidebarWidth, Padding = new Thickness(14), Child = layout };
        sidebar.Classes.Add("sidebar");
        return sidebar;
    }

    private static TextBlock GroupLabel(string label)
    {
        var title = ViewKit.Caption(label);
        title.FontWeight = FontWeight.SemiBold;
        title.Margin = new Thickness(10, 0, 0, 4);
        return title;
    }

    private ToggleButton CreateNavigation(WorkspacePage page)
    {
        var descriptor = Pages[page];
        var icon = ViewKit.Icon(descriptor.Icon);
        var label = new TextBlock
        {
            Text = descriptor.Title, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center,
        };
        var button = new ToggleButton { Name = "Nav" + page, Content = ViewKit.Row(icon, label) };
        button.Classes.Add("navigation");
        AutomationProperties.SetName(button, descriptor.Title);
        button.Click += (_, _) => Navigate(page);
        _navigation.Add(page, button);
        return button;
    }

    private Grid CreateHeader()
    {
        _title.Name = "WorkspaceTitle";
        var heading = ViewKit.Column(_title, _detail);
        heading.Spacing = 5;
        var status = ViewKit.Row(_microphone, _status);
        status.HorizontalAlignment = HorizontalAlignment.Right;
        var recordControl = ViewKit.Column(_record, _shortcut, status);
        recordControl.Spacing = 5;
        _shortcut.HorizontalAlignment = HorizontalAlignment.Right;
        var header = ViewKit.Split(heading, recordControl);
        header.Margin = new Thickness(0, 4, 0, 13);
        return header;
    }

    private Border CreateRecoveryBanner()
    {
        var copy = new Button { Content = "Copy text" };
        AutomationProperties.SetName(copy, "Copy waiting dictation");
        copy.Click += async (_, _) =>
        {
            if (_inlineRecovery is { } item) await CopyWaitingAsync(item);
        };
        var dismiss = ViewKit.Action("Dismiss", () =>
        {
            if (_inlineRecovery is { } item) DismissWaiting(item.Id);
        });
        var message = ViewKit.Column(ViewKit.Heading("Dictation ready to copy"),
            ViewKit.Note("Chit-Chat couldn’t insert this transcript into the original app."));
        message.Spacing = 4;
        return ViewKit.Card(ViewKit.Split(message, ViewKit.Row(copy, dismiss, _waitingCount)));
    }

    internal async Task CopyWaitingDictationAsync()
    {
        if (_session?.Recovery is { } item) await CopyWaitingAsync(item);
    }

    private async Task CopyWaitingAsync(WaitingDictation item)
    {
        if (await CopyAsync(item.Text)) DismissWaiting(item.Id);
    }

    private void DismissWaiting(Guid id)
    {
        _session?.DismissRecovery(id);
        RefreshStatus();
    }

    private async Task<bool> CopyAsync(string text)
    {
        if (Clipboard is not { } clipboard) return false;
        try
        {
            await clipboard.SetTextAsync(text);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void Navigate(WorkspacePage page)
    {
        if (page == WorkspacePage.Setup && _page != WorkspacePage.Setup) _previousPage = _page;
        if (page is >= WorkspacePage.Recording and <= WorkspacePage.Access) _settingsPage = page;
        _page = page;
        (_title.Text, _detail.Text) = (Pages[page].Title, Pages[page].Detail);
        foreach (var (destination, button) in _navigation) button.IsChecked = destination == page;

        if (page == WorkspacePage.Setup)
        {
            if (_content.Content is not Control { Name: "SetupWorkspacePage" })
            {
                var view = CreatePage(page);
                view.Name = "SetupWorkspacePage";
                _content.Content = view;
            }
        }
        else
        {
            if (!_views.TryGetValue(page, out var view))
            {
                view = CreatePage(page);
                _views.Add(page, view);
            }
            _content.Content = view;
        }
        SetState(_state);
    }

    private Control CreatePage(WorkspacePage page)
    {
        if (_session is null)
            return ViewKit.Empty(Pages[page].Title, page switch
            {
                WorkspacePage.History => "Your transcriptions will appear here.",
                WorkspacePage.Dictionary => "Add preferred spellings for words and names you use.",
                _ => "Desktop services are unavailable.",
            });

        return page switch
        {
            WorkspacePage.History => new TranscriptionsView(_session.Transcripts, () => _session.Settings.Data.LearnFromEdits),
            WorkspacePage.Dictionary => new DictionaryView(_session.Dictionary),
            WorkspacePage.Recording => ViewKit.Scroll(WorkspacePreferences.Recording(_session)),
            WorkspacePage.Speech => ViewKit.Scroll(_session.CreateSpeechSettingsView()),
            WorkspacePage.Text => ViewKit.Scroll(WorkspacePreferences.Text(_session, () => Navigate(WorkspacePage.OpenAI))),
            WorkspacePage.OpenAI => ViewKit.Scroll(_session.CreateOpenAiSettingsView()),
            WorkspacePage.Access => ViewKit.Scroll(WorkspacePreferences.Access(_session, OpenSetup)),
            WorkspacePage.Setup => ViewKit.Scroll(_session.CreateSetupCheckView()),
            _ => throw new ArgumentOutOfRangeException(nameof(page)),
        };
    }

    private void RefreshStatus()
    {
        var engine = _session?.Engine;
        if (engine is not null && _session is not null)
        {
            var position = _session.Settings.Data;
            _recordingOverlay?.Update(engine.State, engine.Level, position.IndicatorPosition);
            var suggestion = engine.State == DictationState.Idle ? _session.CorrectionSuggestion : null;
            _correctionPopup?.Update(suggestion, position.CorrectionPopupPosition);
            _recoveryPopup?.Update(_session.Recovery, _session.RecoveryCount);
        }
        if (!IsVisible || WindowState == WindowState.Minimized) return;
        SetState(engine?.State ?? _state);
        if (engine is not null) SetLevel(engine.Level);
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.OemComma && e.KeyModifiers == KeyModifiers.Control)
        {
            OpenSettings();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        _clock.Stop();
        _recordingOverlay?.Close();
        _correctionPopup?.Close();
        _recoveryPopup?.Close();
        base.OnClosed(e);
    }
}
