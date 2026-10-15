using System.Globalization;
using Playout.Library;

// radio-schedule: makes the stations' daily logs from their clocks, rotation rules and commercial bookings, and
// keeps the adverts of logs already made up to date. Run it every night (cron, Task Scheduler), or every few
// minutes to take late bookings too. See docs/scheduling-design.md.

const string Usage = """
    Usage: radio-schedule [library] [--station NAME|ID] COMMAND

    Commands:
      make [--days N]          make the logs missing from today to N days ahead (default: the station's
                               days_ahead setting, else 2), and update the adverts of those already made
      make --date YYYY-MM-DD   make that day's log, if it has none
      remake --date YYYY-MM-DD make that day's log again (refused once any of it went on air)
      traffic [--date …]       only update the adverts (late bookings, cancelled campaigns)
      show [--date …]          print a day's log (default today)
      check --date … --by NAME mark a day's log as checked
      stations                 list the stations
      example                  set up an example clock for every hour of the week (a station with no clocks)

    The library is the app's (Settings → Library) unless given:
      --db FILE                     an SQLite library file
      --mysql USER:PASSWORD@HOST[:PORT]/DATABASE
    The station is the first one unless given with --station.
    """;

var library = LibrarySettings.FromApp();
string? stationArg = null, dateArg = null, by = null, command = null;
int? days = null;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--db" when i + 1 < args.Length: library = new LibrarySettings { File = args[++i] }; break;
        case "--mysql" when i + 1 < args.Length:
            if (ParseMySql(args[++i]) is not { } my) { Console.Error.WriteLine("--mysql needs USER:PASSWORD@HOST[:PORT]/DATABASE"); return 2; }
            library = my;
            break;
        case "--station" when i + 1 < args.Length: stationArg = args[++i]; break;
        case "--date" when i + 1 < args.Length: dateArg = args[++i]; break;
        case "--by" when i + 1 < args.Length: by = args[++i]; break;
        case "--days" when i + 1 < args.Length && int.TryParse(args[i + 1], out int d) && d >= 0: days = d; i++; break;
        case "--help" or "-h" or "/?": Console.WriteLine(Usage); return 0;
        default:
            if (args[i].StartsWith("--") || command != null)
            {
                Console.Error.WriteLine($"Unknown option {args[i]}\n");
                Console.Error.WriteLine(Usage);
                return 2;
            }
            command = args[i];
            break;
    }
}
if (command == null) { Console.Error.WriteLine(Usage); return 2; }

