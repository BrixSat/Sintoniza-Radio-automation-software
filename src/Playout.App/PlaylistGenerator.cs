using System.Text.Json;
using Playout.Engine;
using Playout.Library;

namespace Playout.App;

/// <summary>An event or ad break in an hour of the plan: when, and what it will play (as booked now).</summary>
public sealed record PlannedEntry(DateTime At, ScheduleEntry Entry, IReadOnlyList<PlannedPart> Parts)
{
    public double Seconds => Parts.Sum(p => p.Seconds);
    public string Label => ScheduleItems.Label(Entry, At);
}

/// <summary>An item of an event or break in the plan.</summary>
public sealed record PlannedPart(string Path, TrackKind Kind, string Title, string? Advertiser, double Seconds);

/// <summary>
/// Makes the hours of the daily playlists from the hour templates placed on the calendar (Sintoniza Pro →
/// Playlists): each line of the template in turn (a song from a library category, a file from a folder), until the
/// hour is full with the ad breaks and events booked in it. Songs and artists are kept apart by the rest times, counting
/// what played and what is planned before; the songs rested longest come first. The music rules: a line may ask for a
/// tempo and energy range; a template may keep an artist and an album apart for some hours, and limit slow songs in a
/// row. A rule that leaves nothing to play is let go, the least important first (album, slow songs, artist). What doesn't quite fit at the end is
/// marked optional, with one spare song, so the playout can start the next hour on time either way.
/// </summary>
public sealed class PlaylistGenerator
{
    public sealed record Options(double Crossfade, double OtherCrossfade, TimeSpan SongGap, TimeSpan ArtistGap);

    /// <summary>Picks among the songs rested longest: this share of the ones that may play.</summary>
    const double OldestShare = 0.25;

    readonly LibraryDb _db;
    readonly ScheduleData _data;
    readonly Options _options;
    readonly Dictionary<long, List<RotationTrack>> _categories = new();
    readonly Dictionary<string, FillMusic> _folders = new();
    readonly Dictionary<string, double> _lengths = new();
    /// <summary>What played and what is planned: each song and artist's latest time.</summary>
    readonly Times<long> _trackTimes = new();
    readonly Times<string> _artistTimes = new();
    readonly Times<string> _pathTimes = new(StringComparer.Ordinal);
    readonly Times<string> _albumTimes = new();
    /// <summary>The songs' tempos (null: not set) by when they play, for the slow songs in a row.</summary>
    readonly SortedList<DateTime, int?> _tempos = new();
    /// <summary>Lines whose tempo or energy found no song (the whole category played instead).</summary>
    readonly HashSet<string> _relaxed = new();

    public PlaylistGenerator(LibraryDb db, ScheduleData data, Options options)
    {
        _db = db;
        _data = data;
        _options = options;
    }

    /// <summary>The template as it matters for the plan: an hour made from another version of it is out of date.</summary>
    public static string Signature(PlaylistTemplate? t) => t == null ? ""
        : t.Id + JsonSerializer.Serialize(t.Lines.Select(l => new { l.Source, l.CategoryId, l.Kind, l.Folder }))
          + (t.Lines.Any(l => l.HasFilter) ? JsonSerializer.Serialize(t.Lines.Select(l => new { l.TempoMin, l.TempoMax, l.EnergyMin, l.EnergyMax })) : "")
          + (t.ArtistHours > 0 || t.AlbumHours > 0 || t.MaxSlowInRow > 0 ? $"|{t.ArtistHours}|{t.AlbumHours}|{t.MaxSlowInRow}" : "");

