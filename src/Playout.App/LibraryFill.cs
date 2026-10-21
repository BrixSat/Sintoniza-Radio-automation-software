using Playout.Library;

namespace Playout.App;

/// <summary>
/// Fill music from the library: songs from chosen categories (enabled, file not missing), keeping songs and artists
/// apart. It uses the play history of every studio sharing the library, and prefers the songs that haven't been
/// on air for longest, so the whole category gets played. When the library can't be reached or has nothing to
/// pick, the fill folder (if any) takes over.
/// </summary>
sealed class LibraryFill
{
    static readonly TimeSpan Reload = TimeSpan.FromMinutes(10);
    static readonly TimeSpan ReloadEmpty = TimeSpan.FromSeconds(30);
    static readonly TimeSpan PlaysReload = TimeSpan.FromMinutes(1);
    /// <summary>Picks among the songs rested longest: this share of the ones that may play.</summary>
    const double OldestShare = 0.25;

    readonly LibraryService _service;
    readonly IReadOnlyCollection<long> _categories;
    readonly TimeSpan _songGap, _artistGap;
    readonly FillMusic? _fallback;
    readonly object _lock = new();

    Candidate[] _tracks = [];
    List<RecentPlay> _plays = new();
    readonly List<RecentPlay> _picks = new(); // ours, until the play history has them
    DateTime _loaded = DateTime.MinValue, _playsLoaded = DateTime.MinValue;
    int _gone; // songs set aside because their file isn't there (the first is logged)
    Task? _load;

    /// <param name="categories">Category ids; empty = every music category.</param>
    /// <param name="fallback">Used while the library has nothing to pick.</param>
    public LibraryFill(LibraryService service, IReadOnlyCollection<long> categories, TimeSpan songGap, TimeSpan artistGap, FillMusic? fallback)
    {
        _service = service;
        _categories = categories.ToList();
        _songGap = songGap;
        _artistGap = artistGap;
        _fallback = fallback;
        StartLoad();
    }

    /// <summary>A file to play, or null if there is none yet. Called on the engine thread: never waits for the library.</summary>
    public string? Next()
    {
        var now = DateTime.UtcNow;
        if (now - _loaded > (_tracks.Length == 0 ? ReloadEmpty : Reload) || now - _playsLoaded > PlaysReload) StartLoad();

        lock (_lock)
        {
            if (_tracks.Length == 0) return _fallback?.Next();

            // When each song and artist was last on air: the library's history and our own recent picks.
            var trackLast = new Dictionary<long, DateTime>();
            var artistLast = new Dictionary<string, DateTime>();
            foreach (var p in _plays.Concat(_picks))
            {
                if (p.TrackId is { } id && (!trackLast.TryGetValue(id, out var t) || p.At > t)) trackLast[id] = p.At;
                if (Artist(p.Artist) is { } a && (!artistLast.TryGetValue(a, out var at) || p.At > at)) artistLast[a] = p.At;
            }
            DateTime? Last(Candidate c)
            {
                DateTime? last = c.Track.LastPlayed;
                if (trackLast.TryGetValue(c.Track.Id, out var recent) && (last == null || recent > last)) last = recent;
                return last;
            }
            bool SongRested(Candidate c) => Last(c) is not { } last || now - last >= _songGap;
            bool ArtistRested(Candidate c) =>
                c.Artist is not { } a || !artistLast.TryGetValue(a, out var last) || now - last >= _artistGap;

            // A song whose file isn't there (moved or deleted since the last scan) is set aside, and another picked:
            // it would only be skipped on air. It comes back with the next reload if the file does.
            // Songs whose dates, days or hours don't allow them now are left out (unless that leaves nothing: never silence).
            var local = DateTime.Now;
            for (int tries = 0; tries < 50 && _tracks.Length > 0; tries++)
            {
                var pool = _tracks.Where(t => t.Track.Rules?.AllowsAt(local) != false).ToArray();
                if (pool.Length == 0) pool = _tracks;
                // Both rules if possible; if the categories are too small for that, the song rule; else the longest rested.
                var choices = pool.Where(t => SongRested(t) && ArtistRested(t)).ToList();
                if (choices.Count == 0) choices = pool.Where(SongRested).ToList();
                if (choices.Count == 0) choices = pool.ToList();

                var oldest = choices.OrderBy(t => Last(t) ?? DateTime.MinValue).ToList();
                int take = Math.Max(1, (int)Math.Ceiling(oldest.Count * OldestShare));
                var chosen = oldest[Random.Shared.Next(take)];
                if (!File.Exists(chosen.Track.Path))
                {
                    _tracks = _tracks.Where(t => t != chosen).ToArray();
                    if (_gone++ == 0) AppLog.Write($"Fill music: {chosen.Track.Path} isn't there (moved or deleted?); a library scan brings the library up to date");
                    continue;
                }
                _picks.Add(new RecentPlay(chosen.Track.Id, chosen.Track.Artist, now));
                return chosen.Track.Path;
            }
            return _fallback?.Next();
        }
    }

    /// <summary>A song that may be picked, with its artist as compared (lower case, no accents; null if none).</summary>
    sealed record Candidate(RotationTrack Track, string? Artist);

    static string? Artist(string artist) => LibraryDb.Normalize(artist).Trim() is { Length: > 0 } a ? a : null;

    /// <summary>Reads the songs (every few minutes) and the recent history (every minute) in the background.</summary>
    void StartLoad()
    {
        lock (_lock)
        {
            if (_load is { IsCompleted: false }) return;
            var now = DateTime.UtcNow;
            bool tracks = now - _loaded > (_tracks.Length == 0 ? ReloadEmpty : Reload);
            _playsLoaded = now;
            if (tracks) _loaded = now;
            _load = Task.Run(() =>
            {
                if (_service.Db is not { } db)
                {
                    // Library still opening or away: keep what we have (or the folder), and ask again next time.
                    lock (_lock) _loaded = _playsLoaded = DateTime.MinValue;
                    return;
                }
                try
                {
                    var window = _songGap > _artistGap ? _songGap : _artistGap;
                    var plays = db.PlaysSince(now - window - TimeSpan.FromMinutes(5));
                    Candidate[]? found = null;
                    if (tracks)
                    {
                        var ids = _categories.Count > 0
                            ? _categories
                            : db.Categories().Where(c => c.Kind == "music").Select(c => c.Id).ToList();
                        found = db.RotationTracks(ids).Select(t => new Candidate(t, Artist(t.Artist))).ToArray();
                    }
                    lock (_lock)
                    {
                        _plays = plays;
                        // Picks the history now has (or that are older than any rule) are no longer needed.
                        _picks.RemoveAll(p => p.At < now - window || plays.Any(x => x.TrackId == p.TrackId && x.At >= p.At));
                        if (found != null) _tracks = found;
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Write($"Fill music: library unavailable ({ex.Message})");
                }
            });
        }
    }
}
