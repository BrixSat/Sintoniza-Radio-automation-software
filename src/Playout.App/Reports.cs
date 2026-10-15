using System.Globalization;
using System.Net;
using System.Text;

namespace Playout.App;

public enum ReportKind
{
    /// <summary>Every song played, with what collecting societies ask for (royalties).</summary>
    MusicPlays,
    /// <summary>Each song once, with how often and how long it was played.</summary>
    MusicTotals,
    /// <summary>Every advert that went on air, with its exact time (proof of broadcast).</summary>
    Adverts,
    /// <summary>The whole as-run log.</summary>
    Everything,
}

/// <summary>A report as a table: column names and rows of text, ready for a CSV file or a printable page.</summary>
public sealed record Report(string Title, string Period, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows, string Summary)
{
    public static Report Build(ReportKind kind, IReadOnlyList<AsRunEntry> entries, DateTime from, DateTime to, string station, string? advertiser = null)
    {
        string period = from.Date == to.Date ? $"{from:yyyy-MM-dd}" : $"{from:yyyy-MM-dd} to {to:yyyy-MM-dd}";
        string name = string.IsNullOrWhiteSpace(station) ? AppInfo.Name : station.Trim();
        var music = entries.Where(e => e.Type == "Music").ToList();
        switch (kind)
        {
            case ReportKind.MusicPlays:
                return new Report($"{name}: music played", period,
                    ["Date", "Start", "Duration", "Seconds", "Artist", "Title", "Album", "Composer", "Label", "ISRC", "Year"],
                    music.Select(e => Row(e.Start.ToString("yyyy-MM-dd"), e.Start.ToString("HH:mm:ss"), Duration(e.Seconds), Seconds(e.Seconds),
                        e.Artist, e.Title, e.Album, e.Composer, e.Label, e.Isrc, e.Year)).ToList(),
                    $"{Plural(music.Count, "song")} played, {Duration(music.Sum(e => e.Seconds ?? 0))} in all");

            case ReportKind.MusicTotals:
                var songs = music
                    .GroupBy(e => (Key(e.Artist), Key(e.Title), Key(e.Isrc)))
                    .Select(g => (First: g.First(), Plays: g.Count(), Seconds: g.Sum(e => e.Seconds ?? 0)))
                    .OrderByDescending(s => s.Plays).ThenBy(s => s.First.Artist, StringComparer.CurrentCultureIgnoreCase).ThenBy(s => s.First.Title)
                    .ToList();
                return new Report($"{name}: music totals", period,
                    ["Artist", "Title", "Album", "Composer", "Label", "ISRC", "Year", "Plays", "Total time", "Total seconds"],
                    songs.Select(s => Row(s.First.Artist, s.First.Title, s.First.Album, s.First.Composer, s.First.Label, s.First.Isrc, s.First.Year,
                        s.Plays.ToString(CultureInfo.InvariantCulture), Duration(s.Seconds), Seconds(s.Seconds))).ToList(),
                    $"{Plural(songs.Count, "different song")}, {Plural(music.Count, "play")}");

            case ReportKind.Adverts:
                var ads = entries.Where(e => e.Type == "Advert" && (advertiser == null || e.Advertiser == advertiser)).ToList();
                string title = advertiser == null ? $"{name}: proof of broadcast" : $"{name}: proof of broadcast for {advertiser}";
                return new Report(title, period,
                    ["Date", "Start", "End", "Duration", "Advertiser", "Advert", "Break"],
                    ads.Select(e => Row(e.Start.ToString("yyyy-MM-dd"), e.Start.ToString("HH:mm:ss"), e.End?.ToString("HH:mm:ss") ?? "",
                        Duration(e.Seconds), e.Advertiser, AdvertName(e), e.Event)).ToList(),
                    $"{Plural(ads.Count, "advert")} broadcast" + (ads.Count > 0 ? $", {Duration(ads.Sum(e => e.Seconds ?? 0))} in all" : ""));

            default:
                return new Report($"{name}: as-run log", period,
                    ["Date", "Start", "End", "Duration", "Type", "Source", "Event", "Advertiser", "Artist", "Title", "Album", "Composer", "Label", "ISRC", "Year", "File"],
                    entries.Select(e => Row(e.Start.ToString("yyyy-MM-dd"), e.Start.ToString("HH:mm:ss"), e.End?.ToString("HH:mm:ss") ?? "",
                        Duration(e.Seconds), e.Type, e.Source, e.Event, e.Advertiser, e.Artist, e.Title, e.Album, e.Composer, e.Label, e.Isrc, e.Year, e.File)).ToList(),
                    $"{Plural(entries.Count, "item")}");
        }
    }

    static IReadOnlyList<string> Row(params string[] fields) => fields;

    static string Plural(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";

    static string Key(string text) => text.Trim().ToLowerInvariant();

    /// <summary>The advert's title, else its file name.</summary>
    static string AdvertName(AsRunEntry e) =>
        !string.IsNullOrWhiteSpace(e.Title) ? (string.IsNullOrWhiteSpace(e.Artist) ? e.Title : $"{e.Artist} - {e.Title}") : Path.GetFileNameWithoutExtension(e.File);

    /// <summary>"3:25" (or "1:02:03"); empty when the end isn't known.</summary>
    static string Duration(double? seconds)
    {
        if (seconds is not { } s) return "";
        var t = TimeSpan.FromSeconds(Math.Round(s));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t:mm\\:ss}" : $"{t.Minutes}:{t:ss}";
    }

    static string Seconds(double? seconds) => seconds is { } s ? Math.Round(s).ToString(CultureInfo.InvariantCulture) : "";

    /// <summary>A CSV file (UTF-8 with a byte order mark, so spreadsheets show accents); a semicolon for spreadsheets set up for a decimal comma.</summary>
    public string ToCsv(char separator)
    {
        var text = new StringBuilder();
        text.Append(Csv.Line(Columns, separator)).Append("\r\n");
        foreach (var row in Rows) text.Append(Csv.Line(row, separator)).Append("\r\n");
        return text.ToString();
    }

    /// <summary>A page to print or save as PDF from the browser.</summary>
    public string ToHtml(string generated)
    {
        static string E(string s) => WebUtility.HtmlEncode(s);
        var html = new StringBuilder();
        html.Append($$"""
            <!DOCTYPE html>
            <html><head><meta charset="utf-8"><title>{{E(Title)}} — {{E(Period)}}</title>
            <style>
            body { font-family: system-ui, sans-serif; font-size: 12px; margin: 24px; color: #111; }
            h1 { font-size: 18px; margin: 0 0 4px; } p { margin: 0 0 12px; color: #444; }
            table { border-collapse: collapse; width: 100%; }
            th, td { border-bottom: 1px solid #ccc; padding: 4px 6px; text-align: left; vertical-align: top; }
            th { background: #eee; } tr { page-break-inside: avoid; }
            footer { margin-top: 16px; color: #666; font-size: 11px; }
            </style></head><body>
            <h1>{{E(Title)}}</h1>
            <p>{{E(Period)}} · {{E(Summary)}}</p>
            <table><thead><tr>
            """);
        foreach (var c in Columns) html.Append($"<th>{E(c)}</th>");
        html.Append("</tr></thead><tbody>\n");
        foreach (var row in Rows)
        {
            html.Append("<tr>");
            foreach (var f in row) html.Append($"<td>{E(f)}</td>");
            html.Append("</tr>\n");
        }
        html.Append($"</tbody></table>\n<footer>{E(generated)}</footer>\n</body></html>\n");
        return html.ToString();
    }
}
