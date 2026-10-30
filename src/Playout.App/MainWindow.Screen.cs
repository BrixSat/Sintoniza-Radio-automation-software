using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Playout.Engine;
using Playout.Library;

namespace Playout.App;

/// <summary>What the on-air screen shows besides the deck (this computer's: Settings → Screen).</summary>
public sealed class ScreenSettings
{
    /// <summary>The status lamps as a panel under the deck, each with its state; off: small lights in the status bar.</summary>
    public bool StatusPanel { get; set; } = true;
    /// <summary>Lamps not shown (their ids: <see cref="MainWindow.LampIds"/>).</summary>
    public List<string> HiddenLamps { get; set; } = new();
    /// <summary>Remaining, the cue points, the next event and the top of the hour in big digits, to be read across the studio.</summary>
    public bool BigCounters { get; set; }
    /// <summary>A red ON AIR / MIC LIVE sign while the mic is open or a live show or input is on air.</summary>
    public bool OnAirSign { get; set; } = true;
}

/// <summary>The on-air screen's status lamps, big counters and ON AIR sign.</summary>
public partial class MainWindow
{
    enum LampState { Off, Ok, Warn, Bad, Live }

    /// <summary>A lamp: its state, a few words for the panel, the whole story for its tooltip, and the Settings tab it opens.</summary>
    sealed record Lamp(string Id, string Name, LampState State, string Short, string Detail, string? Tab = null);

    /// <summary>Every lamp, in the order shown, with its name (for Settings → Screen).</summary>
    public static IReadOnlyList<(string Id, string Name)> LampIds =>
    [
        ("studio", L.T("STUDIO")), ("stream", L.T("STREAM")), ("rds", "RDS"), ("website", L.T("WEBSITE")), ("rec", "REC"),
        ("library", L.T("LIBRARY")), ("plan", L.T("TODAY'S PLAYLIST")), ("deadair", L.T("DEAD AIR")), ("mic", "MIC"),
        ("gpio", "GPIO"), ("alerts", L.T("ALERTS")),
    ];

    static readonly IBrush LampLive = new SolidColorBrush(Color.Parse("#FF3B3B"));

    /// <summary>The desk's mic fader is up (a GPIO input): MIC LIVE.</summary>
    bool _deskMic;
    double _lastSilence;
    readonly Dictionary<string, (Ellipse Dot, TextBlock Short, Control Box)> _lamps = new();
    bool _lampsAsPanel;
    /// <summary>Today's hours that should have a daily playlist and don't (checked every minute in the background).</summary>
    (DateTime At, List<DateTime> Missing, int Planned)? _planCheck;
    Task? _planChecking;

    /// <summary>The screen as its settings say: where the lamps go, the counters' size, the sign.</summary>
    void ApplyScreen()
    {
        var s = _settings.Screen;
        if (_lampsAsPanel != s.StatusPanel || _lamps.Count == 0)
        {
            _lampsAsPanel = s.StatusPanel;
            _lamps.Clear();
            Lights.Children.Clear();
            LampPanel.Children.Clear();
        }
        bool big = s.BigCounters;
        Elapsed.FontSize = big ? 46 : 30;
        Remaining.FontSize = big ? 64 : 30;
        foreach (var cue in new[] { IntroTime, OutroTime, MixTime }) cue.FontSize = big ? 36 : 20;
        NextEventCountdown.FontSize = big ? 52 : 30;
        TopOfHourText.FontSize = big ? 34 : 17;
        UpdateLights();
        UpdateOnAirSign();
    }

