using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using ChitChat.App.Views;
using ChitChat.Core;

namespace ChitChat.App;

/// <summary>Manages the workspace and notification-area entry point.</summary>
public partial class App : Application
{
    private bool _exiting;

    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var session = Composition.Create();
            var workspace = new MainWindow(session);
            desktop.MainWindow = workspace;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            workspace.Closing += (_, args) =>
            {
                if (_exiting) return;

                args.Cancel = true;
                workspace.HideToTray();
            };
            desktop.ShutdownRequested += (_, _) => _exiting = true;
            desktop.Exit += (_, _) =>
            {
                _exiting = true;
                session.DisposeAsync().AsTask().GetAwaiter().GetResult();
            };

            TrayIcon.SetIcons(this, new TrayIcons { MakeTray(desktop, workspace, session) });
            session.Updates.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private TrayIcon MakeTray(IClassicDesktopStyleApplicationLifetime desktop, MainWindow workspace, Composition session)
    {
        var menu = new NativeMenu();
        menu.Items.Add(Item("Open Chit-Chat", ShowWorkspace));
        var copy = new NativeMenuItem("Copy last chat");
        copy.Click += async (_, _) => await workspace.CopyLastTranscriptAsync();
        menu.Items.Add(copy);
        var waiting = new NativeMenuItem("Copy waiting dictation");
        waiting.Click += async (_, _) => await workspace.CopyWaitingDictationAsync();
        menu.Items.Add(waiting);
        menu.Items.Add(new NativeMenuItemSeparator());
        var shortcut = new NativeMenuItem { IsEnabled = false };
        menu.Items.Add(shortcut);
        menu.Items.Add(Item("Settings…", () =>
        {
            ShowWorkspace();
            workspace.OpenSettings();
        }));
        menu.Items.Add(Item("Check my setup…", () =>
        {
            ShowWorkspace();
            workspace.OpenSetup();
        }));
        menu.Items.Add(new NativeMenuItemSeparator());
        var speech = new NativeMenu();
        var parakeet = Toggle("Parakeet", data => data with { UseStreamingSpeech = false });
        var streaming = Toggle("Live speech", data => data with { UseStreamingSpeech = true });
        speech.Items.Add(parakeet);
        speech.Items.Add(streaming);
        menu.Items.Add(new NativeMenuItem("Speech engine") { Menu = speech });
        var cleanup = Toggle("Clean up text", data => data with { CleanupEnabled = !data.CleanupEnabled });
        var smart = Toggle("Smart cleanup (on-device AI)", data =>
            data with { LocalAi = data.LocalAi with { Enabled = !data.LocalAi.Enabled } });
        var sounds = Toggle("Recording sounds", data => data with { PlayRecordingSounds = !data.PlayRecordingSounds });
        var mute = Toggle("Mute audio while recording", data => data with { MuteAudioWhileRecording = !data.MuteAudioWhileRecording });
        var openAi = Toggle("OpenAI enhancement", data => data with { OpenAi = data.OpenAi with { Enabled = !data.OpenAi.Enabled } });
        var openAiStatus = new NativeMenuItem { IsEnabled = false };
        menu.Items.Add(cleanup);
        menu.Items.Add(smart);
        menu.Items.Add(sounds);
        menu.Items.Add(mute);
        menu.Items.Add(openAi);
        menu.Items.Add(openAiStatus);
        var update = new NativeMenuItem("View Chit-Chat update");
        update.Click += async (_, _) =>
        {
            if (session.Updates.Available is { } version) await workspace.OpenReleaseAsync(version);
        };
        menu.Items.Add(update);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(Item("Quit Chit-Chat", () =>
        {
            _exiting = true;
            desktop.Shutdown();
        }));

        menu.NeedsUpdate += (_, _) => Refresh();
        session.Settings.Changed += (_, _) => Refresh();
        session.DictationAvailabilityChanged += (_, _) => Refresh();
        session.Updates.Changed += (_, _) => Refresh();
        Refresh();

        using var image = AssetLoader.Open(new Uri("avares://ChitChat.App/Assets/tray.ico"));
        return new TrayIcon
        {
            Icon = new WindowIcon(image), Menu = menu, IsVisible = true, ToolTipText = "Chit-Chat",
        };

        void ShowWorkspace()
        {
            workspace.Show();
            workspace.WindowState = WindowState.Normal;
            workspace.Activate();
        }

        NativeMenuItem Toggle(string title, Func<SettingsData, SettingsData> change)
        {
            var item = new NativeMenuItem(title) { ToggleType = NativeMenuItemToggleType.CheckBox };
            item.Click += (_, _) =>
            {
                try { session.Settings.Update(change(session.Settings.Data)); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    // Window preferences and the tray continue to show the last saved value.
                }
            };
            return item;
        }

        void Refresh()
        {
            var data = session.Settings.Data;
            copy.IsEnabled = session.LatestTranscript is not null;
            waiting.IsVisible = session.RecoveryCount > 0;
            waiting.Header = $"Copy waiting dictation ({session.RecoveryCount})";
            shortcut.Header = WorkspacePreferences.DescribeShortcut(data.PushToTalkKey, data.ShortcutModifiers) +
                (data.TapToToggle ? " · Tap or hold" : " · Hold to dictate");
            parakeet.IsChecked = !data.UseStreamingSpeech;
            streaming.IsChecked = data.UseStreamingSpeech;
            cleanup.IsChecked = data.CleanupEnabled;
            smart.IsChecked = data.LocalAi.Enabled;
            sounds.IsChecked = data.PlayRecordingSounds;
            mute.IsChecked = data.MuteAudioWhileRecording;
            openAi.IsChecked = data.OpenAi.Enabled;
            openAiStatus.Header = session.OpenAiStatus;
            openAiStatus.IsVisible = !string.IsNullOrWhiteSpace(session.OpenAiStatus);
            update.IsVisible = session.Updates.Available is not null;
        }
    }

    private static NativeMenuItem Item(string title, Action run)
    {
        var item = new NativeMenuItem(title);
        item.Click += (_, _) => run();
        return item;
    }
}
