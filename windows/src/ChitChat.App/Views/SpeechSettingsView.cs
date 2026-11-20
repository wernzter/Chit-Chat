using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using ChitChat.Core;
using ChitChat.Speech;

namespace ChitChat.App.Views;

/// <summary>Sets up and selects the local speech model used for dictation.</summary>
public sealed class SpeechSettingsView : UserControl
{
    private readonly AppSettings _settings;
    private readonly SpeechModelInstaller _installer = new();
    private readonly ComboBox _variants = new()
    {
        ItemsSource = new[] { "Parakeet v2 for English", "Parakeet v3 for multiple languages", "Zipformer streaming for English" },
        SelectedIndex = 0,
    };
    private readonly ToggleSwitch _streaming;
    private readonly TextBox _path = new() { Watermark = "Automatic model location" };
    private readonly TextBlock _modelStatus = ViewKit.Note(string.Empty);
    private readonly TextBlock _downloadStatus = ViewKit.Note(string.Empty);
    private readonly TextBlock _downloadSize = ViewKit.Note(string.Empty);
    private readonly ProgressBar _meter = new() { Minimum = 0, Maximum = 100, IsVisible = false };
    private readonly Button _download = new() { Content = "Download model" };
    private readonly Button _cancel = new() { Content = "Cancel download", IsVisible = false };
    private readonly Button _browse = new() { Content = "Browse…" };
    private readonly Button _useFolder = new() { Content = "Use this folder" };
    private readonly Button _automatic = new() { Content = "Use automatic location" };
    private CancellationTokenSource? _downloadCancellation;
    private Window? _owner;
    private bool _synchronizing;

    /// <summary>Creates the speech settings for the current desktop session.</summary>
    /// <param name="settings">The user's persistent preferences.</param>
    public SpeechSettingsView(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _streaming = ViewKit.Toggle("Live English transcription", settings.Data.UseStreamingSpeech, ChangeSpeechEngine);
        _path.Text = settings.Data.ModelDirectory;
        if (settings.Data.UseStreamingSpeech) _variants.SelectedIndex = 2;
        AutomationProperties.SetName(_variants, "Speech model variant");
        AutomationProperties.SetName(_path, "Local Parakeet model folder");
        AutomationProperties.SetName(_meter, "Model download progress");

        _download.Click += async (_, _) => await DownloadModelAsync();
        _cancel.Click += (_, _) => _downloadCancellation?.Cancel();
        _browse.Click += async (_, _) => await BrowseAsync();
        _useFolder.Click += (_, _) => SelectFolder(_path.Text?.Trim());
        _automatic.Click += (_, _) => SelectFolder(null, automatic: true);
        _variants.SelectionChanged += (_, _) => UpdateDownloadSize();

        Content = ViewKit.Column(
            ViewKit.Card(ViewKit.Column(
                ViewKit.Title("On-device speech"), _streaming, _modelStatus,
                ViewKit.Note("Zipformer shows words as you speak in English. Parakeet transcribes when you finish. Both run offline after model setup."))),
            ViewKit.Card(ViewKit.Column(
                ViewKit.Title("Download a model"), _variants, _downloadSize,
                ViewKit.Row(_download, _cancel), _meter, _downloadStatus)),
            ViewKit.Card(ViewKit.Column(
                ViewKit.Title("Existing Parakeet model folder"),
                ViewKit.Note("Choose the folder containing the encoder, decoder, joiner and tokens files."),
                ViewKit.Split(_path, _browse), ViewKit.Row(_useFolder, _automatic))));

        RefreshModelStatus();
        UpdateDownloadSize();
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is Window owner && !ReferenceEquals(_owner, owner))
        {
            if (_owner is not null) _owner.Closed -= OwnerClosed;
            _owner = owner;
            _owner.Closed += OwnerClosed;
        }
        _path.Text = _settings.Data.ModelDirectory;
        _settings.Changed += OnSettingsChanged;
        OnSettingsChanged(null, EventArgs.Empty);
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _settings.Changed -= OnSettingsChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnSettingsChanged(object? sender, EventArgs args)
    {
        SyncSpeechEngine();
        RefreshModelStatus();
    }

    private SpeechModelVariant SelectedVariant => _variants.SelectedIndex switch
    {
        1 => SpeechModelVariant.Multilingual,
        2 => SpeechModelVariant.StreamingEnglish,
        _ => SpeechModelVariant.English,
    };

    private void UpdateDownloadSize()
    {
        var mb = SpeechModelInstaller.DownloadSize(SelectedVariant) / 1_000_000d;
        _downloadSize.Text = string.Create(CultureInfo.CurrentCulture, $"About {mb:N0} MB downloaded from Hugging Face.");
    }

    private void RefreshModelStatus()
    {
        if (_settings.Data.UseStreamingSpeech)
        {
            _modelStatus.Text = ZipformerTranscriber.Locate() is not null
                ? "Ready. The streaming English model is available."
                : "Download Zipformer streaming for English to start live transcription.";
            return;
        }

        var folder = _settings.Data.ModelDirectory ?? ParakeetTranscriber.Locate();
        _modelStatus.Text = folder is not null && ParakeetTranscriber.IsComplete(folder)
            ? "Ready. The local Parakeet model files are available."
            : "No complete model found. Download or select a model to enable recording.";
    }

