using Avalonia;
using Avalonia.Threading;

namespace Playout.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        GraphicsDriver.Prepare(); // before any graphics library is loaded
        if (Watchdog.ShouldSupervise(args)) return Watchdog.Supervise(args);

        AppLog.CleanUp();
        AppDomain.CurrentDomain.UnhandledException += (_, e) => AppLog.Write($"Crash: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Write($"Background error: {e.Exception.InnerException ?? e.Exception}");
            e.SetObserved();
        };
        if (args.Contains(Watchdog.ChildArg)) Watchdog.StartHeartbeat();

        var builder = BuildAvaloniaApp();
        // The schedule program groups apart from the playout in the Linux taskbar, under its own icon.
        if (args.Contains(AppInfo.ScheduleArg) || args.Contains(AppInfo.PlaylistsArg)) builder.With(new X11PlatformOptions { WmClass = AppInfo.ScheduleWmClass });
        else if (args.Contains(AppInfo.ImportArg)) builder.With(new X11PlatformOptions { WmClass = AppInfo.ImportWmClass });
        // A mistake in a window must not take the station off air: the engine runs on, so log it and carry on.
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            AppLog.Write($"Error: {e.Exception}");
            e.Handled = true;
        };
        return builder.StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
