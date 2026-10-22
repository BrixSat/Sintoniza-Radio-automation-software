using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Net.Sockets;
using Playout.Engine;

namespace Playout.App;

/// <summary>Where alerts go (Telegram, email, a web address) and what raises one.</summary>
public sealed class AlertSettings
{
    public bool Enabled { get; set; }

    public bool Silence { get; set; } = true;
    public bool Stream { get; set; } = true;
    public bool Engine { get; set; } = true;
    public bool Database { get; set; } = true;
    public bool Disk { get; set; } = true;
    /// <summary>Free space below this many GB on the disk of the settings or the music.</summary>
    public double DiskGb { get; set; } = 2;
    public bool Clock { get; set; } = true;
    /// <summary>The computer's clock off by more than this many seconds from the time server.</summary>
    public double ClockSeconds { get; set; } = 2;
    public string TimeServer { get; set; } = "pool.ntp.org";
    public bool Backup { get; set; } = true;
    public bool Restart { get; set; } = true;

    public string TelegramToken { get; set; } = "";
    public string TelegramChat { get; set; } = "";

    public string SmtpServer { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public bool SmtpSsl { get; set; } = true;
    public string SmtpUser { get; set; } = "";
    public string SmtpPassword { get; set; } = "";
    public string MailFrom { get; set; } = "";
    /// <summary>One or more addresses, separated by commas.</summary>
    public string MailTo { get; set; } = "";

    /// <summary>A web address that gets a JSON POST: station, kind, ok, message, time.</summary>
    public string WebhookUrl { get; set; } = "";

    public bool HasTelegram => TelegramToken.Trim().Length > 0 && TelegramChat.Trim().Length > 0;
    public bool HasMail => SmtpServer.Trim().Length > 0 && MailTo.Trim().Length > 0;
    public bool HasWebhook => Uri.TryCreate(WebhookUrl.Trim(), UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https");
    public bool HasChannel => HasTelegram || HasMail || HasWebhook;
}

/// <summary>Sends an alert to every channel that is set up.</summary>
static class AlertSender
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>Sends to every channel; returns the problems ("" when all went).</summary>
    public static async Task<string> SendAsync(AlertSettings s, string station, string kind, bool ok, string message)
    {
        string text = $"{(ok ? "✅" : "⚠️")} {station}: {message} ({DateTime.Now:HH:mm:ss})";
        var problems = new List<string>();
        if (s.HasTelegram)
            try
            {
                using var r = await Http.PostAsJsonAsync($"https://api.telegram.org/bot{s.TelegramToken.Trim()}/sendMessage",
                    new { chat_id = s.TelegramChat.Trim(), text });
                if (!r.IsSuccessStatusCode) problems.Add($"Telegram: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
            }
            catch (Exception ex) { problems.Add("Telegram: " + ex.Message); }
        if (s.HasWebhook)
            try
            {
                using var r = await Http.PostAsJsonAsync(s.WebhookUrl.Trim(),
                    new { station, kind, ok, message, time = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture) });
                if (!r.IsSuccessStatusCode) problems.Add($"Webhook: {(int)r.StatusCode}");
            }
            catch (Exception ex) { problems.Add("Webhook: " + ex.Message); }
        if (s.HasMail)
            try
            {
                using var smtp = new SmtpClient(s.SmtpServer.Trim(), s.SmtpPort) { EnableSsl = s.SmtpSsl, Timeout = 15000 };
                if (s.SmtpUser.Trim().Length > 0) smtp.Credentials = new NetworkCredential(s.SmtpUser.Trim(), s.SmtpPassword);
                string from = s.MailFrom.Trim().Length > 0 ? s.MailFrom.Trim() : s.SmtpUser.Trim();
                if (!from.Contains('@')) throw new InvalidOperationException(L.T("no sender: fill in From (or a user that is an email address)"));
                using var mail = new MailMessage { From = new MailAddress(from), Subject = text, Body = text };
                foreach (var to in s.MailTo.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) mail.To.Add(to);
                await smtp.SendMailAsync(mail);
            }
            catch (Exception ex) { problems.Add("Email: " + (ex.InnerException is { } inner ? $"{ex.Message} {inner.Message}" : ex.Message)); }
        return string.Join("; ", problems);
    }

    /// <summary>
    /// From the watchdog (no window, no engine): sends with the saved settings and waits a little for it, so the
    /// alert goes before the app is started again.
    /// </summary>
    public static void SendFromWatchdog(string message)
    {
        try
        {
            var settings = AppSettings.Load();
            var a = settings.Alerts;
            if (a is not { Enabled: true, Restart: true } || !a.HasChannel) return;
            Localization.Load(settings.Language);
            string station = string.IsNullOrWhiteSpace(settings.StationName) ? AppInfo.Name : settings.StationName.Trim();
            var send = Task.Run(() => SendAsync(a, station, "restart", false, message)); // never on a thread the send needs
            if (send.Wait(TimeSpan.FromSeconds(20)) && send.Result.Length > 0) AppLog.Write("Alert not sent: " + send.Result);
        }
        catch (Exception ex) { AppLog.Write("Alert not sent: " + ex.Message); }
    }
}

/// <summary>
/// Watches the station and sends an alert when something goes wrong, and another when it is back to normal: silence on
/// air, the stream down, the engine not responding, the library database unavailable, a disk nearly full, the clock
/// off. Each problem is reported once when it starts and once when it ends, never in a flood.
/// </summary>
sealed class HealthMonitor : IDisposable
{
    static readonly TimeSpan StreamGrace = TimeSpan.FromSeconds(60);
    static readonly TimeSpan DatabaseGrace = TimeSpan.FromSeconds(60);

