using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Playout.Library;
using Playout.Engine;

namespace Playout.App;

/// <summary>
/// Listener counts (the station's settings, Sintoniza Pro → Station settings → Streaming): the computer on air reads
/// how many listen to the stream from the Icecast or Shoutcast server's public statistics every minute.
/// </summary>
public sealed class ListenerSettings
{
    public bool Enabled { get; set; }
    /// <summary>The server's address (http://stream.example.com:8000), or its statistics page; empty: the stream server.</summary>
    public string Server { get; set; } = "";
    /// <summary>Icecast: the mount points counted, separated by commas ("*": all of them); empty: the stream's.</summary>
    public string Mounts { get; set; } = "";

    public ListenerSettings Clone() => (ListenerSettings)MemberwiseClone();
}

/// <summary>What a statistics page said: the listeners, or (null) why there is no count.</summary>
public sealed record ListenerReading(int? Count, string? Problem = null);

/// <summary>Reading a server's statistics: where they are, and the listeners in what it answers.</summary>
public static partial class ListenerStats
{
    /// <summary>Server pages that are statistics themselves (any other path is a folder of the server, or a mount point).</summary>
    static readonly string[] StatsPages = ["status-json.xsl", "stats", "statistics", "7.html"];

    /// <summary>
    /// The pages to ask, in order, until one answers with a count. A statistics page given (…/status-json.xsl,
    /// …/stats?sid=1&amp;json=1, …/7.html): only that one. Another path (a mount point pasted with the address, or the
    /// folder a web server puts the stream server under): the pages under it, then at the server's root.
    /// </summary>
    public static IReadOnlyList<Uri> Pages(string server, bool shoutcast)
    {
        string text = server.Trim();
        if (text.Length == 0) return [];
        if (!text.Contains("://", StringComparison.Ordinal)) text = "http://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return [];
        if (IsStatsPage(uri)) return [uri];
        var roots = new List<Uri> { new(uri.GetLeftPart(UriPartial.Authority) + "/") };
        string path = uri.AbsolutePath.Trim('/');
        if (path.Length > 0) roots.Insert(0, new Uri(uri.GetLeftPart(UriPartial.Authority) + "/" + path + "/"));
        return roots.SelectMany(root => shoutcast
            ? new[] { new Uri(root, "statistics?json=1"), new Uri(root, "7.html"), new Uri(root, "status-json.xsl") }
            : new[] { new Uri(root, "status-json.xsl"), new Uri(root, "statistics?json=1"), new Uri(root, "7.html") }).ToList();
    }

    static bool IsStatsPage(Uri uri) =>
        StatsPages.Contains(uri.Segments.LastOrDefault()?.Trim('/') ?? "", StringComparer.OrdinalIgnoreCase);

    /// <summary>A mount point in the server's address (http://host:8000/live): "/live"; null when there is none.</summary>
    public static string? MountIn(string server)
    {
        string text = server.Trim();
        if (!text.Contains("://", StringComparison.Ordinal)) text = "http://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || IsStatsPage(uri)) return null;
        string path = Uri.UnescapeDataString(uri.AbsolutePath).TrimEnd('/');
        return path.Length > 0 ? path : null;
    }

    /// <summary>
    /// The listeners in what a statistics page answered: Icecast's status-json.xsl (the mount points asked for, added
    /// up), Shoutcast 2's statistics (the streams of those mount points when it has several) or stats, Shoutcast 1's
    /// 7.html. Null: not one of them.
    /// </summary>
    public static ListenerReading? Parse(string body, IReadOnlyCollection<string> mounts)
    {
        body = body.Trim();
        bool all = mounts.Count == 0 || mounts.Contains("*");
        bool Wanted(string mount) => all || mounts.Contains(mount, StringComparer.OrdinalIgnoreCase);
        if (body.StartsWith('{'))
        {
            try
            {
                using var json = JsonDocument.Parse(body);
                var top = json.RootElement;
                if (top.TryGetProperty("icestats", out var stats))
                {
                    if (!stats.TryGetProperty("source", out var source)) return new(0); // nothing on air
                    var sources = source.ValueKind == JsonValueKind.Array ? source.EnumerateArray().ToList() : [source];
                    var counted = sources.Where(s => Wanted(Mount(s, "listenurl"))).ToList();
                    // On air, but not the mount points asked for: say so, rather than 0 listeners for ever.
                    if (counted.Count == 0 && sources.Count > 0)
                        return new(null, L.F("{0} isn't on the server; it has {1}", string.Join(", ", mounts), string.Join(", ", sources.Select(s => Mount(s, "listenurl")))));
                    return new(counted.Sum(s => Number(s, "listeners") ?? 0));
                }
                // Shoutcast 2 with several streams (stations) on one server: only this station's.
                if (!all && top.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array && streams.GetArrayLength() > 1)
                {
                    var mine = streams.EnumerateArray().Where(s => Wanted(Path(s))).ToList();
                    if (mine.Count > 0) return new(mine.Sum(s => Number(s, "currentlisteners") ?? 0));
                }
                return Number(top, "currentlisteners") is { } n ? new(n) : null;
            }
            catch (JsonException) { return null; }
        }
        // Shoutcast 1 (and 2): <html><body>listeners,status,peak,max,unique,bitrate,title</body></html>.
        var line = SevenHtml().Match(body);
        return line.Success ? new(int.Parse(line.Groups[1].Value, CultureInfo.InvariantCulture)) : null;
    }

