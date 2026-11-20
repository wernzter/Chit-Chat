using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ChitChat.Abstractions;
using ChitChat.Core;

namespace ChitChat.App.Views;

internal static class WorkspacePreferences
{
    internal static string DescribeShortcut(int virtualKey, HotkeyModifiers modifiers)
    {
        var primary = virtualKey switch
        {
            >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),
            >= 0x70 and <= 0x87 => $"F{virtualKey - 0x6F}",
            >= 0x60 and <= 0x69 => $"Num {virtualKey - 0x60}",
            0x08 => "Backspace", 0x09 => "Tab", 0x0D => "Enter", 0x13 => "Pause",
            0x14 => "Caps Lock", 0x1B => "Esc", 0x20 => "Space", 0x21 => "Page Up",
            0x22 => "Page Down", 0x23 => "End", 0x24 => "Home", 0x25 => "Left",
            0x26 => "Up", 0x27 => "Right", 0x28 => "Down", 0x2C => "Print Screen",
            0x2D => "Insert", 0x2E => "Delete", 0x90 => "Num Lock", 0x91 => "Scroll Lock",
            0xA0 => "Left Shift", 0xA1 => "Right Shift", 0xA2 => "Left Ctrl",
            0xA3 => "Right Ctrl", 0xA4 => "Left Alt", 0xA5 => "Right Alt",
            0xBA => ";", 0xBB => "=", 0xBC => ",", 0xBD => "-", 0xBE => ".",
            0xBF => "/", 0xC0 => "`", 0xDB => "[", 0xDC => "Backslash", 0xDD => "]",
            0xDE => "Apostrophe", 0xE2 => "Extra punctuation key",
            _ => $"Key 0x{virtualKey:X2}",
        };
        var labels = new List<string>(4);
        if ((modifiers & HotkeyModifiers.Control) != 0) labels.Add("Ctrl");
        if ((modifiers & HotkeyModifiers.Shift) != 0) labels.Add("Shift");
        if ((modifiers & HotkeyModifiers.Alt) != 0) labels.Add("Alt");
        labels.Add(primary);
        return string.Join(" + ", labels);
    }

    internal static Control Recording(Composition session)
    {
        var settings = session.Settings;
        var watchSeconds = new NumericUpDown
        {
            Minimum = 15, Maximum = 600, Increment = 1, FormatString = "0",
            Value = Math.Clamp(settings.Data.EditWatchSeconds, 15, 600),
        };
        watchSeconds.ValueChanged += (_, _) =>
        {
            if (watchSeconds.Value is { } seconds)
                settings.Update(settings.Data with { EditWatchSeconds = (int)seconds });
        };
        return ViewKit.Column(
            ViewKit.Section("Recording shortcut",
                new ShortcutEditor(session),
                ViewKit.Rule(),
                SavedToggle("Tap to start and stop", settings, data => data.TapToToggle,
                    (data, value) => data with { TapToToggle = value }),
                ViewKit.Note("Hold the shortcut to record and release to finish. With tap-to-toggle enabled, a quick tap starts continuous recording.")),
            ViewKit.Section("On-screen indicators",
                Placement("Listening indicator", settings.Data.IndicatorPosition,
                    position => settings.Update(settings.Data with { IndicatorPosition = position })),
                Placement("Correction popup", settings.Data.CorrectionPopupPosition,
                    position => settings.Update(settings.Data with { CorrectionPopupPosition = position }))),
            ViewKit.Section("Sound",
                SavedToggle("Play recording sounds", settings, data => data.PlayRecordingSounds,
                    (data, value) => data with { PlayRecordingSounds = value }),
                SavedToggle("Mute audio while recording", settings, data => data.MuteAudioWhileRecording,
                    (data, value) => data with { MuteAudioWhileRecording = value }),
                ViewKit.Note("Chit-Chat restores system audio when you stop or cancel.")),
            ViewKit.Section("After recording",
                SavedToggle("Type into the focused app", settings, data => data.InjectText,
                    (data, value) => data with { InjectText = value })),
            ViewKit.Section("Learn from edits",
                SavedToggle("Learn word corrections", settings, data => data.LearnFromEdits,
                    (data, value) => data with { LearnFromEdits = value }),
                ViewKit.Field("Watch for edits (seconds)", watchSeconds),
                ViewKit.Note("Chit-Chat watches supported text fields for changes after insertion. You can also teach a correction by editing a transcript in History.")));
    }

    private static ToggleSwitch SavedToggle(string name, AppSettings settings, Func<SettingsData, bool> read,
                                            Func<SettingsData, bool, SettingsData> change)
    {
        var toggle = new ToggleSwitch { OnContent = name, OffContent = name };
        Avalonia.Automation.AutomationProperties.SetName(toggle, name);

        void Refresh(object? _, EventArgs args) => toggle.IsChecked = read(settings.Data);
        Refresh(null, EventArgs.Empty);
        toggle.AttachedToVisualTree += (_, _) =>
        {
            settings.Changed += Refresh;
            Refresh(null, EventArgs.Empty);
        };
        toggle.DetachedFromVisualTree += (_, _) => settings.Changed -= Refresh;
        toggle.IsCheckedChanged += (_, _) =>
        {
            var state = toggle.IsChecked == true;
            var data = settings.Data;
            if (state == read(data)) return;
            try { settings.Update(change(data, state)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Refresh(null, EventArgs.Empty); }
        };
        return toggle;
    }

    private static Control Placement(string name, RecordingIndicatorPosition current,
                                     Action<RecordingIndicatorPosition> apply)
    {
        var selection = new ComboBox
        {
            ItemsSource = new[] { "Top of screen", "Bottom of screen", "Hidden" },
            SelectedIndex = current switch
            {
                RecordingIndicatorPosition.Top => 0,
                RecordingIndicatorPosition.Hidden => 2,
                _ => 1,
            },
        };
        selection.SelectionChanged += (_, _) => apply(selection.SelectedIndex switch
        {
            0 => RecordingIndicatorPosition.Top,
            2 => RecordingIndicatorPosition.Hidden,
            _ => RecordingIndicatorPosition.Bottom,
        });
        return ViewKit.Field(name, selection);
    }

    internal static Control Text(Composition session, Action openCloud)
    {
        var settings = session.Settings;
        var minimum = new NumericUpDown
        {
            Minimum = 0, Maximum = int.MaxValue, ClipValueToMinMax = true, Increment = 1, FormatString = "0",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, MinWidth = 104,
        };
        void RefreshMinimum(object? _, EventArgs args) => minimum.Value = Math.Max(0, settings.Data.OpenAi.MinimumWords);
        RefreshMinimum(null, EventArgs.Empty);
        minimum.AttachedToVisualTree += (_, _) =>
        {
            settings.Changed += RefreshMinimum;
            RefreshMinimum(null, EventArgs.Empty);
        };
        minimum.DetachedFromVisualTree += (_, _) => settings.Changed -= RefreshMinimum;
        minimum.ValueChanged += (_, _) =>
        {
            var current = settings.Data;
            if (minimum.Value is not decimal value || value == current.OpenAi.MinimumWords) return;
            try { settings.Update(current with { OpenAi = current.OpenAi with { MinimumWords = (int)value } }); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                RefreshMinimum(null, EventArgs.Empty);
            }
        };
        return ViewKit.Column(
            ViewKit.Section("Short dictations", ViewKit.Field("Minimum words for AI", minimum),
                ViewKit.Note("Shorter dictations skip on-device smart cleanup and OpenAI. Set 0 to use AI at any length.")),
            ViewKit.Section("Transcript cleanup",
            SavedToggle("Clean up text", settings, data => data.CleanupEnabled,
                (data, value) => data with { CleanupEnabled = value }),
            ViewKit.Note("Remove filler sounds and tidy spacing and punctuation before inserting your text.")),
            session.CreateLocalAiSettingsView(),
            ViewKit.Section("AI enhancement",
                ViewKit.Note("OpenAI runs after local cleanup. Dictionary corrections apply last."),
                ViewKit.Action("Open OpenAI settings", openCloud)));
    }

    internal static Control Access(Composition session, Action openSetup, Func<Version, Task> openRelease)
    {
        var settings = session.Settings;
        var feedback = ViewKit.Note(string.Empty);
        feedback.IsVisible = false;
        var dataFolder = Path.GetDirectoryName(AppSettings.DefaultPath)!;

        void Open(string location)
        {
            try
            {
                if (string.Equals(location, dataFolder, StringComparison.Ordinal))
                    Directory.CreateDirectory(dataFolder);
                else if (string.Equals(location, session.Dictionary.FilePath, StringComparison.Ordinal) && !File.Exists(location))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(location)!);
                    using var file = File.Open(location, FileMode.OpenOrCreate);
                }
                using var process = Process.Start(new ProcessStartInfo(location) { UseShellExecute = true });
                feedback.IsVisible = false;
            }
            catch (Exception exc) when (exc is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                feedback.Text = "Windows couldn’t open that location.";
                feedback.IsVisible = true;
            }
        }

        return ViewKit.Column(
            ViewKit.Section("Check my setup",
                ViewKit.Note("Verify the shortcut, microphone signal, and insertion in a test field."),
                ViewKit.Primary("Check my setup", openSetup)),
            ViewKit.Section("Windows access",
                ViewKit.Heading("Microphone"),
                ViewKit.Note("Windows must allow desktop apps to use the microphone."),
                ViewKit.Action("Open microphone privacy settings", () => Open("ms-settings:privacy-microphone")),
                ViewKit.Rule(),
                ViewKit.Heading("Shortcut and insertion"),
                ViewKit.Note(session.IsPlatformAvailable
                    ? "Windows support is loaded. Use the setup check to verify the shortcut and insertion."
                    : "The Windows recording and insertion services are unavailable.")),
            ViewKit.Section("Your data",
                SavedToggle("Keep transcription history", settings, data => data.KeepHistory,
                    (data, value) => data with { KeepHistory = value }),
                ViewKit.Note("History and dictionary entries are saved locally for this Windows account."),
                ViewKit.Row(ViewKit.Action("Open data folder", () => Open(dataFolder)),
                            ViewKit.Action("Open dictionary file", () => Open(session.Dictionary.FilePath))),
                feedback),
            UpdateSettings(session.Updates, openRelease));
    }

    private static Border UpdateSettings(ReleaseUpdates updates, Func<Version, Task> openRelease)
    {
        var status = ViewKit.Note(string.Empty);
        var check = ViewKit.Action("Check for updates", () => _ = updates.CheckAsync());
        var view = new Button { Content = "View release" };
        view.Click += async (_, _) =>
        {
            if (updates.Available is { } available) await openRelease(available);
        };
        void Refresh(object? _, EventArgs args)
        {
            status.Text = updates.Status;
            status.IsVisible = !string.IsNullOrEmpty(updates.Status);
            check.IsEnabled = !updates.IsChecking;
            view.IsVisible = updates.Available is not null;
        }

        var card = ViewKit.Section("Updates",
            ViewKit.Note($"Installed version {ReleaseUpdates.Installed.ToString(3)}."),
            ViewKit.Row(check, view), status);
        card.AttachedToVisualTree += (_, _) =>
        {
            updates.Changed += Refresh;
            Refresh(null, EventArgs.Empty);
        };
        card.DetachedFromVisualTree += (_, _) => updates.Changed -= Refresh;
        Refresh(null, EventArgs.Empty);
        return card;
    }
}

