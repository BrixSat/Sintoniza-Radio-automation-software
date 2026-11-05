using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Playout.Engine;

namespace Playout.App;

/// <summary>Where a download comes from: the file's own address, a web page with a link to it, or an FTP server.</summary>
public enum DownloadSource { Link, Page, Ftp }

/// <summary>
/// A file fetched again and again (Sintoniza Pro → Station → Downloads), such as the news bulletin an agency puts on
/// its website every hour: saved always under the same name, so the playlists and events that play it get the latest.
/// </summary>
public sealed class DownloadSettings
{
    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "";
    public DownloadSource Source { get; set; } = DownloadSource.Page;
    /// <summary>The file's address (Link: http(s)://…, FTP: ftp(s)://host/folder/file.mp3), or the page's (Page).</summary>
    public string Address { get; set; } = "";
    /// <summary>Page: the first link on it that matches this (a regular expression) is the file.</summary>
    public string Pattern { get; set; } = DefaultPattern;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    /// <summary>Where it is saved (the library's form of the path: every studio finds it, through its paths).</summary>
    public string SaveAs { get; set; } = "";
    /// <summary>Fetched every so many minutes (0: at <see cref="AtMinutes"/> past each hour).</summary>
    public int EveryMinutes { get; set; } = 15;
    /// <summary>Minutes past each hour it is fetched at, e.g. "2, 32" (when <see cref="EveryMinutes"/> is 0).</summary>
    public string AtMinutes { get; set; } = "";
    /// <summary>Not fetched for this long: too old, passed over on air (0: never).</summary>
    public int TooOldMinutes { get; set; } = 90;
    /// <summary>Shorter than this, what was fetched isn't taken (an error page, a cut file).</summary>
    public int ShortestSeconds { get; set; } = 20;

    public const string DefaultPattern = """https?://[^"'\s<>]+\.mp3""";

    public DownloadSettings Clone() => (DownloadSettings)MemberwiseClone();

    /// <summary>The minutes past the hour in <see cref="AtMinutes"/> (0–59, in order).</summary>
    public List<int> Minutes() => AtMinutes.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
        .Select(m => int.TryParse(m, out int n) ? n : -1).Where(n => n is >= 0 and < 60).Distinct().Order().ToList();

    /// <summary>
    /// When it is due (UTC), last fetched at <paramref name="lastUtc"/>: that plus the minutes; or at the minutes past
    /// the hour (the station's hours), the latest of them gone by at <paramref name="nowUtc"/> if it wasn't fetched
    /// since (never otherwise). In UTC, so the night the clocks change takes nothing away.
    /// </summary>
    public DateTime DueAfter(DateTime lastUtc, DateTime nowUtc)
    {
        if (EveryMinutes > 0) return lastUtc == DateTime.MinValue ? lastUtc : lastUtc.AddMinutes(EveryMinutes);
        var minutes = Minutes();
        if (minutes.Count == 0) return lastUtc == DateTime.MinValue ? lastUtc : lastUtc.AddMinutes(60);
        var local = StationTime.FromUtc(nowUtc);
        var hour = new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0);
        // The latest of the times at or before now (the hour before too): due if it was fetched before it.
        DateTime? latest = null;
        for (int back = 0; back < 3; back++, hour = hour.AddHours(-1))
            foreach (int m in minutes)
                if (StationTime.ToUtc(hour.AddMinutes(m)) is var slot && slot <= nowUtc && (latest == null || slot > latest)) latest = slot;
        return latest is { } at && at > lastUtc ? at : DateTime.MaxValue;
    }

    /// <summary>The minutes past the hour typed, and whether all of them are minutes (0–59).</summary>
    public bool MinutesAreRight() => AtMinutes.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
        .All(m => int.TryParse(m, out int n) && n is >= 0 and < 60);

    /// <summary>The longest it waits between two fetches (for the too-old time to be longer).</summary>
    public int IntervalMinutes()
    {
        if (EveryMinutes > 0) return EveryMinutes;
        var minutes = Minutes();
        if (minutes.Count <= 1) return 60;
        return Math.Max(minutes.Zip(minutes.Skip(1), (a, b) => b - a).Max(), 60 - minutes[^1] + minutes[0]);
    }
}

