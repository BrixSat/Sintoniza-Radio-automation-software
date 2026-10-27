using Avalonia.Threading;
using Playout.Engine;

namespace Playout.App;

/// <summary>On air or standby: several computers running Sintoniza Emi for one station (see <see cref="StudioRole"/>).</summary>
public partial class MainWindow
{
    StudioRole? _role;
    bool _tookOnAir; // Take on air was pressed here: the playlist made here is kept

    /// <summary>Another computer is on air: nothing goes on air from this one.</summary>
    bool IsStandby => _role?.Standby == true;

    /// <summary>Before the playlist is put back: whether this computer starts standby (it was, and the library is shared).</summary>
    void StartStudioRole()
    {
        if (_engine is not { } engine) return;
        _role = new StudioRole(_library, _settings.WasStandby && _settings.Library.IsMySql,
            () => engine.GetStatus().Current is { } t && engine.State == PlayerState.Playing ? t.Display : "",
            () => (_settings.TakeOver, _settings.TakeOverSeconds));
        SetStandby(_role.Standby);
        _role.Changed += (standby, why) => Dispatcher.UIThread.Post(() => ApplyRole(standby, why));
        _role.Seen += () => Dispatcher.UIThread.Post(ShowStudios);
        TakeOnAirButton.Click += async (_, _) => await AskTakeOnAir();
        _role.Start();
        ShowStudios();
    }

    /// <summary>Everything that would go on air from here: the engine, the daily playlists, breaks and events, RDS and the website.</summary>
    void SetStandby(bool standby)
    {
        if (_engine is { } engine) engine.Standby = standby;
        if (_plan != null) _plan.Standby = standby;
        if (_scheduler != null) _scheduler.Standby = standby;
        ApplyRds();
        ApplyWebNowPlaying();
    }

    /// <summary>
    /// With a shared library, before carrying on after a restart: whether this computer is on air (at most a few
    /// seconds; with the database away, as it was).
    /// </summary>
    async Task WaitForRole()
    {
        if (_role is { } role && _settings.Library.IsMySql) await Task.WhenAny(role.Known, Task.Delay(TimeSpan.FromSeconds(8)));
    }

    /// <summary>This computer went on air or standby.</summary>
    void ApplyRole(bool standby, string why)
    {
        if (_engine is not { } engine) return;
        if (why.Length > 0)
        {
            AppLog.Write(why);
            LastLog.Text = why;
        }
        _settings.WasStandby = standby;
        SetStandby(standby);
        if (standby)
        {
            // The stream is the one on air's (its setting stays as it was, for when this computer goes on air).
            if (engine.GetStatus().Stream is StreamState.OnAir or StreamState.Connecting) engine.StopStreaming();
        }
        else
        {
            // At start the saved playlist is being put back: that carries on (CarryOn), not this.
            if (_restored) _ = GoOnAir(_tookOnAir);
            _tookOnAir = false;
            if (_settings.StreamingOn && _settings.UsesStreaming && !string.IsNullOrWhiteSpace(_settings.StreamHost)
                && engine.GetStatus().Stream is StreamState.Off or StreamState.Failed) ToggleStreaming();
        }
        ShowStudios();
    }

    /// <summary>
    /// Just gone on air: in AUTO it plays the hour now from the item planned for now. What was left in this computer's
    /// playlist goes, except, when Take on air was pressed here, what the presenter put in it.
    /// </summary>
    async Task GoOnAir(bool keepOwn)
    {
        if (_engine is not { } engine || !engine.AutoAdvance) return;
        bool own = keepOwn && _order.Any(r => !r.IsPlayed && !r.IsPlaying && !r.IsPreview && r.Track.Event == null && PlanPlayer.ReadTag(r.Track.Tag) == null);
        if (!own)
        {
            engine.Clear();
            if (_plan != null) await _plan.LoadNow();
        }
        engine.Play();
    }

    /// <summary>Take on air (or Play while standby): asked first, as the computer on air stops.</summary>
    async Task AskTakeOnAir()
    {
        if (_role is not { } role) return;
        string other = role.Studios?.OnAir?.Computer ?? L.T("another computer");
        if (!await Dialogs.Confirm(this, L.T("Take on air"),
                L.F("{0} is on air. Put this computer on air instead? {0} goes standby and stops playing.", other), L.T("Take on air")))
            return;
        try
        {
            _tookOnAir = true;
            await role.TakeOnAir();
        }
        catch (Exception ex)
        {
            _tookOnAir = false;
            LastLog.Text = L.F("Can't go on air: {0}", ex.Message);
        }
    }

    /// <summary>The standby banner: which computer is on air, what it is playing, whether it answers.</summary>
    void ShowStudios()
    {
        var list = _role?.Studios;
        StandbyBanner.IsVisible = IsStandby;
        if (!IsStandby) return;
        if (list?.OnAir is not { } onAir)
        {
            StandbyText.Text = L.T("Another computer is on air. This one plays nothing until it goes on air.");
            return;
        }
        double silent = list.Silent(onAir).TotalSeconds;
        string state = silent >= 6 ? L.F("{0} hasn't answered for {1:0} s.", onAir.Computer, silent)
            : onAir.Playing.Length > 0 ? L.F("On air: {0}, playing {1}.", onAir.Computer, onAir.Playing)
            : L.F("On air: {0}.", onAir.Computer);
        string then = _settings.TakeOver
            ? L.F("This computer goes on air by itself if it stops answering for {0} s.", Math.Max(5, _settings.TakeOverSeconds))
            : L.T("This computer goes on air only when you press Take on air.");
        StandbyText.Text = $"{state} {then}";
    }
}
