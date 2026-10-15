using System.Globalization;
using Dapper;
using Playout.Engine;

namespace Playout.Library;

/// <summary>What making (or updating) a day's log did.</summary>
/// <param name="LogId">0 when no log was made (no clocks for that day).</param>
/// <param name="Notes">Rules that had to give, adverts that didn't fit, hours far from 60 minutes…</param>
public sealed record MakeResult(DateOnly Date, long LogId, int Songs, int Spots, int Breaks, IReadOnlyList<string> Notes, bool Made, bool Changed);

/// <summary>
/// Makes a station's daily logs from its clocks: songs (and jingles) picked by the rotation rules, the booked
/// adverts placed in the breaks, and every item's expected start, timing and lock. Also brings the traffic of
/// logs already made up to date (late bookings, cancelled campaigns). See docs/scheduling-design.md.
/// </summary>
public sealed class LogMaker
{
    /// <summary>An hour shorter than this under 60 minutes gets extra songs; longer than this over is reported.</summary>
    const double HourTolerance = 60;
    /// <summary>Rest rules look back (and ahead, at logs already made) this far at most.</summary>
    static readonly TimeSpan HistoryWindow = TimeSpan.FromHours(48);
    /// <summary>A break whose band is full may take a spot from up to this far outside it (bookings that aren't fixed).</summary>
    const double NearbySeconds = 3600;
    /// <summary>Breaks starting sooner than this aren't changed by a traffic update (the playout may have them already).</summary>
    static readonly TimeSpan TrafficLead = TimeSpan.FromMinutes(5);
    const double UnknownEventSeconds = 180;
    /// <summary>A song filling the gap before a hard start or an end time may run over it by this much (it is cut on air).</summary>
    const double EndOverrun = 10;

    readonly ScheduleStore _store;
    readonly int? _seed;

    /// <param name="seed">Random choices (among equally rested songs) are repeatable with a seed; by default each day has its own.</param>
    public LogMaker(ScheduleStore store, int? seed = null)
    {
        _store = store;
        _seed = seed;
    }

    /// <summary>
    /// Makes the logs that are missing from today to <paramref name="days"/> days ahead (station time), and brings
    /// the traffic of the ones that exist up to date.
    /// </summary>
    public List<MakeResult> MakeAhead(Station station, int days)
    {
        var now = station.Now();
        var today = DateOnly.FromDateTime(now);
        var results = new List<MakeResult>();
        for (int d = 0; d <= days; d++)
        {
            var date = today.AddDays(d);
            results.Add(_store.Log(station.Id, date) is null ? Make(station, date) : UpdateTraffic(station, date, now));
        }
        return results;
    }

    // ───────────────────────── Making a day ─────────────────────────

    /// <summary>A log item being made, with what is needed to place and time it.</summary>
    sealed class Draft
    {
        public required LogItem Item;
        public int SlotPosition;          // the clock slot it came from (fill songs: the slot they go before)
        public double? Due;               // seconds from midnight, for the first item of a timed group
        public BreakPlan? Break;          // the break this is the placeholder of (no spots yet)
    }

    sealed class BreakPlan
    {
        public required ClockSlot Slot;
        public required int Hour;
        public required string Key;
        public required string Name;
        public double Start;              // expected, seconds from midnight
        public TrackRow? Intro, Outro;
        public List<SpotPlan> Spots = new();
        double Jingles => (Intro?.Length ?? 0) + (Outro?.Length ?? 0);
        /// <summary>The adverts and, once there are any, the jingles around them.</summary>
        public double Length => Spots.Count == 0 ? 0 : Spots.Sum(s => s.Duration) + Jingles;
        /// <summary>How long it would be with one more advert of this length.</summary>
        public double LengthWith(double advert) => Spots.Sum(s => s.Duration) + Jingles + advert;
    }

    sealed record SpotPlan(Booking Booking, Campaign Campaign, Advertiser Advertiser, CampaignSpot Spot, TrackRow Track)
    {
        public double Duration => Track.Length;
    }

    sealed class TrackRow
    {
        public long Id { get; set; }
        public string Path { get; set; } = "";
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public double Duration { get; set; }
        public double? CueIn { get; set; }
        public double? CueOut { get; set; }
        public long? CategoryId { get; set; }
        public int? Tempo { get; set; }
        public string? Voice { get; set; }
        public double Length => Math.Max(0, (CueOut ?? Duration) - (CueIn ?? 0));
        string? _artistKey, _titleKey;
        /// <summary>Artist and title as compared (lower case, no accents); worked out once.</summary>
        public string ArtistKey => _artistKey ??= LibraryDb.Normalize(Artist).Trim();
        public string TitleKey => _titleKey ??= LibraryDb.Normalize(Title).Trim();
    }

    const string TrackSelect = """
        SELECT id, path, title, artist, duration, cue_in, cue_out, category_id, tempo, voice FROM tracks
        """;

