using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Playout.Engine;

namespace Playout.App;

/// <summary>
/// Now playing for the station's website (see docs/now-playing): sent as JSON to an address of your choice, and/or
/// written from templates to files or FTP (a text line, an XML or HTML page, the cover picture).
/// </summary>
public sealed class WebNowPlayingSettings
{
    public bool Enabled { get; set; }
    /// <summary>Where the JSON is posted, e.g. https://example.com/nowplaying.php; empty: only the outputs.</summary>
    public string Url { get; set; } = "";
    /// <summary>Sent in the X-Api-Key header, so the page can refuse posts that aren't from the station.</summary>
    public string Key { get; set; } = "";
    /// <summary>Only songs (and streams): during jingles and adverts the website keeps showing the last song.</summary>
    public bool SongsOnly { get; set; } = true;
    /// <summary>Seconds every change is held back, so the website changes when listeners of the stream hear it.</summary>
    public double DelaySeconds { get; set; }
    /// <summary>The JSON has the song's cover picture (a data: URL), when the file has one.</summary>
    public bool SendCover { get; set; }
    /// <summary>Files and FTP uploads made from templates.</summary>
    public List<MetadataOutput> Outputs { get; set; } = new();

    public WebNowPlayingSettings Clone()
    {
        var copy = (WebNowPlayingSettings)MemberwiseClone();
        copy.Outputs = Outputs.Select(o => o.Clone()).ToList();
        return copy;
    }
}

/// <summary>What an output writes: a text from its template, or the cover picture.</summary>
public enum MetadataOutputKind { Text, Cover }

/// <summary>A file, or an FTP upload, written each time now playing changes.</summary>
public sealed class MetadataOutput
{
    public bool Enabled { get; set; } = true;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MetadataOutputKind Kind { get; set; }
    /// <summary>Text: the fields of <see cref="MetadataTemplate"/>; may have several lines (an XML or HTML page).</summary>
    public string Template { get; set; } = "{artist} - {title}";
    /// <summary>Cover: the picture written when the song has none (the station's logo); empty: the file is left as it was.</summary>
    public string Fallback { get; set; } = "";
    /// <summary>
    /// A file on this computer (or a share), ftp://server/folder/file (ftps:// for FTP over TLS), or an http(s)://
    /// address that takes the file in a PUT (docs/now-playing/nowplaying.php?file=song.txt does).
    /// </summary>
    public string Target { get; set; } = "";
    /// <summary>FTP only.</summary>
    public string User { get; set; } = "";
    /// <summary>FTP: the password; HTTP: the key, sent in the X-Api-Key header.</summary>
    public string Password { get; set; } = "";

    public MetadataOutput Clone() => (MetadataOutput)MemberwiseClone();

    /// <summary>How long an FTP upload may take.</summary>
    public static TimeSpan FtpTimeout { get; set; } = TimeSpan.FromSeconds(20);

