using Avalonia;

namespace ChitChat.App;

/// <summary>Desktop application entry point.</summary>
public static class Program
{
    /// <summary>Starts the app or verifies a packaged build.</summary>
    /// <param name="args">Command-line arguments.</param>
    [STAThread]
    public static int Main(string[] args) =>
        args.Contains("--selftest", StringComparer.OrdinalIgnoreCase)
            ? SelfTest.Run()
            : BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>Configures the native desktop host.</summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