    readonly PlayoutEngine _engine;
    readonly LibraryService _library;
    readonly Func<AppSettings> _settings;
    readonly Timer _timer;
    readonly Dictionary<string, string> _active = new(); // kind → what was reported
    DateTime? _streamBadSince, _dbBadSince;
    DateTime _nextDisk = DateTime.MinValue, _nextClock = DateTime.MinValue, _nextDbPing = DateTime.MinValue;
    bool _dbPingFailed;
    int _checking;

    /// <summary>A line for the status bar and the log (raised off the UI thread).</summary>
    public event Action<string>? Message;

    public HealthMonitor(PlayoutEngine engine, LibraryService library, Func<AppSettings> settings)
    {
        _engine = engine;
        _library = library;
        _settings = settings;
        _timer = new Timer(_ => Check(), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2));
    }

    /// <summary>The problems going on now (what was alerted and hasn't cleared yet), for the ALERTS light.</summary>
    public IReadOnlyList<string> Active
    {
        get { lock (_active) return _active.Values.ToList(); }
    }

    /// <summary>An alert from elsewhere (a backup that failed): once, nothing to clear.</summary>
    public void Report(string kind, string message)
    {
        var a = _settings().Alerts;
        if (!a.Enabled || !a.HasChannel || (kind == "backup" && !a.Backup)) return;
        _ = Send(kind, false, message);
    }

    void Check()
    {
        if (Interlocked.Exchange(ref _checking, 1) == 1) return;
        try { CheckAll(DateTime.UtcNow); }
        catch (Exception ex) { AppLog.Write("Health check: " + ex.Message); }
        finally { Volatile.Write(ref _checking, 0); }
    }

    void CheckAll(DateTime now)
    {
        var settings = _settings();
        var a = settings.Alerts;
        if (!a.Enabled || !a.HasChannel)
        {
            lock (_active) _active.Clear();
            return;
        }
        var st = _engine.GetStatus();

        // Silence: as soon as the engine counts it as silence (Settings → Safety).
        double needed = Math.Max(5, settings.Silence?.Seconds ?? 15);
        Problem("silence", a.Silence && st.Silence >= needed,
            () => L.F("Silence on air for {0:0} s", st.Silence), L.T("Sound back on air"));

        // The stream: wanted but not on air for a minute.
        bool streamBad = settings.StreamingOn && settings.UsesStreaming && st.Stream is StreamState.Failed or StreamState.Connecting;
        _streamBadSince = streamBad ? _streamBadSince ?? now : null;
        Problem("stream", a.Stream && _streamBadSince is { } sb && now - sb >= StreamGrace,
            () => L.F("The stream is down{0}", string.IsNullOrEmpty(st.StreamError) ? "" : ": " + st.StreamError), L.T("The stream is back on air"));

        Problem("engine", a.Engine && !_engine.IsResponsive(10), () => L.T("The audio engine isn't responding"), L.T("The audio engine responds again"));

        // The library database: not open, or a quick query failing (checked every minute, off this timer).
        if (now >= _nextDbPing)
        {
            _nextDbPing = now.AddMinutes(1);
            if (_library.Db is { } db) _ = Task.Run(() =>
            {
                try { db.Categories(); _dbPingFailed = false; }
                catch { _dbPingFailed = true; }
            });
        }
        bool dbBad = _library.Db == null || _dbPingFailed;
        _dbBadSince = dbBad ? _dbBadSince ?? now : null;
        Problem("database", a.Database && _dbBadSince is { } db0 && now - db0 >= DatabaseGrace,
            () => L.F("The library database is unavailable{0}", _library.Error is { Length: > 0 } e ? ": " + e : ""), L.T("The library database is available again"));

        if (now >= _nextDisk)
        {
            _nextDisk = now.AddMinutes(10);
            var low = LowDisks(settings, a.DiskGb);
            Problem("disk", a.Disk && low.Count > 0,
                () => L.F("Disk almost full: {0}", string.Join(", ", low)), L.T("Disk space is fine again"));
        }

        if (a.Clock && now >= _nextClock)
        {
            _nextClock = now.AddHours(6);
            _ = Task.Run(async () =>
            {
                if (await ClockOffset(a.TimeServer) is not { } offset) { _nextClock = DateTime.UtcNow.AddMinutes(30); return; }
                Problem("clock", Math.Abs(offset.TotalSeconds) > a.ClockSeconds,
                    () => L.F("The computer's clock is {0:0.0} s off (time server {1}): turn on automatic time", offset.TotalSeconds, a.TimeServer),
                    L.T("The computer's clock is right again"));
            });
        }
    }