    [JsonIgnore]
    public bool IsFtp => Target.Trim().StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) || IsFtps;
    bool IsFtps => Target.Trim().StartsWith("ftps://", StringComparison.OrdinalIgnoreCase);
    [JsonIgnore]
    public bool IsHttp => Target.Trim().StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                          || Target.Trim().StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>The target as shown in messages.</summary>
    [JsonIgnore]
    public string Display => Target.Trim();

    /// <summary>Writes the bytes to the file (whole, never half) or uploads them. Throws with a readable message.</summary>
    public async Task WriteAsync(byte[] bytes, HttpClient http, CancellationToken token)
    {
        string target = Target.Trim();
        if (target.Length == 0) throw new InvalidOperationException(L.T("no file or FTP address set"));
        if (IsHttp)
        {
            using var put = new HttpRequestMessage(HttpMethod.Put, target) { Content = new ByteArrayContent(bytes) };
            put.Content.Headers.ContentType = new MediaTypeHeaderValue(Kind == MetadataOutputKind.Cover
                ? (bytes.Length > 4 && bytes[0] == 0x89 && bytes[1] == 0x50 ? "image/png" : "image/jpeg") : "text/plain");
            if (Password.Trim().Length > 0) put.Headers.Add("X-Api-Key", Password.Trim());
            using var answer = await http.SendAsync(put, token);
            if (!answer.IsSuccessStatusCode)
                throw new HttpRequestException(L.F("the website answered {0} {1}", (int)answer.StatusCode, answer.ReasonPhrase));
            return;
        }
        if (!IsFtp)
        {
            // Chosen on any studio's computer: as this one sees the shared folder (Settings → Paths).
            string path = Path.GetFullPath(SharedPaths.ToLocal(target));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string tmp = $"{path}.{Guid.NewGuid():N}.tmp"; // its own, so Send now and the playout never share one
            try
            {
                await File.WriteAllBytesAsync(tmp, bytes, token);
                File.Move(tmp, path, overwrite: true);
            }
            finally
            {
                File.Delete(tmp);
            }
            return;
        }
        if (!Uri.TryCreate(IsFtps ? "ftp://" + target[7..] : target, UriKind.Absolute, out var uri) || uri.AbsolutePath.EndsWith('/'))
            throw new InvalidOperationException(L.T("the FTP address needs a file name (ftp://server/folder/file.txt)"));
#pragma warning disable SYSLIB0014 // FtpWebRequest: .NET's only FTP client, and all this needs
        var request = (FtpWebRequest)WebRequest.Create(uri);
#pragma warning restore SYSLIB0014
        request.Method = WebRequestMethods.Ftp.UploadFile;
        request.Credentials = new NetworkCredential(User.Trim().Length > 0 ? User.Trim() : "anonymous", Password);
        request.EnableSsl = IsFtps;
        request.UseBinary = true;
        request.UsePassive = true;
        request.KeepAlive = false;
        request.ContentLength = bytes.Length;
        // The async calls don't heed Timeout: a server that stops answering (a blocked data port) is cut off here.
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(FtpTimeout);
        using var abort = limit.Token.Register(request.Abort);
        try
        {
            await using (var stream = await request.GetRequestStreamAsync()) await stream.WriteAsync(bytes, limit.Token);
            using var response = (FtpWebResponse)await request.GetResponseAsync();
        }
        catch (WebException ex) when (ex.Response is FtpWebResponse r)
        {
            throw new InvalidOperationException(L.F("the FTP server answered {0}", r.StatusDescription?.Trim() ?? r.StatusCode.ToString()));
        }
        catch (Exception) when (limit.IsCancellationRequested && !token.IsCancellationRequested)
        {
            throw new InvalidOperationException(L.T("no answer from the FTP server"));
        }
    }
}

/// <summary>What the website gets. Times are UTC (ISO 8601).</summary>
public sealed record NowPlayingInfo(
    [property: JsonPropertyName("station")] string Station,
    [property: JsonPropertyName("updated")] string Updated,
    [property: JsonPropertyName("now")] NowPlayingItem? Now,
    [property: JsonPropertyName("next")] NowPlayingItem? Next,
    [property: JsonPropertyName("show")] ShowInfo? Show = null,
    [property: JsonPropertyName("cover"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Cover = null);

/// <summary>The live show on air (Sintoniza Pro → Shows): for RDS ({show}, {presenter}) and the website. Ends: UTC.</summary>
public sealed record ShowInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("presenter")] string Presenter,
    [property: JsonPropertyName("ends")] string Ends);

public sealed record NowPlayingItem(
    [property: JsonPropertyName("artist")] string Artist,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("duration")] double Duration,
    [property: JsonPropertyName("started")] string? Started)
{
    public static NowPlayingItem From(Track t, DateTime? startedUtc) =>
        new(t.Artist, t.Title, AsRunLog.TypeName(t.Kind).ToLowerInvariant(), Math.Round(t.IsOpenEnded ? 0 : t.PlayLength, 1),
            startedUtc?.ToString("yyyy-MM-ddTHH:mm:ssZ"));
}

