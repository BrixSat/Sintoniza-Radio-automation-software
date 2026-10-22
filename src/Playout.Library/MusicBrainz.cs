using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Playout.Library;

/// <summary>What MusicBrainz says about a song: who it found, the performer's country, the language, the first edition.</summary>
/// <param name="Artist">The performer as MusicBrainz names them (null: not found).</param>
/// <param name="Country">The performer's country, ISO 3166 (PT, BR…); null: MusicBrainz doesn't say.</param>
/// <param name="Language">The language of the lyrics, as the app writes it (pt-PT, pt-BR, pt, en, zxx…); null: unknown.</param>
/// <param name="FirstRelease">yyyy-mm-dd, yyyy-mm or yyyy; null: unknown.</param>
/// <param name="Score">How sure the search was (0–100).</param>
public sealed record MusicBrainzInfo(string? Artist, string? Title, string? Country, string? Language, string? FirstRelease, int Score)
{
    public bool Found => Artist != null;
    public static readonly MusicBrainzInfo NotFound = new(null, null, null, null, null, 0);
}

/// <summary>
/// Looks songs up on MusicBrainz (musicbrainz.org, free, no account): by ISRC when the song has one, else by artist and
/// title cleaned of what DJ edits add ([Extended Mix], (Radio Edit), 124 - …). MusicBrainz asks for at most one request
/// a second and a User-Agent that says who is asking: both are kept here, for every lookup in the program.
/// </summary>
public sealed class MusicBrainz(string userAgent)
{
    const string Api = "https://musicbrainz.org/ws/2/";
    static readonly SemaphoreSlim Gate = new(1, 1);
    static DateTime _last = DateTime.MinValue;

    readonly HttpClient _http = Client(userAgent);
    readonly Dictionary<string, string?> _artistCountry = new();
    readonly Dictionary<string, string?> _releaseLanguage = new();

