using System.Globalization;
using System.Net;
using System.Security;
using System.Text;
using System.Text.Json;
using Playout.Engine;

namespace Playout.App;

/// <summary>Sintoniza Rec → Settings → Podcasts: the shows made into episodes from the recordings, and where they go.</summary>
public sealed class PodcastSettings
{
    public bool Enabled { get; set; }
    /// <summary>The episodes and the feed (empty: "Podcasts" in the recordings' folder).</summary>
    public string Folder { get; set; } = "";
    /// <summary>The shows made into episodes, by name (empty: every show).</summary>
    public List<string> Shows { get; set; } = new();
    /// <summary>The latest so many episodes are kept (and in the feed); older ones are deleted.</summary>
    public int Keep { get; set; } = 20;
    public int Bitrate { get; set; } = 128;
    /// <summary>The feed's name (empty: the station's).</summary>
    public string Title { get; set; } = "";
    /// <summary>Where the folder is published on the web (https://…/podcasts/): the feed links the episodes there.</summary>
    public string Address { get; set; } = "";
    /// <summary>Sent there too, each episode and the feed: ftp://server/folder/ or a folder (empty: not sent).</summary>
    public string UploadTo { get; set; } = "";
    public string UploadUser { get; set; } = "";
    public string UploadPassword { get; set; } = "";

    public PodcastSettings Clone() => new()
    {
        Enabled = Enabled, Folder = Folder, Shows = Shows.ToList(), Keep = Keep, Bitrate = Bitrate, Title = Title, Address = Address,
        UploadTo = UploadTo, UploadUser = UploadUser, UploadPassword = UploadPassword,
    };

    /// <summary>The episodes' folder as used.</summary>
    public string FolderFor(RecSettings rec) => Folder.Trim().Length > 0 ? Folder.Trim() : Path.Combine(rec.Folder, "Podcasts");

    public bool Wants(LiveShow show) => show.Enabled && show.Minutes > 0 && (Shows.Count == 0 || Shows.Contains(show.Name.Trim()));
}

/// <summary>An episode made: which show, when it was on (the station's time), its file and length.</summary>
/// <param name="Uploaded">Sent where the podcast is published (or nothing to send to).</param>
public sealed record PodcastEpisode(string Show, DateTime Start, DateTime End, string File, long Bytes, double Seconds, bool Uploaded = false);

/// <summary>
/// Sintoniza Rec makes the shows into podcast episodes: once a show has ended and the recording file holding its end has
/// closed (its times one after the other are one show), its stretch of the recordings is saved as an MP3 in the podcast folder, the feed (podcast.xml) is
/// written with the latest episodes, older ones are deleted, and both are sent where set (FTP or a folder). The shows
/// are the schedule's as the on-air program on this computer last had it. What was made is remembered, so nothing is
/// made twice.
/// </summary>
sealed class PodcastMaker
{
    /// <summary>A show is made this long after it ends (the recording's last file closed, the next minute written).</summary>
    static readonly TimeSpan After = TimeSpan.FromMinutes(3);
    /// <summary>Shows that ended longer ago than this aren't made any more (a computer switched on later, Rec off meanwhile).</summary>
    static readonly TimeSpan Within = TimeSpan.FromHours(12);
    static readonly TimeSpan UploadTimeout = TimeSpan.FromMinutes(15);

    readonly Func<RecSettings> _settings;
    readonly Action<string> _say;
    int _busy;
    /// <summary>Episodes that failed, and how often: tried a few times, then said once and let be.</summary>
    readonly Dictionary<string, int> _tries = new();
    const int MaxTries = 10; // a minute apart: a share away for a few minutes doesn't lose the episode

    /// <summary>What is still to be done where the podcast is published: the feed to send, old episodes to take away.</summary>
    sealed record Pending(bool Feed, List<string> Deletes);

    static string PendingFile => Path.Combine(AppSettings.DataFolder, "podcasts-pending.json");

    static Pending LoadPending()
    {
        try { return File.Exists(PendingFile) ? JsonSerializer.Deserialize<Pending>(File.ReadAllText(PendingFile)) ?? new(false, new()) : new(false, new()); }
        catch (Exception) { return new(false, new()); }
    }