    /// <summary>
    /// What played (the library's history) and what is planned (<paramref name="planned"/>, from its hour's start)
    /// before the hours to make: for the rest times. Call once, then <see cref="Generate"/> the hours in order.
    /// </summary>
    public void Remember(DateTime from, IReadOnlyDictionary<DateTime, List<PlanItem>> planned)
    {
        var window = new[] { _options.SongGap, _options.ArtistGap }
            .Concat(_data.Templates.SelectMany(t => new[] { TimeSpan.FromHours(t.ArtistHours), TimeSpan.FromHours(t.AlbumHours) })).Max();
        try
        {
            foreach (var p in _db.PlaysSince((from - window).ToUniversalTime()))
                Note(p.TrackId, p.Artist, null, p.At.ToLocalTime(), p.Album, p.Tempo, p.TrackId != null);
        }
        catch { /* no history: the rest times count only what is planned */ }
        Dictionary<long, (string Album, int? Tempo)> songs;
        try { songs = _db.AlbumsAndTempos(planned.Values.SelectMany(i => i).Where(i => i.TrackId != null).Select(i => i.TrackId!.Value).ToList()); }
        catch { songs = new(); }
        foreach (var (hour, items) in planned)
        {
            var at = hour;
            foreach (var i in items)
            {
                var song = i.TrackId is { } id ? songs.GetValueOrDefault(id) : default;
                Note(i.TrackId, i.Artist, i.Path, at, song.Album ?? "", song.Tempo, i.Kind == TrackKind.Music);
                at = at.AddSeconds(i.Seconds);
            }
        }
    }

    void Note(long? trackId, string artist, string? path, DateTime at, string album = "", int? tempo = null, bool music = false)
    {
        if (trackId is { } id) _trackTimes.Add(id, at);
        if (ArtistKey(artist) is { } a) _artistTimes.Add(a, at);
        if (path != null) _pathTimes.Add(path, at);
        if (AlbumKey(artist, album) is { } b) _albumTimes.Add(b, at);
        if (music) _tempos[at] = tempo;
    }

    /// <summary>An album by its artist and name ("Greatest Hits" of two artists are two albums).</summary>
    static string? AlbumKey(string artist, string album) =>
        LibraryDb.Normalize(album).Trim() is { Length: > 0 } b ? (ArtistKey(artist) ?? "") + "\u0001" + b : null;

    static bool Slow(int? tempo) => tempo is >= 1 and <= 2;

    /// <summary>How many slow songs play in a row just before <paramref name="at"/>.</summary>
    int SlowBefore(DateTime at)
    {
        int n = 0;
        for (int i = _tempos.Count - 1; i >= 0; i--)
        {
            if (_tempos.Keys[i] >= at) continue;
            if (!Slow(_tempos.Values[i])) break;
            n++;
        }
        return n;
    }

    /// <summary>The artists planned or played last before <paramref name="at"/>, the latest first.</summary>
    List<string> LatestArtists(DateTime at, int count) => _artistTimes.LatestBefore(at, count);

    static string? ArtistKey(string artist) => LibraryDb.Normalize(artist).Trim() is { Length: > 0 } a ? a : null;

    /// <summary>The events and ad breaks booked from <paramref name="hour"/> to an hour later, with what they play now.</summary>
    public List<PlannedEntry> Entries(DateTime hour)
    {
        var list = new List<PlannedEntry>();
        foreach (var entry in _data.Events.Cast<ScheduleEntry>().Concat(_data.Breaks).Where(e => e.Enabled))
            // One that ends at its time is in the hour it ends in: a break that ends at 18:00 is the last of 17:00.
            foreach (var at in entry.TimesOn(hour.Date).Concat(entry.TimesOn(hour.Date.AddDays(1))).Where(t => entry.Start == EventStart.EndAt
                         ? t > hour && t <= hour.AddHours(1)
                         : t >= hour && t < hour.AddHours(1)))
            {
                var parts = ScheduleItems.For(_data, entry, at, null).Select(i => i.StreamName != null
                    ? new PlannedPart(i.Path, TrackKind.Stream, i.StreamName, null, i.StreamSeconds)
                    : new PlannedPart(i.Path, i.Kind ?? TrackClassifier.Classify(i.Path), Path.GetFileNameWithoutExtension(i.Path), i.Advertiser, Length(i.Path)))
                    .ToList();
                list.Add(new PlannedEntry(at, entry, parts));
            }
        return list.OrderBy(e => e.At).ToList();
    }

