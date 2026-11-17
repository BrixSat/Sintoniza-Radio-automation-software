namespace Sintoniza.Online;

/// <summary>Sintoniza Online: sends the station's sound to the streaming server and keeps it there, as its own program.</summary>
static class Program
{
    [STAThread]
    public static int Main(string[] args) =>
        Playout.App.Program.Main(args.Contains(Playout.App.AppInfo.OnlineArg) ? args : [Playout.App.AppInfo.OnlineArg, .. args]);
}