    /// <summary>The mount points to count: "/live, live.aac" → "/live", "/live.aac".</summary>
    public static IReadOnlyCollection<string> MountList(string mounts, string streamMount) =>
        (mounts.Trim().Length > 0 ? mounts : streamMount).Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(m => m == "*" || m.StartsWith('/') ? m : "/" + m).ToHashSet(StringComparer.OrdinalIgnoreCase);

    static string Mount(JsonElement source, string name)
    {
        if (source.TryGetProperty(name, out var url) && url.ValueKind == JsonValueKind.String
            && Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri)) return Uri.UnescapeDataString(uri.AbsolutePath);
        return "";
    }

    static string Path(JsonElement stream) =>
        stream.TryGetProperty("streampath", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";

    static int? Number(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n)) return n;
        return v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out n) ? n : null;
    }

    [GeneratedRegex(@"<body[^>]*>\s*(\d+),", RegexOptions.IgnoreCase)]
    private static partial Regex SevenHtml();
}

/// <summary>
/// Reads the listeners every minute (in the background) and keeps each count in the library (the listeners table),
/// for Sintoniza Pro. Only the computer on air counts: the standby ones would count the same listeners again.
/// </summary>
public sealed class ListenerCounter : IDisposable
{
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(1);
    /// <summary>The most of a page read: statistics are small, a stream (an address of the audio) never ends.</summary>
    const int MaxPage = 1024 * 1024;

    readonly IReadOnlyList<Uri> _pages;
    readonly IReadOnlyCollection<string> _mounts;
    readonly Func<LibraryDb?> _db;
    readonly long _station;
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    readonly CancellationTokenSource _stop = new();
    Uri? _answered;

    /// <summary>The last count; null until the server has answered (or when it last didn't).</summary>
    public int? Count { get; private set; }
    /// <summary>When <see cref="Count"/> was read.</summary>
    public DateTime CountedAt { get; private set; }
    /// <summary>Why the last reading failed; null when it worked.</summary>
    public string? Error { get; private set; }
    /// <summary>The server's address, for the screen.</summary>
    public string Where { get; }

    /// <summary>Raised (on a background thread) after each reading.</summary>
    public event Action? Counted;

    public ListenerCounter(ListenerSettings settings, string streamHost, int streamPort, string streamMount, bool shoutcast, Func<LibraryDb?> db)
    {
        string server = settings.Server.Trim().Length > 0 ? settings.Server.Trim() : streamHost.Trim().Length > 0 ? $"http://{streamHost.Trim()}:{streamPort}" : "";
        Where = server;
        _pages = ListenerStats.Pages(server, shoutcast);
        // The mount points given, else one in the address given (http://host:8000/stream), else the stream's.
        _mounts = ListenerStats.MountList(settings.Mounts, settings.Server.Trim().Length > 0 ? ListenerStats.MountIn(settings.Server) ?? streamMount : streamMount);
        _db = db;
        _station = CurrentStation.Id;
        // Shoutcast 1 answers 7.html only to a browser.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; RadioPlayout)");
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RadioPlayout", Updates.CurrentText));
        if (_pages.Count == 0) Error = L.T("no server address");
        else Task.Run(Run);
    }