    private void ChangeSpeechEngine(bool streaming)
    {
        if (_synchronizing) return;
        if (streaming) _variants.SelectedIndex = 2;
        SaveSpeechEngine(streaming);
    }

    private void SyncSpeechEngine()
    {
        _synchronizing = true;
        _streaming.IsChecked = _settings.Data.UseStreamingSpeech;
        _synchronizing = false;
    }

    private bool SaveSpeechEngine(bool streaming)
    {
        try
        {
            _settings.Update(_settings.Data with { UseStreamingSpeech = streaming });
            SyncSpeechEngine();
            RefreshModelStatus();
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            SyncSpeechEngine();
            _modelStatus.Text = "Chit-Chat could not save the selected speech engine.";
            return false;
        }
    }

    private bool SelectFolder(string? folder, bool automatic = false)
    {
        if (!automatic && (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder) ||
                           !ParakeetTranscriber.IsComplete(folder)))
        {
            _modelStatus.Text = "Choose an absolute folder path containing all four model files.";
            return false;
        }

        try
        {
            _settings.Update(_settings.Data with { ModelDirectory = folder, UseStreamingSpeech = false });
            _path.Text = folder;
            SyncSpeechEngine();
            RefreshModelStatus();
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _modelStatus.Text = "The selected model folder could not be saved. Please try again.";
            return false;
        }
    }

    private async Task BrowseAsync()
    {
        if (_downloadCancellation is not null) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null || !storage.CanPickFolder)
        {
            _modelStatus.Text = "Enter the full model folder path and select Use this folder.";
            return;
        }

        SetInputEnabled(false);
        try
        {
            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select the Parakeet model folder",
                AllowMultiple = false,
            });
            using var folder = folders.Count == 0 ? null : folders[0];
            if (folder is not null) SelectFolder(folder.TryGetLocalPath());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException)
        {
            _modelStatus.Text = "The folder picker could not open. Enter the model folder path directly.";
        }
        finally { SetInputEnabled(true); }
    }

    private async Task DownloadModelAsync()
    {
        if (_downloadCancellation is not null) return;
        using var cancellation = new CancellationTokenSource();
        _downloadCancellation = cancellation;
        SetInputEnabled(false);
        _cancel.IsVisible = true;
        _meter.Value = 0;
        _meter.IsVisible = true;
        _downloadStatus.Text = "Preparing model download…";
        var progress = new Progress<SpeechModelProgress>(value =>
        {
            if (!ReferenceEquals(cancellation, _downloadCancellation) || cancellation.IsCancellationRequested) return;
            _meter.Value = value.DownloadedBytes / (double)value.TotalBytes * 100;
            _downloadStatus.Text = string.Create(CultureInfo.CurrentCulture,
                $"Downloaded {value.DownloadedBytes / 1_000_000d:N0} of {value.TotalBytes / 1_000_000d:N0} MB.");
        });

        try
        {
            var variant = SelectedVariant;
            var folder = await _installer.InstallAsync(variant, progress, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            var selected = variant == SpeechModelVariant.StreamingEnglish ? SaveSpeechEngine(streaming: true) : SelectFolder(folder);
            _downloadStatus.Text = selected ? "Model files verified and installed."
                                           : "Model files installed. Use speech settings to select the model.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            _downloadStatus.Text = "Download canceled.";
        }
        catch (OperationCanceledException)
        {
            _downloadStatus.Text = "The download timed out. Check your connection and try again.";
        }
        catch (HttpRequestException)
        {
            _downloadStatus.Text = "The model download failed. Check your network connection and try again.";
        }
        catch (InvalidDataException)
        {
            _downloadStatus.Text = "The model files could not be verified. Please try the download again.";
        }
        catch (IOException)
        {
            _downloadStatus.Text = "Could not install the model. Check disk space or choose an existing model folder.";
        }
        catch (UnauthorizedAccessException)
        {
            _downloadStatus.Text = "Chit-Chat could not write to the model library.";
        }
        finally
        {
            _downloadCancellation = null;
            _cancel.IsVisible = false;
            _meter.IsVisible = false;
            SetInputEnabled(true);
        }
    }

    private void SetInputEnabled(bool enabled)
    {
        _streaming.IsEnabled = enabled;
        _variants.IsEnabled = enabled;
        _path.IsEnabled = enabled;
        _download.IsEnabled = enabled;
        _browse.IsEnabled = enabled;
        _useFolder.IsEnabled = enabled;
        _automatic.IsEnabled = enabled;
    }

    private void OwnerClosed(object? sender, EventArgs e)
    {
        _downloadCancellation?.Cancel();
        if (_owner is not null) _owner.Closed -= OwnerClosed;
        _owner = null;
    }
}
