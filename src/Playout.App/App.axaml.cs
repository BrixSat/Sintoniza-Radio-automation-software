using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Playout.Engine;
using Playout.Library;

namespace Playout.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var settings = AppSettings.Load();
        Localization.Load(settings.Language);
        if (Localization.Problems.Length > 0) AppLog.Write("Translations: " + Localization.Problems);
        PlaylistLook.Apply(settings.PlaylistColors);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The schedule program (its own program, Sintoniza Pro): events, ad breaks, clients, hour templates and
            // the daily playlists. Not part of the on-air screen. --playlists opens it on the daily playlists.
            bool playlists = desktop.Args?.Contains(AppInfo.PlaylistsArg) == true;
            bool schedule = playlists || desktop.Args?.Contains(AppInfo.ScheduleArg) == true;
            if (schedule)
            {
                AppSettings.ForPro = true;
                CurrentStation.Id = settings.ProStation > 0 ? settings.ProStation : settings.Station;
            }
            UseStationTime(settings);
            // The import program (its own menu entry): folders of audio into the library, in categories.
            bool import = !schedule && desktop.Args?.Contains(AppInfo.ImportArg) == true;
            // The recorder (its own program, Sintoniza Rec): the station's sound, kept as the law asks.
            bool rec = !schedule && !import && desktop.Args?.Contains(AppInfo.RecArg) == true;
            // The studio clock (its own program, Sintoniza Tempo): the time, for a screen in the studio.
            bool tempo = !schedule && !import && !rec && desktop.Args?.Contains(AppInfo.TempoArg) == true;
            var (lockName, name) = schedule ? ("schedule", AppInfo.ScheduleName) : import ? ("import", AppInfo.ImportName)
                : rec ? ("rec", AppInfo.RecName) : tempo ? ("tempo", AppInfo.TempoName) : ("playout", AppInfo.Name);
            // One of each per computer: a second start says so instead of opening another (two playouts would fight
            // over the sound card and the playlist).
            bool locked = SingleInstance.Acquire(lockName);
            // Started again by itself (Sintoniza Pro on another station): the copy that started it is closing.
            if (!locked && desktop.Args?.Contains(AppInfo.RestartedArg) == true) locked = SingleInstance.Wait(lockName, 20);
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
                : tempo ? TempoProgram()
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

    /// <summary>The studio clock; starting at login follows it if it moved.</summary>
    static TempoWindow TempoProgram()
    {
        Autostart.Tempo.Refresh();
        return new TempoWindow();
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
        // Sintoniza Pro started again by itself on another station: who was logged in still is.
        if (program == AppInfo.ScheduleName)
        {
            SintonizaUser? kept = null;
            try { kept = LoginHandoff.Take(desktop.Args, new UserStore(db)); }
            catch (Exception ex) { AppLog.Write($"{program}: the login wasn't kept: {ex.Message}"); }
            if (kept != null && kept.MayUse(CurrentStation.Id))
            {
                AppLog.Write($"{program}: {kept.Name} still logged in, on station {CurrentStation.Id}");
                return open(db, kept);
            }
        }
        string station = "";
        try { station = new StationStore(db).Load().Settings?.StationName ?? ""; } catch { /* no name */ }
        LoginWindow.LastUser = settings.LastUser;
        var login = new LoginWindow(db, program, permission, null, station);
        if (program == AppInfo.ImportName) login.AnyStation = true; // the music library is every station's
        if (program == AppInfo.ScheduleName) login.ChooseStation = true;
        login.LoggedIn += user =>
        {
            // Sintoniza Pro on the station chosen in the log-in (nothing is open yet: no need to start again).
            if (program == AppInfo.ScheduleName && login.ChosenStation != CurrentStation.Id)
            {
                long chosen = login.ChosenStation;
                CurrentStation.Id = chosen;
                UseStationTime(settings);
                settings.ProStation = chosen == settings.Station ? 0 : chosen;
                AppSettings.Change(s => s.ProStation = chosen == s.Station ? 0 : chosen);
                AppLog.Write($"{program}: working on station {chosen}");
            }
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
            // Its station's clock, from the station's own settings (the copy here may be another station's), before
            // any window works out what "today" is.
            try
            {
                string zone = (new StationStore(db).Load().Settings?.TimeZone ?? "").Trim();
                if (!StationTime.Use(zone)) AppLog.Write($"Sintoniza Pro: the station's time zone ({zone}) isn't known on this computer: its own clock is used");
                else if (!StationTime.IsComputers) AppLog.Write($"Sintoniza Pro: the station's time: {StationTime.Name}, {StationTime.Now:HH:mm}");
            }
            catch (Exception ex) { AppLog.Write($"Sintoniza Pro: the station's time zone not read ({ex.Message}): as the settings here say"); }
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

    /// <summary>
    /// The station's clock from the start: its time zone as this computer's copy of its settings says (only if that
    /// copy is this station's; Sintoniza Pro on another station takes that one's once it has read its settings).
    /// </summary>
    static void UseStationTime(AppSettings settings)
    {
        bool ours = settings.StationSettingsStation == CurrentStation.Id || settings.StationSettingsStation == 0 && CurrentStation.Id <= 1;
        string zone = ours ? settings.TimeZone : "";
        if (!StationTime.Use(zone)) AppLog.Write($"The station's time zone ({zone}) isn't known on this computer: its own clock is used");
        else if (!StationTime.IsComputers) AppLog.Write($"The station's time: {StationTime.Name}, {StationTime.Now:HH:mm} (this computer: {DateTime.Now:HH:mm})");
    }
}