    async Task Run()
    {
        var token = _stop.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await ReadOnce(token);
                    Counted?.Invoke();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    AppLog.Write($"Listeners: {ex.Message}");
                }
                // On the minute, so each count is for its own minute.
                var now = StationTime.Now;
                await Task.Delay(TimeSpan.FromSeconds(60 - now.Second) - TimeSpan.FromMilliseconds(now.Millisecond) + TimeSpan.FromSeconds(2), token);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    async Task ReadOnce(CancellationToken token)
    {
        string? error = null;
        // The page that answered last time first; the others only when it doesn't.
        foreach (var page in _answered != null ? _pages.Where(p => p == _answered).Concat(_pages.Where(p => p != _answered)) : _pages)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var response = await _http.GetAsync(page, HttpCompletionOption.ResponseHeadersRead, token);
                if (!response.IsSuccessStatusCode) { error ??= $"{(int)response.StatusCode} {response.ReasonPhrase}"; continue; }
                // The audio itself (the stream's address in place of the server's): not read.
                string type = response.Content.Headers.ContentType?.MediaType ?? "";
                if (type.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) || type.StartsWith("video/", StringComparison.OrdinalIgnoreCase) || type == "application/ogg")
                {
                    error ??= L.T("that is the stream's address: give the server's (http://host:port)");
                    continue;
                }
                string body = await ReadPage(response, token);
                var reading = ListenerStats.Parse(body, _mounts);
                if (reading?.Count is not { } count) { error ??= reading?.Problem ?? L.T("the server's statistics weren't understood"); continue; }
                _answered = page;
                Count = count;
                CountedAt = StationTime.Now;
                Error = null;
                Store(count, CountedAt);
                return;
            }
            catch (Exception) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); } // stopped (disposed) meanwhile
            catch (Exception ex)
            {
                error ??= ex is TaskCanceledException ? L.T("the server didn't answer") : ex.Message;
            }
        }
        if (error != Error && error != null) AppLog.Write(L.F("Listeners: can't read them from {0} ({1})", Where, error));
        Count = null;
        Error = error;
    }

    static async Task<string> ReadPage(HttpResponseMessage response, CancellationToken token)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        var buffer = new byte[MaxPage];
        int read = 0, n;
        while (read < buffer.Length && (n = await stream.ReadAsync(buffer.AsMemory(read), token)) > 0) read += n;
        return System.Text.Encoding.UTF8.GetString(buffer, 0, read);
    }

    void Store(int count, DateTime at)
    {
        try
        {
            if (_db() is { } db) ListenersDb.Add(db, _station, at, count);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Listeners not kept in the database: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _http.Dispose();
        _stop.Dispose();
    }
}

/// <summary>A count: the listeners at a minute (local time).</summary>
public readonly record struct ListenerSample(DateTime At, int Listeners);

/// <summary>An hour's listeners: the average of its counts, the most and the fewest; Counts: how many minutes were read.</summary>
public sealed record ListenerHour(DateTime Hour, double Average, int Peak, int Low, int Counts);

/// <summary>The listener counts in the library database (listeners), for the station.</summary>
public static class ListenersDb
{
    const string Minute = "yyyy-MM-ddTHH:mm";

    public static void Add(LibraryDb db, long station, DateTime at, int listeners)
    {
        using var c = db.Connect();
        c.Execute($"{(db.IsMySql ? "REPLACE" : "INSERT OR REPLACE")} INTO listeners (station_id, minute, listeners) VALUES (@station, @minute, @listeners)",
            new { station, minute = at.ToString(Minute, CultureInfo.InvariantCulture), listeners });
    }

    /// <summary>Every count from one day to another (inclusive, local dates), in time order.</summary>
    public static List<ListenerSample> Read(LibraryDb db, DateTime from, DateTime to, long? station = null)
    {
        using var c = db.Connect();
        var rows = c.Query<(string Minute, int Listeners)>(
            "SELECT minute, listeners FROM listeners WHERE station_id = @station AND minute >= @from AND minute < @to ORDER BY minute",
            new
            {
                station = station ?? CurrentStation.Id,
                from = from.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                to = to.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            });
        var list = new List<ListenerSample>();
        foreach (var (minute, listeners) in rows)
            if (DateTime.TryParseExact(minute, Minute, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)) list.Add(new(at, listeners));
        return list;
    }

    /// <summary>The latest count, if it is from the last few minutes (the computer on air is counting).</summary>
    public static ListenerSample? Now(LibraryDb db, DateTime now, long? station = null)
    {
        using var c = db.Connect();
        var row = c.QueryFirstOrDefault<(string Minute, int Listeners)?>(
            "SELECT minute, listeners FROM listeners WHERE station_id = @station AND minute >= @since ORDER BY minute DESC LIMIT 1",
            new { station = station ?? CurrentStation.Id, since = now.AddMinutes(-3).ToString(Minute, CultureInfo.InvariantCulture) });
        return row is { } r && DateTime.TryParseExact(r.Minute, Minute, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? new ListenerSample(at, r.Listeners) : null;
    }

    /// <summary>The counts hour by hour (only the hours that have some).</summary>
    public static List<ListenerHour> Hours(IEnumerable<ListenerSample> samples) =>
        samples.GroupBy(s => new DateTime(s.At.Year, s.At.Month, s.At.Day, s.At.Hour, 0, 0))
            .OrderBy(g => g.Key)
            .Select(g => new ListenerHour(g.Key, g.Average(s => s.Listeners), g.Max(s => s.Listeners), g.Min(s => s.Listeners), g.Count()))
            .ToList();

    /// <summary>The listeners when something started: the count of its minute, else the last one before it (up to 5 minutes).</summary>
    public static int? At(IReadOnlyList<ListenerSample> samples, DateTime at)
    {
        // The samples are in time order: the last one at or before the item's minute.
        int lo = 0, hi = samples.Count - 1, found = -1;
        var minute = new DateTime(at.Year, at.Month, at.Day, at.Hour, at.Minute, 0);
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (samples[mid].At <= minute) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return found >= 0 && minute - samples[found].At <= TimeSpan.FromMinutes(5) ? samples[found].Listeners : null;
    }
}
