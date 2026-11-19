using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ChitChat.App.Views;

internal static class ViewKit
{
    internal const double Gap = 10;
    internal const double Inset = 20;
    internal static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(27, 77, 194));
    internal static readonly IBrush Recording = new SolidColorBrush(Color.FromRgb(181, 46, 66));

    internal static TextBlock Title(string text) => new() { Text = text, FontSize = 22, FontWeight = FontWeight.SemiBold };

    internal static TextBlock Note(string text) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.78 };

    internal static Button Action(string caption, Action run)
    {
        var button = new Button { Content = caption };
        button.Click += (_, _) => run();
        return button;
    }

    internal static StackPanel Column(params Control[] controls) => Stack(Orientation.Vertical, controls);

    internal static StackPanel Row(params Control[] controls) => Stack(Orientation.Horizontal, controls);

    private static StackPanel Stack(Orientation orientation, Control[] controls)
    {
        var panel = new StackPanel { Orientation = orientation, Spacing = Gap };
        panel.Children.AddRange(controls);
        return panel;
    }

    internal static Grid Split(Control first, Control second)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = Gap };
        Grid.SetColumn(second, 1);
        grid.Children.Add(first);
        grid.Children.Add(second);
        return grid;
    }

    internal static Grid Page(Control header, Control content)
    {
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = Gap };
        Grid.SetRow(content, 1);
        grid.Children.Add(header);
        grid.Children.Add(content);
        return grid;
    }

    internal static Border Card(Control content) =>
        new() { Child = content, Padding = new Thickness(Gap), CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(55, 90, 107, 139)) };
}
