namespace Playout.App;

public enum CertificateStatus
{
    /// <summary>Booked, and it went on air.</summary>
    Aired,
    /// <summary>Booked, its time has passed, and it didn't go on air.</summary>
    Missed,
    /// <summary>Booked for later on.</summary>
    ToCome,
    /// <summary>It went on air without being booked (put in by hand, or its booking was changed since).</summary>
    NotBooked,
}

/// <summary>One line of a certificate: a booked spot, and when it went on air (null: it didn't).</summary>
/// <param name="Booked">The break it was booked in (date and time); null: it wasn't booked.</param>
public sealed record CertificateLine(DateTime? Booked, AsRunEntry? Aired, string Campaign, string Advert, CertificateStatus Status)
{
    public DateTime When => Aired?.Start ?? Booked ?? DateTime.MinValue;
}

/// <summary>
/// Proof of broadcast for a client: what was booked (its campaigns' placements, day by day) against what went on air
/// (the as-run log), spot by spot. A booked spot counts as aired by an advert of that campaign that went on air within
/// <see cref="Window"/> of its break's time (breaks that end at their time start before it); each aired advert is
/// counted once.
/// </summary>
public static class Certificates
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(30);

    public static List<CertificateLine> Build(AdClient client, AdCampaign? only, IReadOnlyList<AsRunEntry> asRun, DateTime from, DateTime to, DateTime now)
    {
        var campaigns = client.Campaigns.Where(k => only == null || k.Id == only.Id).ToList();
        var lines = new List<CertificateLine>();
        string clientName = client.Name.Trim();
        var adverts = asRun.Where(e => e.Type == "Advert" && e.Start.Date >= from.Date && e.Start.Date <= to.Date).OrderBy(e => e.Start).ToList();
        var used = new HashSet<AsRunEntry>(ReferenceEqualityComparer.Instance);

        bool OfCampaign(AsRunEntry e, AdCampaign k) => k.Files.Any(f => SamePath(f, e.File));

        foreach (var k in campaigns)
        {
            var booked = new List<DateTime>();
            for (var day = from.Date; day <= to.Date; day = day.AddDays(1))
                foreach (var at in k.Placements.Select(p => p.At).Distinct().Order())
                    if (k.PlaysAt(day + at)) booked.Add(day + at);
            var aired = adverts.Where(e => OfCampaign(e, k)).ToList();
            foreach (var time in booked)
            {
                // The nearest of this campaign's adverts on air around the break's time, not counted yet.
                var match = aired.Where(e => !used.Contains(e) && (e.Start - time).Duration() <= Window)
                    .OrderBy(e => (e.Start - time).Duration()).FirstOrDefault();
                if (match != null) used.Add(match);
                var status = match != null ? CertificateStatus.Aired : time > now ? CertificateStatus.ToCome : CertificateStatus.Missed;
                lines.Add(new CertificateLine(time, match, k.Name, Name(match) ?? Path.GetFileNameWithoutExtension(k.FileFor(time) ?? ""), status));
            }
            foreach (var e in aired.Where(e => !used.Contains(e)))
            {
                used.Add(e);
                lines.Add(new CertificateLine(null, e, k.Name, Name(e)!, CertificateStatus.NotBooked));
            }
        }
        // The client's adverts that aren't any campaign's (a file put in by hand): only for the whole client.
        if (only == null && clientName.Length > 0)
            foreach (var e in adverts.Where(e => !used.Contains(e) && string.Equals(e.Advertiser.Trim(), clientName, StringComparison.CurrentCultureIgnoreCase)))
                lines.Add(new CertificateLine(null, e, "", Name(e)!, CertificateStatus.NotBooked));
        return lines.OrderBy(l => l.When).ToList();
    }

    /// <summary>The certificate as a report: a line a spot, and the totals.</summary>
    public static Report ToReport(IReadOnlyList<CertificateLine> lines, string station, AdClient client, AdCampaign? only, DateTime from, DateTime to)
    {
        string period = from.Date == to.Date ? $"{from:yyyy-MM-dd}" : L.F("{0:yyyy-MM-dd} to {1:yyyy-MM-dd}", from, to);
        string name = string.IsNullOrWhiteSpace(station) ? AppInfo.Name : station.Trim();
        string whom = only == null ? client.Name : $"{client.Name} · {only.Name}";
        return new Report(L.F("{0}: broadcast certificate for {1}", name, whom), period,
            new[] { "Date", "Booked", "On air", "Duration", "Campaign", "Advert", "Status" }.Select(L.T).ToArray(),
            lines.Select(l => (IReadOnlyList<string>)new[]
            {
                l.When.ToString("yyyy-MM-dd"), l.Booked?.ToString("HH:mm") ?? "", l.Aired?.Start.ToString("HH:mm:ss") ?? "",
                l.Aired?.Seconds is { } s ? Format.Duration(s) : "", l.Campaign, l.Advert, StatusText(l.Status),
            }).ToList(),
            Summary(lines));
    }

    public static string Summary(IReadOnlyList<CertificateLine> lines)
    {
        int booked = lines.Count(l => l.Booked != null && l.Status != CertificateStatus.ToCome);
        int aired = lines.Count(l => l.Status == CertificateStatus.Aired);
        int missed = lines.Count(l => l.Status == CertificateStatus.Missed);
        int extra = lines.Count(l => l.Status == CertificateStatus.NotBooked);
        int toCome = lines.Count(l => l.Status == CertificateStatus.ToCome);
        var parts = new List<string> { L.F("{0} of {1} booked spots on air", aired, booked) };
        if (missed > 0) parts.Add(L.F("{0} not on air", missed));
        if (extra > 0) parts.Add(L.F("{0} more on air without being booked", extra));
        if (toCome > 0) parts.Add(L.F("{0} still to come", toCome));
        return string.Join(" · ", parts);
    }

    public static string StatusText(CertificateStatus status) => status switch
    {
        CertificateStatus.Aired => L.T("On air"),
        CertificateStatus.Missed => L.T("NOT ON AIR"),
        CertificateStatus.ToCome => L.T("To come"),
        _ => L.T("On air, not booked"),
    };

    static string? Name(AsRunEntry? e) =>
        e == null ? null
        : !string.IsNullOrWhiteSpace(e.Title) ? (string.IsNullOrWhiteSpace(e.Artist) ? e.Title : $"{e.Artist} - {e.Title}")
        : Path.GetFileNameWithoutExtension(e.File);

    static bool SamePath(string a, string b) =>
        a.Length > 0 && b.Length > 0 && (string.Equals(a, b, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase));
}

