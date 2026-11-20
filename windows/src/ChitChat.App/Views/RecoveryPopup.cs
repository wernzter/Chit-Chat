using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using ChitChat.Core;

namespace ChitChat.App.Views;

internal sealed class RecoveryPopup : OverlayWindow
{
    private readonly TextBlock _remaining = ViewKit.Caption(string.Empty);
    private WaitingDictation? _item;

    internal RecoveryPopup(Window workspace, Composition session,
                          Func<WaitingDictation, Task> copy, Action<Guid> dismiss)
        : base(workspace, session, width: 366, height: 132, clickThrough: false)
    {
        Title = "Dictation ready to copy";
        Classes.Add("workspace");
        var copyText = new Button { Content = "Copy text", Focusable = false };
        copyText.Classes.Add("primary");
        AutomationProperties.SetName(copyText, "Copy waiting dictation");
        copyText.Click += async (_, _) =>
        {
            if (_item is { } item) await copy(item);
        };
        var dismissButton = ViewKit.Action("Dismiss", () =>
        {
            if (_item is { } item) dismiss(item.Id);
        });
        dismissButton.Focusable = false;

        _remaining.VerticalAlignment = VerticalAlignment.Center;
        var actions = ViewKit.Split(ViewKit.Row(copyText, dismissButton), _remaining);
        var surface = ViewKit.Section("Dictation ready to copy",
            ViewKit.Note("Chit-Chat couldn’t insert this transcript into the original app."), actions);
        surface.Padding = new Thickness(14);
        Content = surface;
    }

    internal void Update(WaitingDictation? item, int count)
    {
        _item = item;
        if (item is null)
        {
            Conceal();
            return;
        }

        _remaining.Text = $"{count} waiting";
        Present(RecordingIndicatorPosition.Top, alignRight: true);
    }
}