    static void SavePending(Pending pending)
    {
        try { File.WriteAllText(PendingFile, JsonSerializer.Serialize(pending)); }
        catch (Exception) { /* sent again with the next episode */ }
    }

    /// <summary>A show's run that has ended and whose recording file holding its end has closed (a stretch can be cut whole).</summary>
    static bool Closed(RecSettings rec, DateTime end, DateTime now)
    {
        int minutes = Math.Clamp(rec.FileMinutes, 1, 60);
        return now >= RecService.SegmentStart(end.AddSeconds(-1), minutes, rec.StartMinute).AddMinutes(minutes) + After;
    }

    static string StateFile => Path.Combine(AppSettings.DataFolder, "podcasts.json");

    public PodcastMaker(Func<RecSettings> settings, Action<string> say)
    {
        _settings = settings;
        _say = say;
    }

    /// <summary>The episodes made, the latest last.</summary>
    public static List<PodcastEpisode> Made()
    {
        try { return File.Exists(StateFile) ? JsonSerializer.Deserialize<List<PodcastEpisode>>(File.ReadAllText(StateFile)) ?? new() : new(); }
        catch (Exception) { return new(); }
    }

    static void SaveMade(List<PodcastEpisode> made)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.DataFolder);
            File.WriteAllText(StateFile, JsonSerializer.Serialize(made));
        }
        catch (Exception) { /* made again next time: better twice than never */ }
    }

    /// <summary>Every minute (from Rec's tick): the shows that ended and aren't made yet, in the background.</summary>
    public void Check(DateTime now)
    {
        var s = _settings();
        if (!s.Podcasts.Enabled || Interlocked.Exchange(ref _busy, 1) == 1) return;
        var rec = s.Clone();
        Task.Run(() =>
        {
            try
            {
                MakeDue(rec, now);
                SendPending(rec);
            }
            catch (Exception ex) { _say(L.F("Podcasts: {0}", ex.Message)); }
            finally { Volatile.Write(ref _busy, 0); }
        });
    }

    /// <summary>The latest show that has ended, made now (again, if it was): Settings → Podcasts → Make the last one now.</summary>
    public Task<string> MakeLatestAsync(RecSettings rec) => Task.Run(() =>
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return L.T("Busy making an episode: try again in a moment.");
        try
        {
            var now = StationTime.Now;
            var runs = Runs(rec, now.AddDays(-2), now);
            var last = runs.LastOrDefault(r => Closed(rec, r.End, now));
            if (last.Show == null)
                return runs.Count > 0
                    ? L.F("{0} can be made once its recording file has closed (after {1:HH:mm}).", Name(runs[^1].Show), RecService.SegmentStart(runs[^1].End.AddSeconds(-1), Math.Clamp(rec.FileMinutes, 1, 60), rec.StartMinute).AddMinutes(Math.Clamp(rec.FileMinutes, 1, 60)) + After)
                    : L.T("No show has ended in the last two days (in the schedule the on-air program on this computer has).");
            var episode = Make(rec, last.Show, last.Start, last.End);
            SendPending(rec);
            return episode != null ? L.F("Made: {0}", Path.GetFileName(episode.File)) : L.T("Not made: the log says why.");
        }
        catch (Exception ex) { return L.F("Not made: {0}", ex.Message); }
        finally { Volatile.Write(ref _busy, 0); }
    });

    void MakeDue(RecSettings rec, DateTime now)
    {
        var made = Made();
        foreach (var (show, start, end) in Runs(rec, now - Within, now - After))
        {
            if (made.Any(e => e.Show == Name(show) && e.Start == start)) continue;
            // Only once the recording file that holds its end is closed (written to its end, its length in it).
            if (!Closed(rec, end, now)) continue;
            string key = $"{Name(show)}|{start:O}";
            try
            {
                Make(rec, show, start, end);
                _tries.Remove(key);
            }
            catch (Exception ex)
            {
                // A few times (a share away for a while), then said once and let be: never blocking the others.
                int tries = _tries[key] = _tries.GetValueOrDefault(key) + 1;
                if (tries < MaxTries) continue;
                _say(L.F("Podcasts: {0} {1:dd/MM HH:mm} not made: {2}", Name(show), start, ex.Message));
                var failed = Made();
                failed.Add(new PodcastEpisode(Name(show), start, end, "", 0, 0, Uploaded: true));
                SaveMade(failed);
                _tries.Remove(key);
            }
            made = Made();
        }
    }

    /// <summary>The show's name as episodes are kept under it.</summary>
    static string Name(LiveShow show) => show.Name.Trim().Length > 0 ? show.Name.Trim() : "Show"; // not translated: the same key in any language

    /// <summary>The kept episodes not sent yet (the server was away), sent now, and the feed with them.</summary>
    void SendPending(RecSettings rec)
    {
        var p = rec.Podcasts;
        if (p.UploadTo.Trim().Length == 0) return;
        var made = Made();
        var pending = made.Where(e => e.File.Length > 0 && !e.Uploaded && File.Exists(e.File)).ToList();
        var todo = LoadPending();
        if (pending.Count == 0 && !todo.Feed && todo.Deletes.Count == 0) return;
        foreach (var e in pending)
        {
            try { Upload(p, e.File); }
            catch (Exception ex)
            {
                _say(L.F("Podcasts: {0} not sent to {1}: {2}", Path.GetFileName(e.File), Display(p.UploadTo), ex.Message));
                return; // the rest next time, in order
            }
            made[made.IndexOf(e)] = e with { Uploaded = true };
            SaveMade(made);
            _say(L.F("Podcasts: {0} sent to {1}", Path.GetFileName(e.File), Display(p.UploadTo)));
        }
        // The feed (now listing only what is there), then the old episodes taken away: never a feed pointing at gone files.
        if (todo.Feed || pending.Count > 0)
        {
            try { Upload(p, Path.Combine(p.FolderFor(rec), "podcast.xml")); }
            catch (Exception ex)
            {
                _say(L.F("Podcasts: {0} not sent to {1}: {2}", "podcast.xml", Display(p.UploadTo), ex.Message));
                SavePending(todo with { Feed = true });
                return;
            }
        }
        var left = new List<string>();
        foreach (var name in todo.Deletes)
            try { Delete(p, name); }
            catch (Exception ex)
            {
                left.Add(name);
                _say(L.F("Podcasts: {0} not deleted from {1}: {2}", name, Display(p.UploadTo), ex.Message));
            }
        SavePending(new Pending(false, left));
    }

    /// <summary>The wanted shows' times that ended from <paramref name="from"/> to <paramref name="to"/> (one after the other: one), in order.</summary>
    static List<(LiveShow Show, DateTime Start, DateTime End)> Runs(RecSettings rec, DateTime from, DateTime to)
    {
        var data = ScheduleSource.LoadLocal();
        var runs = new List<(LiveShow, DateTime, DateTime)>();
        foreach (var show in data.Shows.Where(rec.Podcasts.Wants))
        {
            DateTime until = DateTime.MinValue;
            var starts = Enumerable.Range(-2, 3).SelectMany(d => show.TimesOn(to.Date.AddDays(d))).Distinct().Order();
            foreach (var start in starts)
            {
                if (start < until) continue; // inside the one before: the same show going on
                var end = show.RunEnd(start.AddMinutes(show.Minutes));
                until = end;
                if (end > from && end <= to) runs.Add((show, start, end));
            }
        }
        return runs.OrderBy(r => r.Item3).ToList();
    }

    PodcastEpisode? Make(RecSettings rec, LiveShow show, DateTime start, DateTime end)
    {
        var p = rec.Podcasts;
        string name = Name(show);
        string folder = p.FolderFor(rec);
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, $"{Safe(name)} {start.ToString("yyyy-MM-dd HH-mm", CultureInfo.InvariantCulture)}.mp3");
        var files = RecService.Recordings(rec.Folder, start, end);
        var made = Made();
        made.RemoveAll(e => e.Show == name && e.Start == start);
        if (files.Count == 0)
        {
            _say(L.F("Podcasts: {0} {1:dd/MM HH:mm} not made: there is no recording of that time", name, start));
            made.Add(new PodcastEpisode(name, start, end, "", 0, 0, Uploaded: true)); // not tried again every minute
            SaveMade(made);
            return null;
        }
        string tmp = file + ".part";
        double seconds;
        try { seconds = RecordingExport.Export(files, start, end, tmp, RecFormat.Mp3, Math.Clamp(p.Bitrate, 32, 320)); }
        catch (Exception)
        {
            try { File.Delete(tmp); } catch (Exception) { /* not there */ }
            throw;
        }
        if (seconds < 60)
        {
            File.Delete(tmp);
            _say(L.F("Podcasts: {0} {1:dd/MM HH:mm} not made: the recording has only {2:0} s of it", name, start, seconds));
            made.Add(new PodcastEpisode(name, start, end, "", 0, 0, Uploaded: true));
            SaveMade(made);
            return null;
        }
        File.Move(tmp, file, overwrite: true);
        bool send = p.UploadTo.Trim().Length > 0;
        var episode = new PodcastEpisode(name, start, end, file, new FileInfo(file).Length, seconds, Uploaded: !send);
        made.Add(episode);
        // The latest so many episodes; the others' files go, here and where the podcast is published.
        var keep = made.Where(e => e.File.Length > 0).OrderBy(e => e.End).TakeLast(Math.Max(1, p.Keep)).ToList();
        var gone = new List<string>();
        foreach (var old in made.Where(e => e.File.Length > 0 && !keep.Contains(e)))
        {
            try { File.Delete(old.File); } catch (Exception) { /* gone already */ }
            if (send && old.Uploaded) gone.Add(Path.GetFileName(old.File)); // there too, once the new feed is (SendPending)
        }
        if (send)
        {
            var todo = LoadPending();
            SavePending(new Pending(true, todo.Deletes.Concat(gone).Distinct().ToList()));
        }
        made = made.Where(e => e.File.Length == 0 || keep.Contains(e)).Where(e => e.File.Length > 0 || e.End > StationTime.Now.AddDays(-3)).ToList();
        SaveMade(made);
        // The feed written whole, then put in place: a web server reading the folder never serves half of it.
        string feed = Path.Combine(folder, "podcast.xml");
        File.WriteAllText(feed + ".part", Feed(rec, keep), new UTF8Encoding(false));
        File.Move(feed + ".part", feed, overwrite: true);
        _say(L.F("Podcasts: {0} {1:dd/MM HH:mm} made ({2:0} min)", name, start, seconds / 60));
        return episode; // sent with the feed by SendPending, right after
    }

    /// <summary>A name any computer and share takes as a file's (also Windows' and SMB's forbidden characters, on any system).</summary>
    static string Safe(string name) => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || "/\\:*?\"<>|".Contains(c) ? '-' : c)).Trim();

    /// <summary>An address without the password that may be in it.</summary>
    static string Display(string target) => Uri.TryCreate(target.Trim(), UriKind.Absolute, out var u) && u.UserInfo.Length > 0
        ? target.Trim().Replace(u.UserInfo + "@", "") : target.Trim();

    /// <summary>The podcast feed (RSS 2.0 with the iTunes tags podcast apps read): the latest episodes first.</summary>
    internal static string Feed(RecSettings rec, IReadOnlyList<PodcastEpisode> episodes)
    {
        var p = rec.Podcasts;
        string title = p.Title.Trim().Length > 0 ? p.Title.Trim() : rec.StationName.Trim().Length > 0 ? rec.StationName.Trim() : "Sintoniza";
        string address = p.Address.Trim();
        string Link(string file) => address.Length == 0 ? Uri.EscapeDataString(Path.GetFileName(file))
            : address.TrimEnd('/') + "/" + Uri.EscapeDataString(Path.GetFileName(file));
        static string X(string text) => SecurityElement.Escape(text) ?? "";
        var sb = new StringBuilder();
        sb.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
        sb.AppendLine("""<rss version="2.0" xmlns:itunes="http://www.itunes.com/dtds/podcast-1.0.dtd">""");
        sb.AppendLine("<channel>");
        sb.AppendLine($"  <title>{X(title)}</title>");
        if (address.Length > 0) sb.AppendLine($"  <link>{X(address)}</link>");
        sb.AppendLine($"  <description>{X(L.F("The shows of {0}", title))}</description>");
        sb.AppendLine($"  <language>{X(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)}</language>");
        sb.AppendLine($"  <itunes:author>{X(title)}</itunes:author>");
        foreach (var e in episodes.OrderByDescending(e => e.End))
        {
            var utc = StationTime.ToUtc(e.End);
            sb.AppendLine("  <item>");
            sb.AppendLine($"    <title>{X(e.Show + " · " + e.Start.ToString("dd'/'MM'/'yyyy HH':'mm", CultureInfo.InvariantCulture))}</title>");
            sb.AppendLine($"    <guid isPermaLink=\"false\">{X(e.Show + "|" + e.Start.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture))}</guid>");
            sb.AppendLine($"    <pubDate>{utc.ToString("R", CultureInfo.InvariantCulture)}</pubDate>");
            sb.AppendLine($"    <enclosure url=\"{X(Link(e.File))}\" length=\"{e.Bytes}\" type=\"audio/mpeg\" />");
            var length = TimeSpan.FromSeconds(Math.Round(e.Seconds));
            sb.AppendLine($"    <itunes:duration>{(int)length.TotalHours:00}:{length.Minutes:00}:{length.Seconds:00}</itunes:duration>");
            sb.AppendLine("  </item>");
        }
        sb.AppendLine("</channel>");
        sb.AppendLine("</rss>");
        return sb.ToString();
    }

    /// <summary>An old episode taken away where the podcast is published (a folder, or FTP).</summary>
    static void Delete(PodcastSettings p, string name)
    {
        string target = p.UploadTo.Trim();
        if (!target.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) && !target.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase))
        {
            string file = Path.Combine(SharedPaths.ToLocal(target), name);
            if (File.Exists(file)) File.Delete(file);
            return;
        }
        var request = Ftp(p, name);
        request.Method = WebRequestMethods.Ftp.DeleteFile;
        try { using var response = (FtpWebResponse)request.GetResponse(); }
        catch (WebException ex) when (ex.Response is FtpWebResponse { StatusCode: FtpStatusCode.ActionNotTakenFileUnavailable }) { /* gone already */ }
    }

    static FtpWebRequest Ftp(PodcastSettings p, string name)
    {
        string target = p.UploadTo.Trim();
        bool ftps = target.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase);
        var uri = new Uri((ftps ? "ftp://" + target[7..] : target).TrimEnd('/') + "/" + Uri.EscapeDataString(name));
