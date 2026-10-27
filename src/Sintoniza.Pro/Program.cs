namespace Sintoniza.Pro;

/// <summary>Sintoniza Pro: the station's settings, categories, cartwalls, schedule (events, ad breaks, clients, hour templates, daily playlists), as its own program.</summary>
static class Program
{
    [STAThread]
    public static int Main(string[] args) =>
        Playout.App.Program.Main(args.Contains(Playout.App.AppInfo.ScheduleArg) || args.Contains(Playout.App.AppInfo.PlaylistsArg)
            ? args : [Playout.App.AppInfo.ScheduleArg, .. args]);
}
