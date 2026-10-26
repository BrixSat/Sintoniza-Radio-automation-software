using System.Reflection;
using Avalonia.Threading;

namespace Playout.App;

public partial class MainWindow
{
    Timer? _screenWatch;
    long _lastTick = Environment.TickCount64, _lastScreenReport;

    /// <summary>
    /// From a thread of its own, every second: has the screen been updated in the last 3 s? On some computers the
    /// window's event loop stopped waking up — the screen froze while the sound went on — and only an event on the
    /// window (bringing it to the front) moved it again. Then what the event loop was doing goes in the log (at most
    /// once a minute, to find why) and it is woken: the screen's timer started again.
    /// </summary>
    void WatchScreen()
    {
        long now = Environment.TickCount64, since = now - Volatile.Read(ref _lastTick);
        if (since < 3000) return;
        if (now - _lastScreenReport > 60_000)
        {
            _lastScreenReport = now;
            AppLog.Write($"The screen stopped updating {since / 1000.0:0.0} s ago; woken. {DispatcherState()}");
        }
        try
        {
            // A wake-up that was asked for and never came leaves the loop waiting: it is asked for again.
            var dispatcher = Dispatcher.UIThread;
            if (Field(dispatcher, "_signaled") is true && Field(dispatcher, "_impl") is { } impl)
                impl.GetType().GetMethod("Signal")?.Invoke(impl, null);
            dispatcher.Post(() =>
            {
                // The loop's own timer believed set (for a time long past) is set again, then the screen's.
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(Dispatcher).GetField("_osTimerSetTo", flags)?.SetValue(dispatcher, null);
                typeof(Dispatcher).GetMethod("RescheduleTimers", flags)?.Invoke(dispatcher, null);
                typeof(Dispatcher).GetMethod("UpdateOSTimer", flags)?.Invoke(dispatcher, null);
                _fastTimer.Stop();
                _fastTimer.Start();
                Tick();
            }, DispatcherPriority.Send);
        }
        catch (Exception ex)
        {
            AppLog.Write($"The screen couldn't be woken: {ex.Message}");
        }
    }

    /// <summary>The event loop's state, for the log (Avalonia's own fields: whatever is there).</summary>
    static string DispatcherState()
    {
        try
        {
            var d = Dispatcher.UIThread;
            var impl = Field(d, "_impl");
            var timers = Field(d, "_timers") as System.Collections.ICollection;
            var queue = Field(d, "_queue");
            string Show(object? o, string name) => $"{name}={Field(o, name) ?? "null"}";
            return string.Join(", ",
                Show(d, "_signaled"), Show(d, "_osTimerSetTo"), Show(d, "_dueTimeForTimers"), Show(d, "_dueTimeForBackgroundProcessing"),
                Show(d, "_explicitBackgroundProcessingRequested"), $"timers={timers?.Count}",
                $"queue={queue?.GetType().GetProperty("MaxPriority", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(queue)}",
                $"now={d.GetType().GetProperty("Now", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(d)}",
                $"impl={impl?.GetType().Name}", Show(impl, "_nextTimer"), Show(impl, "_signaled"), Show(impl, "_wakeupRequested"));
        }
        catch (Exception ex)
        {
            return "state unknown: " + ex.Message;
        }
    }

    static object? Field(object? o, string name) =>
        o?.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(o);
}
