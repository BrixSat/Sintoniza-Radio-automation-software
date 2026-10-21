namespace Playout.App;

/// <summary>
/// The names shown to users. The on-air program is Sintoniza Emi (its executable SintonizaEmi); the packages, install
/// folders and settings folder keep the name RadioPlayout / radio-playout, so existing installs and settings carry on.
/// </summary>
public static class AppInfo
{
    /// <summary>The on-air program (from "emissão").</summary>
    public const string Name = "Sintoniza Emi";
    /// <summary>Its window class on Linux (StartupWMClass in its .desktop file).</summary>
    public const string WmClass = "SintonizaEmi";
    /// <summary>The station's production program: its settings, library, categories, cartwalls, schedule and traffic.</summary>
    public const string ScheduleName = "Sintoniza Pro";
    /// <summary>Starts the app as the schedule program.</summary>
    public const string ScheduleArg = "--schedule";
    /// <summary>Its window class on Linux (StartupWMClass in its .desktop file).</summary>
    public const string ScheduleWmClass = "SintonizaPro";
    /// <summary>The import program: folders of audio into the library, in categories.</summary>
    public const string ImportName = "Sintoniza Import";
    /// <summary>Starts the app as the import program.</summary>
    public const string ImportArg = "--import";
    /// <summary>Its window class on Linux (StartupWMClass in its .desktop file).</summary>
    public const string ImportWmClass = "SintonizaImport";
    /// <summary>The recorder: the station's sound kept as the law asks (its own program, SintonizaRec).</summary>
    public const string RecName = "Sintoniza Rec";
    /// <summary>Starts the app as the recorder.</summary>
    public const string RecArg = "--rec";
    /// <summary>Its window class on Linux (StartupWMClass in its .desktop file).</summary>
    public const string RecWmClass = "SintonizaRec";
    /// <summary>Where users can support the project (Settings → About, Pro's Help menu, Rec).</summary>
    public const string SupportUrl = "https://buymeacoffee.com/sintoniza";
    /// <summary>Opens the schedule program on its daily playlists (what was Sintoniza Playlists).</summary>
    public const string PlaylistsArg = "--playlists";

    /// <summary>This computer's name as shown (SINTONIZA_COMPUTER: another, e.g. for the manual's screenshots).</summary>
    public static string ComputerName => Environment.GetEnvironmentVariable("SINTONIZA_COMPUTER") is { Length: > 0 } c ? c : Environment.MachineName;
    /// <summary>The user as shown (SINTONIZA_USER: another).</summary>
    public static string UserName => Environment.GetEnvironmentVariable("SINTONIZA_USER") is { Length: > 0 } u ? u : Environment.UserName;
}
