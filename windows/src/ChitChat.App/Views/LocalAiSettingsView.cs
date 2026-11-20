using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using ChitChat.Core;

namespace ChitChat.App.Views;

internal sealed class LocalAiSettingsView : UserControl
{
    private static readonly Uri WindowsSetup = new("https://docs.ollama.com/windows");
    private static readonly Uri PrivateModeSetup = new("https://docs.ollama.com/faq#how-do-i-disable-ollama-cloud-features");
    private readonly AppSettings _settings;
    private readonly LocalAiEnhancement _enhancement;
    private readonly ToggleSwitch _enabled;
    private readonly ComboBox _models = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _refresh;
    private readonly Button _test;
    private readonly TextBlock _status = ViewKit.Note("Refresh to look for models installed on this PC.");
    private readonly TextBlock _dictationStatus = ViewKit.Caption(string.Empty);
    private CancellationTokenSource? _pending;
    private bool _synchronizing;

    internal LocalAiSettingsView(AppSettings settings, LocalAiEnhancement enhancement)
    {
        _settings = settings;
        _enhancement = enhancement;
        _enabled = ViewKit.Toggle("Use on-device smart cleanup", settings.Data.LocalAi.Enabled,
            enabled => Save(settings.Data.LocalAi with { Enabled = enabled }));
        _refresh = ViewKit.Action("Refresh models", () => _ = RunAsync(RefreshAsync));
        _test = ViewKit.Action("Try a sample", () => _ = RunAsync(TestAsync));
        _models.Name = "LocalAiModel";
        AutomationProperties.SetName(_models, "Installed local AI model");
        _models.SelectionChanged += (_, _) =>
        {
            if (_models.SelectedItem is string model) Save(settings.Data.LocalAi with { Model = model });
            UpdateControls();
        };
        ShowModels(string.IsNullOrEmpty(settings.Data.LocalAi.Model) ? [] : [settings.Data.LocalAi.Model]);

        var windowsHelp = ViewKit.Action("Install Ollama", () => _ = OpenAsync(WindowsSetup));
        var privacyHelp = ViewKit.Action("Local-only setup", () => _ = OpenAsync(PrivateModeSetup));
        _status.Name = "LocalAiStatus";
        Content = ViewKit.Section("On-device smart cleanup",
            ViewKit.Note("Ollama runs the selected text model on this PC. Chit-Chat uses basic cleanup if the model is unavailable."),
            _enabled,
            ViewKit.Field("Installed local AI model", _models),
            ViewKit.Row(_refresh, _test),
            _status,
            _dictationStatus,
            ViewKit.Rule(),
            ViewKit.Caption("Install and start Ollama, disable its cloud features, then run ollama pull qwen2.5:1.5b in Windows Terminal to install a starter model."),
            ViewKit.Row(windowsHelp, privacyHelp));
        UpdateControls();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _enhancement.DictationStatusChanged += DictationStatusChanged;
        _settings.Changed += RefreshSharedSettings;
        RefreshSharedSettings(null, EventArgs.Empty);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _enhancement.DictationStatusChanged -= DictationStatusChanged;
        _settings.Changed -= RefreshSharedSettings;
        _pending?.Cancel();
        base.OnDetachedFromVisualTree(e);
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (_pending is not null) return;
        using var source = new CancellationTokenSource();
        _pending = source;
        UpdateControls();
        try
        {
            await action(source.Token);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            _status.Text = "Refresh to look for models installed on this PC.";
        }
        finally
        {
            _pending = null;
            UpdateControls();
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        _status.Text = "Checking Ollama…";
        var result = await _enhancement.CheckAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ShowModels(result.Models);
        _status.Text = result.Status;
    }

    private async Task TestAsync(CancellationToken cancellationToken)
    {
        if (_models.SelectedItem is not string model) return;
        _status.Text = "Trying the selected model…";
        var result = await _enhancement.TestAsync(model, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _status.Text = result;
    }

    private async Task OpenAsync(Uri url)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;
        try
        {
            if (!await topLevel.Launcher.LaunchUriAsync(url))
                _status.Text = "Windows could not open the Ollama setup guide.";
        }
        catch (Exception error) when (error is InvalidOperationException or PlatformNotSupportedException)
        {
            _status.Text = "Windows could not open the Ollama setup guide.";
        }
    }

    private void ShowModels(IReadOnlyList<string> models)
    {
        _synchronizing = true;
        _models.ItemsSource = models;
        _models.SelectedItem = models.Contains(_settings.Data.LocalAi.Model, StringComparer.Ordinal)
            ? _settings.Data.LocalAi.Model : null;
        _synchronizing = false;
    }

    private void Save(LocalAiSettings preferences)
    {
        if (_synchronizing || preferences == _settings.Data.LocalAi) return;
        try
        {
            _settings.Update(_settings.Data with { LocalAi = preferences });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _synchronizing = true;
            _enabled.IsChecked = _settings.Data.LocalAi.Enabled;
            _models.SelectedItem = _settings.Data.LocalAi.Model;
            _synchronizing = false;
            _status.Text = "Chit-Chat could not save the local AI preference.";
        }
    }

    private void RefreshSharedSettings(object? sender, EventArgs args)
    {
        _synchronizing = true;
        _enabled.IsChecked = _settings.Data.LocalAi.Enabled;
        _synchronizing = false;
        UpdateDictationStatus();
    }

    private void UpdateControls()
    {
        var ready = _pending is null;
        _refresh.IsEnabled = ready;
        _models.IsEnabled = ready;
        _test.IsEnabled = ready && _models.SelectedItem is string;
    }

    private void DictationStatusChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(UpdateDictationStatus);

    private void UpdateDictationStatus()
    {
        _dictationStatus.Text = _enhancement.LastDictationStatus;
        _dictationStatus.IsVisible = _settings.Data.CleanupEnabled && _settings.Data.LocalAi.Enabled &&
            !string.IsNullOrEmpty(_dictationStatus.Text);
    }
}