    /// <summary>Makes the log of a day (there mustn't be one). Hours without a clock are left empty.</summary>
    public MakeResult Make(Station station, DateOnly date, string madeBy = "auto", string status = LogStatus.Ready)
    {
        if (_store.Log(station.Id, date) != null) throw new InvalidOperationException($"{ScheduleStore.Date(date)} already has a log");
        var notes = new List<string>();
        var clockOf = _store.ClocksOn(station.Id, date);
        if (clockOf.Count == 0)
            return new MakeResult(date, 0, 0, 0, 0, ["No clock is set for any hour of this day (clock grid)."], false, false);

        var random = new Random(_seed ?? unchecked(date.DayNumber * 397 + (int)station.Id)); // the same day, the same choices
        var slotsOf = clockOf.Values.Distinct().ToDictionary(id => id, id => _store.Slots(id));
        var context = Load(station, date, slotsOf.Values.SelectMany(s => s));
        var picker = new Picker(context, random);

        // 1. Each hour from its clock, the breaks still empty.
        var hours = new SortedDictionary<int, List<Draft>>();
        var breaks = new List<BreakPlan>();
        foreach (var (hour, clockId) in clockOf.OrderBy(kv => kv.Key))
        {
            var drafts = new List<Draft>();
            double t = hour * 3600;
            foreach (var slot in slotsOf[clockId])
            {
                double? due = slot.AtSeconds is { } at && slot.Timing != Timing.None ? hour * 3600 + at : null;
                if (due is { } d && slot.Timing != Timing.EndAt) t = slot.Timing == Timing.Soft ? Math.Max(t, d) : d;
                switch (slot.Kind)
                {
                    case SlotKind.Pick:
                        if (slot.CategoryId is not { } category) break;
                        if (picker.Pick(category, t, hour, out var level) is { } track)
                        {
                            drafts.Add(TrackDraft(track, KindOf(context, track), slot, t, due));
                            if (level > 0) picker.Relaxed[level]++;
                            t += track.Length;
                        }
                        else notes.Add($"{hour:00}:00 slot {slot.Position}: nothing to pick in category {context.CategoryName(category)}");
                        break;
                    case SlotKind.Item:
                        if (slot.TrackId is { } id && context.Track(id) is { } item)
                        {
                            drafts.Add(TrackDraft(item, KindOf(context, item), slot, t, due));
                            t += item.Length;
                        }
                        break;
                    case SlotKind.Break:
                        var plan = new BreakPlan
                        {
                            Slot = slot, Hour = hour, Start = t,
                            Key = $"B{ScheduleStore.Time(due ?? t)}",
                            Name = string.IsNullOrWhiteSpace(slot.Name) ? $"Break {ScheduleStore.Time(due ?? t)[..5]}" : slot.Name.Trim(),
                            Intro = slot.IntroTrackId is { } i ? context.Track(i) : null,
                            Outro = slot.OutroTrackId is { } o ? context.Track(o) : null,
                        };
                        breaks.Add(plan);
                        drafts.Add(new Draft { Item = new LogItem { Hour = hour }, SlotPosition = slot.Position, Due = due, Break = plan });
                        t += slot.MaxSeconds ?? 0; // until traffic is done
                        break;
                    case SlotKind.Event:
                        var ev = EventItem(slot, hour, t, due);
                        drafts.Add(ev);
                        t += ev.Item.Duration;
                        break;
                }
            }
            hours[hour] = drafts;
        }

        // 2. Traffic: the booked adverts into the breaks.
        var traffic = new Traffic(_store, station, date, context);
        notes.AddRange(traffic.Place(breaks, notBefore: double.MinValue, existing: new()));

        // 3. Gaps get extra songs: before a timed break or event the hour would reach early, and at the end of an
        //    hour that is short. A song that fits the gap is preferred. Then the times are worked out.
        foreach (var (hour, drafts) in hours)
        {
            var slots = slotsOf[clockOf[hour]];
            var unfillable = new HashSet<int>(); // gaps (by what follows them) no song fits: left to the playout's backtiming
            for (int guard = 0; guard < 40; guard++)
            {
                var (at, gap) = FirstGap(drafts, hour, unfillable);
                if (at < 0) break;
                // From the music category of the slots before the gap (else the hour's last one).
                int before = at < drafts.Count ? drafts[at].SlotPosition : int.MaxValue;
                var fillSlot = slots.LastOrDefault(x => x.Position < before && x.Kind == SlotKind.Pick && x.CategoryId is { } c && context.IsMusic(c))
                               ?? slots.LastOrDefault(x => x.Kind == SlotKind.Pick && x.CategoryId is { } c2 && context.IsMusic(c2));
                if (fillSlot == null) break;
                double t = Start(drafts, hour, at);
                // How far past the gap a song may run: a soft start can wait (within its limit); a hard start or an end
                // time would cut it, so only a little there.
                double over = at < drafts.Count && TimingOf(drafts[at]) == Timing.Soft
                    ? Math.Min(HourTolerance, drafts[at].Break?.Slot.MaxDelay ?? drafts[at].Item.MaxDelay ?? HourTolerance)
                    : at < drafts.Count ? EndOverrun : HourTolerance;
                if (picker.Pick(fillSlot.CategoryId!.Value, t, hour, out var level, maxLength: gap + over, fitOnly: true) is not { } extra)
                {
                    unfillable.Add(at < drafts.Count ? drafts[at].GetHashCode() : -1);
                    continue;
                }
                if (level > 0) picker.Relaxed[level]++;
                var fill = TrackDraft(extra, ItemKind.Song, fillSlot, t, null);
                fill.SlotPosition = before == int.MaxValue ? fillSlot.Position : before;
                drafts.Insert(at, fill);
            }
            double length = HourLength(drafts, hour);
            if (length > 3600 + 2 * HourTolerance) notes.Add($"{hour:00}:00 is {Format(length)} long ({Format(length - 3600)} over): what doesn't fit is faded or moved on air");
            else if (length < 3600 - 2 * HourTolerance) notes.Add($"{hour:00}:00 is {Format(length)} long ({Format(3600 - length)} short)");
        }
        notes.AddRange(picker.Report());

        // 4. Write it.
        var items = new List<LogItem>();
        foreach (var (hour, drafts) in hours) items.AddRange(Expand(drafts, hour));
        long logId;
        using (var c = _store.Connect())
        using (var tx = c.BeginTransaction())
        {
            string now = LibraryDb.Now();
            logId = c.ExecuteScalar<long>($"""
                INSERT INTO logs (station_id, date, status, revision, made_by, created_at, updated_at)
                VALUES (@stationId, @date, @status, 1, @madeBy, @now, @now);
                SELECT {(_store.Library.IsMySql ? "LAST_INSERT_ID()" : "last_insert_rowid()")};
                """, new { stationId = station.Id, date = ScheduleStore.Date(date), status, madeBy, now }, tx);
            foreach (var item in items) item.LogId = logId;
            InsertItems(c, tx, items);
            tx.Commit();
        }
        return new MakeResult(date, logId, items.Count(i => i.Kind == ItemKind.Song), items.Count(i => i.Kind == ItemKind.Spot),
            breaks.Count(b => b.Spots.Count > 0), notes, true, true);
    }