/// <summary>What fetching a download found: the file's address, and the file itself (a temporary file) and its length.</summary>
public sealed record Fetched(string Url, string File, double Seconds);

/// <summary>Fetches downloads (<see cref="DownloadSettings"/>): from the page the link, then the file, checked.</summary>
public static class Download
{
    static readonly HttpClient Http = CreateClient();
    /// <summary>A whole fetch takes at most this long (a server that stops sending, a live stream behind the link).</summary>
    public static readonly TimeSpan MaxTime = TimeSpan.FromMinutes(5);
    /// <summary>A bulletin is never this large: more, and it isn't one (a live stream).</summary>
    const long MaxBytes = 200L * 1024 * 1024;

    static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        // Some sites answer only a browser.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        return http;
    }

    /// <summary>
    /// Fetches it into <paramref name="temp"/> and checks it plays for at least the shortest time set. Throws with
    /// what went wrong (said in the log and in Pro's Test).
    /// </summary>
    public static async Task<Fetched> FetchAsync(DownloadSettings d, string temp, CancellationToken stop = default)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop);
        limit.CancelAfter(MaxTime);
        try { return await FetchWithin(d, temp, limit.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested)
        {
            throw new InvalidOperationException(L.F("not fetched within {0} minutes", (int)MaxTime.TotalMinutes));
        }
    }

    static async Task<Fetched> FetchWithin(DownloadSettings d, string temp, CancellationToken cancel)
    {
        string address = d.Address.Trim();
        if (address.Length == 0) throw new InvalidOperationException(L.T("no address"));
        string url = d.Source == DownloadSource.Page ? await FindLink(d, address, cancel).ConfigureAwait(false) : address;
        Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
        if (d.Source == DownloadSource.Ftp || url.StartsWith("ftp", StringComparison.OrdinalIgnoreCase))
            await Task.Run(() => FetchFtp(d, url, temp, cancel), cancel).ConfigureAwait(false);
        else
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            // The user and password only to the page's own server (a link to another one goes without them).
            if (d.Source != DownloadSource.Page || SameHost(address, url)) Authorize(d, request);
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(L.F("the website answered {0} {1}", (int)response.StatusCode, response.ReasonPhrase));
            if (response.Content.Headers.ContentLength > MaxBytes) throw new InvalidOperationException(L.T("it is too large to be a bulletin"));
            await using var body = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
            await using var file = File.Create(temp);
            await Copy(body, file, cancel).ConfigureAwait(false);
        }
        double seconds = AudioProbe.Duration(temp) ?? 0;
        if (seconds <= 0) throw new InvalidOperationException(L.T("what was fetched isn't audio"));
        if (seconds < d.ShortestSeconds) throw new InvalidOperationException(L.F("what was fetched plays only {0:0} s", seconds));
        return new Fetched(url, temp, seconds);
    }

    /// <summary>The page's first link that matches the pattern (relative ones taken from the page's address).</summary>
    static async Task<string> FindLink(DownloadSettings d, string page, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, page);
        Authorize(d, request);
        using var response = await Http.SendAsync(request, cancel).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(L.F("the page answered {0} {1}", (int)response.StatusCode, response.ReasonPhrase));
        // Links written in the page's script have their slashes escaped (https:\/\/…).
        string html = (await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false)).Replace("\\/", "/");
        Regex pattern;
        try { pattern = new Regex(d.Pattern.Trim().Length > 0 ? d.Pattern.Trim() : DownloadSettings.DefaultPattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)); }
        catch (ArgumentException ex) { throw new InvalidOperationException(L.F("the pattern isn't right: {0}", ex.Message)); }
        var match = pattern.Match(html);
        if (!match.Success) throw new InvalidOperationException(L.T("no link on the page matches the pattern"));
        string link = WebUtility.HtmlDecode(match.Value);
        return Uri.TryCreate(response.RequestMessage?.RequestUri ?? new Uri(page), link, out var uri) ? uri.ToString() : link;
    }

    /// <summary>Copies at most <see cref="MaxBytes"/> (a live stream behind the link never ends).</summary>
    static async Task Copy(Stream from, Stream to, CancellationToken cancel)
    {
        var buffer = new byte[81920];
        long total = 0;
        while (await from.ReadAsync(buffer, cancel).ConfigureAwait(false) is var n and > 0)
        {
            if ((total += n) > MaxBytes) throw new InvalidOperationException(L.T("it is too large to be a bulletin"));
            await to.WriteAsync(buffer.AsMemory(0, n), cancel).ConfigureAwait(false);
        }
    }

    static bool SameHost(string a, string b) =>
        Uri.TryCreate(a, UriKind.Absolute, out var x) && Uri.TryCreate(b, UriKind.Absolute, out var y)
        && string.Equals(x.Host, y.Host, StringComparison.OrdinalIgnoreCase);

    static void Authorize(DownloadSettings d, HttpRequestMessage request)
    {
        if (d.User.Trim().Length == 0) return;
        string token = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{d.User.Trim()}:{d.Password}"));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", token);
    }

    static void FetchFtp(DownloadSettings d, string url, string temp, CancellationToken cancel)
    {
        bool ftps = url.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase);
