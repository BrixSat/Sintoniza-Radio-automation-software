using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Playout.Engine;

namespace Playout.App;

/// <summary>
/// Keeps the station on air when the app fails. Started normally, the app becomes a small supervisor (no window)
/// that runs the real app as a child process (<c>--child</c>) and starts it again if it crashes, or if its audio
/// engine stops responding (the child stops touching its heartbeat file). Closing the app normally ends both.
/// </summary>
static class Watchdog
{
    public const string ChildArg = "--child";
    /// <summary>Started again after one it stopped or saw fail: the copy before may still be letting go of its lock.</summary>
    public const string RetryArg = "--retry";
    /// <summary>
    /// The child's exit when another copy holds the program's lock (App: it couldn't take it in time). Not 3, which is
    /// also a native abort on Windows (a crash in a sound card's driver must still be started again, with alerts).
    /// </summary>
    public const int LockHeldExit = 167;
    const string NoWatchdogArg = "--no-watchdog";
    const double HeartbeatSeconds = 5;       // the child touches its file this often…
    const double EngineStuckSeconds = 20;    // …unless the engine thread has been stuck this long
    const double HungSeconds = 30;           // the supervisor restarts a child whose file is older than this
    const double StartupGraceSeconds = 60;   // time to start before the heartbeat counts

    static PlayoutEngine? _engine;
    static List<PosixSignalRegistration?>? _signals; // kept alive: a collected registration stops handling its signal

    /// <summary>True when this process should supervise rather than be the app.</summary>
    public static bool ShouldSupervise(string[] args) =>
        !args.Contains(ChildArg) && !args.Contains(NoWatchdogArg) && AppSettings.Load().RestartAfterCrash
        // Only the playout: the schedule, import and playlist programs are just closed if they fail.
        && !args.Contains(AppInfo.ScheduleArg) && !args.Contains(AppInfo.ImportArg) && !args.Contains(AppInfo.PlaylistsArg);

    static string HeartbeatFile(int pid) => Path.Combine(Path.GetTempPath(), $"RadioPlayout-{pid}.alive");