    double Length(string path)
    {
        if (_lengths.TryGetValue(path, out double s)) return s;
        s = File.Exists(path) ? AudioProbe.Duration(path) ?? 0 : 0;
        return _lengths[path] = s;
    }

    /// <summary>
    /// Makes the hour that starts at <paramref name="hour"/> (local): its items in order, and the hour's record. With no
    /// template placed there, an hour with no items (the playout then fills with music).
    /// </summary>
    public (PlanHour Hour, List<PlanItem> Items, List<string> Problems) Generate(DateTime hour)
    {
        var template = _data.TemplateAt(hour);
        var record = new PlanHour
        {
            Start = hour, TemplateId = template?.Id, Signature = Signature(template), GeneratedAt = DateTime.UtcNow, GeneratedBy = Environment.MachineName,
        };
        var items = new List<PlanItem>();
        var problems = new List<string>();
        if (template == null) return (record, items, problems);

        // The end of the hour before may run past the hour (its optional songs, if they play): it comes before this one.
        var tail = _tempos.Where(kv => kv.Key >= hour && kv.Key < hour.AddMinutes(15)).ToList();
        for (int i = 0; i < tail.Count; i++)
        {
            _tempos.Remove(tail[i].Key);
            _tempos[hour.AddMilliseconds(i - tail.Count)] = tail[i].Value;
        }

        // The hour less what the breaks and events booked in it take.
        double target = 3600 - Entries(hour).Sum(e => e.Seconds);
        double used = 0;
        int line = 0, misses = 0;
        var lines = template.Lines;
        bool full = false;
        while (items.Count < 200 && misses < lines.Count)
        {
            var l = lines[line % lines.Count];
            var next = lines[(line + 1) % lines.Count];
            line++;
            var at = hour.AddSeconds(used);
            if (Pick(l, next, at, null, template) is not { } item)
            {
                string what = l.Source == LineSource.Folder ? l.Folder : l.CategoryName;
                if (!problems.Contains(what)) problems.Add(what);
                misses++;
                continue;
            }
            misses = 0;
            item.Line = (line - 1) % lines.Count;
            if (full)
            {
                // One spare song after the end, for an hour that runs short on air.
                if (item.Kind != TrackKind.Music) continue;
                item.Optional = true;
                items.Add(item);
                Remember(item, at);
                break;
            }
            items.Add(item);
            Remember(item, at);
            used += item.Seconds;
            if (used >= target)
            {
                // Mostly past the end of the hour: only if there is time.
                if (used - target > item.Seconds / 2) item.Optional = true;
                full = true;
            }
        }
        foreach (var what in _relaxed)
            if (!problems.Contains(what)) problems.Add(what);
        _relaxed.Clear();
        if (items.Count > 0 && lines.Any(x => x.TrackKind is null or TrackKind.Music) && !items.Any(i => i.Kind == TrackKind.Music))
        {
            // Music lines that found no music: nothing rather than an hour of jingles (the playout fills with music).
            problems.Add(L.T("no music for its music lines"));
            items.Clear();
        }
        return (record, items, problems);
    }

    /// <summary>One more song for a template line (the manager's Replace), not one of <paramref name="avoid"/>.</summary>
    public PlanItem? Another(TemplateLine line, TemplateLine next, DateTime at, IReadOnlyCollection<string> avoid, PlaylistTemplate? template = null)
    {
        var item = Pick(line, next, at, avoid, template);
        if (item != null) Remember(item, at); // the next one asked for keeps the rules with it
        _relaxed.Clear();
        return item;
    }

    /// <summary>The album and tempo of the songs picked (for the rules of the next picks).</summary>
    readonly Dictionary<long, (string Album, int? Tempo)> _picked = new();

    void Remember(PlanItem item, DateTime at)
    {
        var song = item.TrackId is { } id ? _picked.GetValueOrDefault(id) : default;
        Note(item.TrackId, item.Artist, item.Path, at, song.Album ?? "", song.Tempo, item.Kind == TrackKind.Music);
    }