#pragma warning disable SYSLIB0014 // FtpWebRequest: .NET's only FTP client
        var request = (FtpWebRequest)WebRequest.Create(new Uri(ftps ? "ftp://" + url[7..] : url));
#pragma warning restore SYSLIB0014
        request.Method = WebRequestMethods.Ftp.DownloadFile;
        request.Credentials = new NetworkCredential(d.User.Trim().Length > 0 ? d.User.Trim() : "anonymous", d.Password);
        request.EnableSsl = ftps;
        request.UseBinary = true;
        request.UsePassive = true;
        request.KeepAlive = false;
        request.Timeout = request.ReadWriteTimeout = (int)TimeSpan.FromMinutes(5).TotalMilliseconds;
        try
        {
            using var response = (FtpWebResponse)request.GetResponse();
            using var body = response.GetResponseStream();
            using var file = File.Create(temp);
            Copy(body, file, cancel).GetAwaiter().GetResult();
        }
        catch (WebException ex) when (ex.Response is FtpWebResponse r)
        {
            throw new InvalidOperationException(L.F("the FTP server answered {0}", r.StatusDescription?.Trim() ?? r.StatusCode.ToString()));
        }
    }

    public static bool SameFile(string a, string b)
    {
        try
        {
            if (!File.Exists(a) || !File.Exists(b) || new FileInfo(a).Length != new FileInfo(b).Length) return false;
            using var fa = File.OpenRead(a);
            using var fb = File.OpenRead(b);
            return SHA256.HashData(fa).AsSpan().SequenceEqual(SHA256.HashData(fb));
        }
        catch (Exception) { return false; }
    }
}

/// <summary>
/// Runs the station's downloads on the computer on air (never a standby one, so two studios don't fetch at once):
/// each when due, into a temporary file next to its own, checked, then put in its place when it isn't on air (the
/// same file again: only its time is updated, so it counts as fresh). A file not fetched for its too-old time is
/// passed over on air (<see cref="Stale"/>) and an alert says so. Off the screen's thread.
/// </summary>
sealed class Downloader : IDisposable
{
    static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(2);

    readonly Func<AppSettings> _settings;
    readonly Func<bool> _onAir;
    readonly PlayoutEngine _engine;
    readonly Action<string, string> _alert;
    readonly Timer _timer;
    int _busy;

    sealed class State
    {
        public DateTime? Fetched;    // UTC: when it was last fetched (or found fetched: its file's time)
        public DateTime Tried;       // UTC: the last attempt
        public string? Failing;      // why the last attempt failed
        public bool TooOldSaid;
        public DateTime Since = DateTime.UtcNow; // first looked at (with no file yet, too old counts from here)
        public string? Waiting;      // a checked new file (temporary) waiting until the old one is off air
        public DateTime WaitingSince;
        public string Local = "";
    }

