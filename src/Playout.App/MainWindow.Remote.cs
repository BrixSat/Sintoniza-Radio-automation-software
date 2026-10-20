using System.Text.Json.Nodes;
using Avalonia.Threading;
using Playout.Engine;
using Playout.Library;

namespace Playout.App;

/// <summary>The web remote (Settings → Remote): what it shows, and what it does, as the buttons on screen do.</summary>
public partial class MainWindow : IRemoteHandler
{
    RemoteServer? _remote;
    RemoteSettings? _remoteRunning;

    /// <summary>Starts, restarts or stops the web remote as the settings say.</summary>
    void ApplyRemote()
    {
        var wanted = _settings.Remote;
        if (_remote != null && _remoteRunning is { } running && running.Enabled == wanted.Enabled && running.Port == wanted.Port
            && running.Password == wanted.Password) return;
        _remote?.Dispose();
        _remote = null;
        _remoteRunning = new RemoteSettings { Enabled = wanted.Enabled, Port = wanted.Port, Password = wanted.Password };
        if (!wanted.Enabled) return;
        try
        {
            _remote = new RemoteServer(wanted, this, message =>
            {
                AppLog.Write(message);
                Dispatcher.UIThread.Post(() => LastLog.Text = message);
            });
            AppLog.Write($"Web remote on port {wanted.Port}");
        }
        catch (Exception ex)
        {
            LastLog.Text = L.F("Web remote not started: {0}", ex.Message);
            AppLog.Write($"Web remote not started: {ex.Message}");
        }
    }

    public Task<object> Status() => Dispatcher.UIThread.InvokeAsync<object>(StatusNow).GetTask();

    object StatusNow()
    {
        var st = _engine?.GetStatus() ?? default;
        var rows = _order.Where(r => !r.IsPlayed && !r.IsPlaying).Take(12).Select(r => new
        {
            id = r.IsPreview ? null : r.Id.ToString(),
            start = r.StartTime,
            title = r.Title,
            artist = r.Artist,
            length = r.Length,
            tag = r.IsPreview ? Strings.Scheduled : r.IsOverHour ? Strings.WontPlay : r.Track.Event,
            removable = !r.IsPreview && !r.IsLocked,
        }).ToList();
        var carts = _page.Carts.Select((c, i) => new
        {
            name = c.Path == null ? "" : Path.GetFileNameWithoutExtension(c.Path),
            color = c.Color,
            time = c.Path != null && CartLength(c.Path) is { } length ? Format.Duration(Math.Ceiling(length)) : "",
            playing = _playingCarts.ContainsKey((_page, i)),
        }).ToList();
        return new
        {
            station = _settings.StationName,
            clock = DateTime.Now.ToString("HH:mm:ss"),
            state = st.State switch { PlayerState.Playing => "playing", PlayerState.Paused => "paused", _ => "stopped" },
            stateText = st.State switch { PlayerState.Playing => L.T("ON AIR"), PlayerState.Paused => L.T("PAUSED"), _ => L.T("STOPPED") },
            auto = _engine?.AutoAdvance == true,
            onAir = st.Current is { } t && st.State != PlayerState.Stopped
                ? new { title = t.Title, artist = t.Artist, position = st.Position, length = st.Length, advert = t.Kind == TrackKind.Commercial }
                : null,
            nextBreak = _nextBreak is { } b ? new { at = b.At.ToString("HH:mm:ss"), name = b.Name } : null,
            stream = !_settings.UsesStreaming ? "none" : st.Stream switch { StreamState.OnAir => "on", StreamState.Failed => "error", _ => "off" },
            silence = st.Silence >= 5,
            next = rows,
            cartwall = _page.Name,
            carts,
        };
    }

    public Task<object> Command(JsonObject c) => Dispatcher.UIThread.InvokeAsync<object>(() => CommandNow(c)).GetTask();

    object CommandNow(JsonObject c)
    {
        if (_engine is not { } engine) return new { ok = false, message = L.T("The audio engine isn't running.") };
        string cmd = c["cmd"]?.GetValue<string>() ?? "";
        bool force = c["force"]?.GetValue<bool>() == true;
        var st = engine.GetStatus();
        // An advert on air is cut only when the remote says so too (as the question on screen).
        object? Guard(string question) => AdvertOnAir(st) && !force
            ? new { ok = false, confirm = L.F("An advert is on air: {0}.", st.Current!.Display) + " " + question }
            : null;
        Guid? Id() => Guid.TryParse(c["id"]?.GetValue<string>(), out var id) ? id : null;
        switch (cmd)
        {
            case "play":
                engine.Play();
                break;
            case "pause":
                if (st.State != PlayerState.Paused && Guard(L.T("Pause it anyway?")) is { } p) return p;
                engine.TogglePause();
                break;
            case "next":
                if (Guard(L.T("Cut it and go to the next item now?")) is { } n) return n;
                engine.Next();
                break;
            case "stop":
                if (Guard(L.T("Stop it anyway?")) is { } s) return s;
                engine.Stop();
                break;
            case "auto":
                AutoSwitch.IsChecked = c["on"]?.GetValue<bool>() == true; // as the switch on screen
                break;
            case "cart":
                int index = c["index"]?.GetValue<int>() ?? -1;
                if (index >= 0 && index < _page.Carts.Count) ToggleCart(index);
                break;
            case "playnow" when Id() is { } id:
                if (Guard(L.T("Cut it and put this on air now?")) is { } g) return g;
                engine.Play(id);
                break;
            case "remove" when Id() is { } id:
                engine.Remove(id);
                break;
            case "add":
                string path = c["path"]?.GetValue<string>() ?? "";
                if (!File.Exists(path)) return new { ok = false, message = L.F("Not found: {0}", path) };
                AddPaths([path], NextInsertIndex());
                break;
            default:
                return new { ok = false, message = L.F("Unknown command: {0}", cmd) };
        }
        LastLog.Text = L.F("Web remote: {0}", cmd);
        return new { ok = true };
    }

    /// <summary>A Sintoniza user with the web remote permission.</summary>
    public Task<string?> LogIn(string user, string password) => Task.Run(() =>
        _library.Db is { } db && new UserStore(db).LogIn(user, password) is { } u && u.Can(Permission.Remote) ? u.Name : null);

    public Task<object> Search(string text) => Task.Run<object>(() =>
    {
        if (_library.Db is not { } db || text.Trim().Length < 2) return new { songs = Array.Empty<object>() };
        var songs = db.Search(text, CategoryFilter.All, 30).Tracks.Where(t => !t.Missing)
            .Select(t => new { path = t.Path, title = t.Title.Length > 0 ? t.Title : Path.GetFileNameWithoutExtension(t.Path), artist = t.Artist, length = Format.Duration(t.Duration) })
            .ToList();
        return new { songs };
    });
}
