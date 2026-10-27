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
            // The recorder (its own program, Sintoniza Rec): the station's sound, kept as the law asks.
            bool rec = !schedule && !import && desktop.Args?.Contains(AppInfo.RecArg) == true;
            var (lockName, name) = schedule ? ("schedule", AppInfo.ScheduleName) : import ? ("import", AppInfo.ImportName)
                : rec ? ("rec", AppInfo.RecName) : ("playout", AppInfo.Name);
            // One of each per computer: a second start says so instead of opening another (two playouts would fight
            // over the sound card and the playlist).
            bool locked = SingleInstance.Acquire(lockName);
            if (!locked && desktop.Args?.Contains(Watchdog.ChildArg) == true)
            {
                // Started again by the watchdog while the copy it stopped is still letting go: wait for it, and if it
                // doesn't, end with an error (the watchdog tries again later) rather than wait for an OK nobody clicks.
                locked = SingleInstance.Wait(lockName, 20);
                if (!locked)
                {
                    AppLog.Write($"{name}: the copy stopped by the watchdog still holds the lock; trying again later");
                    Environment.Exit(3);
                }
            }
            desktop.MainWindow = !locked ? SingleInstance.AlreadyRunning(name)
                : schedule ? WithLogin(desktop, settings, AppInfo.ScheduleName, null, (db, user) => ScheduleProgram(settings, playlists, db, user),
                    error => ScheduleOffline(settings, error))
                : import ? WithLogin(desktop, settings, AppInfo.ImportName, Permission.Library, (db, user) => new ImportWindow(db, null),
                    error => new ImportWindow(null, error))
                : rec ? RecProgram()
                : PlayoutProgram();
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>The recorder: recording as soon as it opens (it may be started at login), set to start there again if it moved.</summary>
    static RecWindow RecProgram()
    {
        Autostart.Rec.Refresh();
        return new RecWindow(start: true);
    }

    /// <summary>The on-air program; starting at login follows it if it was renamed or moved by an update.</summary>
    static MainWindow PlayoutProgram()
    {
        Autostart.Playout.Refresh();
        return new MainWindow();
    }

    /// <summary>
    /// A program that needs a Sintoniza user: the login first, then the program (as that user). Without the database
    /// there are no users to ask for: <paramref name="offline"/> says why.
    /// </summary>
    static Avalonia.Controls.Window WithLogin(IClassicDesktopStyleApplicationLifetime desktop, AppSettings settings, string program, string? permission,
        Func<LibraryDb, SintonizaUser, Avalonia.Controls.Window> open, Func<string, Avalonia.Controls.Window> offline)
    {
        LibraryDb db;
        try { db = LibraryDb.Open(settings.Library); }
        catch (Exception ex)
        {
            string error = DbErrors.Explain(ex, settings.Library);
            AppLog.Write($"{program}: database unavailable: {error}");
            return offline(error);
        }
        string station = "";
        try { station = new StationStore(db).Load().Settings?.StationName ?? ""; } catch { /* no name */ }
        LoginWindow.LastUser = settings.LastUser;
        var login = new LoginWindow(db, program, permission, null, station);
        login.LoggedIn += user =>
        {
            settings.LastUser = user.Name;
            // Only this: the on-air program may have saved its playlist since this copy was read.
            AppSettings.Change(s => s.LastUser = user.Name);
            AppLog.Write($"{program}: {user.Name} logged in");
            var window = open(db, user);
            desktop.MainWindow = window;
            window.Show();
            login.Close();
        };
        return login;
    }

    /// <summary>
    /// The schedule program: the schedule from the library database (the old files are imported the first time). If
    /// the database can't be reached, the playout's last copy is shown, and it can't be saved until it can.
    /// </summary>
    /// <param name="daily">Open on the daily playlists.</param>
    static ScheduleWindow ScheduleProgram(AppSettings settings, bool daily, LibraryDb db, SintonizaUser user)
    {
        string folder = settings.Library.Folders.FirstOrDefault() ?? settings.MusicFolder;
        try
        {
            new ScheduleSource(() => db).Version(); // imports the old schedule files the first time
            var store = new ScheduleStore(db);
            var (data, version) = store.Load();
            // The music library: the daily playlists' library panel (songs to put in, listen to, tag).
            var library = new LibraryService();
            _ = library.OpenAsync(settings.Library);
            var window = new ScheduleWindow(data, folder, store, version, null, library, settings, user);
            if (daily) window.ShowDailyPlaylists();
            return window;
        }
        catch (Exception ex)
        {
            return ScheduleOffline(settings, DbErrors.Explain(ex, settings.Library));
        }
    }

    /// <summary>Without the database: the playout's last copy of the schedule, to look at.</summary>
    static ScheduleWindow ScheduleOffline(AppSettings settings, string error)
    {
        AppLog.Write($"Schedule program: database unavailable: {error}");
        return new ScheduleWindow(ScheduleSource.LoadLocal(), settings.Library.Folders.FirstOrDefault() ?? settings.MusicFolder, null, 0,
            L.F("The database can't be reached ({0}). This is the playout's last copy; it can't be saved until the database is back.", error));
    }
}
