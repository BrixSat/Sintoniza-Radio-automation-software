using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace Playout.App;

/// <summary>A window with a line that says what went wrong (the on-air program's last message, Sintoniza Pro's status bar).</summary>
public interface IShowsProblems
{
    void ShowProblem(string text);
}

/// <summary>
/// A click that goes wrong says so where it was clicked, never nothing at all: the error is written to the log and to
/// the window's status line (the one in front, else the program's main window).
/// </summary>
public static class Problems
{
    static readonly List<Window> Open = new();

    /// <summary>A window that says problems, while it is open.</summary>
    public static void Watch<T>(T window) where T : Window, IShowsProblems
    {
        Open.Add(window);
        window.Closed += (_, _) => Open.Remove(window);
    }

    /// <summary>Says it on the window in front (from any thread).</summary>
    public static void Show(string text) => Dispatcher.UIThread.Post(() =>
    {
        var windows = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows ?? [];
        // The window in front, or the one that owns it (a dialog); else the last one opened that says problems.
        Window? w = windows.Concat(Open).FirstOrDefault(x => x.IsActive);
        while (w is not IShowsProblems && w?.Owner is Window owner) w = owner;
        var target = w as IShowsProblems ?? Open.Where(x => x.IsVisible).OfType<IShowsProblems>().LastOrDefault();
        target?.ShowProblem(text);
    });

    /// <summary>An error no handler caught: in the log, and said.</summary>
    public static void Unhandled(Exception ex)
    {
        AppLog.Write($"Error: {ex}");
        // Something cancelled (a window closed while it worked, a time-out): only in the log.
        if (ex.GetBaseException() is OperationCanceledException) return;
        string text = L.F("Something went wrong: {0} (the log has the details)", ex.GetBaseException().Message);
        // The same again and again (a timer that keeps failing): said once a minute, not over every other message.
        lock (Open)
        {
            if (text == _last && DateTime.UtcNow - _lastAt < TimeSpan.FromMinutes(1)) return;
            _last = text;
            _lastAt = DateTime.UtcNow;
        }
        Show(text);
    }

    static string? _last;
    static DateTime _lastAt;

    /// <summary>Work started from a click and not waited for: if it fails, it is said (not lost until the task is collected).</summary>
    public static void Report(this Task task) =>
        task.ContinueWith(t => Unhandled(t.Exception!.GetBaseException()), CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
