using Playout.Engine;

namespace Playout.App;

/// <summary>
/// Shows (Sintoniza Pro → Shows). A live one asks whoever is logged in on air whether they are presenting it (the
/// presenter view and the bar over the playlist): once they say so, the playout is ASSISTED (each item waits for the
/// presenter) until it ends or they log out, then AUTO again, and the playlist starts if nothing is on air. Nobody
/// logged in, nobody saying so, or "Keep AUTO": AUTO goes on (an alert when nobody said so in 2 minutes). An
/// automated show only names that part of the day.
/// </summary>
public partial class MainWindow
{
    /// <summary>How long a live show waits for the presenter to say they are there before the alert.</summary>
    static readonly TimeSpan PresenterWait = TimeSpan.FromMinutes(2);

    enum ShowMode { Automated, NobodyLoggedIn, Asking, Assisted, KeptAuto }

    /// <summary>The show on air as followed: which one and when it started (its times one after the other are one).</summary>
    (Guid Id, DateTime Start)? _showOn;
    (LiveShow Show, DateTime Start, DateTime End)? _show;
    ShowMode _showMode;
    /// <summary>When this computer started following the show (for the alert), and whether it was sent.</summary>
    DateTime _showFollowedAt;
    bool _showAlerted;

    /// <summary>The show on air, for the presenter view and the web remote: it waits for the presenter to say they are there.</summary>
    internal bool ShowAsking => _show != null && _showMode == ShowMode.Asking;
    /// <summary>A live show on air that nobody has said they are presenting (logged in or not): for the web remote.</summary>
    internal bool ShowWaiting => _show != null && _showMode is ShowMode.Asking or ShowMode.NobodyLoggedIn;

    /// <summary>The live show on air, for RDS and the website; null when none is.</summary>
    /// <summary>For the website's Test buttons: the live show, and the item after the one on air.</summary>
    internal ShowInfo? WebShow => ShowNow;
    internal Track? WebNext => NextItem();

    ShowInfo? ShowNow => _show is { } s ? new ShowInfo(s.Show.Name.Trim(), s.Show.Presenter.Trim(), StationTime.ToUtc(s.End).ToString("yyyy-MM-ddTHH:mm:ssZ")) : null;

    static string ShowName(LiveShow show) => show.Name.Trim().Length > 0 ? show.Name.Trim() : L.T("Show");

    /// <summary>Once a second: a show starting or ending, or the presenter logging in or out (on the studio on air only).</summary>
    void FollowShow(DateTime now)
    {
        if (_engine is not { } engine || _scheduler is not { } scheduler) return;
        var show = IsStandby ? null : scheduler.ShowAt(now);
        // The same show going on (its next time starting as one ends): not a new one.
        if (show is { } same && _showOn is { } followed && same.Show.Id == followed.Id) show = (same.Show, followed.Start, same.End);
        _show = show;
        (Guid, DateTime)? key = show is { } on ? (on.Show.Id, on.Start) : null;
        // ASSISTED left over from a show while the app was closed: undone once the schedule has been read.
        bool leftOver = key == null && _showOn == null && _settings.AssistedForShow && scheduler.Loaded;
        if (key == _showOn && !leftOver)
        {
            if (show is { } going) FollowPresenter(going, now);
            return;
        }
        bool watched = _showOn != null;
        _showOn = key;
        if (show is { } started)
        {
            _showFollowedAt = now;
            _showAlerted = false;
            // Not ASSISTED until the presenter says so: the show before it straight into this one (and the player waiting
            // for its presenter: on with the playlist), or ASSISTED left from before the app closed (AUTO meanwhile).
            if (_settings.AssistedForShow) AutoOn(play: watched && _showMode == ShowMode.Assisted && Idle(engine));
            string name = ShowName(started.Show);
            if (!started.Show.Live)
            {
                _showMode = ShowMode.Automated;
                UpdateShowBar();
                ShowMessage(L.F("{0}, until {1:HH:mm}: an automated show, AUTO", name, started.End));
                return;
            }
            _showMode = _locked ? ShowMode.NobodyLoggedIn : ShowMode.Asking;
            UpdateShowBar();
            if (_locked) ShowMessage(L.F("{0}, until {1:HH:mm}: nobody is logged in on air, AUTO goes on (ASSISTED once the presenter logs in and says they are there)", name, started.End));
            else AskPresenter(started);
            return;
        }
        // Over (or it was on when the app closed): AUTO again, if the show made it ASSISTED.
        var was = _showMode;
        _showMode = ShowMode.Automated;
        UpdateShowBar();
        if (!_settings.AssistedForShow)
        {
            if (watched) ShowMessage(IsStandby ? L.T("The show is over here: this computer is standby") : L.T("The show is over"));
            return;
        }
        if (IsStandby)
        {
            AutoOn(play: false);
            ShowMessage(L.T("The show is over here: this computer is standby"));
            return;
        }
        // Nothing on air when the presenter leaves: the playlist goes on by itself (not after a restart: the
        // settings say whether it resumes then).
        bool silent = watched && was == ShowMode.Assisted && Idle(engine);
        AutoOn(play: silent);
        ShowMessage(silent ? L.T("The show is over: AUTO, the playlist starts") : L.T("The show is over: AUTO"));
    }

