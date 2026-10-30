using Playout.Engine;

namespace Playout.App;

/// <summary>
/// Live shows (Sintoniza Pro → Shows): while one is on, the playout is ASSISTED (each item waits for the presenter)
/// and the presenter view is open; when it ends, AUTO again, and the playlist starts if nothing is on air.
/// </summary>
public partial class MainWindow
{
    /// <summary>The show on air as followed: which one and when it started (a show twice a day is two).</summary>
    (Guid Id, DateTime Start)? _showOn;
    (LiveShow Show, DateTime Start, DateTime End)? _show;

    /// <summary>The live show on air, for RDS and the website; null when none is.</summary>
    ShowInfo? ShowNow => _show is { } s ? new ShowInfo(s.Show.Name.Trim(), s.Show.Presenter.Trim(), s.End.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")) : null;

    /// <summary>Once a second: a show starting or ending switches the mode (on the studio on air only).</summary>
    void FollowShow(DateTime now)
    {
        if (_engine is not { } engine || _scheduler is not { } scheduler) return;
        var show = IsStandby ? null : scheduler.ShowAt(now);
        _show = show;
        (Guid, DateTime)? key = show is { } on ? (on.Show.Id, on.Start) : null;
        // ASSISTED left over from a show while the app was closed: undone once the schedule has been read.
        bool leftOver = key == null && _showOn == null && _settings.AssistedForShow && scheduler.Loaded;
        if (key == _showOn && !leftOver) return;
        bool watched = _showOn != null;
        _showOn = key;
        if (show is { } started)
        {
            AutoSwitch.IsChecked = false; // ASSISTED: the presenter starts each item
            _settings.AssistedForShow = true;
            string name = started.Show.Name.Trim().Length > 0 ? started.Show.Name.Trim() : L.T("Show");
            ShowMessage(started.Show.Presenter.Trim() is { Length: > 0 } presenter
                ? L.F("{0} with {1}, until {2:HH:mm}: ASSISTED", name, presenter, started.End)
                : L.F("{0}, until {1:HH:mm}: ASSISTED", name, started.End));
            OpenPresenterView();
            return;
        }
        // Over (or it was on when the app closed): AUTO again.
        _settings.AssistedForShow = false;
        AutoSwitch.IsChecked = true;
        if (IsStandby)
        {
            ShowMessage(L.T("The show is over here: this computer is standby"));
            return;
        }
        // Nothing on air when the presenter leaves: the playlist goes on by itself (not after a restart: the
        // settings say whether it resumes then).
        bool silent = watched && engine.State == PlayerState.Stopped;
        if (silent) engine.Play();
        ShowMessage(silent ? L.T("The show is over: AUTO, the playlist starts") : L.T("The show is over: AUTO"));
    }

    void ShowMessage(string message)
    {
        LastLog.Text = message;
        AppLog.Write(message);
    }
}

/// <summary>What the presenter view shows, read from the on-air screen a few times a second.</summary>
public sealed record PresenterState(
    string ShowName, string Presenter, DateTime? ShowEnds, string ShowNotes,
    PlayerState State, bool Auto, Track? OnAir, double Remaining, bool OpenEnded, double? TalkUp,
    IReadOnlyList<Track> Next, string? NoteNow, string? NoteNext, (DateTime At, string Name)? Break);

public partial class MainWindow
{
    PresenterWindow? _presenter;

    void OpenPresenterView()
    {
        if (_presenter is { } open) { open.Activate(); return; }
        _presenter = new PresenterWindow(this);
        _presenter.Closed += (_, _) => _presenter = null;
        _presenter.Show();
    }

    public PresenterState PresenterNow()
    {
        var st = _engine?.GetStatus() ?? default;
        var on = st.State == PlayerState.Stopped ? null : st.Current;
        double end = on != null ? Math.Min(on.End, st.Length) : 0;
        // Talk-up: until the vocals come in (the Intro marker).
        double? talkUp = on?.Cues?.Intro is { } intro && intro > st.Position ? intro - st.Position : null;
        var next = new List<Track>();
        if (_snapshot is { } snap)
        {
            int from = snap.NextId is { } id ? snap.Items.ToList().FindIndex(t => t.Id == id) : -1;
            if (from >= 0) next.AddRange(snap.Items.Skip(from).Take(3));
        }
        string? Note(Track? t) => t == null ? null : t.Note ?? (t.IsPause ? t.Title : null);
        var show = _show;
        return new PresenterState(
            show?.Show.Name.Trim() ?? "", show?.Show.Presenter.Trim() ?? "", show?.End, show?.Show.Notes.Trim() ?? "",
            st.State, AutoSwitch.IsChecked == true, on, Math.Max(0, end - st.Position), on?.IsOpenEnded == true, talkUp,
            next, Note(on), Note(next.FirstOrDefault()), _nextBreak is { } b ? (b.At, b.Name) : null);
    }

    public void PresenterPlay() => PlayPressed();
    public Task PresenterNext() => NextPressed();
    public Task PresenterStop() => StopPressed();
}