    readonly Dictionary<string, State> _states = new(StringComparer.Ordinal);
    /// <summary>The local path of each download's file, and the time it was fetched (UTC), for <see cref="Stale"/>.</summary>
    readonly ConcurrentDictionary<string, (string Name, DateTime Fetched, int TooOld)> _fresh =
        new(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);

    public Downloader(Func<AppSettings> settings, Func<bool> onAir, PlayoutEngine engine, Action<string, string> alert)
    {
        _settings = settings;
        _onAir = onAir;
        _engine = engine;
        _alert = alert;
        _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Why the file mustn't go on air now (a download not fetched for its too-old time), or null. Quick: from what the
    /// downloads last saw, never the disk (called by the engine before an item starts).
    /// </summary>
    public string? Stale(string path)
    {
        if (_fresh.IsEmpty || !_fresh.TryGetValue(SharedPaths.ToLocal(path), out var f) || f.TooOld <= 0) return null;
        if (DateTime.UtcNow - f.Fetched < TimeSpan.FromMinutes(f.TooOld)) return null;
        return L.F("{0} is out of date (last fetched {1:dd/MM HH:mm})", f.Name, StationTime.FromUtc(f.Fetched));
    }

    void Tick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try { RunAsync().GetAwaiter().GetResult(); }
        catch (Exception ex) { AppLog.Write("Downloads: " + ex.Message); }
        finally { Volatile.Write(ref _busy, 0); }
    }

    async Task RunAsync()
    {
        var downloads = (_settings().Downloads ?? new()).Where(d => d.Enabled && d.SaveAs.Trim().Length > 0 && d.Address.Trim().Length > 0).ToList();
        var keys = downloads.Select(Key).ToHashSet();
        foreach (var gone in _states.Keys.Where(k => !keys.Contains(k)).ToList())
        {
            // Taken out or changed: its temporary file goes with it.
            if (_states[gone].Waiting is { } waiting) TryDelete(waiting);
            _states.Remove(gone);
        }
        foreach (var path in _fresh.Keys.Where(p => !downloads.Any(d => SharedPaths.ToLocal(d.SaveAs.Trim()) == p)).ToList()) _fresh.TryRemove(path, out _);
        bool onAir = _onAir();
        var now = DateTime.UtcNow;
        foreach (var d in downloads)
        {
            string key = Key(d), local = SharedPaths.ToLocal(d.SaveAs.Trim());
            if (!_states.TryGetValue(key, out var state)) _states[key] = state = new State { Local = local };
            // Fetched by another computer too (the one on air before this one), or before a restart: the file's own
            // time, at every look (here, never on the engine's thread).
            try
            {
                if (File.Exists(local) && File.GetLastWriteTimeUtc(local) is var written && (state.Fetched == null || written > state.Fetched))
                    state.Fetched = written;
            }
            catch (Exception) { /* not reachable now */ }
            _fresh[local] = (Name(d), state.Fetched ?? DateTime.MinValue, d.TooOldMinutes);
            if (!onAir)
            {
                // Standby now: the computer on air fetches its own.
                if (state.Waiting is { } waiting) TryDelete(waiting);
                state.Waiting = null;
                continue;
            }

            if (state.Waiting != null) await PutInPlace(d, state).ConfigureAwait(false);
            else
            {
                bool due = now >= d.DueAfter(state.Fetched ?? DateTime.MinValue, now)
                           && now - state.Tried >= (state.Failing != null ? RetryAfter : TimeSpan.Zero);
                if (due) await Fetch(d, state, local).ConfigureAwait(false);
            }
            TooOld(d, state, now);
        }
    }

    static string Key(DownloadSettings d) => d.SaveAs.Trim() + "|" + d.Address.Trim();

    static string Name(DownloadSettings d) => d.Name.Trim().Length > 0 ? d.Name.Trim() : Path.GetFileName(d.SaveAs.Trim());