internal sealed class ShortcutEditor : UserControl
{
    private readonly Composition _session;
    private readonly Button _change = new() { Content = "Change shortcut" };
    private readonly TextBlock _selected = ViewKit.Heading(string.Empty);
    private readonly TextBlock _hint = ViewKit.Note("Choose a key, with optional Ctrl, Shift, or Alt.");
    private Window? _window;
    private IDisposable? _lease;
    private (Key Key, int Code, HotkeyModifiers Modifiers)? _pending;

    internal ShortcutEditor(Composition session)
    {
        _session = session;
        _change.Name = "ChangeRecordingShortcut";
        _change.Click += (_, _) =>
        {
            if (_window is null) BeginCapture();
            else EndCapture();
        };
        Content = ViewKit.Column(ViewKit.Split(_selected, _change), _hint);
        DetachedFromVisualTree += (_, _) => EndCapture();
        ReadCurrent();
    }

    private void ReadCurrent()
    {
        var settings = _session.Settings.Data;
        _selected.Text = WorkspacePreferences.DescribeShortcut(settings.PushToTalkKey, settings.ShortcutModifiers);
    }

    private void BeginCapture()
    {
        if (TopLevel.GetTopLevel(this) is not Window window) return;
        _lease = _session.Engine?.TryBeginSetup(static _ => { });
        if (_session.Engine is not null && _lease is null)
        {
            _hint.Text = "Finish recording or close the setup check before changing your shortcut.";
            return;
        }

        _window = window;
        _window.AddHandler(KeyDownEvent, PreviewKey, RoutingStrategies.Tunnel);
        _window.AddHandler(KeyUpEvent, SaveKey, RoutingStrategies.Tunnel);
        _window.Deactivated += CancelOnDeactivate;
        _change.Content = "Cancel";
        _hint.Text = "Press the new shortcut, then release its main key to save it. Press Esc to cancel.";
    }

