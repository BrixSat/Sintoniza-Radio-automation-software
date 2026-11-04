namespace Sintoniza.Tempo;

/// <summary>Sintoniza Tempo: the studio clock, as its own program.</summary>
static class Program
{
    [STAThread]
    public static int Main(string[] args) =>
        Playout.App.Program.Main(args.Contains(Playout.App.AppInfo.TempoArg) ? args : [Playout.App.AppInfo.TempoArg, .. args]);
}
