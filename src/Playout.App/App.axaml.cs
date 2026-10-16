using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Playout.Library;

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
            desktop.MainWindow = desktop.Args?.Contains(AppInfo.ScheduleArg) == true ? ScheduleProgram(settings) : new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// The schedule program: the schedule from the library database (the old files are imported the first time). If
    /// the database can't be reached, the playout's last copy is shown, and it can't be saved until it can.
    /// </summary>
    static ScheduleWindow ScheduleProgram(AppSettings settings)
    {
        string folder = settings.Library.Folders.FirstOrDefault() ?? settings.MusicFolder;
        try
        {
            var db = LibraryDb.Open(settings.Library);
            new ScheduleSource(() => db).Version(); // imports the old schedule files the first time
            var store = new ScheduleStore(db);
            var (data, version) = store.Load();
            return new ScheduleWindow(data, folder, store, version, null);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Schedule program: database unavailable: {ex.Message}");
            return new ScheduleWindow(ScheduleSource.LoadLocal(), folder, null, 0,
                L.F("The database can't be reached ({0}). This is the playout's last copy; it can't be saved until the database is back.", ex.Message));
        }
    }
}
