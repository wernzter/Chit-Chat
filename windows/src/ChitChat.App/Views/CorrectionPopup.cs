using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using ChitChat.Core;

namespace ChitChat.App.Views;

internal sealed class CorrectionPopup : OverlayWindow
{
    private const double CountdownSeconds = 10;
    private const double RingSize = 32;
    private static readonly IBrush RingTrack = new SolidColorBrush(Color.FromArgb(90, 113, 130, 157));
    private readonly TextBlock _rule = new()
    {
        FontSize = 14, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap,
        MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis,
    };
    private readonly TextBlock _countdown = ViewKit.Caption(string.Empty);
    private readonly Arc _remaining = new()
    {
        Width = RingSize, Height = RingSize, StartAngle = -90, SweepAngle = 0,
        StrokeThickness = 2, Stretch = Stretch.Fill,
    };

    internal CorrectionPopup(Window workspace, Composition session, Action cancel)
        : base(workspace, session, width: 332, height: 76, clickThrough: false)
    {
        Title = "Chit-Chat dictionary suggestion";
        Classes.Add("workspace");
        _remaining.Classes.Add("correction-countdown");

        var ring = new Ellipse
        {
            Width = RingSize, Height = RingSize, Stroke = RingTrack, StrokeThickness = 2,
        };
        var cross = new TextBlock
        {
            Text = "×", FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var cancelButton = new Button
        {
            Content = new Grid { Children = { ring, _remaining, cross } },
            Width = 40, Height = 40, Focusable = false, Padding = new Thickness(0),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(cancelButton, "Cancel dictionary addition");
        ToolTip.SetTip(cancelButton, "Cancel dictionary addition");
        cancelButton.Click += (_, _) =>
        {
            cancel();
            Conceal();
        };
        var metadata = ViewKit.Column(_rule, _countdown);
        metadata.Spacing = 4;
        metadata.VerticalAlignment = VerticalAlignment.Center;
        var surface = ViewKit.Card(ViewKit.Split(metadata, cancelButton));
        surface.Padding = new Thickness(13, 8);
        Content = surface;
    }

    internal void Update(LiveCorrectionSuggestion? suggestion, RecordingIndicatorPosition placement)
    {
        if (suggestion is null || placement == RecordingIndicatorPosition.Hidden)
        {
            Conceal();
            return;
        }

        double remaining = Math.Max(0, suggestion.SecondsRemaining);
        _rule.Text = $"“{suggestion.Heard}” → “{suggestion.Written}”";
        _countdown.Text = $"Adding to your dictionary in {Math.Ceiling(remaining)}s";
        _remaining.SweepAngle = 359.9 * Math.Min(1, remaining / CountdownSeconds);
        Present(placement);
    }
}
