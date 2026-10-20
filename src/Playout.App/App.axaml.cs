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
        {
            // The schedule program (its own program, Sintoniza Pro): events, ad breaks, clients, hour templates and
            // the daily playlists. Not part of the on-air screen. --playlists opens it on the daily playlists.
            bool playlists = desktop.Args?.Contains(AppInfo.PlaylistsArg) == true;
            bool schedule = playlists || desktop.Args?.Contains(AppInfo.ScheduleArg) == true;
            // The import program (its own menu entry): folders of audio into the library, in categories.
            bool import = !schedule && desktop.Args?.Contains(AppInfo.ImportArg) == true;
            var (lockName, name) = schedule ? ("schedule", AppInfo.ScheduleName) : import ? ("import", AppInfo.ImportName) : ("playout", AppInfo.Name);
            // One of each per computer: a second start says so instead of opening another (two playouts would fight
            // over the sound card and the playlist).
            desktop.MainWindow = !SingleInstance.Acquire(lockName) ? SingleInstance.AlreadyRunning(name)
                : schedule ? ScheduleProgram(settings, playlists)
                : import ? ImportProgram(settings)
                : new MainWindow();
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>The import program, on the library set in Settings → Library.</summary>
    static ImportWindow ImportProgram(AppSettings settings)
    {
        try
        {
            return new ImportWindow(LibraryDb.Open(settings.Library), null);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Import program: library unavailable: {ex.Message}");
            return new ImportWindow(null, ex.Message);
        }
    }

    /// <summary>
    /// The schedule program: the schedule from the library database (the old files are imported the first time). If
    /// the database can't be reached, the playout's last copy is shown, and it can't be saved until it can.
    /// </summary>
    /// <param name="daily">Open on the daily playlists.</param>
    static ScheduleWindow ScheduleProgram(AppSettings settings, bool daily)
    {
        string folder = settings.Library.Folders.FirstOrDefault() ?? settings.MusicFolder;
        try
        {
            var db = LibraryDb.Open(settings.Library);
            new ScheduleSource(() => db).Version(); // imports the old schedule files the first time
            var store = new ScheduleStore(db);
            var (data, version) = store.Load();
            // The music library: the daily playlists' library panel (songs to put in, listen to, tag).
            var library = new LibraryService();
            _ = library.OpenAsync(settings.Library);
            var window = new ScheduleWindow(data, folder, store, version, null, library, settings);
            if (daily) window.ShowDailyPlaylists();
            return window;
        }
        catch (Exception ex)
        {
            AppLog.Write($"Schedule program: database unavailable: {ex.Message}");
            return new ScheduleWindow(ScheduleSource.LoadLocal(), folder, null, 0,
                L.F("The database can't be reached ({0}). This is the playout's last copy; it can't be saved until the database is back.", ex.Message));
        }
    }
}
