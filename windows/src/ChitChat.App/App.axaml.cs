using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using ChitChat.App.Views;

namespace ChitChat.App;

/// <summary>Manages the workspace and notification-area entry point.</summary>
public partial class App : Application
{
    private bool _exiting;

    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var session = Composition.Create();
            var workspace = new MainWindow(session);
            desktop.MainWindow = workspace;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            workspace.Closing += (_, args) =>
            {
                if (_exiting) return;

                args.Cancel = true;
                workspace.Hide();
            };
            desktop.ShutdownRequested += (_, _) => _exiting = true;
            desktop.Exit += (_, _) =>
            {
                _exiting = true;
                session.DisposeAsync().AsTask().GetAwaiter().GetResult();
            };

            TrayIcon.SetIcons(this, new TrayIcons { MakeTray(desktop, workspace) });
        }

        base.OnFrameworkInitializationCompleted();
    }

    private TrayIcon MakeTray(IClassicDesktopStyleApplicationLifetime desktop, MainWindow workspace)
    {
        var menu = new NativeMenu();
        menu.Items.Add(Item("Open Chit-Chat", ShowWorkspace));
        menu.Items.Add(Item("Settings…", () =>
        {
            ShowWorkspace();
            workspace.OpenSettings();
        }));
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(Item("Quit Chit-Chat", () =>
        {
            _exiting = true;
            desktop.Shutdown();
        }));

        using var image = AssetLoader.Open(new Uri("avares://ChitChat.App/Assets/tray.ico"));
        return new TrayIcon
        {
            Icon = new WindowIcon(image), Menu = menu, IsVisible = true, ToolTipText = "Chit-Chat",
        };

        void ShowWorkspace()
        {
            workspace.Show();
            workspace.WindowState = WindowState.Normal;
            workspace.Activate();
        }
    }

    private static NativeMenuItem Item(string title, Action run)
    {
        var item = new NativeMenuItem(title);
        item.Click += (_, _) => run();
        return item;
    }
}