    static HttpClient Client(string userAgent)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return http;
    }

    /// <summary>The song on MusicBrainz, or <see cref="MusicBrainzInfo.NotFound"/>. Throws when MusicBrainz can't be reached.</summary>
    public async Task<MusicBrainzInfo> LookUp(string artist, string title, string? isrc, CancellationToken cancel = default)
    {
        JsonElement? recording = null;
        int score = 0;
        string? earliest = null;
        if (!string.IsNullOrWhiteSpace(isrc) && Regex.IsMatch(isrc.Trim(), "^[A-Za-z]{2}[A-Za-z0-9]{3}[0-9]{7}$"))
        {
            using var byIsrc = await Get($"isrc/{Uri.EscapeDataString(isrc.Trim().ToUpperInvariant())}?inc=artist-credits+releases&fmt=json", cancel);
            if (byIsrc?.RootElement.TryGetProperty("recordings", out var list) == true && list.GetArrayLength() > 0)
            {
                recording = list[0].Clone();
                score = 100;
            }
        }
        var (cleanArtist, cleanTitle) = Clean(artist, title);
        if (recording == null)
        {
            if (cleanTitle.Length == 0 || cleanArtist.Length == 0) return MusicBrainzInfo.NotFound;
            (recording, score, earliest) = await Search(cleanArtist, cleanTitle, cancel);
            // A DJ edit's title: "Careless Whisper, Hype Saxo Redrum" → "Careless Whisper".
            if (recording == null && cleanTitle.IndexOfAny([',', ':']) is > 2 and var cut)
                (recording, score, earliest) = await Search(cleanArtist, cleanTitle[..cut].Trim(), cancel);
        }
        if (recording is not { } rec) return MusicBrainzInfo.NotFound;

        string? artistId = rec.TryGetProperty("artist-credit", out var credit) && credit.GetArrayLength() > 0
                           && credit[0].TryGetProperty("artist", out var a) && a.TryGetProperty("id", out var id) ? id.GetString() : null;
        string? country = artistId != null ? await ArtistCountry(artistId, cancel) : null;
        // The work's language; else a release's, only when it is Portuguese (releases are often labelled wrongly, and
        // whether the lyrics are Portuguese is what matters to the ERC).
        string? language = await WorkLanguage(rec, cancel) ?? (await Language(rec, cancel) is "pt" ? "pt" : null);
        if (language == "pt") language = country switch { "PT" => "pt-PT", "BR" => "pt-BR", _ => "pt" };
        string? release = earliest ?? FirstDate(rec);
        return new MusicBrainzInfo(CreditName(rec), rec.TryGetProperty("title", out var t) ? t.GetString() : cleanTitle, country, language, release, score);
    }

    /// <summary>
    /// The song among the search's matches whose performer is the one asked for (not a cover by someone else): the same
    /// title first (not a live or remixed one); the earliest first edition among those with its title is its first
    /// edition (the same song is often on later compilations).
    /// </summary>
    async Task<(JsonElement? Recording, int Score, string? Earliest)> Search(string artist, string title, CancellationToken cancel)
    {
        string query = $"recording:\"{Escape(title)}\" AND artist:\"{Escape(artist)}\"";
        using var found = await Get($"recording?query={Uri.EscapeDataString(query)}&limit=10&fmt=json", cancel);
        if (found?.RootElement.TryGetProperty("recordings", out var list) != true) return (null, 0, null);
        string plainTitle = Plain(title);
        var matches = list.EnumerateArray()
            .Select(r => (R: r, Score: r.TryGetProperty("score", out var sc) ? sc.GetInt32() : 0))
            .Where(m => m.Score >= 85 && SameArtist(artist, CreditName(m.R))).ToList();
        if (matches.Count == 0) return (null, 0, null);
        bool SameTitle(JsonElement r) => Plain(r.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "") == plainTitle;
        var best = matches.FirstOrDefault(m => SameTitle(m.R));
        if (best.Score == 0) best = matches[0];
        string? earliest = matches.Where(m => SameTitle(m.R)).Select(m => FirstDate(m.R)).OfType<string>()
            .Order(StringComparer.Ordinal).FirstOrDefault();
        return (best.R.Clone(), best.Score, earliest);
    }

    async Task<string?> ArtistCountry(string artistId, CancellationToken cancel)
    {
        if (_artistCountry.TryGetValue(artistId, out var known)) return known;
        using var doc = await Get($"artist/{artistId}?fmt=json", cancel);
        string? country = null;
        if (doc?.RootElement is { } root)
        {
            if (root.TryGetProperty("country", out var c) && c.GetString() is { Length: 2 } code) country = code;
            else
                foreach (var areaName in new[] { "area", "begin-area" })
                    if (root.TryGetProperty(areaName, out var area) && area.ValueKind == JsonValueKind.Object
                        && area.TryGetProperty("iso-3166-1-codes", out var codes) && codes.GetArrayLength() > 0)
                    {
                        country = codes[0].GetString();
                        break;
                    }
        }
        return _artistCountry[artistId] = country?.ToUpperInvariant();
    }

    /// <summary>The language of the lyrics, from the song's releases (their text is written in it).</summary>
    async Task<string?> Language(JsonElement recording, CancellationToken cancel)
    {
        if (!recording.TryGetProperty("releases", out var releases)) return null;
        // An official release first; at most two asked about.
        var ids = releases.EnumerateArray()
            .OrderBy(r => r.TryGetProperty("status", out var s) && s.GetString() == "Official" ? 0 : 1)
            .Select(r => r.TryGetProperty("id", out var id) ? id.GetString() : null).OfType<string>().Take(2);
        foreach (var id in ids)
        {
            if (!_releaseLanguage.TryGetValue(id, out var language))
            {
                using var doc = await Get($"release/{id}?fmt=json", cancel);
                language = doc?.RootElement.TryGetProperty("text-representation", out var tr) == true && tr.ValueKind == JsonValueKind.Object
                           && tr.TryGetProperty("language", out var l) ? LanguageCode(l.GetString()) : null;
                _releaseLanguage[id] = language;
            }
            if (language != null) return language;
        }
        return null;
    }

    /// <summary>The language of the lyrics as the song's work (the composition) gives it: the surest there is.</summary>
    async Task<string?> WorkLanguage(JsonElement recording, CancellationToken cancel)
    {
        if (!recording.TryGetProperty("id", out var id) || id.GetString() is not { } recordingId) return null;
        using var doc = await Get($"recording/{recordingId}?inc=work-rels&fmt=json", cancel);
        if (doc?.RootElement.TryGetProperty("relations", out var relations) != true) return null;
        foreach (var relation in relations.EnumerateArray())
        {
            if (!relation.TryGetProperty("work", out var work) || work.ValueKind != JsonValueKind.Object) continue;
            if (work.TryGetProperty("languages", out var languages) && languages.ValueKind == JsonValueKind.Array && languages.GetArrayLength() == 1)
                return LanguageCode(languages[0].GetString());
            if (work.TryGetProperty("language", out var language) && language.ValueKind == JsonValueKind.String) return LanguageCode(language.GetString());
        }
        return null;
    }

    /// <summary>When the recording was first edited: its own date, else its earliest release's.</summary>
    static string? FirstDate(JsonElement recording) =>
        recording.TryGetProperty("first-release-date", out var d) && d.GetString() is { Length: >= 4 } date ? date
        : recording.TryGetProperty("releases", out var releases)
            ? releases.EnumerateArray().Select(r => r.TryGetProperty("date", out var rd) ? rd.GetString() : null)
                .Where(x => x is { Length: >= 4 }).Order(StringComparer.Ordinal).FirstOrDefault()
            : null;

    static string? CreditName(JsonElement recording)
    {
        if (!recording.TryGetProperty("artist-credit", out var credit) || credit.GetArrayLength() == 0) return null;
        var name = new StringBuilder();
        foreach (var part in credit.EnumerateArray())
            name.Append(part.TryGetProperty("name", out var n) ? n.GetString() : "").Append(part.TryGetProperty("joinphrase", out var j) ? j.GetString() : "");
        return name.ToString().Trim();
    }

    /// <summary>ISO 639-3 (MusicBrainz) to the codes the app uses: por → pt, eng → en, zxx stays (no lyrics); mul/und: unknown.</summary>
    internal static string? LanguageCode(string? iso3)
    {
        if (string.IsNullOrWhiteSpace(iso3) || iso3 is "mul" or "und" or "mis") return null;
        if (iso3 == "zxx") return "zxx";
        var culture = CultureInfo.GetCultures(CultureTypes.NeutralCultures)
            .FirstOrDefault(c => c.ThreeLetterISOLanguageName == iso3 && c.TwoLetterISOLanguageName.Length == 2);
        return culture?.TwoLetterISOLanguageName ?? iso3;
    }

    async Task<JsonDocument?> Get(string path, CancellationToken cancel)
    {
        for (int attempt = 0; ; attempt++)
        {
            await Gate.WaitAsync(cancel);
            try
            {
                var wait = _last.AddMilliseconds(1100) - DateTime.UtcNow;
                if (wait > TimeSpan.Zero) await Task.Delay(wait, cancel);
                _last = DateTime.UtcNow;
            }
            finally { Gate.Release(); }
            using var response = await _http.GetAsync(Api + path, cancel);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            // Too many requests (shared with other users of the address): wait and try again.
            if (response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests && attempt < 5)
            {
                await Task.Delay(TimeSpan.FromSeconds(3 << Math.Min(attempt, 3)), cancel);
                continue;
            }
            response.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
        }
    }

    // ───────────────────────── Names ─────────────────────────

    static readonly Regex Brackets = new(@"\s*[\[\(\{][^\]\)\}]*[\]\)\}]", RegexOptions.Compiled);
    static readonly Regex LeadingNumber = new(@"^\s*\d{1,4}\s*[-.)_]\s*", RegexOptions.Compiled);
    static readonly Regex Featuring = new(@"\s+(feat\.?|ft\.?|featuring|vs\.?|versus|x|&|e|and|with|com)\s+.*$|\s*[,;/].*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex Extras = new(@"\s+-\s+(.*\b(mix|edit|remix|version|vers[aã]o|bootleg|rework|remaster(ed)?|live|ao vivo|intro|outro|extended|radio|instrumental|acoustic|ac[uú]stico|clean|dirty)\b.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// The artist and title to search for: underscores as spaces, no track number, no [..] or (..), no "- Radio Edit",
    /// the first artist only (not who is featured); "Artist - Title" in the title when there is no artist.
    /// </summary>
    public static (string Artist, string Title) Clean(string artist, string title)
    {
        artist = (artist ?? "").Replace('_', ' ').Trim();
        title = (title ?? "").Replace('_', ' ').Trim();
        title = LeadingNumber.Replace(title, "");
        if (artist.Length == 0 && title.Split(" - ", 2) is [var a, var t]) (artist, title) = (a, t);
        artist = LeadingNumber.Replace(artist, "");
        title = Brackets.Replace(title, "");
        title = Extras.Replace(title, "");
        title = Regex.Replace(title, @"\s+(feat\.?|ft\.?)\s+.*$", "", RegexOptions.IgnoreCase);
        artist = Brackets.Replace(artist, "");
        artist = Featuring.Replace(artist, "");
        return (Squeeze(artist), Squeeze(title));
    }

    static string Squeeze(string s) => Regex.Replace(s, @"\s+", " ").Trim(' ', '-', '.', ',');

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>The same performer: their names match once accents, case and punctuation are left aside (or one holds the other).</summary>
    internal static bool SameArtist(string asked, string? found)
    {
        if (found == null) return false;
        string a = Plain(asked), f = Plain(found);
        return a.Length > 0 && f.Length > 0 && (a == f || f.Contains(a) || a.Contains(f));
    }

    static string Plain(string s)
    {
        s = Regex.Replace(s.Trim(), "^the ", "", RegexOptions.IgnoreCase);
        var text = new StringBuilder();
        foreach (char c in s.Normalize(NormalizationForm.FormD))
            if (char.IsLetterOrDigit(c)) text.Append(char.ToLowerInvariant(c));
        return text.ToString();
    }
}
