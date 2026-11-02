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

    /// <summary>Starts Sintoniza Pro again (it waits for this copy to let go); null, or why it can't.</summary>
    public static string? RestartPro()
    {
        try
        {
            // Run as "dotnet Sintoniza.Pro.dll" (a build, not the installed program): the .dll is the first argument.
            bool viaDotnet = Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet";
            var args = Environment.GetCommandLineArgs().Skip(viaDotnet ? 0 : 1).Where(a => a != AppInfo.RestartedArg).Append(AppInfo.RestartedArg);
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            foreach (var a in args) start.ArgumentList.Add(a);
            Process.Start(start);
            return null;
        }
        catch (Exception ex)
        {
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

    /// <summary>Pro works on another station: it starts again on it (asking first when there are changes not saved).</summary>
    async Task WorkOn(StationInfo station)
    {
        if (Changed && !await Dialogs.Confirm(this, L.T("Work on another station"),
                L.F("The changes not saved here are lost. Work on {0}?", station.Name), L.T("Work on it")))
            return;
        AppSettings.Change(s => s.ProStation = station.Id == s.Station ? 0 : station.Id); // the computer's own: follows it
        AppLog.Write($"Sintoniza Pro: working on {station.Name} (station {station.Id})");
        if (RestartPro() is { } error)
        {
            ErrorText.Text = L.F("Sintoniza Pro can't start again: {0}. Open it again by hand.", error);
            return;
        }
        _saved = Snapshot(); // nothing to ask about on the way out
        Close();
    }
}