    static bool InRange(int? value, int? min, int? max) =>
        (min == null && max == null) || (value is { } v && (min == null || v >= min) && (max == null || v <= max));

    PlanItem? Pick(TemplateLine l, TemplateLine next, DateTime at, IReadOnlyCollection<string>? avoid, PlaylistTemplate? template)
    {
        var kind = l.TrackKind;
        if (l.Source == LineSource.Folder)
        {
            if (!_folders.TryGetValue(l.Folder, out var folder)) _folders[l.Folder] = folder = new FillMusic(l.Folder);
            for (int tries = 0; tries < 20; tries++)
            {
                if (folder.Next() is not { } path) return null;
                if (avoid?.Contains(path) == true) continue;
                double length = Length(path);
                if (length <= 0) continue;
                var k = kind ?? TrackClassifier.Classify(path);
                return new PlanItem
                {
                    Kind = k, Path = path, Title = Path.GetFileNameWithoutExtension(path),
                    Seconds = Plays(length, null, k, next),
                };
            }
            return null;
        }

        if (!_categories.TryGetValue(l.CategoryId, out var tracks))
        {
            try { tracks = _db.RotationTracks([l.CategoryId]); }
            catch { tracks = new(); }
            _categories[l.CategoryId] = tracks;
        }
        bool music = kind is null or TrackKind.Music;
        var songGap = music ? _options.SongGap : TimeSpan.Zero;
        var artistGap = !music ? TimeSpan.Zero : template?.ArtistHours > 0 ? TimeSpan.FromHours(template.ArtistHours) : _options.ArtistGap;
        var albumGap = music && template?.AlbumHours > 0 ? TimeSpan.FromHours(template.AlbumHours) : TimeSpan.Zero;
        bool slowFull = music && template?.MaxSlowInRow > 0 && SlowBefore(at) >= template.MaxSlowInRow;
        // How far from this item its song, artist or album plays: before it (played or planned), or after it in an hour
        // already planned. The nearest time counts.
        TimeSpan SongAway(RotationTrack t)
        {
            var away = new[] { _trackTimes.Away(t.Id, at), _pathTimes.Away(t.Path, at) }.Min();
            return t.LastPlayed is { } played && (at - played.ToLocalTime()).Duration() < away ? (at - played.ToLocalTime()).Duration() : away;
        }
        TimeSpan ArtistAway(RotationTrack t) => ArtistKey(t.Artist) is { } a ? _artistTimes.Away(a, at) : TimeSpan.MaxValue;
        bool SongRested(RotationTrack t) => SongAway(t) >= songGap;
        bool ArtistRested(RotationTrack t) => ArtistAway(t) >= artistGap;
        bool AlbumRested(RotationTrack t) => albumGap == TimeSpan.Zero || AlbumKey(t.Artist, t.Album) is not { } b || _albumTimes.Away(b, at) >= albumGap;
        bool TempoOk(RotationTrack t) => !slowFull || !Slow(t.Tempo);

        for (int tries = 0; tries < 50 && tracks.Count > 0; tries++)
        {
            var pool = tracks.Where(t => avoid?.Contains(t.Path) != true).ToList();
            if (pool.Count == 0) return null;
            // The line's tempo and energy; none of its songs has them: the whole category (and the line is reported).
            if (l.HasFilter)
            {
                var wanted = pool.Where(t => InRange(t.Tempo, l.TempoMin, l.TempoMax) && InRange(t.Energy, l.EnergyMin, l.EnergyMax)).ToList();
                if (wanted.Count > 0) pool = wanted;
                else _relaxed.Add(L.F("{0}: no song with that tempo or energy", l.CategoryName));
            }
            // Whatever the rules allow, not the artists of the last two songs while others can play (two: the last of an
            // hour may be an optional one that is dropped on air).
            if (music)
            {
                var recent = LatestArtists(at, 2);
                if (pool.Any(t => !recent.Contains(ArtistKey(t.Artist) ?? ""))) pool = pool.Where(t => !recent.Contains(ArtistKey(t.Artist) ?? "")).ToList();
                else if (recent.Count > 0 && pool.Any(t => ArtistKey(t.Artist) != recent[0])) pool = pool.Where(t => ArtistKey(t.Artist) != recent[0]).ToList();
            }
            // Both rules if possible, the songs rested longest first. If the category is too small for the artist rule,
            // the artists rested longest (so never the same one twice in a row while there are others); if too small
            // for the song rule too, the songs rested longest.
            List<RotationTrack> oldest;
            var choices = pool.Where(t => SongRested(t) && ArtistRested(t) && AlbumRested(t) && TempoOk(t)).ToList();
            if (choices.Count == 0) choices = pool.Where(t => SongRested(t) && ArtistRested(t) && TempoOk(t)).ToList();
            if (choices.Count == 0) choices = pool.Where(t => SongRested(t) && ArtistRested(t)).ToList();
            if (choices.Count > 0) oldest = choices.OrderByDescending(SongAway).ToList();
            else if ((choices = pool.Where(SongRested).ToList()).Count > 0) oldest = choices.OrderByDescending(ArtistAway).ToList();
            else oldest = pool.OrderByDescending(SongAway).ToList();
            int take = Math.Max(1, (int)Math.Ceiling(oldest.Count * OldestShare));
            var pick = oldest[Random.Shared.Next(take)];
            if (!File.Exists(pick.Path))
            {
                tracks.Remove(pick); // moved or deleted since the last scan
                continue;
            }
            double length = (pick.CueOut is > 0 ? pick.CueOut.Value : pick.Duration) - (pick.CueIn ?? 0);
            if (length <= 0) length = Length(pick.Path);
            if (length <= 0) { tracks.Remove(pick); continue; }
            var k = kind ?? TrackKind.Music;
            _picked[pick.Id] = (pick.Album, pick.Tempo);
            return new PlanItem
            {
                Kind = k, Path = pick.Path, Title = pick.Title.Length > 0 ? pick.Title : Path.GetFileNameWithoutExtension(pick.Path),
                Artist = pick.Artist, TrackId = pick.Id, CategoryId = l.CategoryId,
                Seconds = Plays(length, pick.Mix is > 0 ? pick.Mix - (pick.CueIn ?? 0) : null, k, next),
            };
        }
        return null;
    }

