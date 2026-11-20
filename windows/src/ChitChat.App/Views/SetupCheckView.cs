using System.ComponentModel;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using ChitChat.Abstractions;
using ChitChat.Core;

namespace ChitChat.App.Views;

internal sealed class SetupCheckView : UserControl
{
    private const string Sample = "Chit-Chat can insert text here.";
    private readonly DictationEngine? _engine;
    private readonly IPlatformServices? _platform;
    private readonly TextBlock _shortcut = ViewKit.Note("Start the check to test your recording shortcut.");
    private readonly TextBlock _microphone = ViewKit.Note("Microphone has not been tested.");
    private readonly TextBlock _insertion = ViewKit.Note("Text insertion has not been tested.");
    private readonly ProgressBar _meter = new() { Maximum = 1, Minimum = 0, Height = 8 };
    private readonly TextBox _sample = new() { Watermark = "Text appears here after the test", IsReadOnly = false };
    private readonly Button _start;
    private readonly Button _retry;
    private readonly Button _testMicrophone;
    private readonly Button _testInsertion;
    private IDisposable? _pause;
    private CancellationTokenSource? _session;
    private CancellationTokenSource? _shortcutTimeout;
    private bool _sawPress;

    internal SetupCheckView(DictationEngine? engine, IPlatformServices? platform)
    {
        _engine = engine;
        _platform = platform;
        AutomationProperties.SetName(_sample, "Text insertion test field");
        AutomationProperties.SetName(_meter, "Microphone signal");
        _start = ViewKit.Action("Start check", ToggleCheck);
        _retry = ViewKit.Action("Retry shortcut", WaitForShortcut);
        _testMicrophone = ViewKit.Action("Test microphone", () => _ = TestMicrophoneAsync());
        _testInsertion = ViewKit.Action("Test insertion here", () => _ = TestInsertionAsync());
        var privacy = ViewKit.Action("Open microphone privacy settings", OpenPrivacySettings);
        privacy.IsEnabled = platform is not null;
        Content = ViewKit.Column(
            ViewKit.Card(ViewKit.Column(ViewKit.Title("Recording shortcut"), _shortcut, ViewKit.Row(_start, _retry))),
            ViewKit.Card(ViewKit.Column(ViewKit.Title("Microphone"), _microphone, _meter,
                ViewKit.Column(_testMicrophone, privacy))),
            ViewKit.Card(ViewKit.Column(ViewKit.Title("Text insertion"), _insertion, _sample, _testInsertion)));
        DetachedFromVisualTree += (_, _) => Stop();
        SetActive(false);
    }

    private void ToggleCheck()
    {
        if (_session is not null)
        {
            Stop();
            return;
        }
        if (_engine is null || _platform is null)
        {
            _shortcut.Text = "Windows recordings are unavailable in this session.";
            return;
        }

        var session = new CancellationTokenSource();
        _pause = _engine.TryBeginSetup(down => Dispatcher.UIThread.Post(() => ObserveShortcut(session, down)));
        if (_pause is null)
        {
            session.Dispose();
            _shortcut.Text = "Finish the current recording or shortcut change before checking your setup.";
            return;
        }
        _session = session;
        _microphone.Text = "Microphone has not been tested.";
        _insertion.Text = "Text insertion has not been tested.";
        SetActive(true);
        WaitForShortcut();
    }

    private void WaitForShortcut()
    {
        if (_session is not { } active || _engine is null) return;
        CancelShortcutTimeout();
        _sawPress = false;
        if (!_engine.Start())
        {
            _shortcut.Text = "The global keyboard listener could not start. Retry the shortcut check.";
            return;
        }
        _shortcut.Text = "Press and release your recording shortcut within 15 seconds.";
        _shortcutTimeout = CancellationTokenSource.CreateLinkedTokenSource(active.Token);
        _ = ReportShortcutTimeoutAsync(_shortcutTimeout.Token);
    }