    private void PreviewKey(object? sender, KeyEventArgs args)
    {
        args.Handled = true;
        if (args.Key == Key.Escape && args.KeyModifiers == KeyModifiers.None)
        {
            EndCapture();
            return;
        }

        var code = VirtualKey(args.Key);
        if (code == 0 || (args.KeyModifiers & KeyModifiers.Meta) != 0)
        {
            _pending = null;
            ReadCurrent();
            _hint.Text = "Choose a supported key without the Windows modifier, or press Esc to cancel.";
            return;
        }
        var modifiers = HotkeyModifiers.None;
        if ((args.KeyModifiers & KeyModifiers.Control) != 0 && code is not (0xA2 or 0xA3))
            modifiers |= HotkeyModifiers.Control;
        if ((args.KeyModifiers & KeyModifiers.Shift) != 0 && code is not (0xA0 or 0xA1))
            modifiers |= HotkeyModifiers.Shift;
        if ((args.KeyModifiers & KeyModifiers.Alt) != 0 && code is not (0xA4 or 0xA5))
            modifiers |= HotkeyModifiers.Alt;

        _pending = (args.Key, code, modifiers);
        _selected.Text = WorkspacePreferences.DescribeShortcut(code, modifiers);
    }

    private void SaveKey(object? sender, KeyEventArgs args)
    {
        if (_pending is not { } candidate || candidate.Key != args.Key) return;
        args.Handled = true;
        EndCapture();
        try
        {
            if (_session.TryChangeShortcut(candidate.Code, candidate.Modifiers))
            {
                ReadCurrent();
                if (candidate.Code == 0xA5) _hint.Text = "Right Alt may act as AltGr on some keyboard layouts.";
                return;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Re-read the saved shortcut if the settings file could not be written.
        }
        ReadCurrent();
        _hint.Text = "Couldn’t change the shortcut. Try again.";
    }

    private void CancelOnDeactivate(object? sender, EventArgs e) => EndCapture();

    private void EndCapture()
    {
        if (_window is not { } window) return;
        _window = null;
        _pending = null;
        window.RemoveHandler(KeyDownEvent, PreviewKey);
        window.RemoveHandler(KeyUpEvent, SaveKey);
        window.Deactivated -= CancelOnDeactivate;
        _lease?.Dispose();
        _lease = null;
        _change.Content = "Change shortcut";
        _hint.Text = "Choose a key, with optional Ctrl, Shift, or Alt.";
        ReadCurrent();
    }

    private static int VirtualKey(Key key) => key switch
    {
        >= Key.A and <= Key.Z => 0x41 + (int)key - (int)Key.A,
        >= Key.D0 and <= Key.D9 => 0x30 + (int)key - (int)Key.D0,
        >= Key.NumPad0 and <= Key.NumPad9 => 0x60 + (int)key - (int)Key.NumPad0,
        >= Key.F1 and <= Key.F24 => 0x70 + (int)key - (int)Key.F1,
        Key.Back => 0x08, Key.Tab => 0x09, Key.Enter => 0x0D, Key.Pause => 0x13,
        Key.CapsLock => 0x14, Key.Escape => 0x1B, Key.Space => 0x20, Key.PageUp => 0x21,
        Key.PageDown => 0x22, Key.End => 0x23, Key.Home => 0x24, Key.Left => 0x25,
        Key.Up => 0x26, Key.Right => 0x27, Key.Down => 0x28, Key.PrintScreen => 0x2C,
        Key.Insert => 0x2D, Key.Delete => 0x2E, Key.NumLock => 0x90, Key.Scroll => 0x91,
        Key.LeftShift => 0xA0, Key.RightShift => 0xA1, Key.LeftCtrl => 0xA2, Key.RightCtrl => 0xA3,
        Key.LeftAlt => 0xA4, Key.RightAlt => 0xA5,
        Key.OemSemicolon => 0xBA, Key.OemPlus => 0xBB, Key.OemComma => 0xBC, Key.OemMinus => 0xBD,
        Key.OemPeriod => 0xBE, Key.OemQuestion => 0xBF, Key.OemTilde => 0xC0,
        Key.OemOpenBrackets => 0xDB, Key.OemPipe => 0xDC, Key.OemCloseBrackets => 0xDD,
        Key.OemQuotes => 0xDE, Key.OemBackslash => 0xE2,
        _ => 0,
    };
}
