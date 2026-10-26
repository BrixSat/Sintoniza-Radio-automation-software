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
        // As the playlist on screen: the item on air and what comes after it.
        var rows = _order.Where(r => r.IsPlaying || !r.IsPlayed).Take(40).Select(r => new
        {
            id = r.IsPreview ? null : r.Id.ToString(),
            number = r.NumberText,
            start = r.StartTime,
            title = r.Title,
            artist = r.Artist,
            length = r.Length,
            kind = r.IsStream ? "stream" : r.Track.Kind.ToString().ToLowerInvariant(),
            playing = r.IsPlaying,
            next = r.IsNext,
            preview = r.IsPreview,
            adBreak = r.IsAdBreak,
            overHour = r.IsOverHour,
            hourMark = r.HourMark,
            eventTag = r.HasEvent ? r.EventTag : "",
            locked = r.IsLocked,
            cues = r.HasCues,
            removable = !r.IsPreview && !r.IsLocked && !r.IsPlaying,
        }).ToList();
        var carts = _page.Carts.Select((c, i) => new
        {
            name = c.Path == null ? "" : Path.GetFileNameWithoutExtension(c.Path),
            color = c.Color,
            icon = CartLook.Icons.FirstOrDefault(i => i.Key == c.Icon).Data,
            time = c.Path != null && CartLength(c.Path) is { } length ? Format.Duration(Math.Ceiling(length)) : "",
            playing = _playingCarts.ContainsKey((_page, i)),
        }).ToList();
        return new
        {
            station = _settings.StationName,
            logo = LogoImage.Source != null,
            clock = DateTime.Now.ToString("HH:mm:ss"),
            date = DateText.Text,
            topOfHour = TopOfHourText.Text,
            playlistEnds = PlaylistEndsText.Text,
            playedToday = PlayedTodayText.Text,
            streamTime = StreamTimePanel.IsVisible ? StreamTimeText.Text : null,
            nextEvent = NextEventPanel.IsVisible ? NextEventText.Text : null,
            state = st.State switch { PlayerState.Playing => "playing", PlayerState.Paused => "paused", _ => "stopped" },
            stateText = OnAirText.Text,
            auto = _engine?.AutoAdvance == true,
            onAir = st.Current is { } t && st.State != PlayerState.Stopped
                ? new { id = t.Id.ToString(), title = t.Title, artist = t.Artist, position = st.Position, length = st.Length, advert = t.Kind == TrackKind.Commercial }
                : null,
            nextTitle = NextTitle.Text,
            cue = CueBadge.IsVisible ? new { label = CueLabel.Text, time = CueTime.Text } : null,
            silenceTime = SilenceBadge.IsVisible ? SilenceTime.Text : null,
            breakIn = BreakBadge.IsVisible ? new { time = BreakTime.Text, how = BreakHowText.Text } : null,
            nextBreak = _nextBreak is { } b ? new { at = b.At.ToString("HH:mm:ss"), name = b.Name } : null,
            stream = !_settings.UsesStreaming ? "none" : st.Stream switch { StreamState.OnAir => "on", StreamState.Failed => "error", StreamState.Connecting => "connecting", _ => "off" },
            streamText = StreamText.Text,
            silence = st.Silence >= 5,
            playlistInfo = PlaylistInfo.Text,
            next = rows,
            cartwall = _page.Name,
            cartwalls = _settings.Cartwalls.Select(c => c.Name).ToList(),
            carts,
            lastLog = LastLog.Text,
            currency = PlaylistRow.Currency,
            words = new { scheduled = Strings.Scheduled, wontPlay = Strings.WontPlay, cue = Strings.Cue },
            rowColors = PlaylistLook.Kinds.ToDictionary(k => k.Key, k => Css(
                Avalonia.Application.Current?.Resources.TryGetValue(k.Key, out var v) == true && v is Avalonia.Media.ISolidColorBrush b ? b.Color : Avalonia.Media.Color.Parse(k.Default))),
        };
    }

    static string Css(Avalonia.Media.Color c) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"rgba({c.R},{c.G},{c.B},{c.A / 255.0:0.###})");

    public Task<object> Levels() => Dispatcher.UIThread.InvokeAsync<object>(() =>
    {
        var st = _engine?.GetStatus() ?? default;
        return new
        {
            l = Math.Round(st.LevelLeft, 4),
            r = Math.Round(st.LevelRight, 4),
            id = st.State != PlayerState.Stopped ? st.Current?.Id.ToString() : null,
            position = st.Position,
            length = st.Length,
            playing = st.State == PlayerState.Playing,
        };
    }).GetTask();

    (Guid Id, Waveform Wave)? _remoteWave;

    /// <summary>The waveform on screen, at 25 points a second (peak and loudness, 0–1).</summary>
    public Task<object?> Waveform() => Dispatcher.UIThread.InvokeAsync<object?>(() =>
    {
        if (_remoteWave is not { } w || w.Id != _waveTrackId) return null;
        int step = Math.Max(1, (int)Math.Round(w.Wave.PointsPerSecond / 25));
        float[] Down(float[] values)
        {
            var result = new float[(values.Length + step - 1) / step];
            for (int i = 0; i < result.Length; i++)
            {
                float max = 0;
                for (int j = i * step; j < Math.Min(values.Length, (i + 1) * step); j++) max = Math.Max(max, values[j]);
                result[i] = MathF.Round(max, 3);
            }
            return result;
        }
        return new { id = w.Id.ToString(), pointsPerSecond = w.Wave.PointsPerSecond / step, peak = Down(w.Wave.Peak), rms = Down(w.Wave.Rms) };
    }).GetTask();

    public Task<byte[]?> Logo() => Dispatcher.UIThread.InvokeAsync(() =>
    {
        if (LogoImage.Source is not Avalonia.Media.Imaging.Bitmap bitmap) return null;
        using var stream = new MemoryStream();
        bitmap.Save(stream, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        return (byte[]?)stream.ToArray();
    }).GetTask();

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
            case "cartwall":
                string name = c["name"]?.GetValue<string>() ?? "";
                if (_settings.Cartwalls.Any(p => p.Name == name)) CartwallPicker.SelectedItem = name;
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
                if (!SharedPaths.FileExists(path)) return new { ok = false, message = L.F("Not found: {0}", path) };
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
