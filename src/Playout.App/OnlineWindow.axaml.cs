using Avalonia.Controls;
using Avalonia.Threading;
using Playout.Engine;
using Playout.Library;

namespace Playout.App;

/// <summary>
/// Sintoniza Online: sends the station's sound (an input of this computer, or a stream it relays) to an Icecast or
/// Shoutcast server and keeps it there: tried again every few seconds when it fails or drops, a backup server, silence
/// or a fallback file while the source is quiet or stopped, alerts. Its level, listening live, what happened each day
/// and today's figures. It goes online when it opens; a password can be asked for to stop it, change its settings or
/// close it.
/// </summary>
public partial class OnlineWindow : Window
{
    OnlineSettings _settings;
    readonly OnlineService _service = null!;
    readonly DispatcherTimer? _fast, _tick;
    bool _closing;
    DateTime _unlockedUntil = DateTime.MinValue, _nextList;
    int _wrong;
    DateTime _waitUntil = DateTime.MinValue;

    // Parameterless constructor for the XAML previewer.
    public OnlineWindow() : this(false) { }

    /// <param name="start">Go online now if the settings say so (as when it is opened as a program).</param>
    public OnlineWindow(bool start)
    {
        InitializeComponent();
        Localization.Apply(this);
        Title = AppInfo.OnlineName;
        _settings = OnlineSettings.Load();
        if (_settings.StationName.Length == 0) _settings.StationName = AppSettings.Load().StationName;
        _service = new OnlineService(_settings);
        _service.Message += m => Dispatcher.UIThread.Post(() => Log(m));

        DayBox.SelectedDate = StationTime.Today;
        DayBox.SelectedDateChanged += (_, _) => ShowDay();
        TodayButton.Click += (_, _) => DayBox.SelectedDate = StationTime.Today;
        StreamButton.Click += async (_, _) => await ToggleStreaming();
        MonitorButton.IsChecked = _settings.Monitor;
        MonitorButton.IsCheckedChanged += (_, _) =>
        {
            _service.SetMonitor(MonitorButton.IsChecked == true);
            _settings.Save();
        };
        SettingsButton.Click += async (_, _) => await OpenSettings();
        LockButton.Click += (_, _) =>
        {
            _unlockedUntil = DateTime.MinValue;
            Tick();
        };
        SupportButton.Click += (_, _) => Updates.OpenInBrowser(AppInfo.SupportUrl);

        _fast = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Render, (_, _) => Meters());
        _tick = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Tick());
        _fast.Start();
        _tick.Start();
        Closing += async (_, e) =>
        {
            if (_closing || !_service.IsStreaming || e.IsProgrammatic) return;
            e.Cancel = true;
            if (!await Unlocked(L.T("Close Sintoniza Online"))) return;
            if (!await Dialogs.Confirm(this, L.T("Stop streaming?"), L.T("Closing takes the station off the internet. Close anyway?"), L.T("Stop and close"), enterConfirms: false)) return;
            _service.Note(L.T("Closed by hand"));
            _closing = true;
            Close();
        };
        Closed += (_, _) =>
        {
            _fast.Stop();
            _tick.Stop();
            _service.Dispose();
        };
        ShowSettings();
        if (start)
        {
            _service.Note(L.F("{0} opened", AppInfo.OnlineName));
            if (_settings.StartOnline) _service.Start();
        }
        Tick();
        ShowDay();
    }

    internal OnlineService Service => _service;

    void Log(string message)
    {
        LastLog.Text = $"{StationTime.Now:HH:mm:ss}  {message}";
        if (DayBox.SelectedDate?.Date == StationTime.Today) ShowDay();
    }

    void ShowSettings()
    {
        StationText.Text = _settings.StationName.Length > 0 ? _settings.StationName : AppInfo.OnlineName;
        LockButton.IsVisible = _settings.IsLocked;
    }

    // ───────────────────────── The lock ─────────────────────────

    bool IsUnlocked => !_settings.IsLocked || DateTime.UtcNow < _unlockedUntil;

    /// <summary>With the lock on: the password asked for (true: right, and unlocked for a few minutes).</summary>
    async Task<bool> Unlocked(string what)
    {
        if (IsUnlocked) return true;
        string? password = await Dialogs.AskText(this, what, L.T("This needs the lock password (Settings → Security)."), "", text =>
        {
            if (DateTime.UtcNow < _waitUntil) return L.F("Too many wrong passwords: try again in {0:0} s.", Math.Ceiling((_waitUntil - DateTime.UtcNow).TotalSeconds));
            if (UserStore.Verify(text, _settings.LockHash)) return null;
            _wrong++;
            _service.Note(L.T("Wrong lock password"));
            if (_wrong % 5 == 0) _waitUntil = DateTime.UtcNow.AddSeconds(30); // every 5 wrong ones: half a minute before the next
            return L.T("That isn't the password.");
        }, secret: true, action: L.T("Unlock"));
        if (password == null) return false;
        _unlockedUntil = DateTime.UtcNow.AddMinutes(Math.Clamp(_settings.LockMinutes, 1, 120));
        if (_wrong > 0) _service.Note(L.F("Unlocked (after {0} wrong passwords)", _wrong));
        _wrong = 0;
        Tick();
        return true;
    }

    // ───────────────────────── Streaming ─────────────────────────

    async Task ToggleStreaming()
    {
        if (_service.IsStreaming)
        {
            if (!await Unlocked(L.T("Stop streaming"))) return;
            if (!await Dialogs.Confirm(this, L.T("Stop streaming?"), L.T("The station is off the internet while it is stopped."), L.T("Stop"), enterConfirms: false)) return;
            _service.Note(L.T("Stopped by hand"));
            _service.Stop();
        }
        else
        {
            if (!_settings.HasServer)
            {
                await OpenSettings();
                if (!_settings.HasServer) return;
            }
            _service.Note(L.T("Started by hand"));
            _service.Start();
        }
        Tick();
    }

    void Meters()
    {
        var (left, right) = _service.TakePeaks();
        MeterL.Push(left);
        MeterR.Push(right);
    }

    void Tick()
    {
        var now = StationTime.Now;
        _service.Tick();
        ClockText.Text = now.ToString("HH:mm:ss");
        var s = _service.Streamer;
        var state = s?.State ?? OnlineState.Off;
        bool silent = s != null && state == OnlineState.Online && s.SilentSeconds >= 5;
        bool trouble = s != null && state is OnlineState.Waiting or OnlineState.Connecting && _service.OffSince is { } off && (now - off).TotalSeconds > 10;
        StateBadge.Classes.Set("on", state == OnlineState.Online && !silent && !s!.OnBackup);
        StateBadge.Classes.Set("warn", state == OnlineState.Online && (silent || s!.OnBackup) || state == OnlineState.Connecting && !trouble);
        StateBadge.Classes.Set("trouble", trouble || state == OnlineState.Waiting);
        StateText.Text = state switch
        {
            OnlineState.Online when s!.OnFallback => L.T("● ONLINE · FALLBACK"),
            OnlineState.Online when silent => L.F("● ONLINE · SILENCE {0}", Format.Duration(s!.SilentSeconds)),
            OnlineState.Online when s!.OnBackup => L.T("● ONLINE · BACKUP"),
            OnlineState.Online => L.T("● ONLINE"),
            OnlineState.Connecting => L.T("CONNECTING…"),
            OnlineState.Waiting => L.T("OFF THE AIR"),
            _ => L.T("OFFLINE"),
        };
        StreamButton.Content = _service.IsStreaming ? L.T("■ Stop") : L.T("▶ Go online");
        SinceText.Text = state switch
        {
            OnlineState.Online when s!.OnlineSince is { } since => L.F("online for {0}", Format.Duration((DateTime.UtcNow - since).TotalSeconds)),
            OnlineState.Waiting when s!.NextAttempt is { } next => L.F("trying again in {0:0} s", Math.Max(0, Math.Ceiling((next - DateTime.UtcNow).TotalSeconds))),
            OnlineState.Connecting => L.F("connecting to {0}", s!.Server.Address),
            _ => _settings.HasServer ? L.T("not streaming") : L.T("no server set yet (⚙ Settings)"),
        };
        ServerText.Text = s != null
            ? $"{s.Server.Address} · {s.Format}{(s.Connections > 1 ? " · " + L.F("connection {0}", s.Connections) : "")}"
            : _settings.HasServer ? _settings.Server.ToServer().Address : "";
        SourceText.Text = s?.Source ?? (_settings.Source == RecSource.Stream ? L.F("Relay: {0}", _settings.SourceUrl) : L.F("Input: {0}", _settings.Input.Length > 0 ? _settings.Input : L.T("the default input")));
        ProblemText.Text = s == null ? ""
            : s.SecondsWithoutData > 3 ? L.F("No sound from the source for {0}: the server gets {1}", Format.Duration(s.SecondsWithoutData), s.OnFallback ? L.T("the fallback file") : L.T("silence"))
            : s.Error is { } error && state != OnlineState.Online ? error
            : "";

        OnlineFigure.Text = Format.Duration(_service.OnlineSeconds);
        OffFigure.Text = Format.Duration(_service.OffSeconds);
        DropsFigure.Text = _service.Drops.ToString();
        LongestFigure.Text = Format.Duration(_service.LongestOff);
        ListenersFigure.Text = _service.Listeners?.ToString() ?? "–";
        MostFigure.Text = _service.MostListeners?.ToString() ?? "–";
        SentFigure.Text = s != null ? $"{s.BytesSent / 1e6:0.0} MB" : "–";
        LockNote.Text = !_settings.IsLocked ? ""
            : IsUnlocked ? L.F("Unlocked: locks again at {0:HH:mm}.", StationTime.FromUtc(_unlockedUntil))
            : L.T("Locked: stopping, the settings and closing ask for the password.");
        LockButton.IsEnabled = IsUnlocked && _settings.IsLocked;

        if (now >= _nextList && DayBox.SelectedDate?.Date == now.Date) ShowDay();
    }

    void ShowDay()
    {
        _nextList = StationTime.Now.AddSeconds(30);
        var day = DayBox.SelectedDate?.Date ?? StationTime.Today;
        var events = OnlineService.Events(day);
        EventList.ItemsSource = events;
        DayInfo.Text = events.Count == 0 ? L.T("nothing kept") : L.F("{0} events", events.Count);
    }

    // ───────────────────────── Settings ─────────────────────────

    async Task OpenSettings()
    {
        if (!await Unlocked(L.T("Change the settings"))) return;
        var window = new OnlineSettingsWindow(_settings.Clone());
        if (await window.ShowDialog<OnlineSettings?>(this) is not { } changed) return;
        changed.Monitor = MonitorButton.IsChecked == true;
        changed.Save();
        if (changed.LockHash != _settings.LockHash) _service.Note(changed.IsLocked ? L.T("Lock password set") : L.T("Lock password removed"));
        _service.Note(L.T("Settings saved"));
        _service.Settings = changed; // streams again if what it sends or where changed
        _settings = changed;
        ShowSettings();
        Tick();
    }
}