    /// <summary>How long an item plays before the next starts: to its Mix point, or its length less the crossfade into the next.</summary>
    double Plays(double length, double? mix, TrackKind kind, TemplateLine next)
    {
        if (mix is > 0 && mix < length) return mix.Value;
        bool songToSong = kind == TrackKind.Music && next.TrackKind is null or TrackKind.Music;
        double fade = songToSong ? _options.Crossfade : _options.OtherCrossfade;
        return Math.Max(1, length - Math.Min(fade, length / 2));
    }
}

/// <summary>When each song, artist or album played or is planned (every time, not only the latest).</summary>
sealed class Times<TKey>(IEqualityComparer<TKey>? comparer = null) where TKey : notnull
{
    readonly Dictionary<TKey, List<DateTime>> _times = new(comparer);

    public void Add(TKey key, DateTime at)
    {
        if (!_times.TryGetValue(key, out var list)) _times[key] = list = new();
        list.Add(at);
    }

    /// <summary>How far <paramref name="at"/> is from its nearest time (before or after); the longest span if none.</summary>
    public TimeSpan Away(TKey key, DateTime at) =>
        _times.TryGetValue(key, out var list) ? list.Min(t => (at - t).Duration()) : TimeSpan.MaxValue;

    /// <summary>The keys with a time at or before <paramref name="at"/>, the latest first.</summary>
    public List<TKey> LatestBefore(DateTime at, int count) =>
        _times.Select(kv => (kv.Key, Last: kv.Value.Where(t => t <= at).DefaultIfEmpty(DateTime.MinValue).Max()))
            .Where(x => x.Last > DateTime.MinValue).OrderByDescending(x => x.Last).Take(count).Select(x => x.Key).ToList();
}
