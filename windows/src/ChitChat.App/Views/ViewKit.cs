using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace ChitChat.App.Views;

internal static class ViewKit
{
    internal const double Gap = 12;
    internal const double Inset = 24;
    internal const double CardPadding = 20;
    internal const double SidebarWidth = 224;

    internal static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(30, 87, 209));
    internal static readonly IBrush Recording = new SolidColorBrush(Color.FromRgb(178, 41, 65));

    internal static TextBlock Title(string text) => new()
    {
        Text = text, FontSize = 28, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap,
    };

    internal static TextBlock Heading(string text) => new()
    {
        Text = text, FontSize = 17, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap,
    };

    internal static TextBlock Note(string text)
    {
        var note = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13 };
        note.Classes.Add("muted");
        return note;
    }

    internal static TextBlock Caption(string text)
    {
        var caption = Note(text);
        caption.FontSize = 12;
        return caption;
    }

    internal static Button Action(string caption, Action run)
    {
        var button = new Button { Content = caption };
        button.Click += (_, _) => run();
        return button;
    }

    internal static Button Primary(string caption, Action run)
    {
        var button = Action(caption, run);
        button.Classes.Add("primary");
        return button;
    }

    internal static TextBox Search(string name, string prompt)
    {
        var input = new TextBox { Name = name, Watermark = prompt };
        AutomationProperties.SetName(input, prompt);
        return input;
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

    internal static ScrollViewer Scroll(Control content) => new()
    {
        Content = content,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };

    internal static Border Card(Control content)
    {
        var card = new Border { Child = content, Padding = new Thickness(CardPadding) };
        card.Classes.Add("card");
        return card;
    }

    internal static Border Section(string title, params Control[] content)
    {
        var column = Column(Heading(title));
        column.Children.AddRange(content);
        return Card(column);
    }

    internal static Control Empty(string title, string explanation)
    {
        var headline = Heading(title);
        headline.TextAlignment = TextAlignment.Center;
        var detail = Note(explanation);
        detail.TextAlignment = TextAlignment.Center;
        var message = Column(headline, detail);
        message.MaxWidth = 330;
        message.VerticalAlignment = VerticalAlignment.Center;
        message.HorizontalAlignment = HorizontalAlignment.Center;
        message.Margin = new Thickness(CardPadding);
        return message;
    }

    internal static Control Field(string label, Control input)
    {
        AutomationProperties.SetName(input, label);
        return new StackPanel
        {
            Spacing = 5,
            Children = { new TextBlock { Text = label, FontWeight = FontWeight.Medium }, input },
        };
    }

    internal static ToggleSwitch Toggle(string label, bool enabled, Action<bool> change)
    {
        var toggle = new ToggleSwitch
        {
            OnContent = label, OffContent = label, IsChecked = enabled,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(toggle, label);
        toggle.IsCheckedChanged += (_, _) => change(toggle.IsChecked == true);
        return toggle;
    }

    internal static PathIcon Icon(string drawing, double size = 17) =>
        new() { Data = Geometry.Parse(drawing), Width = size, Height = size };

    internal static Border Rule()
    {
        var border = new Border { Height = 1 };
        border.Classes.Add("rule");
        return border;
    }
}