#pragma warning disable SYSLIB0014 // FtpWebRequest: .NET's only FTP client
        var request = (FtpWebRequest)WebRequest.Create(uri);
#pragma warning restore SYSLIB0014
        request.Credentials = new NetworkCredential(p.UploadUser.Trim().Length > 0 ? p.UploadUser.Trim() : "anonymous", p.UploadPassword);
        request.EnableSsl = ftps;
        request.UseBinary = true;
        request.UsePassive = true;
        request.KeepAlive = false;
        request.Timeout = request.ReadWriteTimeout = (int)UploadTimeout.TotalMilliseconds;
        return request;
    }

    /// <summary>A file sent where set: a folder (copied), or FTP (streamed, with time for an hour of audio).</summary>
    static void Upload(PodcastSettings p, string file)
    {
        string target = p.UploadTo.Trim();
        if (!target.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) && !target.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase))
        {
            string to = SharedPaths.ToLocal(target);
            Directory.CreateDirectory(to);
            string dest = Path.Combine(to, Path.GetFileName(file)), tmp = dest + ".part";
            File.Copy(file, tmp, overwrite: true);
            File.Move(tmp, dest, overwrite: true);
            return;
        }
        var request = Ftp(p, Path.GetFileName(file));
        request.Method = WebRequestMethods.Ftp.UploadFile;
        using var source = File.OpenRead(file);
        request.ContentLength = source.Length;
        try
        {
            using (var stream = request.GetRequestStream()) source.CopyTo(stream);
            using var response = (FtpWebResponse)request.GetResponse();
        }
        catch (WebException ex) when (ex.Response is FtpWebResponse r)
        {
            throw new InvalidOperationException(L.F("the FTP server answered {0}", r.StatusDescription?.Trim() ?? r.StatusCode.ToString()));
        }
    }
}
