using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Playout.Engine;

namespace Playout.App;

/// <summary>Now playing for the station's website: sent as JSON to an address of your choice (see docs/now-playing).</summary>
public sealed class WebNowPlayingSettings
{
    public bool Enabled { get; set; }
    /// <summary>Where the JSON is posted, e.g. https://example.com/nowplaying.php.</summary>
    public string Url { get; set; } = "";
    /// <summary>Sent in the X-Api-Key header, so the page can refuse posts that aren't from the station.</summary>
    public string Key { get; set; } = "";
    /// <summary>Only songs (and streams): during jingles and adverts the website keeps showing the last song.</summary>
    public bool SongsOnly { get; set; } = true;

    public WebNowPlayingSettings Clone() => (WebNowPlayingSettings)MemberwiseClone();
}

/// <summary>What the website gets. Times are UTC (ISO 8601).</summary>
public sealed record NowPlayingInfo(
    [property: JsonPropertyName("station")] string Station,
    [property: JsonPropertyName("updated")] string Updated,
    [property: JsonPropertyName("now")] NowPlayingItem? Now,
    [property: JsonPropertyName("next")] NowPlayingItem? Next);

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
/// Posts now playing to the website when what is on air (or next) changes. Runs in the background; when the website
/// can't be reached it tries again (every 30 s, latest information only), so nothing piles up.
/// </summary>
public sealed class WebNowPlaying : IDisposable
{
    static readonly TimeSpan Retry = TimeSpan.FromSeconds(30);

    readonly WebNowPlayingSettings _s;
    readonly string _station;
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    readonly SemaphoreSlim _wake = new(0);
    readonly CancellationTokenSource _stop = new();
    readonly object _lock = new();
    string? _key;
    Guid? _nowId;
    DateTime _nowStarted;
    NowPlayingInfo? _pending; // not sent yet
    NowPlayingInfo? _last;    // last one sent (Send now sends it again)

    /// <summary>Null after a post that worked; the reason when the last one failed.</summary>
    public string? Error { get; private set; }

    /// <summary>Raised (on a background thread) when posting starts failing or works again: the error, or null.</summary>
    public event Action<string?>? StatusChanged;

    public WebNowPlaying(WebNowPlayingSettings settings, string station)
    {
        _s = settings.Clone();
        _station = station;
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RadioPlayout", Updates.CurrentText));
        Task.Run(Run);
    }

    /// <summary>What is on air and next (null: nothing). Cheap to call often: posts only when they change.</summary>
    public void Update(Track? onAir, Track? next)
    {
        if (onAir?.Id != _nowId)
        {
            _nowId = onAir?.Id;
            _nowStarted = DateTime.UtcNow;
        }
        bool song = onAir == null || onAir.Kind is TrackKind.Music or TrackKind.Stream;
        if (_s.SongsOnly && !song) return; // keep showing the last song
        if (_s.SongsOnly && next != null && next.Kind is not (TrackKind.Music or TrackKind.Stream)) next = null;
        string key = $"{onAir?.Id}|{onAir?.Artist}|{onAir?.Title}|{next?.Id}|{next?.Artist}|{next?.Title}";
        if (key == _key) return;
        _key = key;
        var info = new NowPlayingInfo(_station, DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            onAir == null ? null : NowPlayingItem.From(onAir, _nowStarted), next == null ? null : NowPlayingItem.From(next, null));
        lock (_lock) _pending = info;
        _wake.Release();
    }

    /// <summary>Posts the latest information now and reports how it went (for the Test button).</summary>
    public async Task<string?> SendNowAsync()
    {
        lock (_lock) _pending ??= _last ?? new NowPlayingInfo(_station, DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"), null, null);
        await Send();
        return Error;
    }

    async Task Run()
    {
        var token = _stop.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Send();
                bool waiting;
                lock (_lock) waiting = _pending != null;
                // After a failure, try again in a while; otherwise wait for news.
                await _wake.WaitAsync(waiting ? Retry : Timeout.InfiniteTimeSpan, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    readonly SemaphoreSlim _sending = new(1, 1);

    async Task Send()
    {
        await _sending.WaitAsync();
        try
        {
            NowPlayingInfo? info;
            lock (_lock) info = _pending;
            if (info == null) return;
            string? error = null;
            try
            {
                if (!Uri.TryCreate(_s.Url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                    throw new InvalidOperationException("No website address set (http:// or https://)");
                using var request = new HttpRequestMessage(HttpMethod.Post, uri)
                {
                    Content = new StringContent(JsonSerializer.Serialize(info), Encoding.UTF8, "application/json"),
                };
                if (_s.Key.Trim().Length > 0) request.Headers.Add("X-Api-Key", _s.Key.Trim());
                using var response = await _http.SendAsync(request, _stop.Token);
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"the website answered {(int)response.StatusCode} {response.ReasonPhrase}");
                lock (_lock)
                {
                    if (_pending == info) _pending = null;
                    _last = info;
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                error = ex is TaskCanceledException ? "no answer from the website" : ex.Message;
            }
            if (error == Error) return;
            Error = error;
            StatusChanged?.Invoke(error);
        }
        finally
        {
            _sending.Release();
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _http.Dispose();
    }
}
