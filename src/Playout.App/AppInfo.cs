namespace Playout.App;

/// <summary>The product name shown to users. The executable, package and settings folder keep the name RadioPlayout, so existing installs and settings carry on.</summary>
public static class AppInfo
{
    public const string Name = "Sintoniza Radio Broadcast system";
    /// <summary>The schedule program: events, ad breaks and clients, kept apart from the on-air screen.</summary>
    public const string ScheduleName = "Sintoniza Schedule";
    /// <summary>Starts the app as the schedule program.</summary>
    public const string ScheduleArg = "--schedule";
    /// <summary>Its window class on Linux (StartupWMClass in its .desktop file).</summary>
    public const string ScheduleWmClass = "SintonizaSchedule";
}