    /// <summary>Reports a problem when it starts and when it ends.</summary>
    void Problem(string kind, bool bad, Func<string> message, string fixedMessage)
    {
        lock (_active)
        {
            if (bad && !_active.ContainsKey(kind))
            {
                string text = message();
                _active[kind] = text;
                _ = Send(kind, false, text);
            }
            else if (!bad && _active.Remove(kind)) _ = Send(kind, true, fixedMessage);
        }
    }

    async Task Send(string kind, bool ok, string message)
    {
        var settings = _settings();
        string station = string.IsNullOrWhiteSpace(settings.StationName) ? AppInfo.Name : settings.StationName.Trim();
        Say(L.F("Alert: {0}", message));
        string problems = await AlertSender.SendAsync(settings.Alerts, station, kind, ok, message);
        if (problems.Length > 0) Say(L.F("Alert not sent: {0}", problems));
    }

    /// <summary>The disks (of the settings and the music) with less than <paramref name="gb"/> GB free.</summary>
    static List<string> LowDisks(AppSettings s, double gb)
    {
        var folders = new[] { AppSettings.DataFolder, s.MusicFolder }.Concat(s.Library.Folders).Where(Directory.Exists);
        var low = new List<string>();
        foreach (var root in folders.Select(f => Path.GetPathRoot(Path.GetFullPath(f)) ?? f).Distinct())
        {
            try
            {
                var drive = new DriveInfo(root);
                double free = drive.AvailableFreeSpace / 1073741824.0;
                if (free < gb) low.Add(L.F("{0} ({1:0.0} GB free)", drive.Name, free));
            }
            catch { /* not a local disk */ }
        }
        // Linux: one root "/" for everything; the folders' own mount points are what matters.
        if (OperatingSystem.IsLinux())
            foreach (var mount in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed
                         && folders.Any(f => Path.GetFullPath(f).StartsWith(d.RootDirectory.FullName, StringComparison.Ordinal)))
                         .GroupBy(d => d.RootDirectory.FullName).Select(g => g.First()))
            {
                double free = mount.AvailableFreeSpace / 1073741824.0;
                string label = L.F("{0} ({1:0.0} GB free)", mount.Name, free);
                if (free < gb && !low.Contains(label)) low.Add(label);
            }
        return low.Distinct().ToList();
    }

    /// <summary>How far this computer's clock is from the time server (SNTP); null if it can't be asked.</summary>
    internal static async Task<TimeSpan?> ClockOffset(string server)
    {
        try
        {
            using var udp = new UdpClient();
            udp.Client.ReceiveTimeout = 5000;
            udp.Connect(server.Trim(), 123);
            var request = new byte[48];
            request[0] = 0x1B; // version 3, client
            var sent = DateTime.UtcNow;
            await udp.SendAsync(request);
            var receive = udp.ReceiveAsync();
            if (await Task.WhenAny(receive, Task.Delay(5000)) != receive) return null;
            var got = DateTime.UtcNow;
            var data = receive.Result.Buffer;
            if (data.Length < 48) return null;
            DateTime Stamp(int at)
            {
                ulong seconds = (ulong)data[at] << 24 | (ulong)data[at + 1] << 16 | (ulong)data[at + 2] << 8 | data[at + 3];
                ulong fraction = (ulong)data[at + 4] << 24 | (ulong)data[at + 5] << 16 | (ulong)data[at + 6] << 8 | data[at + 7];
                return new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds + fraction / 4294967296.0);
            }
            var serverReceived = Stamp(32);
            var serverSent = Stamp(40);
            // The usual SNTP offset: ((t1 - t0) + (t2 - t3)) / 2.
            return ((serverReceived - sent) + (serverSent - got)) / 2;
        }
        catch { return null; }
    }

    void Say(string message) => Message?.Invoke($"{DateTime.Now:HH:mm:ss}  {message}");

    public void Dispose() => _timer.Dispose();
}
