using Playout.Engine;

namespace Playout.App;

/// <summary>
/// Smart playlists: while playing in AUTO, keeps the playlist at least <see cref="Ahead"/> long, from the hour templates
/// placed on the calendar (Sintoniza Schedule → Playlists). Each top-up fills from where the playlist ends to the end of
/// that hour with the lines of that hour's template, in order (carrying on where the last top-up of that hour stopped),
/// keeping songs and artists apart as fill music does. What it adds are ordinary playlist items the host may change.
/// </summary>
sealed class SmartPlaylist
{
    public static readonly TimeSpan Ahead = TimeSpan.FromHours(1);
    /// <summary>A category still being read from the library gets this long before its line is passed over.</summary>
    static readonly TimeSpan Warmup = TimeSpan.FromSeconds(60);

    readonly PlayoutEngine _engine;
    readonly LibraryService _library;
    readonly Func<ScheduleData?> _schedule;
    readonly Dictionary<string, (Func<string?> Next, DateTime Made)> _pickers = new();
    readonly Dictionary<DateTime, int> _nextLine = new(); // the hour → its template's next line
    readonly HashSet<string> _reported = new();
    TimeSpan _songGap = TimeSpan.FromHours(3), _artistGap = TimeSpan.FromHours(1);
    Task? _busy;
    DateTime _quietUntil;

    /// <summary>A line for the status bar and the log (raised off the UI thread).</summary>
    public event Action<string>? Message;

    public SmartPlaylist(PlayoutEngine engine, LibraryService library, Func<ScheduleData?> schedule)
    {
        _engine = engine;
        _library = library;
        _schedule = schedule;
    }

    /// <summary>The rest times (as for fill music): a song not again within…, an artist not again within….</summary>
    public void Configure(TimeSpan songGap, TimeSpan artistGap)
    {
        if (songGap == _songGap && artistGap == _artistGap) return;
        _songGap = songGap;
        _artistGap = artistGap;
        if (_busy is { IsCompleted: false }) return; // the pickers are in use: they are made again next time
        _pickers.Clear();
    }

    /// <summary>Every few seconds (UI thread): <paramref name="playlistEnds"/> is when the playlist runs out (local; null if not known).</summary>
    public void Check(DateTime? playlistEnds, bool autoPlaying)
    {
        if (!autoPlaying || playlistEnds is not { } end || _busy is { IsCompleted: false }) return;
        var now = DateTime.Now;
        if (end - now >= Ahead || now < _quietUntil) return;
        var hour = new DateTime(end.Year, end.Month, end.Day, end.Hour, 0, 0);
        if (_schedule()?.TemplateAt(hour) is not { } template)
        {
            _quietUntil = now.AddSeconds(30);
            return;
        }
        _busy = Task.Run(() => TopUp(template, hour, end));
    }

    void TopUp(PlaylistTemplate template, DateTime hour, DateTime end)
    {
        try
        {
            var items = new List<ScheduledItem>();
            var limit = hour.AddHours(1);
            int line;
            lock (_nextLine)
            {
                foreach (var old in _nextLine.Keys.Where(h => h < hour.AddHours(-2)).ToList()) _nextLine.Remove(old);
                line = _nextLine.GetValueOrDefault(hour);
            }
            var at = end;
            int misses = 0;
            while (at < limit && misses < template.Lines.Count && items.Count < 200)
            {
                var l = template.Lines[line % template.Lines.Count];
                var (next, made) = Picker(l);
                string? path = next();
                if (path == null)
                {
                    // Still reading the library or the folder: try the whole stretch again shortly, in order.
                    if (DateTime.UtcNow - made < Warmup)
                    {
                        _quietUntil = DateTime.Now.AddSeconds(5);
                        return;
                    }
                    if (_reported.Add(Describe(l))) Say(L.F("Smart playlist {0}: nothing to play from {1}, passed over", Name(template), Describe(l)));
                    misses++;
                    line++;
                    continue;
                }
                misses = 0;
                line++;
                double seconds = AudioProbe.Duration(path) ?? 0;
                if (seconds <= 0) continue;
                items.Add(new ScheduledItem(path, l.TrackKind));
                bool song = l.TrackKind is null or TrackKind.Music;
                at = at.AddSeconds(Math.Max(1, seconds - (song ? _engine.CrossfadeSeconds : _engine.OtherCrossfadeSeconds)));
            }
            if (items.Count == 0)
            {
                _quietUntil = DateTime.Now.AddSeconds(30);
                return;
            }
            lock (_nextLine) _nextLine[hour] = line;
            _engine.AddItems(items);
            // The engine opens them on its thread; give the playlist time to show them before looking again.
            _quietUntil = DateTime.Now.AddSeconds(10);
            Say(L.F("Smart playlist {0}: {1} added up to {2:HH:mm}", Name(template),
                L.F(items.Count == 1 ? "{0} item" : "{0} items", items.Count), at > limit ? limit : at));
        }
        catch (Exception ex)
        {
            _quietUntil = DateTime.Now.AddSeconds(30);
            Say(L.F("Smart playlist: {0}", ex.Message));
        }
    }

    (Func<string?> Next, DateTime Made) Picker(TemplateLine l)
    {
        string key = l.Source == LineSource.Folder ? "dir:" + l.Folder : "cat:" + l.CategoryId;
        if (_pickers.TryGetValue(key, out var picker)) return picker;
        Func<string?> next = l.Source == LineSource.Folder
            ? new FillMusic(l.Folder).Next
            : new LibraryFill(_library, [l.CategoryId], _songGap, _artistGap, null).Next;
        return _pickers[key] = (next, DateTime.UtcNow);
    }

    static string Describe(TemplateLine l) => l.Source == LineSource.Folder ? l.Folder : l.CategoryName;

    static string Name(PlaylistTemplate t) => string.IsNullOrWhiteSpace(t.Name) ? L.T("Playlist") : t.Name.Trim();

    void Say(string message) => Message?.Invoke($"{DateTime.Now:HH:mm:ss}  {message}");
}
