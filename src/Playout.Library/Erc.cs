using System.Globalization;
using System.Text;
using System.Xml;
using Dapper;

namespace Playout.Library;

/// <summary>A song as the ERC sees it: its own values, or its categories' where it has none.</summary>
/// <param name="Portuguese">Portuguese music (Lei da Rádio art. 41.º); null: nobody said.</param>
/// <param name="Language">pt-PT, pt-BR, en… (null: not set).</param>
/// <param name="Nationality">The performer's country, as the ERC's code (045 = Portugal); null: not set.</param>
/// <param name="Release">First edition, yyyy-mm-dd (from yyyy-mm, yyyy or the year tag); null: not known.</param>
/// <param name="Duration">The file's length in seconds.</param>
public sealed record ErcSong(long TrackId, string Path, bool? Portuguese, string? Language, string? Nationality, DateTime? Release, string? Isrc,
    string Artist = "", string Title = "", double Duration = 0);

/// <summary>A country of the ERC's list: its name, its code in the files the ERC takes, and whether it is in the EU.</summary>
public sealed class ErcCountry
{
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public bool Eu { get; set; }
}

/// <summary>A song that went on air (or is planned to): when, for how long, what.</summary>
public sealed record ErcPlay(DateTime Start, double Seconds, string Artist, string Title, string Path, string Isrc);

/// <summary>One quota: how many of how many, against the minimum.</summary>
public sealed record ErcQuota(int Count, int Of, double Minimum)
{
    public double Percent => Of == 0 ? 0 : 100.0 * Count / Of;
    public bool Met => Of == 0 || Percent >= Minimum;
}

/// <summary>The three quotas of a set of songs.</summary>
/// <param name="Songs">Songs that went on air.</param>
/// <param name="Portuguese">Portuguese music, of all the songs (art. 41.º: at least 30 %).</param>
/// <param name="Language">Of the Portuguese music: in Portuguese, performed by EU citizens (art. 43.º: at least 60 %).</param>
/// <param name="Recent">Of the Portuguese music: first edited in the 12 months before (art. 44.º: at least 35 %).</param>
public sealed record ErcFigures(int Songs, ErcQuota Portuguese, ErcQuota Language, ErcQuota Recent)
{
    public bool Met => Portuguese.Met && Language.Met && Recent.Met;
}

/// <summary>
/// The Portuguese music quotas of the Lei da Rádio (Lei n.º 54/2010 as changed by Lei n.º 16/2024, art. 41.º–47.º) and
/// the file the ERC takes every month (its portal): the songs that went on air, day by day.
/// </summary>
public static class Erc
{
    public const double PortugueseMinimum = 30, LanguageMinimum = 60, RecentMinimum = 35;

    /// <summary>What the station starts with: Portugal, as the ERC codes it.</summary>
    public static List<ErcCountry> DefaultCountries() => [new ErcCountry { Name = "Portugal", Code = "045", Eu = true }];

    /// <summary>In Portuguese (pt, pt-PT, pt-BR…).</summary>
    public static bool IsPortugueseLanguage(string? language) =>
        language is { } l && (l.Equals("pt", StringComparison.OrdinalIgnoreCase) || l.StartsWith("pt-", StringComparison.OrdinalIgnoreCase));

    /// <summary>A language code as the ERC writes it: pt → pt-PT, pt-br → pt-BR, EN → en.</summary>
    public static string? Expression(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return null;
        var parts = language.Trim().Replace('_', '-').Split('-', 2);
        string lang = parts[0].ToLowerInvariant();
        if (parts.Length == 2) return $"{lang}-{parts[1].ToUpperInvariant()}";
        return lang == "pt" ? "pt-PT" : lang;
    }

