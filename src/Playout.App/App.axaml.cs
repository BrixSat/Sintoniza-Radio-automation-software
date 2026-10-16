using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Playout.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var settings = AppSettings.Load();
        Localization.Load(settings.Language);
        PlaylistLook.Apply(settings.PlaylistColors);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            // The schedule program (its own menu entry): events, ad breaks, clients. Not part of the on-air screen.
            desktop.MainWindow = desktop.Args?.Contains(AppInfo.ScheduleArg) == true
                ? new ScheduleWindow(ScheduleData.Load(), settings.Library.Folders.FirstOrDefault() ?? settings.MusicFolder)
                : new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
