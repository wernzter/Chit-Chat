using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ChitChat.Core;

namespace ChitChat.App.Views;

/// <summary>Windows settings for optional OpenAI transcript cleanup.</summary>
public sealed class OpenAiSettingsView : UserControl
{
    private const string ConnectionPending = "Connection not tested.";
    private readonly AppSettings _settings;
    private readonly OpenAiEnhancement _enhancement;
    private readonly TextBox _key = new()
    {
        Watermark = "Enter or replace your API key", PasswordChar = '●', IsUndoEnabled = false,
    };
    private readonly TextBox _customModel = new() { Watermark = "Enter a custom model ID" };
    private readonly NumericUpDown _minimum = new()
    {
        Minimum = 0, Maximum = int.MaxValue, ClipValueToMinMax = true, Increment = 1, FormatString = "0",
        HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 104,
    };
    private readonly ComboBox _sort = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _model = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _effort = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _prompt = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 126 };
    private readonly SelectableTextBlock _preview = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly TextBlock _status = ViewKit.Note("");
    private readonly TextBlock _lastDictation = ViewKit.Note("");
    private readonly TextBlock _connection = ViewKit.Note(ConnectionPending);
    private readonly TextBlock _effortHelp = ViewKit.Note("");
    private readonly ToggleSwitch _enabled;
    private readonly Button _saveKey = new() { Content = "Save key" };
    private readonly Button _removeKey = new() { Content = "Remove saved key" };
    private readonly Button _testConnection = new() { Content = "Test connection" };
    private readonly Button _refresh = new() { Content = "Refresh models" };
    private readonly Button _useCustomModel = new() { Content = "Use model" };
    private readonly Button _testSpeed = new() { Content = "Measure selected model’s speed" };
    private readonly Button _testPrompt = new() { Content = "Test my prompt" };
    private readonly DispatcherTimer _savePrompt = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private CancellationTokenSource? _request;
    private bool _synchronizing;

    /// <summary>Creates the preferences controls for the current desktop session.</summary>
    /// <param name="settings">Application preference store.</param>
    /// <param name="enhancement">Service that accesses Windows credentials and OpenAI.</param>
    public OpenAiSettingsView(AppSettings settings, OpenAiEnhancement enhancement)
    {
        _settings = settings;
        _enhancement = enhancement;
        var preferences = _settings.Data.OpenAi;
        _enabled = ViewKit.Toggle("Enhance dictation with OpenAI", preferences.Enabled,
            value => { if (!_synchronizing) Update(current => current with { Enabled = value }); });
        _prompt.Text = preferences.Prompt;
        _minimum.Value = Math.Max(0, preferences.MinimumWords);

        _sort.ItemsSource = new Option<OpenAiModelSort>[]
        {
            new(OpenAiModelSort.Name, "Name"), new(OpenAiModelSort.Speed, "Fastest tested"),
        };
        _sort.SelectedIndex = preferences.Sort == OpenAiModelSort.Speed ? 1 : 0;

        var keyLink = new HyperlinkButton
        {
            Content = "Get an API key", NavigateUri = new Uri("https://platform.openai.com/api-keys"),
        };
        var resetPrompt = ViewKit.Action("Restore default prompt", () =>
        {
            _prompt.Text = OpenAiSettings.DefaultPrompt;
            CommitPrompt();
        });
        _status.IsVisible = false;
        _lastDictation.IsVisible = false;

        Content = ViewKit.Column(
            ViewKit.Section("AI enhancement", _enabled,
                ViewKit.Note("When enabled, Chit-Chat sends the transcript and your prompt to OpenAI after local cleanup. Audio stays on your PC. API usage is billed to your OpenAI account."),
                ViewKit.Field("Minimum words for AI", _minimum),
                ViewKit.Note("The word count uses the original transcript. Set 0 to remove the minimum. If enhancement fails, Chit-Chat uses locally processed text. Dictionary corrections run last."),
                _lastDictation),
            ViewKit.Section("Connection", ViewKit.Field("OpenAI API key", _key),
                Actions(_saveKey, _removeKey, keyLink), _testConnection, _connection, _status,
                ViewKit.Note("Tests send a short sample and use paid API tokens. The key is stored in Windows Credential Manager for this account. A ChatGPT subscription does not include API credit.")),
            ViewKit.Section("Model & speed", ViewKit.Field("Sort by", _sort),
                ViewKit.Split(ViewKit.Field("Model", _model), _refresh),
                ViewKit.Split(ViewKit.Field("Custom model ID", _customModel), _useCustomModel),
                ViewKit.Field("Reasoning effort", _effort), _effortHelp, _testSpeed,
                ViewKit.Note("Speed tests use the same sample and default prompt. Measurements include network time and are saved separately for each model and effort.")),
            ViewKit.Section("Enhancement prompt", ViewKit.Field("Instructions", _prompt),
                ViewKit.Note("Describe how to edit the transcript. Chit-Chat sends it separately. Changes are saved automatically."),
                Actions(resetPrompt, _testPrompt), ViewKit.Note($"Sample: “{OpenAiEnhancement.Sample}”"), _preview));
        AutomationProperties.SetName(_preview, "Sample result");

        WireInputs();
        WireActions();
        RebuildPicker();
        UpdateActions();
        AttachedToVisualTree += (_, _) =>
        {
            _enhancement.DictationStatusChanged += OnDictationStatusChanged;
            _settings.Changed += RefreshSharedSettings;
            RefreshSharedSettings(null, EventArgs.Empty);
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _enhancement.DictationStatusChanged -= OnDictationStatusChanged;
            _settings.Changed -= RefreshSharedSettings;
            _request?.Cancel();
            _key.Clear();
            CommitPrompt();
        };
    }

    private static WrapPanel Actions(params Control[] controls)
    {
        var panel = new WrapPanel { ItemSpacing = ViewKit.Gap, LineSpacing = ViewKit.Gap };
        panel.Children.AddRange(controls);
        return panel;
    }

    private void WireInputs()
    {
        _key.TextChanged += (_, _) =>
        {
            _testConnection.Content = string.IsNullOrEmpty(_key.Text) ? "Test connection" : "Save & test connection";
            UpdateActions();
        };
        _customModel.TextChanged += (_, _) => UpdateActions();
        _minimum.ValueChanged += (_, _) =>
        {
            if (!_synchronizing && _minimum.Value is { } minimum)
                Update(current => current with { MinimumWords = (int)minimum });
        };
        _sort.SelectionChanged += (_, _) =>
        {
            if (_synchronizing || _sort.SelectedItem is not Option<OpenAiModelSort> choice) return;
            if (Update(current => current with { Sort = choice.Value })) RebuildPicker();
        };
        _model.SelectionChanged += (_, _) =>
        {
            if (!_synchronizing && _model.SelectedItem is Option<string> choice) UseModel(choice.Value);
        };
        _effort.SelectionChanged += (_, _) =>
        {
            if (_synchronizing || _effort.SelectedItem is not Option<OpenAiReasoningEffort> choice) return;
            if (Update(current => current.WithEffort(choice.Value)))
            {
                _connection.Text = ConnectionPending;
                RebuildPicker();
            }
        };
        _prompt.TextChanged += (_, _) =>
        {
            _savePrompt.Stop();
            _savePrompt.Start();
            UpdateActions();
        };
        _savePrompt.Tick += (_, _) => CommitPrompt();
    }

    private void WireActions()
    {
        _saveKey.Click += (_, _) => SavePendingKey();
        _removeKey.Click += (_, _) =>
        {
            if (_enhancement.RemoveKey(out var message))
            {
                _key.Clear();
                _connection.Text = ConnectionPending;
                if (!Update(current => current with { Enabled = false })) return;
                _synchronizing = true;
                _enabled.IsChecked = false;
                _synchronizing = false;
            }
            ShowStatus(message);
        };
        _testConnection.Click += async (_, _) =>
        {
            if (!string.IsNullOrEmpty(_key.Text) && !SavePendingKey()) return;
            _connection.Text = "Testing your key and selected model…";
            await RunAsync(async token =>
            {
                try
                {
                    _connection.Text = await _enhancement.TestConnectionAsync(_settings.Data.OpenAi, token);
                }
                catch (OpenAiRequestException error)
                {
                    _connection.Text = $"Connection test failed. {error.Message}";
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    _connection.Text = ConnectionPending;
                }
            });
        };
        _refresh.Click += async (_, _) => await RunAsync(async token =>
        {
            var models = await _enhancement.RefreshModelsAsync(token);
            if (!Update(current => current with { Models = models.ToArray() })) return;
            RebuildPicker();
            ShowStatus($"Found {models.Count} text-model candidates. Test the selected model to confirm compatibility.");
        });
        _useCustomModel.Click += (_, _) =>
        {
            var model = _customModel.Text?.Trim();
            if (!string.IsNullOrEmpty(model) && UseModel(model)) _customModel.Clear();
        };
        _testSpeed.Click += async (_, _) => await RunSampleAsync(speedOnly: true);
        _testPrompt.Click += async (_, _) =>
        {
            if (CommitPrompt()) await RunSampleAsync(speedOnly: false);
        };
    }

    private bool SavePendingKey()
    {
        if (_enhancement.SaveKey(_key.Text ?? "", out var message))
        {
            _key.Clear();
            _connection.Text = ConnectionPending;
            ShowStatus(message);
            return true;
        }
        ShowStatus(message);
        return false;
    }

    private bool UseModel(string model)
    {
        if (!Update(current => current with { Model = model })) return false;
        _connection.Text = ConnectionPending;
        RebuildPicker();
        return true;
    }

    private bool CommitPrompt()
    {
        _savePrompt.Stop();
        return Update(current => current with { Prompt = _prompt.Text ?? "" });
    }

    private bool Update(Func<OpenAiSettings, OpenAiSettings> change)
    {
        var current = _settings.Data;
        var update = change(current.OpenAi);
        if (update == current.OpenAi) return true;
        try
        {
            _settings.Update(current with { OpenAi = update });
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ShowStatus("Chit-Chat could not save these preferences.");
            return false;
        }
    }

    private async Task RunSampleAsync(bool speedOnly)
    {
        _preview.Text = null;
        _preview.IsVisible = false;
        await RunAsync(async token =>
        {
            var result = await _enhancement.TestModelAsync(_settings.Data.OpenAi, speedOnly, token);
            if (speedOnly)
            {
                if (!Update(current => current.WithTiming(result.Model, result.Effort, result.Elapsed))) return;
                RebuildPicker();
            }
            _preview.Text = result.Text;
            _preview.IsVisible = true;
            var seconds = result.Elapsed.TotalSeconds.ToString("0.00", CultureInfo.CurrentCulture);
            ShowStatus(speedOnly ? $"Completed in {seconds} seconds. Speed ranking updated."
                                 : $"Completed in {seconds} seconds using your prompt.");
        });
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (_request is not null) return;
        using var request = new CancellationTokenSource();
        _request = request;
        ShowStatus("Contacting OpenAI…");
        UpdateActions();
        try
        {
            await action(request.Token);
        }
        catch (OpenAiRequestException error)
        {
            ShowStatus(error.Message);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
        }
        finally
        {
            if (string.Equals(_status.Text, "Contacting OpenAI…", StringComparison.Ordinal)) ShowStatus("");
            _request = null;
            UpdateActions();
        }
    }

    private void ShowStatus(string message)
    {
        _status.Text = message;
        _status.IsVisible = message.Length > 0;
    }

    private void OnDictationStatusChanged(object? sender, EventArgs args) => Dispatcher.UIThread.Post(ShowLastDictation);

    private void RefreshSharedSettings(object? sender, EventArgs args)
    {
        _synchronizing = true;
        _enabled.IsChecked = _settings.Data.OpenAi.Enabled;
        _minimum.Value = Math.Max(0, _settings.Data.OpenAi.MinimumWords);
        _synchronizing = false;
        ShowLastDictation();
    }

    private void ShowLastDictation()
    {
        _lastDictation.Text = _enhancement.LastDictationStatus;
        _lastDictation.IsVisible = _settings.Data.OpenAi.Enabled && !string.IsNullOrEmpty(_lastDictation.Text);
    }

    private void RebuildPicker()
    {
        var preferences = _settings.Data.OpenAi;
        _synchronizing = true;
        try
        {
            var models = preferences.SortedModels().Select(name =>
            {
                var label = preferences.SecondsFor(name) is { } seconds
                    ? string.Create(CultureInfo.CurrentCulture,
                        $"{name} · {seconds:0.00} s · {OpenAiReasoning.Label(preferences.EffortFor(name))}")
                    : $"{name} · untested";
                return new Option<string>(name, label);
            }).ToArray();
            _model.ItemsSource = models;
            _model.SelectedItem = models.FirstOrDefault(model => model.Value == preferences.Model);

            var efforts = OpenAiReasoning.Options(preferences.Model)
                .Select(effort => new Option<OpenAiReasoningEffort>(effort, OpenAiReasoning.Label(effort))).ToArray();
            _effort.ItemsSource = efforts;
            _effort.SelectedItem = efforts.Single(effort => effort.Value == preferences.EffortFor(preferences.Model));
            _effortHelp.Text = efforts.Length == 1
                ? "This model uses its API default reasoning setting."
                : efforts.Any(effort => effort.Value == OpenAiReasoningEffort.Off)
                    ? "Choose Off to disable reasoning. Higher effort can take longer and use more API tokens."
                    : "This model requires reasoning. Higher effort can take longer and use more API tokens.";
        }
        finally
        {
            _synchronizing = false;
            UpdateActions();
        }
    }

    private void UpdateActions()
    {
        var idle = _request is null;
        var hasStore = _enhancement.HasCredentialStore;
        _enabled.IsEnabled = hasStore;
        _key.IsEnabled = idle && hasStore;
        _saveKey.IsEnabled = idle && hasStore && !string.IsNullOrEmpty(_key.Text);
        _removeKey.IsEnabled = idle && hasStore;
        _testConnection.IsEnabled = _refresh.IsEnabled = _testSpeed.IsEnabled = idle && hasStore;
        _testPrompt.IsEnabled = idle && hasStore && !string.IsNullOrWhiteSpace(_prompt.Text);
        _useCustomModel.IsEnabled = idle && !string.IsNullOrWhiteSpace(_customModel.Text);
        _customModel.IsEnabled = _sort.IsEnabled = _model.IsEnabled = idle;
        _effort.IsEnabled = idle && _effort.ItemCount > 1;
    }

    private sealed record Option<T>(T Value, string Label)
    {
        public override string ToString() => Label;
    }
}
