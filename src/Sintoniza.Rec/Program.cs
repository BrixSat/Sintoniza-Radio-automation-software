namespace Sintoniza.Rec;

/// <summary>Sintoniza Rec: records the station's sound around the clock, as its own program.</summary>
static class Program
{
    [STAThread]
    public static int Main(string[] args) =>
        Playout.App.Program.Main(args.Contains(Playout.App.AppInfo.RecArg) ? args : [Playout.App.AppInfo.RecArg, .. args]);
}