    static string Format(double seconds) => TimeSpan.FromSeconds(Math.Round(seconds)).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"mm\:ss", CultureInfo.InvariantCulture);

    static string KindOf(Context context, TrackRow track) => context.CategoryKind(track.CategoryId) switch
    {
        "jingle" => ItemKind.Jingle,
        "commercial" => ItemKind.Spot,
        "other" => ItemKind.Other,
        _ => ItemKind.Song,
    };

    static Draft TrackDraft(TrackRow track, string kind, ClockSlot slot, double t, double? due) => new()
    {
        Item = new LogItem
        {
            Hour = (int)(t / 3600) is var h and < 24 ? h : 23,
            Kind = kind, TrackId = track.Id, Title = track.Title, Artist = track.Artist, Duration = Math.Round(track.Length, 2),
            Timing = due != null ? slot.Timing : Timing.None, MaxDelay = due != null ? slot.MaxDelay : null,
            DueAt = due is { } d ? ScheduleStore.Time(d) : null, LockLevel = slot.LockLevel, ClockSlotId = slot.Id,
            GroupKey = due != null ? $"I{ScheduleStore.Time(due.Value)}" : null,
            GroupName = due != null ? slot.Name : null,
        },
        SlotPosition = slot.Position,
        Due = due,
    };

    static Draft EventItem(ClockSlot slot, int hour, double t, double? due)
    {
        string source = slot.Source?.Trim() ?? "";
        double duration = PlayoutEngine.IsUrl(source) ? slot.MaxSeconds ?? 0
            : File.Exists(source) ? AudioProbe.Duration(source) ?? slot.MaxSeconds ?? UnknownEventSeconds
            : slot.MaxSeconds ?? UnknownEventSeconds;
        string key = $"E{ScheduleStore.Time(due ?? t)}";
        return new Draft
        {
            Item = new LogItem
            {
                Hour = hour, Kind = ItemKind.Event, Source = source, Duration = Math.Round(duration, 2),
                Title = string.IsNullOrWhiteSpace(slot.Name) ? Path.GetFileNameWithoutExtension(source) : slot.Name.Trim(),
                Timing = due != null ? slot.Timing : Timing.None, MaxDelay = due != null ? slot.MaxDelay : null,
                DueAt = due is { } d ? ScheduleStore.Time(d) : null, LockLevel = slot.LockLevel, ClockSlotId = slot.Id,
                GroupKey = key, GroupName = string.IsNullOrWhiteSpace(slot.Name) ? null : slot.Name.Trim(),
            },
            SlotPosition = slot.Position,
            Due = due,
        };
    }

    /// <summary>How long an hour's items run, from the top of the hour, with its timed parts where they go.</summary>
    static double HourLength(List<Draft> drafts, int hour) => Start(drafts, hour, drafts.Count) - hour * 3600;

    /// <summary>When the draft at <paramref name="index"/> would start (seconds from midnight); at the count, when the hour's items end.</summary>
    static double Start(List<Draft> drafts, int hour, int index)
    {
        double t = hour * 3600;
        for (int i = 0; i < index; i++) t = Placed(drafts[i], t) + Length(drafts[i]);
        return t;
    }

    static double Length(Draft d) => d.Break?.Length ?? d.Item.Duration;

    static string TimingOf(Draft d) => d.Break?.Slot.Timing ?? d.Item.Timing;

    /// <summary>When a draft starts if what is before it ends at <paramref name="t"/>: timed ones where their time puts them.</summary>
    static double Placed(Draft d, double t) => d.Due is not { } due ? t : TimingOf(d) switch
    {
        Timing.EndAt => Math.Max(t, due - Length(d)),
        Timing.Soft => Math.Max(t, due),
        _ => due,
    };

    /// <summary>
    /// The first gap longer than <see cref="HourTolerance"/>: before a timed draft that the items before it would
    /// reach early (its index), or at the end of a short hour (the count). (-1, 0) when there is none.
    /// </summary>
    static (int Index, double Gap) FirstGap(List<Draft> drafts, int hour, HashSet<int> skip)
    {
        double t = hour * 3600;
        for (int i = 0; i < drafts.Count; i++)
        {
            var d = drafts[i];
            if (d.Due != null && (d.Break == null || d.Break.Spots.Count > 0 || TimingOf(d) != Timing.EndAt))
            {
                double gap = Placed(d, t) - t;
                // A hard start cuts what runs over, a soft one waits: either way the time before it is a gap to fill.
                if (TimingOf(d) == Timing.Hard) gap = d.Due.Value - t;
                if (gap > HourTolerance && !skip.Contains(d.GetHashCode())) return (i, gap);
            }
            t = Placed(d, t) + Length(d);
        }
        double end = (hour + 1) * 3600 - t;
        return end > HourTolerance && !skip.Contains(-1) ? (drafts.Count, end) : (-1, 0);
    }

    /// <summary>The hour's items in order, breaks expanded (an empty break has none), with expected starts and positions.</summary>
    static List<LogItem> Expand(List<Draft> drafts, int hour)
    {
        var items = new List<LogItem>();
        double t = hour * 3600;
        foreach (var d in drafts)
        {
            var group = d.Break is { } b ? BreakItems(b, hour) : [d.Item];
            if (group.Count == 0) continue;
            double length = group.Sum(i => i.Duration);
            if (d.Due is { } due)
                t = group[0].Timing switch
                {
                    Timing.EndAt => Math.Max(t, due - length),
                    Timing.Soft => Math.Max(t, due),
                    _ => due,
                };
            foreach (var item in group)
            {
                item.StartsAt = ScheduleStore.Time(t);
                item.Hour = hour;
                item.Position = items.Count;
                t += item.Duration;
                items.Add(item);
            }
        }
        return items;
    }

    /// <summary>A break's items: opening jingle, the adverts (first, any, last), closing jingle; none without adverts.</summary>
    static List<LogItem> BreakItems(BreakPlan b, int hour)
    {
        if (b.Spots.Count == 0) return [];
        var slot = b.Slot;
        var items = new List<LogItem>();
        LogItem Base() => new()
        {
            Hour = hour, LockLevel = slot.LockLevel, ClockSlotId = slot.Id, GroupKey = b.Key, GroupName = b.Name,
        };
        if (b.Intro is { } intro) items.Add(With(Base(), intro, ItemKind.Jingle));
        foreach (var s in b.Spots.OrderBy(s => s.Booking.Position switch { "first" => 0, "last" => 2, _ => 1 }))
        {
            var item = With(Base(), s.Track, ItemKind.Spot);
            item.Title = string.IsNullOrWhiteSpace(s.Track.Title) ? Path.GetFileNameWithoutExtension(s.Track.Path) : s.Track.Title;
            item.Artist = s.Advertiser.Name;
            item.CampaignSpotId = s.Spot.Id;
            item.BookingId = s.Booking.Id;
            items.Add(item);
        }
        if (b.Outro is { } outro) items.Add(With(Base(), outro, ItemKind.Jingle));
        if (slot.AtSeconds is { } at && slot.Timing != Timing.None)
        {
            items[0].Timing = slot.Timing;
            items[0].MaxDelay = slot.MaxDelay;
            items[0].DueAt = ScheduleStore.Time(hour * 3600 + at);
        }
        return items;

        static LogItem With(LogItem item, TrackRow t, string kind)
        {
            item.Kind = kind;
            item.TrackId = t.Id;
            item.Title = t.Title;
            item.Artist = t.Artist;
            item.Duration = Math.Round(t.Length, 2);
            return item;
        }
    }

    static void InsertItems(System.Data.Common.DbConnection c, System.Data.Common.DbTransaction tx, IEnumerable<LogItem> items) =>
        c.Execute("""
            INSERT INTO log_items (log_id, hour, position, kind, track_id, source, title, artist, duration, starts_at, due_at, timing,
                max_delay, lock_level, group_key, group_name, clock_slot_id, campaign_spot_id, booking_id, state)
            VALUES (@LogId, @Hour, @Position, @Kind, @TrackId, @Source, @Title, @Artist, @Duration, @StartsAt, @DueAt, @Timing,
                @MaxDelay, @LockLevel, @GroupKey, @GroupName, @ClockSlotId, @CampaignSpotId, @BookingId, @State)
            """, items, tx);

    // ───────────────────────── Updating the traffic of a log ─────────────────────────

    /// <summary>
    /// Brings the adverts of a day's log up to date: spots of campaigns that were cancelled, paused or changed come
    /// out, and bookings with fewer spots than they should have get them (late bookings). Only breaks that start
    /// more than a few minutes from now are changed, and items already on air or played never are. Raises the
    /// log's revision when something changed, so the playout takes the new breaks.
    /// </summary>
    public MakeResult UpdateTraffic(Station station, DateOnly date, DateTime now)
    {
        var log = _store.Log(station.Id, date) ?? throw new InvalidOperationException($"{ScheduleStore.Date(date)} has no log");
        var today = DateOnly.FromDateTime(now);
        if (date < today) return new MakeResult(date, log.Id, 0, 0, 0, [], false, false);
        double notBefore = date == today ? (now - date.ToDateTime(TimeOnly.MinValue) + TrafficLead).TotalSeconds : double.MinValue;

        var items = _store.Items(log.Id);
        var clockOf = _store.ClocksOn(station.Id, date);
        var slotsOf = clockOf.Values.Distinct().ToDictionary(id => id, id => _store.Slots(id));
        var slotById = slotsOf.Values.SelectMany(x => x).ToDictionary(x => x.Id);
        var context = Load(station, date, slotById.Values);
        var traffic = new Traffic(_store, station, date, context);
        var valid = traffic.Bookings.Select(b => b.Id).ToHashSet();
        bool Open(LogItem i) => i.State == ItemState.Scheduled && ScheduleStore.Seconds(i.StartsAt) > notBefore;

        // 1. Spots no longer booked come out (and the jingles of a break left with none).
        var removed = items.Where(i => i.Kind == ItemKind.Spot && Open(i) && (i.BookingId is not { } b || !valid.Contains(b))).ToList();
        foreach (var group in items.Where(i => i.GroupKey != null && i.Kind != ItemKind.Spot && Open(i)).GroupBy(i => (i.Hour, i.ClockSlotId)))
        {
            var key = group.Key;
            bool breakSlot = key.ClockSlotId is { } sid && slotById.TryGetValue(sid, out var sl) && sl.Kind == SlotKind.Break;
            if (breakSlot && !items.Any(i => i.Hour == key.Hour && i.ClockSlotId == key.ClockSlotId && i.Kind == ItemKind.Spot && !removed.Contains(i)))
                removed.AddRange(group);
        }
        var kept = items.Except(removed).ToList();

        // 2. The day's breaks: those in the log (with their spots), and those of the clocks the log has none of yet.
        var plans = new List<BreakPlan>();
        var existing = new Dictionary<long, int>();
        foreach (var i in kept.Where(i => i.Kind == ItemKind.Spot && i.BookingId != null)) existing[i.BookingId!.Value] = existing.GetValueOrDefault(i.BookingId.Value) + 1;
        foreach (var (hour, clockId) in clockOf)
            foreach (var slot in slotsOf[clockId].Where(x => x.Kind == SlotKind.Break))
            {
                var rows = kept.Where(i => i.Hour == hour && i.ClockSlotId == slot.Id).ToList();
                double start = rows.Count > 0 ? ScheduleStore.Seconds(rows[0].StartsAt)
                    : slot.AtSeconds is { } at && slot.Timing != Timing.None ? hour * 3600 + at
                    : kept.Where(i => i.Hour == hour && i.ClockSlotId is { } s && slotById.TryGetValue(s, out var other) && other.Position > slot.Position)
                        .Select(i => ScheduleStore.Seconds(i.StartsAt)).DefaultIfEmpty(hour * 3600 + 3599).Min();
                if (rows.Count > 0 && rows.Any(r => !Open(r))) continue; // on air or played: left alone
                var plan = new BreakPlan
                {
                    Slot = slot, Hour = hour, Start = start,
                    Key = rows.FirstOrDefault()?.GroupKey ?? $"B{ScheduleStore.Time(slot.AtSeconds is { } a2 && slot.Timing != Timing.None ? hour * 3600 + a2 : start)}",
                    Name = rows.FirstOrDefault()?.GroupName ?? (string.IsNullOrWhiteSpace(slot.Name) ? $"Break {ScheduleStore.Time(start)[..5]}" : slot.Name.Trim()),
                    Intro = slot.IntroTrackId is { } i1 ? context.Track(i1) : null,
                    Outro = slot.OutroTrackId is { } o1 ? context.Track(o1) : null,
                };
                plan.Spots.AddRange(rows.Where(r => r.Kind == ItemKind.Spot).Select(r => traffic.Existing(r)).OfType<SpotPlan>());
                plans.Add(plan);
            }
        var before = plans.ToDictionary(p => p, p => p.Spots.Count);
        var notes = traffic.Place(plans, notBefore, existing);
        int added = plans.Sum(p => p.Spots.Count - before[p]);
        if (removed.Count == 0 && added == 0) return new MakeResult(date, log.Id, 0, 0, 0, notes, false, false);

        // 3. The hours that changed, laid out again: kept rows keep their ids; new spots go into their break.
        var newRows = new List<LogItem>();
        var changedHours = removed.Select(i => i.Hour).Concat(plans.Where(p => p.Spots.Count > before[p]).Select(p => p.Hour)).ToHashSet();
        var updates = new List<LogItem>();
        foreach (int hour in changedHours.Order())
        {
            var rows = kept.Where(i => i.Hour == hour).OrderBy(i => i.Position).ToList();
            foreach (var plan in plans.Where(p => p.Hour == hour && p.Spots.Count > 0))
            {
                var group = rows.Where(r => r.ClockSlotId == plan.Slot.Id).ToList();
                var wanted = BreakItems(plan, hour);
                // Reuse the rows that are there (same version and booking, or the same jingle), make the rest.
                var pool = group.ToList();
                var layout = new List<LogItem>();
                foreach (var w in wanted)
                {
                    var same = pool.FirstOrDefault(r => r.Kind == w.Kind && r.TrackId == w.TrackId && r.BookingId == w.BookingId);
                    if (same != null) { pool.Remove(same); layout.Add(same); }
                    else { w.LogId = log.Id; layout.Add(w); newRows.Add(w); }
                }
                // Where the break goes: where it was, else before the first row of a later clock slot.
                int at = group.Count > 0 ? rows.IndexOf(group[0])
                    : rows.FindIndex(r => r.ClockSlotId is { } s && slotById.TryGetValue(s, out var other) && other.Position > plan.Slot.Position);
                foreach (var g in group) rows.Remove(g);
                if (at < 0 || at > rows.Count) at = rows.Count;
                rows.InsertRange(at, layout);
            }
            Retime(rows, hour);
            updates.AddRange(rows.Where(r => r.Id != 0));
        }

        using (var c = _store.Connect())
        using (var tx = c.BeginTransaction())
        {
            c.Execute("DELETE FROM log_items WHERE id = @Id", removed, tx);
            InsertItems(c, tx, newRows);
            c.Execute("UPDATE log_items SET position = @Position, starts_at = @StartsAt WHERE id = @Id", updates, tx);
            c.Execute("UPDATE logs SET revision = revision + 1, updated_at = @now WHERE id = @id", new { id = log.Id, now = LibraryDb.Now() }, tx);
            tx.Commit();
        }
        if (removed.Any(r => r.Kind == ItemKind.Spot)) notes.Insert(0, $"{removed.Count(r => r.Kind == ItemKind.Spot)} spots taken out (campaign cancelled, paused or changed)");
        if (added > 0) notes.Insert(0, $"{added} spots added (late bookings)");
        return new MakeResult(date, log.Id, 0, added, 0, notes, false, true);
    }

    /// <summary>Expected starts and positions of an hour's rows, in their order, with timed parts where they go.</summary>
    static void Retime(List<LogItem> rows, int hour)
    {
        double t = hour * 3600;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.DueAt is { } dueText && row.Timing != Timing.None)
            {
                double due = ScheduleStore.Seconds(dueText);
                double length = rows.Skip(i).TakeWhile(r => r == row || (r.GroupKey != null && r.GroupKey == row.GroupKey)).Sum(r => r.Duration);
                t = row.Timing switch { Timing.EndAt => Math.Max(t, due - length), Timing.Soft => Math.Max(t, due), _ => due };
            }
            row.Position = i;
            row.StartsAt = ScheduleStore.Time(t);
            t += row.Duration;
        }
    }

    // ───────────────────────── What the day needs to know ─────────────────────────

    /// <summary>Categories, tracks, rules and the history the picks and traffic need.</summary>
    sealed class Context
    {
        public required Station Station;
        public required DateOnly Date;
        public Dictionary<long, (string Name, string Kind)> Categories = new();
        public Dictionary<long, List<TrackRow>> ByCategory = new();
        public Dictionary<long, TrackRow> Tracks = new();
        public Dictionary<long, RotationRule> Rules = new();
        public int SongRest, ArtistRest, TitleRest; // minutes, station defaults
        /// <summary>When each track, artist and title was on air or is planned (seconds from this day's midnight).</summary>
        public Dictionary<long, List<double>> TrackTimes = new();
        public Dictionary<string, List<double>> ArtistTimes = new(), TitleTimes = new();
        public LibraryDb Db = null!;

        public TrackRow? Track(long id)
        {
            if (Tracks.TryGetValue(id, out var t)) return t;
            using var c = Db.Connect();
            t = c.QueryFirstOrDefault<TrackRow>($"{TrackSelect} WHERE id = @id", new { id });
            if (t != null) Tracks[id] = t;
            return t;
        }

        public string CategoryName(long id) => Categories.TryGetValue(id, out var c) ? c.Name : $"#{id}";
        public string? CategoryKind(long? id) => id is { } i && Categories.TryGetValue(i, out var c) ? c.Kind : null;
        public bool IsMusic(long id) => CategoryKind(id) == "music";

        public void Played(TrackRow t, double at)
        {
            Add(TrackTimes, t.Id, at);
            if (t.ArtistKey.Length > 0) Add(ArtistTimes, t.ArtistKey, at);
            if (t.TitleKey.Length > 0) Add(TitleTimes, t.TitleKey, at);
        }

        static void Add<TKey>(Dictionary<TKey, List<double>> times, TKey key, double at) where TKey : notnull
        {
            if (!times.TryGetValue(key, out var list)) times[key] = list = new();
            list.Add(at);
        }
    }

    Context Load(Station station, DateOnly date, IEnumerable<ClockSlot> slots)
    {
        var db = _store.Library;
        var context = new Context { Station = station, Date = date, Db = db };
        foreach (var category in db.Categories()) context.Categories[category.Id] = (category.Name, category.Kind);
        context.SongRest = _store.IntSetting(station.Id, "song_rest_minutes", 180);
        context.ArtistRest = _store.IntSetting(station.Id, "artist_rest_minutes", 60);
        context.TitleRest = _store.IntSetting(station.Id, "title_rest_minutes", 120);
        foreach (var r in _store.Rules(station.Id)) context.Rules[r.CategoryId] = r;

        var categories = slots.Where(s => s.Kind == SlotKind.Pick && s.CategoryId != null).Select(s => s.CategoryId!.Value).Distinct().ToArray();
        using var c = db.Connect();
        foreach (var t in c.Query<TrackRow>($"{TrackSelect} WHERE enabled = 1 AND missing = 0 AND category_id IN @categories", new { categories }))
        {
            context.Tracks[t.Id] = t;
            if (!context.ByCategory.TryGetValue(t.CategoryId!.Value, out var list)) context.ByCategory[t.CategoryId.Value] = list = new();
            list.Add(t);
        }
        foreach (var id in slots.SelectMany(s => new[] { s.TrackId, s.IntroTrackId, s.OutroTrackId }).OfType<long>()) context.Track(id);

        // History: what went on air (this station's, and plays from before stations were recorded), and what other
        // logs of this station plan, around the day.
        var zone = station.Zone();
        var midnight = date.ToDateTime(TimeOnly.MinValue);
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(midnight, zone) - HistoryWindow;
        foreach (var (trackId, path, artist, title, at) in c.Query<(long? TrackId, string Path, string Artist, string Title, string At)>(
                     "SELECT track_id, path, artist, title, started_at FROM plays WHERE started_at >= @from AND (station_id = @station OR station_id IS NULL)",
                     new { from = LibraryDb.Time(fromUtc), station = station.Id }))
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.Parse(at, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal), zone);
            var row = new TrackRow { Id = trackId ?? -1, Path = path, Artist = artist, Title = title };
            context.Played(row, (local - midnight).TotalSeconds);
        }
        foreach (var (logDate, startsAt, trackId, artist, title) in c.Query<(string Date, string StartsAt, long TrackId, string Artist, string Title)>("""
                     SELECT l.date, i.starts_at, i.track_id, i.artist, i.title FROM log_items i JOIN logs l ON l.id = i.log_id
                     WHERE l.station_id = @station AND l.date >= @from AND l.date <= @to AND l.date <> @date
                       AND i.track_id IS NOT NULL AND i.kind = 'song' AND i.state IN ('scheduled', 'loaded')
                     """, new { station = station.Id, from = ScheduleStore.Date(date.AddDays(-2)), to = ScheduleStore.Date(date.AddDays(2)), date = ScheduleStore.Date(date) }))
        {
            double at = (ScheduleStore.ParseDate(logDate).DayNumber - date.DayNumber) * 86400.0 + ScheduleStore.Seconds(startsAt);
            context.Played(new TrackRow { Id = trackId, Artist = artist, Title = title }, at);
        }
        return context;
    }

    // ───────────────────────── Picking songs ─────────────────────────

    /// <summary>
    /// Picks from a category: not the same song, artist or title within their rest times, not more than the
    /// category's limit an hour, not two slow songs or two of the same voice in a row (if asked). When nothing
    /// passes, the rules give way one at a time (flow, title, artist, song), and that is reported. Among the songs
    /// that pass, one of the longest-rested quarter, at random.
    /// </summary>
    sealed class Picker(Context context, Random random)
    {
        public readonly int[] Relaxed = new int[5]; // picks that needed rules relaxed, by how far
        readonly Dictionary<(long Category, int Hour), int> _perHour = new();
        TrackRow? _previous;
        readonly HashSet<string> _short = new();

        /// <param name="maxLength">Only songs no longer than this (filling a gap)…</param>
        /// <param name="fitOnly">…or none at all; otherwise a longer one if none that fits keeps the rules.</param>
        public TrackRow? Pick(long category, double at, int hour, out int level, double? maxLength = null, bool fitOnly = false)
        {
            level = 0;
            if (!context.ByCategory.TryGetValue(category, out var all) || all.Count == 0) return null;
            if (maxLength is { } max)
            {
                var fitting = all.Where(t => t.Length <= max).ToList();
                // A rested song that is too long beats breaking the rules to fit; a gap is better left than filled
                // against them.
                if (fitting.Count > 0 && Pick(fitting, category, at, hour, out level, maxLevel: 1) is { } fits) return fits;
                if (fitOnly) return null;
            }
            return Pick(all, category, at, hour, out level, maxLevel: 4);
        }

        TrackRow? Pick(List<TrackRow> tracks, long category, double at, int hour, out int level, int maxLevel)
        {
            level = 0;
            context.Rules.TryGetValue(category, out var rule);
            double song = (rule?.SongRestMinutes ?? context.SongRest) * 60.0;
            double artist = (rule?.ArtistRestMinutes ?? context.ArtistRest) * 60.0;
            double title = (rule?.TitleRestMinutes ?? context.TitleRest) * 60.0;
            int count = _perHour.GetValueOrDefault((category, hour));

            for (level = 0; level <= maxLevel; level++)
            {
                int l = level;
                var ok = tracks.Where(t =>
                    (l >= 4 || Rested(context.TrackTimes, t.Id, at, song))
                    && (l >= 3 || t.ArtistKey.Length == 0 || Rested(context.ArtistTimes, t.ArtistKey, at, artist))
                    && (l >= 2 || t.TitleKey.Length == 0 || Rested(context.TitleTimes, t.TitleKey, at, title))
                    && (l >= 1 || rule?.MaxPerHour is not { } max || count < max)
                    && (l >= 1 || !(rule?.TempoFlow == true && _previous?.Tempo <= 2 && t.Tempo <= 2))
                    && (l >= 1 || !(rule?.VoiceFlow == true && _previous?.Voice != null && _previous.Voice == t.Voice))).ToList();
                if (ok.Count == 0) continue;
                // Longest rested first (never played: first of all; equals in random order), then one of the first
                // quarter at random.
                var oldest = ok.OrderBy(t => Last(t, at)).ThenBy(_ => random.Next()).ToList();
                var pick = oldest[random.Next(Math.Max(1, (int)Math.Ceiling(oldest.Count * 0.25)))];
                context.Played(pick, at);
                _perHour[(category, hour)] = count + 1;
                _previous = pick;
                if (level >= 2) _short.Add(context.CategoryName(category));
                return pick;
            }
            return null;
        }

        /// <summary>When the song was last on air or planned before <paramref name="at"/> (−∞ = never).</summary>
        double Last(TrackRow t, double at) =>
            context.TrackTimes.TryGetValue(t.Id, out var times) && times.Where(x => x <= at).DefaultIfEmpty(double.NegativeInfinity).Max() is var last
                ? last : double.NegativeInfinity;

        static bool Rested<TKey>(Dictionary<TKey, List<double>> times, TKey key, double at, double rest) where TKey : notnull =>
            !times.TryGetValue(key, out var list) || list.All(x => Math.Abs(x - at) >= rest);

        public IEnumerable<string> Report()
        {
            if (Relaxed[1] > 0) yield return $"{Relaxed[1]} songs ignore the hourly limit or the tempo/voice flow";
            if (Relaxed[2] > 0) yield return $"{Relaxed[2]} songs repeat a title sooner than its rest time";
            if (Relaxed[3] > 0) yield return $"{Relaxed[3]} songs repeat an artist sooner than its rest time";
            if (Relaxed[4] > 0) yield return $"{Relaxed[4]} songs repeat sooner than their rest time";
            if (_short.Count > 0) yield return $"Too few songs or artists for the rules in: {string.Join(", ", _short.Order())}";
        }
    }

    // ───────────────────────── Traffic ─────────────────────────

    /// <summary>Places booked adverts into breaks.</summary>
    sealed class Traffic
    {
        readonly ScheduleStore _store;
        readonly DateOnly _date;
        readonly Context _context;
        readonly List<(Booking Booking, Campaign Campaign, Advertiser Advertiser, List<CampaignSpot> Spots)> _bookings = new();
        readonly Dictionary<long, int> _spotCounts; // placements of each campaign version so far, for the rotation

        public Traffic(ScheduleStore store, Station station, DateOnly date, Context context)
        {
            _store = store;
            _date = date;
            _context = context;
            string day = ScheduleStore.Date(date);
            using var c = store.Connect();
            var campaigns = c.Query<Campaign>(
                "SELECT * FROM campaigns WHERE station_id = @station AND status = 'booked' AND starts_on <= @day AND ends_on >= @day",
                new { station = station.Id, day }).ToList();
            var advertisers = store.Advertisers().ToDictionary(a => a.Id);
            var ids = campaigns.Select(x => x.Id).ToArray();
            var spots = c.Query<CampaignSpot>("SELECT * FROM campaign_spots WHERE campaign_id IN @ids", new { ids })
                .Where(s => (s.StartsOn == null || string.CompareOrdinal(s.StartsOn, day) <= 0) && (s.EndsOn == null || string.CompareOrdinal(s.EndsOn, day) >= 0))
                .ToList();
            var weekday = ((int)date.DayOfWeek).ToString(CultureInfo.InvariantCulture);
            foreach (var b in c.Query<Booking>("SELECT * FROM bookings WHERE campaign_id IN @ids", new { ids }))
            {
                if (!b.Days.Contains(weekday)) continue;
                var campaign = campaigns.First(x => x.Id == b.CampaignId);
                var versions = spots.Where(s => s.CampaignId == b.CampaignId && context.Track(s.TrackId) != null).ToList();
                if (versions.Count == 0 || !advertisers.TryGetValue(campaign.AdvertiserId, out var advertiser)) continue;
                _bookings.Add((b, campaign, advertiser, versions));
            }
            _spotCounts = c.Query<(long Id, long Count)>(
                    "SELECT campaign_spot_id, COUNT(*) FROM log_items WHERE campaign_spot_id IN @spots GROUP BY campaign_spot_id",
                    new { spots = spots.Select(s => s.Id).ToArray() })
                .ToDictionary(r => r.Id, r => (int)r.Count);
        }

        /// <summary>The bookings of the day (for the traffic update: which still need spots).</summary>
        public IEnumerable<Booking> Bookings => _bookings.Select(b => b.Booking);

        /// <summary>A spot already in a log, as a placement (null if its booking or version is gone).</summary>
        public SpotPlan? Existing(LogItem item)
        {
            if (_bookings.FirstOrDefault(b => b.Booking.Id == item.BookingId) is not { Booking: not null } b) return null;
            var version = b.Spots.FirstOrDefault(s => s.Id == item.CampaignSpotId);
            return version == null ? null : new SpotPlan(b.Booking, b.Campaign, b.Advertiser, version, _context.Track(version.TrackId)!);
        }

        /// <summary>
        /// Places each booking's spots of the day, spread evenly over its breaks, keeping each break within its
        /// longest length, one spot per campaign and no two competitors (or the same advertiser) in a break.
        /// Only breaks starting after <paramref name="notBefore"/> take spots; <paramref name="existing"/> are the
        /// spots each booking already has. Returns notes on what didn't fit.
        /// </summary>
        public List<string> Place(List<BreakPlan> breaks, double notBefore, Dictionary<long, int> existing)
        {
            var notes = new List<string>();
            // Most tied down first: a named break and fixed, then the narrowest bands.
            foreach (var (booking, campaign, advertiser, versions) in _bookings
                         .OrderByDescending(b => b.Booking.Fixed && b.Booking.BreakName != null)
                         .ThenBy(b => ScheduleStore.Seconds(b.Booking.ToTime) - ScheduleStore.Seconds(b.Booking.FromTime))
                         .ThenByDescending(b => b.Booking.PerDay))
            {
                int need = booking.PerDay - existing.GetValueOrDefault(booking.Id);
                if (need <= 0) continue;
                double from = ScheduleStore.Seconds(booking.FromTime), to = ScheduleStore.Seconds(booking.ToTime);
                bool Named(BreakPlan b) => booking.BreakName == null || b.Name.Equals(booking.BreakName.Trim(), StringComparison.CurrentCultureIgnoreCase);
                var open = breaks.Where(b => b.Start > notBefore).ToList();
                var band = open.Where(b => Named(b) && b.Start >= from && b.Start < to).OrderBy(b => b.Start).ToList();
                // Not fixed: nearby breaks (named ones first) take what the band can't.
                var nearby = booking.Fixed ? [] : open.Except(band)
                    .Where(b => Distance(b.Start, from, to) <= NearbySeconds)
                    .OrderBy(b => Named(b) ? 0 : 1).ThenBy(b => Distance(b.Start, from, to)).ToList();

                int placed = 0;
                for (int k = 0; k < need; k++)
                {
                    BreakPlan? target = null;
                    if (band.Count > 0)
                    {
                        // Evenly over the band: the k-th of `need` falls near this break; else the nearest that fits.
                        var ideal = band[(int)((k + 0.5) * band.Count / need)];
                        target = band.OrderBy(b => Math.Abs(b.Start - ideal.Start)).FirstOrDefault(b => Fits(b, booking, campaign, advertiser, versions));
                    }
                    target ??= nearby.FirstOrDefault(b => Fits(b, booking, campaign, advertiser, versions));
                    if (target == null) break;
                    var version = Version(versions);
                    target.Spots.Add(new SpotPlan(booking, campaign, advertiser, version, _context.Track(version.TrackId)!));
                    placed++;
                }
                if (placed < need)
                    notes.Add($"{advertiser.Name} — {campaign.Name}: {need - placed} of {need} spots didn't fit " +
                              $"({(band.Count == 0 && nearby.Count == 0 ? "no break in its hours" : "breaks full or taken by competitors")})");
            }
            return notes;
        }

        static double Distance(double t, double from, double to) => t < from ? from - t : t >= to ? t - to : 0;

        bool Fits(BreakPlan b, Booking booking, Campaign campaign, Advertiser advertiser, List<CampaignSpot> versions)
        {
            if (b.Spots.Any(s => s.Campaign.Id == campaign.Id || s.Advertiser.Id == advertiser.Id)) return false;
            if (advertiser.Competition is { Length: > 0 } code && b.Spots.Any(s => code.Equals(s.Advertiser.Competition, StringComparison.OrdinalIgnoreCase))) return false;
            if (booking.Position is "first" or "last" && b.Spots.Any(s => s.Booking.Position == booking.Position)) return false;
            double length = versions.Max(v => _context.Track(v.TrackId)!.Length);
            return b.Slot.MaxSeconds is not { } max || b.LengthWith(length) <= max;
        }

        /// <summary>The campaign version due next: the least played for its weight.</summary>
        CampaignSpot Version(List<CampaignSpot> versions)
        {
            var v = versions.OrderBy(s => (double)_spotCounts.GetValueOrDefault(s.Id) / Math.Max(1, s.Weight)).ThenBy(s => s.Id).First();
            _spotCounts[v.Id] = _spotCounts.GetValueOrDefault(v.Id) + 1;
            return v;
        }
    }
}