    /// <summary>Draws a lamp: a tile with its state in the panel, or a small light in the status bar.</summary>
    void Show(Lamp lamp)
    {
        bool hidden = _settings.Screen.HiddenLamps.Contains(lamp.Id);
        if (!_lamps.TryGetValue(lamp.Id, out var shown))
        {
            var dot = new Ellipse { Width = _lampsAsPanel ? 11 : 8, Height = _lampsAsPanel ? 11 : 8, VerticalAlignment = VerticalAlignment.Center };
            var name = new TextBlock { Text = lamp.Name, FontSize = _lampsAsPanel ? 12 : 11, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var state = new TextBlock { FontSize = 11, Opacity = 0.75, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 150 };
            Border box;
            if (_lampsAsPanel)
            {
                var text = new StackPanel { Children = { name, state } };
                box = new Border
                {
                    Padding = new Thickness(10, 5), CornerRadius = new CornerRadius(6), MinWidth = 120, Cursor = new Cursor(StandardCursorType.Hand),
                    Background = new SolidColorBrush(Color.Parse("#1B1F27")), BorderThickness = new Thickness(0, 0, 0, 0),
                    Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { dot, text } },
                };
                LampPanel.Children.Add(box);
            }
            else
            {
                box = new Border
                {
                    Padding = new Thickness(7, 2), CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(Color.Parse("#1B1F27")),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { dot, name } },
                };
                Lights.Children.Add(box);
            }
            string id = lamp.Id;
            box.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(box).Properties.IsLeftButtonPressed) LampClicked(id);
            };
            _lamps[lamp.Id] = shown = (dot, state, box);
        }
        shown.Box.IsVisible = !hidden;
        shown.Dot.Fill = lamp.State switch
        {
            LampState.Ok => LightOk,
            LampState.Warn => Amber,
            LampState.Bad => LightBad,
            LampState.Live => LampLive,
            _ => LightOff,
        };
        shown.Short.Text = lamp.Short;
        // A problem stands out in the panel: a red edge on the tile.
        if (_lampsAsPanel && shown.Box is Border border)
        {
            border.BorderThickness = new Thickness(lamp.State == LampState.Bad ? 2 : 0, 0, 0, 0);
            border.BorderBrush = LightBad;
        }
        ToolTip.SetTip(shown.Box, lamp.Detail + (lamp.Tab != null ? "\n" + L.T("Click: its settings") : ""));
        _lampInfo[lamp.Id] = lamp;
    }

    readonly Dictionary<string, Lamp> _lampInfo = new();

    /// <summary>A lamp clicked: its settings on this computer, else what it says (station settings are in Sintoniza Pro).</summary>
    async void LampClicked(string id)
    {
        if (!_lampInfo.TryGetValue(id, out var lamp)) return;
        if (lamp.Tab != null) await OpenSettings(lamp.Tab);
        else LastLog.Text = $"{lamp.Name}: {lamp.Detail}";
    }

    /// <summary>Every lamp from what is going on now (each second, with the clock).</summary>
    void UpdateLights()
    {
        var st = _lastStreamState;
        Show(!_settings.UsesStreaming
            ? new Lamp("stream", L.T("STREAM"), LampState.Off, L.T("Not set up"), L.T("No stream set up (Sintoniza Pro → Station settings → Streaming)"))
            : new Lamp("stream", L.T("STREAM"), st switch { StreamState.OnAir => LampState.Ok, StreamState.Connecting => LampState.Warn, StreamState.Failed => LampState.Bad, _ => LampState.Off },
                st switch
                {
                    StreamState.OnAir => Format.Duration((DateTime.Now - _streamSince).TotalSeconds),
                    StreamState.Connecting => L.T("Connecting…"),
                    StreamState.Failed => L.T("Can't connect"),
                    _ => L.T("Off"),
                },
                st switch
                {
                    StreamState.OnAir => L.T("Streaming"),
                    StreamState.Connecting => L.T("Connecting to the stream server"),
                    StreamState.Failed => L.T("The stream can't connect"),
                    _ => L.T("Stream off"),
                }));

        Show(_rds == null
            ? new Lamp("rds", "RDS", LampState.Off, IsStandby ? L.T("Standby") : L.T("Off"),
                IsStandby ? L.T("The computer on air sends RDS") : L.T("RDS off (Settings)"), "RDS")
            : new Lamp("rds", "RDS", _rds.Error == null ? LampState.Ok : LampState.Bad, _rds.Error == null ? L.T("Sending") : L.T("Can't send"),
                _rds.Error == null ? L.T("RDS: sending to the encoder") : L.F("RDS: can't send ({0})", _rds.Error), "RDS"));

        Show(_web == null
            ? new Lamp("website", L.T("WEBSITE"), LampState.Off, IsStandby ? L.T("Standby") : L.T("Off"),
                IsStandby ? L.T("The computer on air sends now playing") : L.T("Now playing for the website is off (Sintoniza Pro → Station settings → Website)"))
            : new Lamp("website", L.T("WEBSITE"), _web.Error == null ? LampState.Ok : LampState.Bad, _web.Error == null ? L.T("Sending") : L.T("Can't send"),
                _web.Error == null ? L.T("Website: now playing sent") : L.F("Website: can't send now playing ({0})", _web.Error)));

        var rec = RecService.ReadStatus();
        Show(new Lamp("rec", "REC", rec == null ? LampState.Off : !rec.Recording ? LampState.Bad : rec.SilentSeconds >= 30 ? LampState.Warn : LampState.Ok,
            rec == null ? L.T("Not running") : !rec.Recording ? L.T("NOT recording") : rec.SilentSeconds >= 30 ? L.F("Silence {0:0} s", rec.SilentSeconds) : L.T("Recording"),
            rec == null ? L.T("Sintoniza Rec isn't running on this computer")
            : !rec.Recording ? L.T("Sintoniza Rec is open but NOT recording")
            : rec.SilentSeconds >= 30 ? L.F("Sintoniza Rec is recording silence ({0:0} s)", rec.SilentSeconds)
            : L.F("Sintoniza Rec is recording: {0}", System.IO.Path.GetFileName(rec.File ?? ""))));

        Show(new Lamp("library", L.T("LIBRARY"), _library.Db != null ? LampState.Ok : _library.Error != null ? LampState.Bad : LampState.Off,
            _library.Db != null ? L.T("Open") : _library.Error != null ? L.T("Can't reach") : L.T("None"),
            _library.Db != null ? L.T("The library is open") : _library.Error is { } error ? L.F("The library can't be reached: {0}", error) : L.T("No library"), "Library"));

        Show(PlanLamp());

        var silence = _settings.Silence ?? new();
        double after = Math.Max(5, silence.Seconds);
        Show(!silence.Enabled
            ? new Lamp("deadair", L.T("DEAD AIR"), LampState.Off, L.T("Off"), L.T("Dead air isn't watched (Settings → Safety)"), "Safety")
            : _lastSilence >= after
                ? new Lamp("deadair", L.T("DEAD AIR"), LampState.Bad, L.F("Silent {0:0} s", _lastSilence), L.F("Nothing heard for {0:0} seconds", _lastSilence), "Safety")
                : new Lamp("deadair", L.T("DEAD AIR"), LampState.Ok, L.T("Watching"), L.F("Silence for {0:0} s or more is dead air: the next item plays", after), "Safety"));

        bool micLive = _micOn || _deskMic;
        Show(new Lamp("mic", "MIC", micLive ? LampState.Live : LampState.Off, micLive ? L.T("LIVE") : L.T("Closed"),
            micLive ? (_micOn ? L.T("The microphone is open") : L.T("The desk's mic fader is up (GPIO)")) : L.T("The microphone is closed"), "Microphone"));

        var g = _settings.Gpio ?? new();
        int wanted = (g.Enabled && g.Port.Length > 0 ? 1 : 0) + (g.Network?.Enabled == true ? 1 : 0)
                     + (g.Midi is { Enabled: true } m && (m.Input.Length > 0 || m.Output.Length > 0) ? 1 : 0);
        Show(wanted == 0
            ? new Lamp("gpio", "GPIO", LampState.Off, L.T("Off"), L.T("No GPIO set up"), "GPIO")
            : new Lamp("gpio", "GPIO", _gpio.Count >= wanted ? LampState.Ok : LampState.Bad,
                _gpio.Count >= wanted ? L.T("Ready") : L.F("{0} of {1} open", _gpio.Count, wanted),
                _gpio.Count >= wanted ? L.T("GPIO: the desk's lines are open") : L.T("GPIO: some can't be opened (the log says why)"), "GPIO"));

        // Several computers for the station (a shared library): this one on air, and the standby ones.
        if (IsStandby)
            Show(new Lamp("studio", L.T("STUDIO"), LampState.Warn, L.T("Standby"), L.T("Another computer is on air. This one plays nothing until it goes on air.")));
        else if (_role?.Studios is { } studios)
        {
            var standby = studios.Studios.Where(x => !x.OnAir && studios.Silent(x) < TimeSpan.FromMinutes(1)).Select(x => x.Computer).ToList();
            Show(new Lamp("studio", L.T("STUDIO"), LampState.Ok, standby.Count > 0 ? L.F("Standby: {0}", standby.Count) : L.T("On air"),
                standby.Count > 0 ? L.F("This computer is on air. Standby: {0}", string.Join(", ", standby))
                    : L.T("This computer is on air. No other computer is standby.")));
        }

        var problems = _health?.Active ?? [];
        var alerts = _settings.Alerts;
        Show(new Lamp("alerts", L.T("ALERTS"), problems.Count > 0 ? LampState.Bad : alerts.Enabled && alerts.HasChannel ? LampState.Ok : LampState.Off,
            problems.Count > 0 ? Plural(problems.Count, "{0} problem", "{0} problems") : alerts.Enabled && alerts.HasChannel ? L.T("Nothing wrong") : L.T("Off"),
            problems.Count > 0 ? string.Join("\n", problems) : alerts.Enabled && alerts.HasChannel ? L.T("Alerts on: nothing wrong") : L.T("Alerts off (Settings)")));

        bool any = _settings.Screen.StatusPanel && _lamps.Values.Any(l => l.Box.IsVisible);
        LampPanel.IsVisible = any;
        ScreenStrip.IsVisible = any || OnAirSign.IsVisible;
    }

    /// <summary>Today's daily playlists: the hours left today that have an hour template should have one made.</summary>
    Lamp PlanLamp()
    {
        var now = DateTime.Now;
        // Checked before midnight: yesterday's answer isn't today's.
        if (_planCheck is { } old && old.At.Date != now.Date) _planCheck = null;
        if (_planCheck is not { } check || now - check.At > TimeSpan.FromMinutes(1))
        {
            if (_planChecking is not { IsCompleted: false } && _library.Db is { } db && _scheduler?.Current is { } data)
                _planChecking = Task.Run(() =>
                {
                    try
                    {
                        var hour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);
                        var wanted = Enumerable.Range(0, 24 - now.Hour).Select(h => hour.AddHours(h)).Where(h => data.TemplateAt(h) != null).ToList();
                        var made = wanted.Count == 0 ? new() : new PlanStore(db).Hours(wanted[0], wanted[^1].AddHours(1));
                        var missing = wanted.Where(h => !made.ContainsKey(h)).ToList();
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => _planCheck = (now, missing, wanted.Count));
                    }
                    catch (Exception ex)
                    {
                        AppLog.Write($"Today's playlist check: {ex.Message}");
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => _planCheck = (now, [], -1));
                    }
                });
        }
        // No library (still opening, or lost): nothing known, rather than an old answer; checked as soon as it is there.
        if (_library.Db == null || _scheduler == null)
            return new Lamp("plan", L.T("TODAY'S PLAYLIST"), LampState.Off, L.T("Can't check"), L.T("Today's daily playlists can't be read now"));
        if (_planCheck is not { } c) return new Lamp("plan", L.T("TODAY'S PLAYLIST"), LampState.Off, "…", L.T("Checking today's daily playlists"));
        if (c.Planned < 0) return new Lamp("plan", L.T("TODAY'S PLAYLIST"), LampState.Off, L.T("Can't check"), L.T("Today's daily playlists can't be read now"));
        if (c.Planned == 0) return new Lamp("plan", L.T("TODAY'S PLAYLIST"), LampState.Off, L.T("No templates"),
            L.T("No hour templates for the rest of today: fill music and the playlist on screen play"));
        if (c.Missing.Count == 0) return new Lamp("plan", L.T("TODAY'S PLAYLIST"), LampState.Ok, L.T("Ready"),
            L.T("Every hour left today with an hour template has its daily playlist"));
        return new Lamp("plan", L.T("TODAY'S PLAYLIST"), c.Missing[0] <= now.AddHours(1) ? LampState.Bad : LampState.Warn,
            Plural(c.Missing.Count, "{0} hour missing", "{0} hours missing"),
            L.F("No daily playlist yet for {0} (Sintoniza Pro → Daily playlists makes them)", string.Join(", ", c.Missing.Select(h => h.ToString("HH:mm")))));
    }

    /// <summary>
    /// The red sign: MIC LIVE while the mic is open (the MIC button, or the desk's mic fader by GPIO), ON AIR while a
    /// live show or a live input is on air. Nothing on a standby computer.
    /// </summary>
    void UpdateOnAirSign(PlayerStatus? st = null)
    {
        string? text = null;
        if (!IsStandby)
        {
            var current = st is { State: PlayerState.Playing } playing ? playing.Current : null;
            if (_micOn || _deskMic) text = L.T("MIC LIVE");
            else if (_show is { } show) text = L.F("ON AIR · {0}", show.Show.Name.Trim() is { Length: > 0 } n ? n : L.T("live show"));
            else if (current != null && LiveInput.Is(current.Path)) text = L.T("ON AIR · LIVE INPUT");
        }
        bool lit = text != null;
        OnAirSign.IsVisible = lit && _settings.Screen.OnAirSign;
        if (lit) OnAirSignText.Text = text;
        ScreenStrip.IsVisible = OnAirSign.IsVisible || LampPanel.IsVisible;
        _signLit = lit;
    }

    /// <summary>The sign's state (whether shown or not), for the GPIO output of a real studio lamp.</summary>
    bool _signLit;
}