    /// <summary>Runs the app as a child process until it closes normally; returns its exit code.</summary>
    public static int Supervise(string[] args)
    {
        using var stop = new ManualResetEventSlim(); // the session is ending: don't start it again
        bool stopping() => stop.IsSet;
        _signals = new[] { PosixSignal.SIGTERM, PosixSignal.SIGHUP, PosixSignal.SIGINT }.Select(signal =>
        {
            try { return PosixSignalRegistration.Create(signal, c => { stop.Set(); c.Cancel = true; }); }
            catch (PlatformNotSupportedException) { return null; }
        }).ToList();

        var starts = new Queue<DateTime>();
        bool retry = false; // the first start isn't after one of ours: a lock held then is another copy's, running
        int lockHeld = 0;
        Process? stopped = null; // the copy this watchdog killed for not responding: until it has let go, the lock is its
        while (true)
        {
            starts.Enqueue(DateTime.UtcNow);
            while (starts.Count > 0 && DateTime.UtcNow - starts.Peek() > TimeSpan.FromMinutes(10)) starts.Dequeue();

            var started = DateTime.UtcNow;
            using var child = Process.Start(ChildStartInfo(args, retry));
            retry = true;
            if (child == null)
            {
                AppLog.Write("Watchdog: could not start the app");
                return 1;
            }
            // Timed on a monotonic clock: only *changes* of the file's time count, so the computer waking from sleep
            // or its clock being set never looks like a hang.
            var sinceBeat = Stopwatch.StartNew(); // since start, then since the file last changed
            DateTime lastTouch = default;
            string heartbeat = HeartbeatFile(child.Id);
            bool hung = false, beating = false;
            var sincePoll = Stopwatch.StartNew();
            while (!child.WaitForExit(2000))
            {
                // A check that comes far later than 2 s means the computer was asleep (Windows' clock counts sleep):
                // give the app time to beat again.
                if (sincePoll.Elapsed.TotalSeconds > 10) sinceBeat.Restart();
                sincePoll.Restart();
                var touched = File.Exists(heartbeat) ? File.GetLastWriteTimeUtc(heartbeat) : default;
                if (touched != lastTouch)
                {
                    lastTouch = touched;
                    beating = true;
                    sinceBeat.Restart();
                }
                if (stopping()) continue;
                if (sinceBeat.Elapsed.TotalSeconds < (beating ? HungSeconds : StartupGraceSeconds)) continue;
                hung = true;
                try { child.Kill(entireProcessTree: true); } catch { /* exited meanwhile */ }
                child.WaitForExit(5000);
                if (!child.HasExited)
                {
                    // Stuck where it can't be killed yet (a sound card's driver): kept, to know the lock is still its.
                    stopped?.Dispose();
                    try { stopped = Process.GetProcessById(child.Id); } catch { stopped = null; }
                }
                break;
            }
            try { File.Delete(heartbeat); } catch { /* already gone */ }

            if (child.HasExited && child.ExitCode == 0) return 0; // closed normally (or to update)
            if (stopping()) return child.HasExited ? child.ExitCode : 0;
            // Another copy of the program holds its lock, start after start: it is running (not one this watchdog
            // stopped, which would have let go by now). Nothing to look after here: no more starts, and no alerts.
            bool ownStuck = stopped is { } s0 && !HasExited(s0);
            if (stopped != null && !ownStuck) { stopped.Dispose(); stopped = null; }
            // Counted only when the lock isn't this watchdog's own stuck copy, and after the wait for it (not a crash at start).
            if (!hung && !ownStuck && child.HasExited && child.ExitCode == LockHeldExit && DateTime.UtcNow - started >= TimeSpan.FromSeconds(10))
            {
                if (++lockHeld >= 3)
                {
                    AppLog.Write("Watchdog: another copy of the program is running on this computer (it holds the lock): this one stops");
                    return 0;
                }
            }
            else lockHeld = 0;

            // Crashing straight away again and again (e.g. no sound card): wait longer between attempts.
            bool looping = starts.Count >= 5;
            AppLog.Write((hung ? $"Watchdog: the app has not responded for {HungSeconds:0} s"
                             : lockHeld > 0 ? "Watchdog: the app couldn't start: another copy holds its lock"
                             : ownStuck ? "Watchdog: the app couldn't start: the copy stopped before still holds its lock"
                             : $"Watchdog: the app stopped unexpectedly (exit code {child.ExitCode})") +
                         $"; starting it again{(looping ? " in 30 s" : "")}");
            // Not when it only found the lock still held (nothing went off air): the log has it.
            if (lockHeld == 0)
                AlertSender.SendFromWatchdog(hung ? L.F("The playout stopped responding for {0:0} s: it is being started again", HungSeconds)
                    : L.F("The playout stopped unexpectedly (exit code {0}): it is being started again", child.ExitCode));
            if (stop.Wait(looping ? 30_000 : 1000)) return 0;
        }
    }

    static bool HasExited(Process p)
    {
        try { return p.HasExited; }
        catch { return true; }
    }

    /// <summary>This program again, with the same arguments plus <see cref="ChildArg"/> (and <see cref="RetryArg"/> after the first).</summary>
    static ProcessStartInfo ChildStartInfo(string[] args, bool retry)
    {
        string exe = Environment.ProcessPath!;
        var info = new ProcessStartInfo(exe) { UseShellExecute = false };
        foreach (var (name, value) in GraphicsDriver.Environment) info.Environment.TryAdd(name, value);
        // Run with "dotnet SintonizaEmi.dll" (e.g. dotnet run): pass the dll too.
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
        foreach (var a in args) info.ArgumentList.Add(a);
        info.ArgumentList.Add(ChildArg);
        if (retry) info.ArgumentList.Add(RetryArg);
        return info;
    }

    /// <summary>In the child: touches the heartbeat file while the engine (once there is one) keeps running.</summary>
    public static void StartHeartbeat()
    {
        string file = HeartbeatFile(Environment.ProcessId);
        var thread = new Thread(() =>
        {
            while (true)
            {
                if (!_failed && _engine?.IsResponsive(EngineStuckSeconds) != false)
                {
                    try { File.WriteAllBytes(file, []); } catch { /* temp folder unavailable: nothing to do */ }
                }
                Thread.Sleep(TimeSpan.FromSeconds(HeartbeatSeconds));
            }
        }) { IsBackground = true, Name = "Heartbeat" };
        thread.Start();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { File.Delete(file); } catch { } };
    }

    /// <summary>The engine whose thread the heartbeat watches.</summary>
    public static void Watch(PlayoutEngine engine) => _engine = engine;

    /// <summary>
    /// The audio engine couldn't start (the sound card not ready yet at boot, a driver): the heartbeat stops, so the
    /// watchdog starts the program again (with its usual pauses) instead of leaving the station silent.
    /// </summary>
    public static void EngineFailed() => _failed = true;

    static volatile bool _failed;
}