/// <summary>
/// The texts of the outputs: {artist}, {title}, {type}, {duration} (m:ss), {started} (local hh:mm), {next_artist},
/// {next_title}, {station}, {show}, {presenter}. Nothing on air: {title} is the station's name. In a .xml, .html or
/// .json file the fields are escaped for it. In other files, a line loses the separators an empty field leaves.
/// </summary>
public static class MetadataTemplate
{
    static readonly System.Text.RegularExpressions.Regex FieldPattern = new(@"\{([a-z_]+)\}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    const char Empty = '\u0001';
    const string Separators = "-–—|/:·";

    /// <summary>
    /// Takes out the empty fields with the separator they leave: "{artist} - {title}" with no artist is "Title", and
    /// "A - {x} - B" is "A - B". The song's own text is never changed.
    /// </summary>
    static string DropEmpty(string line)
    {
        int at;
        while ((at = line.IndexOf(Empty)) >= 0)
        {
            // A separator stuck to a word ("Mode:", "-Title") is part of that text, not one to take out.
            int left = at;
            while (left > 0 && (line[left - 1] == ' ' || Separators.Contains(line[left - 1]) && (left < 2 || line[left - 2] == ' ' || Separators.Contains(line[left - 2])))) left--;
            bool separatorLeft = line.AsSpan(left, at - left).IndexOfAny(Separators) >= 0;
            if (separatorLeft && left > 0)
            {
                line = line[..left] + line[(at + 1)..]; // "A - {x}…": the separator before it goes
                continue;
            }
            int right = at + 1;
            while (right < line.Length && (line[right] == ' ' || Separators.Contains(line[right]) && (right + 1 >= line.Length || line[right + 1] == ' ' || Separators.Contains(line[right + 1])))) right++;
            bool textBoth = left > 0 && right < line.Length;
            line = line[..left] + (textBoth ? " " : "") + line[right..]; // "{x} - B", "♪ {x} - B": the one after it
        }
        return line;
    }

    public static string Fill(string template, NowPlayingInfo info, string target = "")
    {
        string ext = Path.GetExtension(target.Trim()).ToLowerInvariant();
        Func<string, string> escape = ext switch
        {
            ".xml" or ".html" or ".htm" => v => v.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;"),
            ".json" => v => JsonEncodedText.Encode(v, JavaScriptEncoder.UnsafeRelaxedJsonEscaping).ToString(),
            _ => v => v,
        };
        var now = info.Now;
        string started = now?.Started is { } s && DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var at)
            ? at.ToLocalTime().ToString("HH:mm") : "";
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["artist"] = now?.Artist ?? "",
            ["title"] = now?.Title ?? info.Station,
            ["type"] = now?.Type ?? "",
            ["duration"] = now is { Duration: > 0 } ? TimeSpan.FromSeconds(Math.Round(now.Duration)).ToString(now.Duration >= 3600 ? @"h\:mm\:ss" : @"m\:ss") : "",
            ["started"] = started,
            ["next_artist"] = info.Next?.Artist ?? "",
            ["next_title"] = info.Next?.Title ?? "",
            ["station"] = info.Station,
            ["show"] = info.Show?.Name ?? "",
            ["presenter"] = info.Show?.Presenter ?? "",
        };
        bool markup = ext is ".xml" or ".html" or ".htm" or ".json";
        // In one pass, so a value with {braces} in it stays as it is; an empty field is marked for the clean-up below.
        var lines = template.Replace("\r\n", "\n").Split('\n').Select(line =>
        {
            string filled = FieldPattern.Replace(line, m => fields.TryGetValue(m.Groups[1].Value, out var value)
                ? value.Trim().Length > 0 ? escape(value.Trim()) : markup ? "" : Empty.ToString()
                : m.Value);
            return markup ? filled : DropEmpty(filled);
        });
        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>
/// Sends now playing to the website when what is on air (or next) changes: the JSON post and the outputs. Runs in the
/// background, each change held back by the delay; a target that can't be reached is tried again (every 30 s, latest
/// information only), so nothing piles up. Songs marked "Not on the website" in the library are never sent: the
/// website keeps what it had.
/// </summary>
public sealed class WebNowPlaying : IDisposable
{
    static readonly TimeSpan Retry = TimeSpan.FromSeconds(30);
    const int MaxCover = 1024 * 1024;

    /// <summary>What was on air at a moment, to be sent when <see cref="Due"/> comes.</summary>
    sealed record Change(Track? OnAir, DateTime Started, Track? Next, ShowInfo? Show, DateTime Due);

    /// <summary>What the targets get: the JSON and the cover picture (null: none).</summary>
    sealed record Prepared(NowPlayingInfo Info, byte[]? Cover);

    /// <summary>The JSON post, or one output, and what it was last sent.</summary>
    sealed class Target(string name, MetadataOutput? output)
    {
        public string Name { get; } = name;
        public MetadataOutput? Output { get; } = output;
        public Prepared? Sent;
        public string? Error;
    }

    readonly WebNowPlayingSettings _s;
    readonly string _station;
    readonly Func<string, bool>? _notForWeb;
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    readonly SemaphoreSlim _wake = new(0);
    readonly SemaphoreSlim _sending = new(1, 1);
    readonly CancellationTokenSource _stop = new();
    readonly object _lock = new();
    readonly Queue<Change> _changes = new();
    readonly List<Target> _targets = new();
    string? _key;
    Guid? _nowId;
    DateTime _nowStarted;
    Change? _lastChange;
    Prepared? _current;
    (string Path, byte[]? Cover)? _cover;

