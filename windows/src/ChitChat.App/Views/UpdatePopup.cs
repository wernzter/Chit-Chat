using Avalonia;
using Avalonia.Controls;
using ChitChat.Core;

namespace ChitChat.App.Views;

internal sealed class UpdatePopup : OverlayWindow
{
    private readonly TextBlock _description = ViewKit.Note(string.Empty);
    private Version? _version;

    internal UpdatePopup(Window workspace, Composition session, Func<Version, Task> openRelease)
        : base(workspace, session, width: 366, height: 128, clickThrough: false)
    {
        Title = "Chit-Chat update available";
        Classes.Add("workspace");
        var open = new Button { Content = "View release", Focusable = false };
        open.Classes.Add("primary");
        open.Click += async (_, _) => { if (_version is { } version) await openRelease(version); };
        var dismiss = ViewKit.Action("Dismiss", () =>
        {
            if (_version is { } version) session.Updates.Dismiss(version);
        });
        dismiss.Focusable = false;
        var surface = ViewKit.Section("Chit-Chat update available", _description, ViewKit.Row(open, dismiss));
        surface.Padding = new Thickness(14);
        Content = surface;
    }

    internal void Update(Version? version)
    {
        _version = version;
        if (version is null)
        {
            Conceal();
            return;
        }
        _description.Text = $"Version {version.ToString(3)} is ready to download from GitHub.";
        Present(RecordingIndicatorPosition.Top, alignRight: true);
    }
}
