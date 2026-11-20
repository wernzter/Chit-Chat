using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ChitChat.Core;

namespace ChitChat.App.Views;

internal sealed class RecordingOverlay : OverlayWindow
{
    private const int CapsuleWidth = 84;
    private const int CapsuleHeight = 36;
    private const int BarCount = 7;
    private static readonly IBrush Surface = new SolidColorBrush(Color.FromArgb(245, 19, 24, 35));
    private static readonly IBrush Outline = new SolidColorBrush(Color.FromArgb(220, 51, 62, 84));
    private static readonly IBrush Listening = new SolidColorBrush(Color.FromRgb(254, 137, 156));
    private static readonly IBrush Processing = new SolidColorBrush(Color.FromRgb(147, 188, 255));

    private readonly Border[] _bars;

    internal RecordingOverlay(Window workspace, Composition session)
        : base(workspace, session, CapsuleWidth, CapsuleHeight, clickThrough: true)
    {
        Title = "Chit-Chat recording indicator";
        var waveform = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 3,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _bars = new Border[BarCount];
        for (var index = 0; index < _bars.Length; index++)
        {
            var bar = new Border { Width = 3, Height = 4, CornerRadius = new CornerRadius(1.5) };
            _bars[index] = bar;
            waveform.Children.Add(bar);
        }
        Content = new Border
        {
            Background = Surface, BorderBrush = Outline, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(CapsuleHeight / 2), Child = waveform,
        };
    }

    internal void Update(DictationState state, float inputLevel, RecordingIndicatorPosition placement)
    {
        if (state == DictationState.Idle || placement == RecordingIndicatorPosition.Hidden)
        {
            Conceal();
            return;
        }

        var elapsed = (double)Environment.TickCount64 / 1_000;
        var listening = state == DictationState.Recording;
        var level = float.IsFinite(inputLevel) ? Math.Clamp(inputLevel, 0, 1) : 0;
        for (var i = 0; i < _bars.Length; i++)
        {
            var envelope = 1 - Math.Abs(i - (_bars.Length - 1) / 2d) / _bars.Length;
            var wave = (Math.Sin(elapsed * (listening ? 10 : 5) + (i * 0.82)) + 1) / 2;
            var amplitude = listening ? Math.Clamp(level * 1.6, 0.10, 1) : 0.55;
            _bars[i].Height = 4 + 18 * envelope * amplitude * (0.55 + 0.45 * wave);
            _bars[i].Background = listening ? Listening : Processing;
        }
        AutomationProperties.SetName(this, listening ? "Microphone recording" : "Preparing transcription");
        Present(placement);
    }
}

internal abstract class OverlayWindow : Window
{
    private const int ScreenInset = 20;
    private readonly Window _workspace;
    private readonly Composition _session;
    private readonly bool _clickThrough;
    private bool? _configured;

    protected OverlayWindow(Window workspace, Composition session,
                            int width, int height, bool clickThrough)
    {
        _workspace = workspace;
        _session = session;
        _clickThrough = clickThrough;
        Win32Properties.AddWindowStylesCallback(this,
            (style, extended) => (style, _session.RecordingOverlayStyles(extended, clickThrough)));

        Width = width;
        Height = height;
        CanResize = false;
        Focusable = false;
        IsHitTestVisible = !clickThrough;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SystemDecorations = SystemDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
    }

    protected void Present(RecordingIndicatorPosition placement, bool alignRight = false)
    {
        if (_configured is null && TryGetPlatformHandle() is { Handle: not 0 } handle)
            _configured = _session.ConfigureRecordingOverlay(handle.Handle, _clickThrough);
        if (_configured != true) return;

        MoveTo(placement, alignRight);
        if (!IsVisible) Show();
    }

    protected void Conceal()
    {
        if (IsVisible) Hide();
    }

    private void MoveTo(RecordingIndicatorPosition placement, bool alignRight)
    {
        var screens = _workspace.Screens;
        var display = (_session.ActiveDisplayPoint() is { } point ? screens.ScreenFromPoint(point) : null) ??
                      screens.ScreenFromWindow(_workspace) ?? screens.Primary;
        if (display is null) return;
        var area = display.WorkingArea;
        var scaledWidth = (int)Math.Round(Width * display.Scaling);
        var scaledHeight = (int)Math.Round(Height * display.Scaling);
        var padding = (int)Math.Round(ScreenInset * display.Scaling);
        var y = placement == RecordingIndicatorPosition.Top
            ? area.Y + padding : area.Y + area.Height - scaledHeight - padding;
        var x = alignRight ? area.X + area.Width - scaledWidth - padding : area.X + (area.Width - scaledWidth) / 2;
        var position = new PixelPoint(x, y);
        if (Position != position) Position = position;
    }
}