LibraryDb db;
try { db = LibraryDb.Open(library); }
catch (Exception ex)
{
    Console.Error.WriteLine($"Cannot open the library {library.Describe()}: {ex.Message}");
    return 1;
}
var store = new ScheduleStore(db);
var stations = store.Stations();
if (command == "stations")
{
    foreach (var s in stations)
        Console.WriteLine($"{s.Id,3}  {s.Name}  ({(string.IsNullOrEmpty(s.TimeZone) ? "this computer's time zone" : s.TimeZone)}){(s.Active ? "" : "  [inactive]")}");
    return 0;
}
var station = stationArg != null ? store.FindStation(stationArg) : stations.FirstOrDefault(s => s.Active);
if (station == null)
{
    Console.Error.WriteLine(stationArg != null ? $"No station {stationArg} (radio-schedule stations lists them)" : "No station in the library");
    return 1;
}
DateOnly? date = null;
if (dateArg != null)
{
    if (!DateOnly.TryParseExact(dateArg, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
    {
        Console.Error.WriteLine("--date needs YYYY-MM-DD");
        return 2;
    }
    date = parsed;
}
var today = DateOnly.FromDateTime(station.Now());
var maker = new LogMaker(store);

try
{
    switch (command)
    {
        case "make":
            if (date is { } one)
            {
                if (store.Log(station.Id, one) != null) { Console.WriteLine($"{ScheduleStore.Date(one)} already has a log (use remake to make it again)"); return 0; }
                Print(maker.Make(station, one));
            }
            else foreach (var r in maker.MakeAhead(station, days ?? store.IntSetting(station.Id, "days_ahead", 2))) Print(r);
            return 0;

        case "remake":
            if (date is not { } again) { Console.Error.WriteLine("remake needs --date"); return 2; }
            if (store.Log(station.Id, again) is { } old)
            {
                if (store.Items(old.Id).Any(i => i.State is ItemState.Loaded or ItemState.Aired))
                {
                    Console.Error.WriteLine($"{ScheduleStore.Date(again)} is (partly) on air already: it isn't made again");
                    return 1;
                }
                store.DeleteLog(old.Id);
            }
            Print(maker.Make(station, again, madeBy: Environment.UserName));
            return 0;

        case "traffic":
            foreach (var d in date is { } td ? [td] : Enumerable.Range(0, (days ?? store.IntSetting(station.Id, "days_ahead", 2)) + 1).Select(today.AddDays))
                if (store.Log(station.Id, d) != null) Print(maker.UpdateTraffic(station, d, station.Now()));
            return 0;

        case "show":
            Show(date ?? today);
            return 0;

        case "check":
            if (date is not { } checkedDate || by == null) { Console.Error.WriteLine("check needs --date and --by"); return 2; }
            if (store.Log(station.Id, checkedDate) is not { } log) { Console.Error.WriteLine($"{ScheduleStore.Date(checkedDate)} has no log"); return 1; }
            store.MarkChecked(log.Id, by);
            Console.WriteLine($"{ScheduleStore.Date(checkedDate)} marked as checked by {by}");
            return 0;

        case "example":
            return Example();

        default:
            Console.Error.WriteLine($"Unknown command {command}\n");
            Console.Error.WriteLine(Usage);
            return 2;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"{station.Name}: {ex.Message}");
    return 1;
}

void Print(MakeResult r)
{
    string day = ScheduleStore.Date(r.Date);
    if (r.Made) Console.WriteLine($"{day}: made, {r.Songs} songs, {r.Spots} spots in {r.Breaks} breaks");
    else if (r.LogId == 0) Console.WriteLine($"{day}: no log made");
    else Console.WriteLine($"{day}: {(r.Changed ? "adverts updated" : "up to date")}");
    foreach (var note in r.Notes) Console.WriteLine($"    {note}");
}

void Show(DateOnly day)
{
    if (store.Log(station.Id, day) is not { } log)
    {
        Console.WriteLine($"{ScheduleStore.Date(day)} has no log");
        return;
    }
    Console.WriteLine($"{station.Name}, {ScheduleStore.Date(day)}: {log.Status}, revision {log.Revision}, made by {log.MadeBy}" +
                      (log.CheckedBy != null ? $", checked by {log.CheckedBy}" : ", not checked"));
    int hour = -1;
    foreach (var i in store.Items(log.Id))
    {
        if (i.Hour != hour) Console.WriteLine($"\n── {hour = i.Hour:00}:00 ──");
        string timing = i.Timing switch
        {
            Timing.Hard => $" hard {i.DueAt}",
            Timing.Soft => $" soft {i.DueAt}{(i.MaxDelay is { } m ? $" ≤{m / 60:0} min" : "")}",
            Timing.EndAt => $" ends {i.DueAt}",
            _ => "",
        };
        string what = i.Kind == ItemKind.Event ? i.Title : string.IsNullOrEmpty(i.Artist) ? i.Title : $"{i.Artist} - {i.Title}";
        Console.WriteLine($"{i.StartsAt}  {i.Kind,-6} {TimeSpan.FromSeconds(Math.Round(i.Duration)):m\\:ss}  {what}" +
                          $"{(i.GroupName != null ? $"  [{i.GroupName}]" : "")}{timing}{(i.LockLevel != LockLevel.Free ? $" {i.LockLevel}" : "")}");
    }
}

int Example()
{
    if (store.Clocks(station.Id).Count > 0)
    {
        Console.Error.WriteLine($"{station.Name} has clocks already; the example is for a station with none.");
        return 1;
    }
    var categories = db.Categories();
    var counts = db.CategoryCounts();
    long? Find(string name, string kind) =>
        (categories.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && c.Kind == kind)
         ?? categories.Where(c => c.Kind == kind).OrderByDescending(c => counts.GetValueOrDefault(c.Id)).FirstOrDefault())?.Id;
    long? current = Find("Current", "music"), recurrent = Find("Recurrent", "music") ?? current, gold = Find("Gold", "music") ?? current;
    long? ids = Find("IDs", "jingle");
    if (current == null)
    {
        Console.Error.WriteLine("The library has no music category.");
        return 1;
    }
    long clock = store.AddClock(new Clock { StationId = station.Id, Name = "Example hour", Color = "#3E8EDE" });
    int position = 0;
    void Pick(long? category) { if (category != null) store.AddSlot(new ClockSlot { ClockId = clock, Position = position++, Kind = SlotKind.Pick, CategoryId = category }); }
    Pick(ids);
    foreach (var c in new[] { current, recurrent, current, gold }) Pick(c);
    store.AddSlot(new ClockSlot
    {
        ClockId = clock, Position = position++, Kind = SlotKind.Break, Name = "Publicidade", AtSeconds = 20 * 60,
        Timing = Timing.Soft, MaxDelay = 300, LockLevel = LockLevel.Locked, MaxSeconds = 240,
    });
    foreach (var c in new[] { current, recurrent, current, gold, current }) Pick(c);
    store.AddSlot(new ClockSlot
    {
        ClockId = clock, Position = position++, Kind = SlotKind.Break, Name = "Publicidade", AtSeconds = 59 * 60 + 55,
        Timing = Timing.EndAt, LockLevel = LockLevel.Locked, MaxSeconds = 240,
    });
    store.SetGrid(station.Id, Enumerable.Range(0, 7).SelectMany(d => Enumerable.Range(0, 24).Select(h => (d, h))), clock);
    Console.WriteLine($"{station.Name}: clock \"Example hour\" (ID, songs, a break around :20 at most 5 minutes late, " +
                      "songs, a break ending at 59:55) set for every hour of the week. radio-schedule make makes the logs.");
    return 0;
}

/// <summary>USER:PASSWORD@HOST[:PORT]/DATABASE (the password may contain ':' but not '@').</summary>
static LibrarySettings? ParseMySql(string text)
{
    int at = text.LastIndexOf('@'), slash = text.IndexOf('/', Math.Max(at, 0));
    if (at <= 0 || slash < 0) return null;
    var (user, password) = text[..at].Split(':', 2) is [var u, var p] ? (u, p) : (text[..at], "");
    var host = text[(at + 1)..slash].Split(':', 2);
    int port = 3306;
    if (host.Length == 2 && !int.TryParse(host[1], out port)) return null;
    return new LibrarySettings
    {
        Provider = "mysql", User = user, Password = password, Server = host[0], Port = port, Database = text[(slash + 1)..],
    };
}