    private void ObserveShortcut(CancellationTokenSource session, bool down)
    {
        if (!ReferenceEquals(_session, session)) return;
        if (down)
        {
            _sawPress = true;
            _shortcut.Text = "Press received. Release the shortcut to finish the check.";
        }
        else if (_sawPress)
        {
            CancelShortcutTimeout();
            _sawPress = false;
            _shortcut.Text = "Passed. Windows reported both the press and the release.";
        }
    }

    private async Task ReportShortcutTimeoutAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
            _shortcut.Text = "No complete press received. Confirm the shortcut in Recording and try again.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task TestMicrophoneAsync()
    {
        if (_session is not { } session || _platform is not { } platform || !_testMicrophone.IsEnabled) return;
        _testMicrophone.IsEnabled = false;
        _microphone.Text = "Speak for five seconds. Audio is discarded after measuring the signal.";
        using var sample = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        sample.CancelAfter(TimeSpan.FromSeconds(5));
        var heard = false;
        try
        {
            await using var capture = platform.CreateCapture();
            try
            {
                await foreach (var block in capture.CaptureAsync(sample.Token))
                {
                    var level = block.Rms();
                    _meter.Value = Math.Clamp(level, 0, 1);
                    heard |= level > 0.035f;
                }
            }
            catch (OperationCanceledException) when (sample.IsCancellationRequested)
            {
            }
            if (ReferenceEquals(_session, session))
                _microphone.Text = heard ? "Passed. The microphone picked up an audible signal."
                    : "No audible signal detected. Check your input device and Windows microphone permissions.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            if (ReferenceEquals(_session, session))
                _microphone.Text = "The microphone could not be opened. Check Windows microphone privacy and sound settings.";
        }
        finally
        {
            if (ReferenceEquals(_session, session))
            {
                _meter.Value = 0;
                _testMicrophone.IsEnabled = true;
            }
        }
    }

    private async Task TestInsertionAsync()
    {
        if (_session is not { } session || _platform is not { } platform || !_testInsertion.IsEnabled) return;
        if (TopLevel.GetTopLevel(_sample) is not Window { IsActive: true } host
            || host.TryGetPlatformHandle() is not { Handle: not 0 } handle)
        {
            _insertion.Text = "Bring Chit-Chat to the foreground before testing insertion.";
            return;
        }
        _sample.Text = string.Empty;
        if (!_sample.Focus())
        {
            _insertion.Text = "The test field could not receive keyboard focus.";
            return;
        }
        _testInsertion.IsEnabled = false;
        var token = session.Token;
        try
        {
            var sent = await platform.CreateTestOutput(handle.Handle).InjectAsync(Sample, token);
            await Task.Delay(TimeSpan.FromMilliseconds(180), token);
            if (ReferenceEquals(_session, session))
                _insertion.Text = sent && _sample.Text == Sample ? "Passed. The sample appeared in this field."
                    : "Insertion could not be verified. Keep this window focused and try again.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_session, session)) _testInsertion.IsEnabled = true;
        }
    }

    private void OpenPrivacySettings()
    {
        try { _platform?.OpenMicrophoneSettings(); }
        catch (Win32Exception) { _microphone.Text = "Open Microphone privacy from Windows Settings."; }
    }

    private void CancelShortcutTimeout()
    {
        _shortcutTimeout?.Cancel();
        _shortcutTimeout?.Dispose();
        _shortcutTimeout = null;
    }

    private void Stop()
    {
        CancelShortcutTimeout();
        if (_session is { } session)
        {
            _session = null;
            session.Cancel();
            session.Dispose();
            _pause?.Dispose();
            _pause = null;
            _shortcut.Text = "Check finished. Recording is available again.";
        }
        SetActive(false);
    }

    private void SetActive(bool active)
    {
        _start.Content = active ? "Finish check" : "Start check";
        _start.IsEnabled = _platform is not null && _engine is not null;
        _retry.IsEnabled = active;
        _testMicrophone.IsEnabled = active;
        _testInsertion.IsEnabled = active;
        _meter.Value = 0;
    }
}
