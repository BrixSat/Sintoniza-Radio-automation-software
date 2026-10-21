using Avalonia.Controls;
using Avalonia.Layout;

namespace Playout.App;

/// <summary>
/// One playout and one schedule program per computer (user): each holds a lock file in the data folder while it runs.
/// A second start finds it taken and shows that it is already running; it then closes normally, so the watchdog
/// doesn't start it again.
/// </summary>
static class SingleInstance
{
    static FileStream? _lock;

    /// <summary>Takes the lock for <paramref name="name"/> ("playout", "schedule"); false if another copy has it.</summary>
    public static bool Acquire(string name)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.DataFolder);
            // Held until the process ends (the system lets go of it then, also after a crash).
            _lock = new FileStream(Path.Combine(AppSettings.DataFolder, name + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return true; } // a read-only folder: don't stop the station for that
    }

    /// <summary>Lets go of the lock before this copy ends (an update starting the new version while this one closes).</summary>
    public static void Release()
    {
        _lock?.Dispose();
        _lock = null;
    }

    /// <summary>The window a second copy shows instead of starting.</summary>
    public static Window AlreadyRunning(string program)
    {
        var ok = new Button { Content = L.T("OK"), MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true };
        var window = new Window
        {
            Title = program,
            Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://SintonizaEmi/Assets/icon.ico"))),
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(24),
                Spacing = 18,
                MaxWidth = 460,
                Children =
                {
                    new TextBlock { Text = L.F("{0} is already running on this computer.", program), FontWeight = Avalonia.Media.FontWeight.SemiBold, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new TextBlock { Text = L.T("Only one copy can run at a time: it is open already (look for its window, or in the taskbar or Dock)."), TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    ok,
                },
            },
        };
        ok.Click += (_, _) => window.Close();
        return window;
    }
}