    /// <summary>A first edition date from what was typed (yyyy-mm-dd, yyyy-mm or yyyy) or the year tag.</summary>
    public static DateTime? ReleaseDate(string? release, int? year)
    {
        if (!string.IsNullOrWhiteSpace(release))
        {
            string[] formats = ["yyyy-MM-dd", "yyyy-M-d", "yyyy-MM", "yyyy-M", "yyyy"];
            if (DateTime.TryParseExact(release.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        }
        return year is >= 1900 and <= 2200 ? new DateTime(year.Value, 1, 1) : null;
    }

    /// <summary>Whether what was typed is a release date the ERC file can take (empty: none).</summary>
    public static bool IsReleaseText(string text) => text.Trim().Length == 0 || ReleaseDate(text, null) != null;

    /// <summary>First edited in the 12 months before <paramref name="day"/> (art. 44.º).</summary>
    public static bool IsRecent(DateTime? release, DateTime day) => release is { } r && r <= day && r > day.AddMonths(-12);

    /// <summary>Monday to Friday, from 7:00 to 20:00 (art. 47.º n.º 2): the percentages must be met there too.</summary>
    public static bool InWeekdayHours(DateTime at) => at.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && at.Hour >= 7 && at.Hour < 20;

    /// <summary>The three quotas of these songs.</summary>
    public static ErcFigures Figures(IReadOnlyList<(ErcPlay Play, ErcSong? Song)> plays, Func<string?, bool> euCountry)
    {
        int songs = plays.Count, portuguese = 0, language = 0, recent = 0;
        foreach (var (play, song) in plays)
        {
            if (song?.Portuguese != true) continue;
            portuguese++;
            if (IsPortugueseLanguage(song.Language) && euCountry(song.Nationality)) language++;
            if (IsRecent(song.Release, play.Start.Date)) recent++;
        }
        return new ErcFigures(songs, new ErcQuota(portuguese, songs, PortugueseMinimum),
            new ErcQuota(language, portuguese, LanguageMinimum), new ErcQuota(recent, portuguese, RecentMinimum));
    }

    /// <summary>
    /// What is missing for the ERC (empty: nothing), in words for the screen to translate: whether it is Portuguese
    /// music; for Portuguese music, its language, country and first edition.
    /// </summary>
    public static IReadOnlyList<string> Missing(ErcSong? song)
    {
        if (song == null) return ["not in the library"];
        if (song.Portuguese == null) return ["Portuguese music or not"];
        if (song.Portuguese == false) return [];
        var missing = new List<string>();
        if (song.Language == null) missing.Add("language");
        if (song.Nationality == null) missing.Add("nationality");
        if (song.Release == null) missing.Add("first edition");
        return missing;
    }

    /// <summary>
    /// The ERC's file: every song that went on air, a PLAYLIST a day. ISO-8859-1, as the ERC's portal takes it; a
    /// character it hasn't is written as a character reference.
    /// </summary>
    /// <param name="alias">The programme service's name at the ERC (ALIAS).</param>
    public static void WriteXml(Stream output, string alias, IEnumerable<(ErcPlay Play, ErcSong? Song)> plays)
    {
        var settings = new XmlWriterSettings { Encoding = Encoding.Latin1, Indent = true, IndentChars = "    " };
        using var w = XmlWriter.Create(output, settings);
        w.WriteStartDocument();
        w.WriteStartElement("ERCRATIO");
        w.WriteElementString("ALIAS", alias);
        foreach (var day in plays.OrderBy(p => p.Play.Start).GroupBy(p => p.Play.Start.Date))
        {
            w.WriteStartElement("PLAYLIST");
            w.WriteElementString("DATE", day.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            foreach (var (play, song) in day)
            {
                w.WriteStartElement("SONG");
                w.WriteElementString("TIME", play.Start.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
                w.WriteElementString("DURATION", ((int)Math.Round(play.Seconds)).ToString(CultureInfo.InvariantCulture));
                w.WriteElementString("ARTIST", play.Artist);
                w.WriteElementString("MUSICTRACK", play.Title);
                Element(w, "NATIONALITY", song?.Nationality);
                Element(w, "EXPRESSION", Expression(song?.Language));
                Element(w, "YEAR", song?.Release?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                w.WriteElementString("PORTUGUESEM", song?.Portuguese == true ? "1" : "0");
                Element(w, "ISRC", string.IsNullOrWhiteSpace(play.Isrc) ? song?.Isrc : play.Isrc);
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }
        w.WriteEndElement();
        w.WriteEndDocument();
    }

    /// <summary>An element, empty (&lt;ISRC/&gt;) when there is nothing to say.</summary>
    static void Element(XmlWriter w, string name, string? value)
    {
        w.WriteStartElement(name);
        if (!string.IsNullOrWhiteSpace(value)) w.WriteString(value.Trim());
        w.WriteEndElement();
    }
}

public sealed partial class LibraryDb
{
    /// <summary>
    /// The songs at these paths as the ERC sees them: each value the song's own, or else its categories' (its own
    /// category first, then the others it is in, each with the categories above it). Paths not in the library are left
    /// out.
    /// </summary>
    public Dictionary<string, ErcSong> ErcSongs(IEnumerable<string> paths)
    {
        var comparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        var wanted = paths.Where(p => !string.IsNullOrEmpty(p)).Distinct(comparer).ToList();
        var result = new Dictionary<string, ErcSong>(comparer);
        if (wanted.Count == 0) return result;
        using var c = Connect();
        var tracks = new List<LibraryTrack>();
        foreach (var chunk in wanted.Chunk(400))
            tracks.AddRange(c.Query<LibraryTrack>($"SELECT {TrackColumns} FROM tracks WHERE path IN @paths", new { paths = chunk }));
        // On Windows and macOS a path may differ in case from the library's.
        if (!OperatingSystem.IsLinux())
            foreach (var path in wanted.Where(p => !tracks.Any(t => comparer.Equals(t.Path, p))))
                if (c.QueryFirstOrDefault<LibraryTrack>($"SELECT {TrackColumns} FROM tracks WHERE {PathIs("path")} ORDER BY id", new { path }) is { } t)
                    tracks.Add(t);
        var also = new Dictionary<long, List<long>>();
        foreach (var chunk in tracks.Select(t => t.Id).Distinct().Chunk(400))
            foreach (var (track, category) in c.Query<(long, long)>("SELECT track_id, category_id FROM track_categories WHERE track_id IN @ids", new { ids = chunk }))
                (also.TryGetValue(track, out var list) ? list : also[track] = new()).Add(category);
        foreach (var song in Erc(tracks, also)) result[song.Path] = song;
        // The paths as asked (they may differ in case from the library's).
        foreach (var path in wanted)
            if (!result.ContainsKey(path) && result.Values.FirstOrDefault(s => comparer.Equals(s.Path, path)) is { } s)
                result[path] = s;
        return result;
    }

    /// <summary>Every song in the library (not missing) as the ERC sees it, by track id: for planning to the quotas.</summary>
    public Dictionary<long, ErcSong> ErcAll()
    {
        using var c = Connect();
        var tracks = c.Query<LibraryTrack>($"SELECT {TrackColumns} FROM tracks WHERE missing = 0").ToList();
        var also = new Dictionary<long, List<long>>();
        foreach (var (track, category) in c.Query<(long, long)>("SELECT track_id, category_id FROM track_categories"))
            (also.TryGetValue(track, out var list) ? list : also[track] = new()).Add(category);
        return Erc(tracks, also).ToDictionary(s => s.TrackId);
    }

    /// <summary>Each track's own ERC values, or else its categories': its own category and those above it first, then the others.</summary>
    IEnumerable<ErcSong> Erc(List<LibraryTrack> tracks, Dictionary<long, List<long>> also)
    {
        var categories = Categories().ToDictionary(c => c.Id);
        IEnumerable<LibraryCategory> Chain(LibraryTrack t)
        {
            foreach (var start in new[] { t.CategoryId }.Concat(also.GetValueOrDefault(t.Id, []).Select(x => (long?)x)))
            {
                var seen = new HashSet<long>();
                for (long? id = start; id is { } i && categories.TryGetValue(i, out var cat) && seen.Add(i); id = cat.ParentId)
                    yield return cat;
            }
        }
        foreach (var t in tracks)
        {
            var chain = Chain(t).ToList();
            bool? portuguese = t.ErcPortuguese ?? chain.FirstOrDefault(x => x.ErcPortuguese != null)?.ErcPortuguese;
            string? language = !string.IsNullOrWhiteSpace(t.Language) ? t.Language : chain.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.ErcLanguage))?.ErcLanguage;
            string? nationality = !string.IsNullOrWhiteSpace(t.ErcNationality) ? t.ErcNationality : chain.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.ErcNationality))?.ErcNationality;
            yield return new ErcSong(t.Id, t.Path, portuguese, language, nationality, Playout.Library.Erc.ReleaseDate(t.ErcRelease, t.Year), t.Isrc,
                t.Artist, t.Title, t.Duration);
        }
    }
}