    /// <summary>Null after sending worked everywhere; the reason when it last failed somewhere.</summary>
    public string? Error { get; private set; }

    /// <summary>Raised (on a background thread) when sending starts failing or works again: the error, or null.</summary>
    public event Action<string?>? StatusChanged;

    /// <param name="notForWeb">Whether the song with this file is kept off the website (asked in the background).</param>
    public WebNowPlaying(WebNowPlayingSettings settings, string station, Func<string, bool>? notForWeb = null)
    {
        _s = settings.Clone();
        _station = station;
        _notForWeb = notForWeb;
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RadioPlayout", Updates.CurrentText));
        if (_s.Url.Trim().Length > 0 || _s.Outputs.All(o => !o.Enabled)) _targets.Add(new Target(L.T("website"), null));
        foreach (var o in _s.Outputs.Where(o => o.Enabled)) _targets.Add(new Target(o.Display, o));
        Task.Run(Run);
    }

    /// <summary>What is on air and next (null: nothing), and the live show. Cheap to call often: sends only when they change.</summary>
    public void Update(Track? onAir, Track? next, ShowInfo? show = null)
    {
        if (onAir?.Id != _nowId)
        {
            _nowId = onAir?.Id;
            _nowStarted = DateTime.UtcNow;
        }
        string key = $"{onAir?.Id}|{onAir?.Artist}|{onAir?.Title}|{next?.Id}|{next?.Artist}|{next?.Title}|{show?.Name}|{show?.Presenter}";
        if (key == _key) return;
        _key = key;
        var change = new Change(onAir, _nowStarted, next, show, DateTime.UtcNow.AddSeconds(Math.Max(0, _s.DelaySeconds)));
        lock (_lock) _changes.Enqueue(change);
        _wake.Release();
    }

    /// <summary>Sends the latest information now, without the delay, and reports how it went (for the Send now button).</summary>
    public async Task<string?> SendNowAsync()
    {
        await _sending.WaitAsync();
        try
        {
            Change? latest;
            lock (_lock)
            {
                latest = _changes.LastOrDefault() ?? _lastChange;
                _changes.Clear();
            }
            if (latest != null) Prepare(latest);
            // What is on air isn't for the website: nothing sent, rather than "nothing on air".
            if (_current == null && latest?.OnAir != null) return L.T("what is on air isn't for the website (a pause, or a song kept off it): nothing sent");
            _current ??= new Prepared(new NowPlayingInfo(_station, Stamp(DateTime.UtcNow), null, null), null);
            foreach (var t in _targets) t.Sent = null;
            await SendAll();
        }
        finally
        {
            _sending.Release();
        }
        return Error;
    }