/// <summary>A line of the billing summary: one campaign of a client over a period (an empty campaign: adverts that are no campaign's).</summary>
public sealed record BillingLine(string Client, string Sector, string Campaign, int Booked, int Aired, int Missed, int NotBooked, double Seconds);

/// <summary>
/// What to invoice: for every client, campaign by campaign, the spots booked, on air, not on air and on air without
/// being booked, and their time on air, over a period. Counted as the broadcast certificates count them
/// (<see cref="Certificates.Build"/>), so the two always agree.
/// </summary>
public static class Billing
{
    public static List<BillingLine> Build(ScheduleData data, IReadOnlyList<AsRunEntry> asRun, DateTime from, DateTime to, DateTime now)
    {
        var lines = new List<BillingLine>();
        foreach (var client in data.Clients)
            foreach (var group in Certificates.Build(client, null, asRun, from, to, now).GroupBy(l => l.Campaign))
            {
                var spots = group.ToList();
                int booked = spots.Count(l => l.Booked != null && l.Status != CertificateStatus.ToCome);
                if (booked == 0 && spots.All(l => l.Aired == null)) continue; // nothing to invoice
                lines.Add(new BillingLine(client.Name.Trim(), client.Sector.Trim(), group.Key, booked,
                    spots.Count(l => l.Status == CertificateStatus.Aired), spots.Count(l => l.Status == CertificateStatus.Missed),
                    spots.Count(l => l.Status == CertificateStatus.NotBooked), spots.Sum(l => l.Aired?.Seconds ?? 0)));
            }
        return lines.OrderBy(l => l.Client, StringComparer.CurrentCultureIgnoreCase).ThenBy(l => l.Campaign, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static Report ToReport(IReadOnlyList<BillingLine> lines, string station, DateTime from, DateTime to)
    {
        string period = from.Date == to.Date ? $"{from:yyyy-MM-dd}" : L.F("{0:yyyy-MM-dd} to {1:yyyy-MM-dd}", from, to);
        string name = string.IsNullOrWhiteSpace(station) ? AppInfo.Name : station.Trim();
        return new Report(L.F("{0}: adverts to invoice", name), period,
            new[] { "Client", "Sector", "Campaign", "Booked", "On air", "Not on air", "On air, not booked", "Time on air" }.Select(L.T).ToArray(),
            lines.Select(l => (IReadOnlyList<string>)new[]
            {
                l.Client, l.Sector, l.Campaign.Length > 0 ? l.Campaign : L.T("(no campaign)"), $"{l.Booked}", $"{l.Aired}", $"{l.Missed}", $"{l.NotBooked}",
                Format.Duration(l.Seconds),
            }).ToList(),
            Summary(lines));
    }

    public static string Summary(IReadOnlyList<BillingLine> lines) =>
        L.F("{0} clients · {1} spots on air of {2} booked · {3} not on air · {4} on air", lines.Select(l => l.Client).Distinct().Count(),
            lines.Sum(l => l.Aired), lines.Sum(l => l.Booked), lines.Sum(l => l.Missed), Format.Duration(lines.Sum(l => l.Seconds)));
}