    /// <summary>A live show going on: the presenter logging in or out, and nobody saying they are there in time.</summary>
    void FollowPresenter((LiveShow Show, DateTime Start, DateTime End) show, DateTime now)
    {
        if (!show.Show.Live || _showMode is ShowMode.Automated) return;
        string name = ShowName(show.Show);
        if (_locked && _showMode is ShowMode.Asking or ShowMode.Assisted or ShowMode.KeptAuto)
        {
            // Logged out: whoever said they were there has left; AUTO again (and on with the playlist if it waits).
            bool assisted = _showMode == ShowMode.Assisted;
            if (_showMode != ShowMode.Asking) _showAlerted = true; // they were there: no "nobody came" alert
            _showMode = ShowMode.NobodyLoggedIn;
            UpdateShowBar();
            if (assisted)
            {
                bool silent = _engine is { } e && Idle(e);
                AutoOn(play: silent);
                ShowMessage(silent ? L.F("Logged out during {0}: AUTO again, the playlist starts", name) : L.F("Logged out during {0}: AUTO again", name));
            }
            return;
        }
        if (!_locked && _showMode == ShowMode.NobodyLoggedIn)
        {
            _showMode = ShowMode.Asking;
            // Logged in late: a minute at least to answer before the alert.
            if (now - _showFollowedAt > PresenterWait - TimeSpan.FromMinutes(1)) _showFollowedAt = now - PresenterWait + TimeSpan.FromMinutes(1);
            AskPresenter(show);
            return;
        }
        if (_showMode is ShowMode.Asking or ShowMode.NobodyLoggedIn && !_showAlerted && now - _showFollowedAt >= PresenterWait)
        {
            _showAlerted = true;
            string text = L.F("{0} started at {1:HH:mm} and nobody said they are presenting it: it goes on in AUTO", name, show.Start);
            ShowMessage(text);
            _health?.Report("show", text);
        }
    }

    /// <summary>A live show and someone logged in on air: is it them presenting it?</summary>
    void AskPresenter((LiveShow Show, DateTime Start, DateTime End) show)
    {
        string name = ShowName(show.Show);
        ShowMessage(show.Show.Presenter.Trim() is { Length: > 0 } presenter
            ? L.F("{0} with {1}, until {2:HH:mm}: AUTO until the presenter says they are there", name, presenter, show.End)
            : L.F("{0}, until {1:HH:mm}: AUTO until the presenter says they are there", name, show.End));
        UpdateShowBar();
        OpenPresenterView();
    }

    /// <summary>"I'm here": the presenter is in the studio, ASSISTED for the rest of the show.</summary>
    internal void PresenterHere()
    {
        if (_show is not { } show || _showMode is not (ShowMode.Asking or ShowMode.KeptAuto)) return;
        _showMode = ShowMode.Assisted;
        _showAlerted = true;
        AutoSwitch.IsChecked = false; // ASSISTED: the presenter starts each item
        _settings.AssistedForShow = true;
        ShowMessage(L.F("{0} with {1}, until {2:HH:mm}: ASSISTED", ShowName(show.Show), _user?.Name ?? show.Show.Presenter.Trim(), show.End));
        UpdateShowBar();
    }

    /// <summary>"Keep AUTO": someone is there but the show goes on by itself.</summary>
    internal void PresenterKeepsAuto()
    {
        if (_show is not { } show || _showMode != ShowMode.Asking) return;
        _showMode = ShowMode.KeptAuto;
        _showAlerted = true;
        // ASSISTED from before (by hand, or the web remote): AUTO now, and on with the playlist if it waits.
        bool wasAssisted = AutoSwitch.IsChecked != true;
        AutoOn(play: wasAssisted && _engine is { } engine && Idle(engine));
        ShowMessage(L.F("{0}: AUTO, as asked", ShowName(show.Show)));
    }

    /// <summary>Nothing on air: stopped, or paused (Play resumes it).</summary>
    static bool Idle(PlayoutEngine engine) => engine.State is PlayerState.Stopped or PlayerState.Paused;

    /// <summary>AUTO again after a show's ASSISTED (and the playlist on, if it waits).</summary>
    void AutoOn(bool play)
    {
        _settings.AssistedForShow = false;
        AutoSwitch.IsChecked = true;
        if (play) _engine?.Play();
        UpdateShowBar();
    }

    /// <summary>The bar over the playlist that asks whether the presenter is there.</summary>
    void UpdateShowBar()
    {
        ShowBar.IsVisible = ShowAsking;
        if (_show is { } show) ShowBarText.Text = L.F("{0} is on air until {1:HH:mm}. Are you presenting it? Until you say so, AUTO goes on.", ShowName(show.Show), show.End);
    }

    void ShowMessage(string message)
    {
        LastLog.Text = message;
        AppLog.Write(message);
    }
}

/// <summary>What the presenter view shows, read from the on-air screen a few times a second.</summary>
public sealed record PresenterState(
    string ShowName, string Presenter, DateTime? ShowEnds, string ShowNotes, bool Asking, bool KeptAuto,
    PlayerState State, bool Auto, Track? OnAir, double Remaining, bool OpenEnded, double? TalkUp,
    IReadOnlyList<Track> Next, string? NoteNow, string? NoteNext, (DateTime At, string Name)? Break);

public partial class MainWindow
{
    PresenterWindow? _presenter;

    void OpenPresenterView()
    {
        if (_presenter is { } open) { Front(open); return; }
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
            show?.Show.Name.Trim() ?? "", show?.Show.Presenter.Trim() ?? "", show?.End, show?.Show.Notes.Trim() ?? "", ShowAsking, _show != null && _showMode == ShowMode.KeptAuto,
            st.State, AutoSwitch.IsChecked == true, on, Math.Max(0, end - st.Position), on?.IsOpenEnded == true, talkUp,
            next, Note(on), Note(next.FirstOrDefault()), _nextBreak is { } b ? (b.At, b.Name) : null);
    }

    public void PresenterPlay() => PlayPressed();
    public Task PresenterNext() => NextPressed();
    public Task PresenterStop() => StopPressed();
}