    async Task Run()
    {
        var token = _stop.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var wait = Timeout.InfiniteTimeSpan;
                await _sending.WaitAsync(token);
                try
                {
                    var due = new List<Change>();
                    lock (_lock)
                    {
                        while (_changes.Count > 0 && _changes.Peek().Due <= DateTime.UtcNow) due.Add(_changes.Dequeue());
                        if (_changes.Count > 0) wait = _changes.Peek().Due - DateTime.UtcNow;
                    }
                    // In order: if the last is a song kept off the website, the one before it is what is shown.
                    foreach (var change in due) Prepare(change);
                    await SendAll();
                    // After a failure, try again in a while; otherwise wait for news (or the next change's time).
                    if (_targets.Any(t => t.Error != null) && (wait == Timeout.InfiniteTimeSpan || wait > Retry)) wait = Retry;
                }
                finally
                {
                    _sending.Release();
                }
                if (wait != Timeout.InfiniteTimeSpan && wait < TimeSpan.Zero) wait = TimeSpan.Zero;
                await _wake.WaitAsync(wait, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Makes what the targets get from a change; a change the website shouldn't show leaves it as it was.</summary>
    void Prepare(Change c)
    {
        _lastChange = c;
        if (c.OnAir != null && !Shown(c.OnAir)) return; // keep showing the last song
        var next = c.Next != null && Shown(c.Next) ? c.Next : null;
        byte[]? cover = null;
        if (c.OnAir != null && (_s.SendCover || _targets.Any(t => t.Output?.Kind == MetadataOutputKind.Cover)))
        {
            if (_cover?.Path != c.OnAir.Path)
            {
                byte[]? read = c.OnAir.IsStream || c.OnAir.Kind == TrackKind.Pause ? null : Tags.ReadCover(c.OnAir.Path);
                _cover = (c.OnAir.Path, read is { Length: <= MaxCover } ? read : null);
            }
            cover = _cover.Value.Cover;
        }
        string? coverUrl = _s.SendCover && cover != null ? $"data:{(IsPng(cover) ? "image/png" : "image/jpeg")};base64,{Convert.ToBase64String(cover)}" : null;
        var info = new NowPlayingInfo(_station, Stamp(DateTime.UtcNow),
            c.OnAir == null ? null : NowPlayingItem.From(c.OnAir, c.Started), next == null ? null : NowPlayingItem.From(next, null), c.Show, coverUrl);
        if (_current != null && Same(_current.Info, info)) return;
        _current = new Prepared(info, cover);
    }

    static bool Same(NowPlayingInfo a, NowPlayingInfo b) => a with { Updated = "" } == b with { Updated = "" };

    /// <summary>A song the website may show: not a pause, not a jingle or advert with Songs only, not marked "Not on the website".</summary>
    bool Shown(Track t)
    {
        if (t.Kind == TrackKind.Pause) return false;
        if (_s.SongsOnly && t.Kind is not (TrackKind.Music or TrackKind.Stream)) return false;
        if (t.IsStream || _notForWeb == null) return true;
        try { return !_notForWeb(t.Path); }
        catch { return true; } // the library can't be read: as usual
    }

    /// <summary>Every target that hasn't got the latest yet, all at once: one that is slow or down holds up no other.</summary>
    async Task SendAll()
    {
        if (_current is not { } current) return;
        await Task.WhenAll(_targets.Where(t => !ReferenceEquals(t.Sent, current)).Select(t => SendTo(t, current)));
        if (_stop.IsCancellationRequested) return;
        string? error = _targets.FirstOrDefault(t => t.Error != null) is { } failed
            ? (_targets.Count > 1 ? $"{failed.Name}: {failed.Error}" : failed.Error) : null;
        if (error == Error) return;
        Error = error;
        StatusChanged?.Invoke(error);
    }

    async Task SendTo(Target t, Prepared current)
    {
        try
        {
            if (t.Output == null) await Post(current.Info);
            else if (t.Output.Kind == MetadataOutputKind.Cover)
            {
                var bytes = current.Cover;
                if (bytes == null && t.Output.Fallback.Trim() is { Length: > 0 } fallback)
                {
                    string file = SharedPaths.ToLocal(fallback);
                    if (!File.Exists(file)) throw new FileNotFoundException(L.F("the picture for songs without a cover isn't there: {0}", file));
                    bytes = await File.ReadAllBytesAsync(file, _stop.Token);
                }
                if (bytes != null) await t.Output.WriteAsync(bytes, _http, _stop.Token);
            }
            else await t.Output.WriteAsync(new UTF8Encoding(false).GetBytes(MetadataTemplate.Fill(t.Output.Template, current.Info, FileName(t.Output.Target))), _http, _stop.Token);
            t.Sent = current;
            t.Error = null;
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            t.Error = ex is TaskCanceledException ? L.T("no answer") : ex.Message;
        }
    }

    async Task Post(NowPlayingInfo info)
    {
        if (!Uri.TryCreate(_s.Url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException(L.T("No website address set (http:// or https://)"));
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(JsonSerializer.Serialize(info), Encoding.UTF8, "application/json"),
        };
        if (_s.Key.Trim().Length > 0) request.Headers.Add("X-Api-Key", _s.Key.Trim());
        using var response = await _http.SendAsync(request, _stop.Token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(L.F("the website answered {0} {1}", (int)response.StatusCode, response.ReasonPhrase));
    }

    /// <summary>The file name a target ends in (for an http address, its ?file=), which says how fields are escaped.</summary>
    static string FileName(string target)
    {
        target = target.Trim();
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            return System.Web.HttpUtility.ParseQueryString(uri.Query)["file"] ?? uri.AbsolutePath;
        return target;
    }

    static string Stamp(DateTime utc) => utc.ToString("yyyy-MM-ddTHH:mm:ssZ");

    static bool IsPng(byte[] b) => b.Length > 4 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47;

    public void Dispose()
    {
        _stop.Cancel();
        _http.Dispose();
    }
}