    static void TryDelete(string file)
    {
        try { if (File.Exists(file)) File.Delete(file); }
        catch (Exception) { /* next time */ }
    }

    /// <summary>The new file checked, into its place once the old one isn't on air (never under the player). Whether it is.</summary>
    async Task<bool> PutInPlace(DownloadSettings d, State state)
    {
        if (state.Waiting is not { } temp) return false;
        // Waited longer than it is fetched every: the source has a newer one by now.
        if (DateTime.UtcNow - state.WaitingSince > TimeSpan.FromMinutes(d.IntervalMinutes()))
        {
            state.Waiting = null;
            TryDelete(temp);
            return false;
        }
        try
        {
            await _engine.ReplaceFileAsync(d.SaveAs.Trim(), () => File.Move(temp, state.Local, overwrite: true)).ConfigureAwait(false);
            state.Waiting = null;
            state.Fetched = DateTime.UtcNow;
            state.Failing = null;
            state.TooOldSaid = false;
            _fresh[state.Local] = (Name(d), state.Fetched.Value, d.TooOldMinutes);
            AppLog.Write($"Download {Name(d)}: a new file in place");
            return true;
        }
        catch (FilePlayingException) { return false; } // on air: tried again in 30 s
        catch (Exception ex)
        {
            // Can't be put there (on Windows: another computer has the file open): fetched again later.
            state.Waiting = null;
            TryDelete(temp);
            string why = ex.GetBaseException().Message;
            if (state.Failing != why) AppLog.Write(L.F("Download failed: {0}: {1}", Name(d), why));
            state.Failing = why;
            state.Tried = DateTime.UtcNow;
            return false;
        }
    }

    async Task Fetch(DownloadSettings d, State state, string local)
    {
        state.Tried = DateTime.UtcNow;
        string name = d.Name.Trim().Length > 0 ? d.Name.Trim() : Path.GetFileName(local);
        // This computer's own: another studio on air after it never shares it.
        string temp = $"{local}.{string.Concat(AppInfo.ComputerName.Where(char.IsLetterOrDigit))}.download";
        try
        {
            var got = await Download.FetchAsync(d, temp).ConfigureAwait(false);
            if (Download.SameFile(temp, local))
            {
                File.Delete(temp);
                File.SetLastWriteTimeUtc(local, DateTime.UtcNow); // still the latest: fresh
            }
            else
            {
                AppLog.Write($"Download {name}: a new file ({got.Seconds:0} s, from {got.Url})");
                state.Waiting = temp;
                state.WaitingSince = DateTime.UtcNow;
                // Or once the old one is off air (it waits); a move that failed is tried again after 2 minutes.
                if (!await PutInPlace(d, state).ConfigureAwait(false)) return;
            }
            state.Fetched = DateTime.UtcNow;
            if (state.Failing != null) AppLog.Write($"Download {name}: fetched again");
            state.Failing = null;
            state.TooOldSaid = false;
        }
        catch (Exception ex)
        {
            string why = ex.GetBaseException().Message;
            if (state.Failing != why) AppLog.Write(L.F("Download failed: {0}: {1}", name, why));
            state.Failing = why;
            TryDelete(temp);
        }
        _fresh[local] = (name, state.Fetched ?? DateTime.MinValue, d.TooOldMinutes);
    }

    /// <summary>Too old: said once (the log, an alert) until it is fetched again.</summary>
    void TooOld(DownloadSettings d, State state, DateTime now)
    {
        if (d.TooOldMinutes <= 0 || state.TooOldSaid || now - (state.Fetched ?? state.Since) < TimeSpan.FromMinutes(d.TooOldMinutes)) return;
        state.TooOldSaid = true;
        string name = d.Name.Trim().Length > 0 ? d.Name.Trim() : Path.GetFileName(d.SaveAs.Trim());
        string text = L.F("Download failed: {0} hasn't been fetched for {1} minutes ({2}); it is passed over on air", name, d.TooOldMinutes, state.Failing ?? L.T("not tried"));
        AppLog.Write(text);
        _alert("download", text);
    }

    public void Dispose() => _timer.Dispose();
}
