using System.Diagnostics;
using Avalonia.Controls;
using Playout.Library;

namespace Playout.App;

/// <summary>
/// Sintoniza Pro → Station: the stations in the library. Pro works on one at a time (this computer's choice, its own:
/// the studio's on-air program keeps its station); the music library is shared by all.
/// </summary>
public partial class ScheduleWindow
{
    void WireStations(LibraryDb? db)
    {
        MenuWorkOn.IsEnabled = MenuNewStation.IsEnabled = db != null;
        if (db == null) return;
        MenuWorkOn.SubmenuOpened += async (_, _) => await ShowStations(db);
        // Something in the submenu, so it opens (and is filled) at all.
        MenuWorkOn.Items.Add(new MenuItem { Header = "…", IsEnabled = false });
        MenuNewStation.Click += async (_, _) => await NewStation(db);
    }

    async Task ShowStations(LibraryDb db)
    {
        List<StationInfo> stations;
        try { stations = await Task.Run(db.Stations); }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            return;
        }
        MenuWorkOn.Items.Clear();
        foreach (var station in stations.Where(s => _user == null || _user.MayUse(s.Id))) // only those this user may work on
        {
            bool current = station.Id == CurrentStation.Id;
            var item = new MenuItem { Header = (current ? "✓  " : "    ") + station.Name, IsEnabled = !current };
            item.Click += async (_, _) => await WorkOn(station);
            MenuWorkOn.Items.Add(item);
        }
    }

    /// <summary>Starts Sintoniza Pro again (it waits for this copy to let go), with <paramref name="user"/> still logged in; null, or why it can't.</summary>
    public static string? RestartPro(SintonizaUser? user = null)
    {
        try
        {
            // Run as "dotnet Sintoniza.Pro.dll" (a build, not the installed program): the .dll is the first argument.
            bool viaDotnet = Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet";
            var args = Environment.GetCommandLineArgs().Skip(viaDotnet ? 0 : 1)
                .Where(a => a != AppInfo.RestartedArg && !a.StartsWith(LoginHandoff.Arg, StringComparison.Ordinal)).Append(AppInfo.RestartedArg);
            if (user != null) args = args.Append(LoginHandoff.Give(user));
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            foreach (var a in args) start.ArgumentList.Add(a);
            Process.Start(start);
            return null;
        }
        catch (Exception ex)
        {
            LoginHandoff.Forget(); // no copy will take it
            return ex.Message;
        }
    }

    /// <summary>A new station: a name, then Pro works on it (with a copy of this station's settings, an empty schedule).</summary>
    async Task NewStation(LibraryDb db)
    {
        if (!May(Permission.Station))
        {
            ErrorText.Text = L.T("Making a station needs the Station permission.");
            return;
        }
        var name = await Dialogs.AskText(this, L.T("New station"),
            L.T("Its name. It starts with a copy of this station's settings, without its stream server, RDS name or website, and an empty schedule; the music library is shared."), "",
            t => t.Length == 0 ? L.T("Give it a name") : null);
        if (name == null) return;
        try
        {
            long id = await Task.Run(() => new StationStore(db).AddStation(name));
            var station = (await Task.Run(db.Stations)).FirstOrDefault(s => s.Id == id) ?? new StationInfo(id, name);
            await WorkOn(station);
        }
        catch (Exception ex)
        {
            ErrorText.Text = L.F("The station can't be made: {0}", ex.Message);
        }
    }

    /// <summary>The station chosen in a log-in (by its number), as that user: null once it is starting again, else why not.</summary>
    async Task<string?> WorkOn(long id, SintonizaUser user)
    {
        if (_store?.Db is not { } db) return L.T("The database can't be reached.");
        StationInfo? station;
        try { station = (await Task.Run(db.Stations)).FirstOrDefault(s => s.Id == id); }
        catch (Exception ex) { return L.F("The stations can't be read: {0}", ex.Message); }
        return station == null ? L.T("That station isn't there any more.") : await WorkOn(station, user);
    }

    /// <summary>
    /// Pro works on another station: it starts again on it, still logged in (asking first when there are changes not
    /// saved). Null once it is starting again; else why not (also shown).
    /// </summary>
    async Task<string?> WorkOn(StationInfo station, SintonizaUser? user = null)
    {
        if (Changed && !await Dialogs.Confirm(this, L.T("Work on another station"),
                L.F("The changes not saved here are lost. Work on {0}?", station.Name), L.T("Work on it")))
            return L.T("Not changed: there are changes not saved here.");
        var who = user ?? _user;
        long before = AppSettings.Load().ProStation;
        AppSettings.Change(s =>
        {
            s.ProStation = station.Id == s.Station ? 0 : station.Id; // the computer's own: follows it
            if (who != null) s.LastUser = who.Name;
        });
        AppLog.Write($"Sintoniza Pro: working on {station.Name} (station {station.Id})");
        if (RestartPro(who) is { } error)
        {
            AppSettings.Change(s => s.ProStation = before); // this copy stays on its station
            string message = L.F("Sintoniza Pro can't start again: {0}. Open it again by hand.", error);
            ErrorText.Text = message;
            return message;
        }
        _saved = Snapshot(); // nothing to ask about on the way out
        Close();
        return null;
    }
}
