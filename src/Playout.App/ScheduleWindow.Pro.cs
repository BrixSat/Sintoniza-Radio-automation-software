using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Playout.Engine;
using Playout.Library;

namespace Playout.App;

/// <summary>
/// Sintoniza Pro's frame (as professional radio software is laid out): a menu and a toolbar, the tools on the left
/// (with what is on air and the station under them), the tool chosen in the middle, and a status bar. It opens on a
/// Home page: the clock, what is on air, today's plan, what needs attention, the library, the database and this computer.
/// </summary>
public partial class ScheduleWindow
{
    DispatcherTimer? _clock;
    DateTime _nextFigures;
    bool _figuresBusy;
    StationSettings? _station;
    List<string> _defaultPasswords = new();
    DateTime _nextPasswordCheck;
    /// <summary>Yesterday's booked adverts that didn't go on air, client by client (read every 10 minutes).</summary>
    List<(string Client, int Missed)> _missedYesterday = new();
    DateTime _nextMissedCheck;
    List<string> _breakWarnings = new();
    DateTime _nextBreakCheck;
    readonly Dictionary<string, double> _fileLengths = new(StringComparer.Ordinal); // for the breaks' lengths, off the UI thread (seconds)
    /// <summary>Who is working here (null: no login, e.g. the manual's screenshots: everything is open).</summary>
    SintonizaUser? _user;

    bool May(string permission) => _user == null || _user.Can(permission);

    /// <summary>The pages in the middle, by what opens them.</summary>
    void WirePro(ScheduleStore? store)
    {
        var pages = new (Button Nav, Button? Tool, MenuItem? Menu, TabItem Tab, string Group, string Title)[]
        {
            (NavHome, ToolHome, null, HomeTab, "", L.T("Home")),
            (NavSongs, ToolSongs, MenuSongs, SongsTab, L.T("Library ›"), L.T("Songs")),
            (NavTemplates, ToolTemplates, MenuTemplates, (TabItem)Tabs.Items[3]!, L.T("Programming ›"), L.T("Hour templates")),
            (NavDaily, ToolDaily, MenuDaily, DailyTab, L.T("Programming ›"), L.T("Daily playlists")),
            (NavEvents, null, MenuEvents, (TabItem)Tabs.Items[0]!, L.T("Programming ›"), L.T("Events")),
            (NavShows, null, MenuShows, ShowsTab, L.T("Programming ›"), L.T("Shows")),
            (NavErc, null, MenuErc, ErcTab, L.T("Programming ›"), L.T("ERC quotas")),
            (NavBreaks, ToolBreaks, MenuBreaks, (TabItem)Tabs.Items[1]!, L.T("Traffic ›"), L.T("Ad breaks")),
            (NavClients, null, MenuClients, (TabItem)Tabs.Items[2]!, L.T("Traffic ›"), L.T("Clients and campaigns")),
            (NavCategories, CategoriesButton, MenuCategories, CategoriesTab, L.T("Library ›"), L.T("Categories")),
            (NavImport, ImportButton, MenuImport, ImportTab, L.T("Library ›"), L.T("Import music")),
            (NavStationSettings, StationSettingsButton, MenuStationSettings, StationSettingsTab, L.T("Station ›"), L.T("Station settings")),
            (NavCartwalls, CartwallsButton, MenuCartwalls, CartwallsTab, L.T("Station ›"), L.T("Cartwalls")),
            (NavUsers, null, MenuUsers, UsersTab, L.T("Station ›"), L.T("Users")),
        };
        _gates = pages.Select(p => (p.Nav, p.Tool, p.Menu, p.Tab)).ToList();
        foreach (var (nav, tool, menu, tab, _, _) in pages)
        {
            nav.Click += (_, _) => Tabs.SelectedItem = tab;
            if (tool != null) tool.Click += (_, _) => Tabs.SelectedItem = tab;
            if (menu != null) menu.Click += (_, _) => Tabs.SelectedItem = tab;
        }
        Tabs.SelectionChanged += (_, _) =>
        {
            foreach (var (nav, _, _, tab, group, title) in pages)
            {
                bool current = Tabs.SelectedItem == tab;
                nav.Classes.Set("current", current);
                if (!current) continue;
                PageGroup.Text = group;
                PageTitle.Text = title;
            }
            if (Tabs.SelectedItem == HomeTab) _nextFigures = DateTime.MinValue; // fresh figures
        };
        bool daily = store != null && _library != null && _settings != null;
        NavDaily.IsVisible = ToolDaily.IsVisible = MenuDaily.IsVisible = daily;
        NavSongs.IsVisible = ToolSongs.IsVisible = MenuSongs.IsVisible = _library != null;
        if (_library != null)
        {
            SongsView.Attach(_library);
            SongsView.EditTagsRequested += path => _ = EditSongTags(path);
            SongsView.EditCuesRequested += path => _ = EditSongCues(path);
            // Listening: on this computer's Preview output (Settings → Audio), never on air.
            SongsListening.Settings = _settings;
            SongsListening.Selected = () => SongsView.SelectedSong;
            SongsListening.Problem += problem => SavedText.Text = problem;
            SongsListening.SpaceListens(SongsView);
            SongsView.ListenRequested += path => SongsListening.Listen(path, SongsView.SelectedSong is { } s && s.Path == path ? s.Title : Path.GetFileNameWithoutExtension(path));
            Closed += (_, _) => SongsListening.Close();
        }

        MenuSave.Click += (_, _) => Save();
        MenuRefresh.Click += (_, _) => Refresh();
        RefreshButton.Click += (_, _) => Refresh();
        // The library changed (an import here, Categories, or another program or computer): every page shows it.
        if (_library != null) _library.Changed += LibraryChanged;
        MenuClose.Click += (_, _) => Close();
        MenuManual.Click += (_, _) =>
        {
            if (Manual.Find() is { } pdf) Updates.OpenInBrowser(pdf);
            else SavedText.Text = L.T("The manual isn't installed with this copy: it is in docs/manual on GitHub.");
        };
        MenuSupport.Click += (_, _) => Updates.OpenInBrowser(AppInfo.SupportUrl);
        MenuAbout.Click += async (_, _) => await Dialogs.Confirm(this, AppInfo.ScheduleName,
            $"{AppInfo.ScheduleName} — {AppInfo.Name}\n" + (Updates.IsDevBuild ? L.T("Development build (built from source)") : L.F("Version {0}", Updates.CurrentText))
            + "\n\nCésar Araújo · cesararaujo.net", L.T("Close"));
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { Save(); e.Handled = true; }
            else if (e.Key == Key.F5) { Refresh(); e.Handled = true; }
        };

        // What doesn't change while it runs.
        StatusVersion.Text = $"{AppInfo.ScheduleName} {(Updates.IsDevBuild ? "dev" : Updates.CurrentText)}";
        MenuSwitchUser.IsVisible = store != null && _user != null;
        MenuSwitchUser.Click += async (_, _) =>
        {
            if (store == null) return;
            var login = new LoginWindow(store.Db, AppInfo.ScheduleName, null, L.T("Switch user"), _station?.StationName ?? "");
            if (await login.ShowDialog<SintonizaUser?>(this) is { } user) ApplyUser(user);
        };
        MenuLogout.IsVisible = store != null && _user != null;
        MenuLogout.Click += async (_, _) => await LogOut(store);
        ApplyUser(_user);
        string database = _settings?.Library is { } l ? (l.IsMySql ? $"MySQL · {l.Server}/{l.Database}" : $"SQLite · {Path.GetFileName(l.SqliteFile)}") : "";
        StatusDatabase.Text = store != null ? $"● {database}" : $"○ {L.T("database offline")}";
        BoxDatabaseText.Text = database;

        _clock = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Tick(store));
        _clock.Start();
        Closed += (_, _) => _clock.Stop();
        Tabs.SelectedItem = HomeTab;
        Opened += (_, _) => Tick(store); // the figures as soon as it is open
    }

    void Tick(ScheduleStore? store)
    {
        var now = DateTime.Now;
        StatusClock.Text = now.ToString("ddd dd/MM/yyyy  HH:mm:ss");
        HomeClock.Text = now.ToString("HH:mm:ss");
        HomeDate.Text = now.ToString("dddd, d MMMM yyyy");
        bool changed = Changed;
        StatusChanges.Text = changed ? L.T("● Unsaved changes") : "";
        // Save is for changes to the schedule: nothing to save, nothing to press.
        SaveButton.IsEnabled = MenuSave.IsEnabled = changed && store != null;
        SaveButton.Classes.Set("accent", changed);
        if (_saved == null) return; // still opening
        if (now >= _nextFigures && !_figuresBusy && store != null) _ = Figures(store.Db, now);
    }

    /// <summary>What the Home page, the status bar and the boxes on the left show, read every 15 seconds.</summary>
    async Task Figures(LibraryDb db, DateTime now)
    {
        _figuresBusy = true;
        _nextFigures = now.AddSeconds(15);
        var data = _data.Clone(); // read off the UI thread while the schedule may be edited: a copy
        try
        {
            var f = await Task.Run(() =>
            {
                var stats = db.Stats();
                var categories = db.Categories();
                var last = db.LastPlay();
                var plan = new PlanStore(db);
                var hours = plan.Hours(now.Date, now.Date.AddDays(1));
                var items = plan.Items(now.Date, now.Date.AddDays(1));
                var (station, _) = new StationStore(db).Load();
                // The first passwords (slow to check on purpose): every few minutes.
                if (DateTime.UtcNow >= _nextPasswordCheck)
                {
                    _defaultPasswords = new UserStore(db).WithDefaultPasswords();
                    _nextPasswordCheck = DateTime.UtcNow.AddMinutes(5);
                }
                if (DateTime.UtcNow >= _nextMissedCheck)
                {
                    var yesterday = now.Date.AddDays(-1);
                    var asRun = AsRunDb.Read(db, yesterday, yesterday);
                    _missedYesterday = data.Clients.Where(c => c.Enabled)
                        .Select(c => (c.Name, Certificates.Build(c, null, asRun, yesterday, yesterday, now).Count(l => l.Status == CertificateStatus.Missed)))
                        .Where(m => m.Item2 > 0).ToList();
                    _nextMissedCheck = DateTime.UtcNow.AddMinutes(10);
                }
                if (DateTime.UtcNow >= _nextBreakCheck)
                {
                    _breakWarnings = BreakWarnings(db, data, now);
                    _nextBreakCheck = DateTime.UtcNow.AddMinutes(2);
                }
                var studios = db.IsMySql ? db.Studios(StationStore.Station) : null;
                return (stats, categories.Count, last, hours, items, station, studios);
            });
            _station = f.station;
            ShowFigures(data, now, f.stats, f.Item2, f.last, f.hours, f.items);
            ShowStudios(f.studios);
        }
        catch (Exception ex)
        {
            StatusDatabase.Text = "○ " + L.F("database: {0}", ex.Message);
        }
        finally
        {
            _figuresBusy = false;
        }
    }

    /// <summary>
    /// Today's breaks still to come: those the adverts booked make longer than their longest, and those where two
    /// clients of the same sector (or one client) follow each other because there aren't enough other adverts to go
    /// between them.
    /// </summary>
    List<string> BreakWarnings(LibraryDb db, ScheduleData data, DateTime now)
    {
        var warnings = new List<string>();
        double Length(string path)
        {
            lock (_fileLengths)
                if (_fileLengths.TryGetValue(path, out var known)) return known;
            double seconds = db.GetByPath(path)?.Duration is > 0 and var d ? d : AudioProbe.Duration(path) ?? 0;
            lock (_fileLengths) _fileLengths[path] = seconds;
            return seconds;
        }
        foreach (var b in data.Breaks.Where(b => b.Enabled))
        {
            var longer = new List<string>();
            var clashes = new Dictionary<string, List<DateTime>>();
            foreach (var at in b.TimesOn(now.Date).Where(t => t > now))
            {
                if (data.InBreak(b, at).Count == 0) continue;
                var items = ScheduleItems.For(data, b, at, null);
                if (b.MaxSeconds > 0)
                {
                    double seconds = items.Where(i => i.StreamName == null).Sum(i => Length(i.Path));
                    if (seconds > b.MaxSeconds) longer.Add(L.F("{0:HH:mm} ({1})", at, Format.Duration(seconds)));
                }
                // Adverts that play one straight after the other (a jingle between them keeps them apart).
                AdClient? Client(ScheduledItem i) => i.Kind == TrackKind.Commercial && i.Advertiser is { } name
                    ? data.Clients.FirstOrDefault(c => c.Enabled && c.Name.Trim() == name) : null;
                for (int i = 1; i < items.Count; i++)
                    if (Client(items[i - 1]) is { } a && Client(items[i]) is { } c && c.Clashes(a))
                    {
                        string pair = a == c ? L.F("{0} twice in a row", ClientName(a)) : L.F("{0} and {1} ({2})", ClientName(a), ClientName(c), a.Sector.Trim());
                        if (!clashes.TryGetValue(pair, out var times)) clashes[pair] = times = new();
                        times.Add(at);
                    }
            }
            if (longer.Count > 0)
                warnings.Add(L.F("€ {0}: longer than its {1} at {2} — fewer adverts, or a longer limit (Ad breaks)", EntryName(b), Format.Duration(b.MaxSeconds),
                    string.Join(", ", longer.Take(4)) + (longer.Count > 4 ? " …" : "")));
            foreach (var (pair, times) in clashes)
                warnings.Add(L.F("€ {0} at {1}: {2} back to back — not enough other adverts to go between them", EntryName(b),
                    string.Join(", ", times.Take(4).Select(t => t.ToString("HH:mm"))) + (times.Count > 4 ? " …" : ""), pair));
        }
        return warnings;
    }

    void ShowFigures(ScheduleData data, DateTime now, LibraryStats stats, int categories, (string Title, string Artist, DateTime At)? last,
        Dictionary<DateTime, PlanHour> hours, Dictionary<DateTime, List<PlanItem>> items)
    {
        string name = _station?.StationName is { Length: > 0 } n ? n : _settings?.StationName ?? "";
        HomeStation.Text = name.Length > 0 ? name : AppInfo.ScheduleName;
        StatusStation.Text = name.Length > 0 ? "📻 " + name : "";
        BoxStationText.Text = name.Length > 0 ? name : L.T("(no name yet: Station settings)");
        HomeGreeting.Text = (now.Hour < 12 ? L.T("Good morning") : now.Hour < 20 ? L.T("Good afternoon") : L.T("Good evening")) + " · " + AppInfo.ScheduleName;
        ShowLogo(_station?.Logo);

        // On air (the play history of every studio) and this hour's template.
        var hour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);
        string Template(DateTime h) => data.TemplateAt(h) is { } t ? (string.IsNullOrWhiteSpace(t.Name) ? L.T("Playlist") : t.Name.Trim()) : L.T("fill music");
        bool recent = last is { } p && DateTime.UtcNow - p.At < TimeSpan.FromMinutes(15);
        string title = recent ? last!.Value.Title : L.T("Nothing on air lately");
        string artist = recent ? last!.Value.Artist : "";
        NowTitleText.Text = HomeNowTitle.Text = title;
        NowArtistText.Text = HomeNowArtist.Text = artist;
        NowAtText.Text = recent ? last!.Value.At.ToLocalTime().ToString("HH:mm") : "";
        NowTemplateText.Text = L.F("{0:HH}:00 · {1}", hour, Template(hour));
        HomeNowHour.Text = L.F("This hour: {0}", Template(hour)) + "\n" + L.F("Next hour: {0}", Template(hour.AddHours(1)));

        // Today.
        int made = hours.Count, edited = hours.Values.Count(h => h.Edited);
        int voice = items.Values.Sum(l => l.Count(i => i.Kind == TrackKind.Voice));
        int breaks = data.Breaks.Where(b => b.Enabled).Sum(b => b.TimesOn(now.Date).Count());
        int adverts = data.Breaks.Where(b => b.Enabled).Sum(b => b.TimesOn(now.Date).Sum(t => data.InBreak(b, t).Count));
        int events = data.Events.Where(e => e.Enabled).Sum(e => e.TimesOn(now.Date).Count());
        HomeToday.Text = string.Join("\n", new[]
        {
            L.F("{0} of 24 hours planned ({1} changed by hand)", made, edited),
            L.F("{0} ad breaks, {1} adverts booked", breaks, adverts),
            L.F("{0} events", events),
            L.F("{0} voice tracks", voice),
        });

        // What needs attention.
        var warnings = new List<string>();
        if (_defaultPasswords.Count > 0) warnings.Add(L.F("🔑 Change the first password of {0} (Station › Users)", string.Join(", ", _defaultPasswords)));
        if (stats.Missing > 0) warnings.Add(L.F("⚠ {0:N0} songs whose file is missing", stats.Missing));
        if (stats.Uncategorised > 0) warnings.Add(L.F("⚠ {0:N0} songs without a category", stats.Uncategorised));
        int noTemplate = Enumerable.Range(now.Hour, 24 - now.Hour).Count(h => data.TemplateAt(now.Date.AddHours(h)) == null);
        if (noTemplate > 0) warnings.Add(L.F("⚠ {0} hours left today without a template (fill music plays)", noTemplate));
        foreach (var c in data.Clients.Where(c => c.Enabled))
            foreach (var k in c.Campaigns.Where(k => k.Enabled && k.To is { } to && to.Date >= now.Date && to.Date <= now.Date.AddDays(7)))
                warnings.Add(L.F("€ {0}: campaign {1} ends {2:dd/MM}", c.Name, k.Name, k.To!.Value));
        warnings.AddRange(_breakWarnings);
        foreach (var (client, missed) in _missedYesterday)
            warnings.Add(L.F("€ {0}: {1} booked adverts didn't go on air yesterday (Clients → Certificate…)", client, missed));
        foreach (var (c, k, places) in Unplaced())
            warnings.Add(L.F("€ {0} · {1}: booked at {2} with no ad break — it won't play there (Clients: Move them to the nearest break)",
                c.Name, k.Name, Plural(places, "{0} time", "{0} times")));
        // Breaks still to come today with no advert: they are skipped, jingles and all.
        foreach (var b in data.Breaks.Where(b => b.Enabled))
        {
            var empty = b.TimesOn(now.Date).Where(t => t > now && data.InBreak(b, t).Count == 0).ToList();
            if (empty.Count > 0)
                warnings.Add(L.F("€ {0}: no adverts booked at {1} — it is skipped then", b.Name,
                    string.Join(", ", empty.Take(4).Select(t => t.ToString("HH:mm"))) + (empty.Count > 4 ? " …" : "")));
        }
        foreach (var t in data.Templates)
            foreach (var (line, number) in t.Lines.Select((l, i) => (l, i + 1)))
                if (PlaysTakesAtRandom(line))
                    warnings.Add(L.F("⚠ Template {0}, line {1}: it plays takes at random (a temperature or time at random) — for the time or temperature now select it → 🕒 Time / temperature…", t.Name, number));
        if (Changed) warnings.Add(L.T("● The schedule has unsaved changes"));
        HomeWarnings.Text = warnings.Count > 0 ? string.Join("\n", warnings.Take(8)) : L.T("✓ Nothing: all in order.");

        HomeLibrary.Text = string.Join("\n", new[]
        {
            L.F("{0:N0} songs and jingles ({1:N0} hours)", stats.Tracks, stats.Hours),
            L.F("{0} categories", categories),
            L.F("{0:N0} missing · {1:N0} without a category", stats.Missing, stats.Uncategorised),
        });
        var lib = _settings?.Library;
        HomeDatabase.Text = string.Join("\n", new[]
        {
            lib == null ? "" : lib.IsMySql ? L.F("MySQL / MariaDB on {0}", lib.Server) : L.T("SQLite (a file on this computer)"),
            lib == null ? "" : lib.IsMySql ? L.F("Database {0}", lib.Database) : Path.GetFileName(lib.SqliteFile),
            L.F("Schema version {0}", LibraryDb.SchemaVersion),
            _version > 0 ? L.F("Schedule saved {0} times", _version) : L.T("Schedule not saved yet"),
        }.Where(x => x.Length > 0));
        HomeComputer.Text = ComputerText();
    }

    string? _logoShown;

    void ShowLogo(string? logo)
    {
        if (logo == _logoShown) return;
        _logoShown = logo;
        (HomeLogo.Source as IDisposable)?.Dispose();
        HomeLogo.Source = null;
        if (string.IsNullOrEmpty(logo)) return;
        try { HomeLogo.Source = new Bitmap(new MemoryStream(Convert.FromBase64String(logo))); }
        catch { /* not an image */ }
    }

    /// <summary>This computer: its name, system, processor, memory, free disk and how long it has been on.</summary>
    static string ComputerText()
    {
        var lines = new List<string> { AppInfo.ComputerName, RuntimeInformation.OSDescription.Trim() };
        string cpu = CpuName();
        lines.Add(cpu.Length > 0 ? L.F("{0} ({1} cores)", cpu, Environment.ProcessorCount) : L.F("{0} cores", Environment.ProcessorCount));
        long memory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        if (memory > 0) lines.Add(L.F("{0:0.#} GB of memory", memory / 1024.0 / 1024 / 1024));
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(AppSettings.DataFolder) ?? "/");
            lines.Add(L.F("{0:0.#} GB free of {1:0.#} GB", drive.AvailableFreeSpace / 1e9, drive.TotalSize / 1e9));
        }
        catch { /* no disk figures */ }
        var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
        lines.Add(L.F("On for {0}d {1}h {2}m", up.Days, up.Hours, up.Minutes));
        return string.Join("\n", lines);
    }

    static string? _cpu;

    static string CpuName()
    {
        if (_cpu != null) return _cpu;
        try
        {
            if (OperatingSystem.IsLinux() && File.Exists("/proc/cpuinfo"))
                return _cpu = File.ReadLines("/proc/cpuinfo").FirstOrDefault(l => l.StartsWith("model name"))?.Split(':', 2)[1].Trim() ?? "";
            if (OperatingSystem.IsWindows())
                return _cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "";
        }
        catch { /* unknown */ }
        return _cpu = "";
    }

    List<(Button Nav, Button? Tool, MenuItem? Menu, TabItem Tab)> _gates = new();

    /// <summary>What the user may use: the other tools are hidden; their name in the status bar and the station box.</summary>
    void ApplyUser(SintonizaUser? user)
    {
        _user = user;
        string who = user?.Name ?? AppInfo.UserName;
        StatusUser.Text = $"👤 {who}@{AppInfo.ComputerName}";
        BoxUserText.Text = $"{who}@{AppInfo.ComputerName}";
        bool Allowed(TabItem tab) =>
            tab == HomeTab ? true
            : tab == SongsTab || tab == CategoriesTab || tab == ImportTab ? May(Permission.Library)
            : tab == DailyTab ? May(Permission.Programming) || May(Permission.Voice)
            : tab == Tabs.Items[3] || tab == Tabs.Items[0] || tab == ShowsTab ? May(Permission.Programming)
            : tab == Tabs.Items[1] || tab == Tabs.Items[2] ? May(Permission.Traffic)
            : tab == StationSettingsTab || tab == CartwallsTab ? May(Permission.Station)
            : tab == UsersTab ? May(Permission.Users)
            : tab == ErcTab ? May(Permission.Programming) && _store != null
            : true;
        foreach (var (nav, tool, menu, tab) in _gates)
        {
            bool allowed = Allowed(tab) && (tab != DailyTab || DailyTab.IsVisible) && (tab != SongsTab || _library != null);
            nav.IsVisible = allowed;
            if (tool != null) tool.IsVisible = allowed;
            if (menu != null) menu.IsVisible = allowed;
        }
        // Only the menus and groups with something left in them.
        foreach (var top in MainMenu.Items.OfType<MenuItem>())
            if (top.Items.OfType<MenuItem>().Any()) top.IsVisible = top.Items.OfType<MenuItem>().Any(m => m.IsVisible);
        foreach (var group in NavPanel.Children.OfType<TextBlock>())
        {
            int i = NavPanel.Children.IndexOf(group);
            group.IsVisible = NavPanel.Children.Skip(i + 1).TakeWhile(c => c is Button).Any(c => c.IsVisible);
        }
        SaveButton.IsVisible = MenuSave.IsVisible = May(Permission.Programming) || May(Permission.Traffic);
        if (_daily != null) _daily.VoiceOnly = !May(Permission.Programming);
        if (Tabs.SelectedItem is TabItem current && !Allowed(current)) Tabs.SelectedItem = HomeTab;
        _nextFigures = DateTime.MinValue;
    }

    /// <summary>
    /// Log out: the changes are saved first (or it stays), then the log-in covers the workstation until someone logs
    /// in — each gets their own tools — or it is closed.
    /// </summary>
    async Task LogOut(ScheduleStore? store)
    {
        if (store == null || _user == null) return;
        if (Changed)
        {
            if (!await Dialogs.Confirm(this, L.T("Unsaved changes"), L.T("Save the changes and log out?"), L.T("Save and log out"))) return;
            Save();
            if (Changed) return; // not saved: the reason is shown
        }
        AppLog.Write($"{AppInfo.ScheduleName}: {_user.Name} logged out");
        if (!_lockWired)
        {
            _lockWired = true;
            LockLogin.Setup(store.Db, AppInfo.ScheduleName, null, _station?.StationName ?? "", L.T("Close"));
            LockLogin.LoggedIn += user =>
            {
                AppLog.Write($"{AppInfo.ScheduleName}: {user.Name} logged in");
                if (_settings != null) _settings.LastUser = user.Name;
                AppSettings.Change(s => s.LastUser = user.Name); // only this, over what the on-air program saved
                LockOverlay.IsVisible = false;
                Workstation.IsEnabled = true;
                UsersTab.Content = null; // made again for the new user
                ApplyUser(user);
                Tabs.SelectedItem = HomeTab;
            };
            LockLogin.Cancelled += Close;
        }
        Tabs.SelectedItem = HomeTab;
        _user = null;
        Workstation.IsEnabled = false;
        LockLogin.Clear();
        LockOverlay.IsVisible = true;
        LockLogin.FocusFirst();
    }

    bool _lockWired;

    /// <summary>Refresh (F5): the library (songs, categories) and the daily playlists read again.</summary>
    void Refresh()
    {
        if (_library != null) _library.NotifyChanged();
        else LibraryChanged();
        if (_daily != null) _ = _daily.Refresh();
        SavedText.Text = L.F("Refreshed at {0:HH:mm:ss}.", DateTime.Now);
    }

    DateTime _categoriesRead;

    void LibraryChanged()
    {
        _takeLines.Clear(); // a category's songs may have changed: looked at again
        // The templates' category list (not more than every few seconds: a scan may change things often).
        if (DateTime.Now - _categoriesRead > TimeSpan.FromSeconds(3))
        {
            _categoriesRead = DateTime.Now;
            _ = LoadCategories();
        }
        foreach (var page in _pages.Values)
            switch (page)
            {
                case CategoriesWindow categories: _ = categories.Refresh(); break;
                case ImportWindow import: _ = import.Refresh(); break;
            }
        _nextFigures = DateTime.MinValue; // Home's figures too
    }

    bool _tagsOpen;

    /// <summary>Songs → Edit tags…: written in the file, then the library and the planned hours show them.</summary>
    async Task EditSongTags(string path)
    {
        if (_tagsOpen) return;
        if (!SharedPaths.FileExists(path))
        {
            SavedText.Text = L.F("Not found: {0}", path);
            return;
        }
        _tagsOpen = true;
        try
        {
            if (await new TagEditorWindow((p, tags, cover) => DailyPlaylistsView.WriteTags(_library?.Db, p, tags, cover), path).ShowDialog<bool>(this))
            {
                SavedText.Text = L.F("Tags saved: {0}", Path.GetFileName(path));
                _library?.NotifyChanged();
            }
        }
        finally
        {
            _tagsOpen = false;
        }
    }

    /// <summary>
    /// Songs → Edit cue points…: on the Preview output, saved to the library (every studio has them, the playout takes
    /// them within a minute) and the planned hours count the new length.
    /// </summary>
    async Task EditSongCues(string path)
    {
        if (_tagsOpen || _library?.Db is not { } db) return;
        if (!SharedPaths.FileExists(path))
        {
            SavedText.Text = L.F("Not found: {0}", path);
            return;
        }
        SongsListening.Stop();
        PreviewDeck deck;
        try { deck = SongsListening.Deck(); }
        catch (Exception ex)
        {
            SavedText.Text = L.F("Can't listen: {0}", ex.Message);
            return;
        }
        var track = db.GetByPath(path);
        var kind = TrackClassifier.Classify(path);
        double Crossfade(TrackKind a, TrackKind b) =>
            a is TrackKind.Music or TrackKind.Stream && b is TrackKind.Music or TrackKind.Stream ? _settings?.Crossfade ?? 0 : _settings?.OtherCrossfade ?? 0;
        var host = new SegueHost
        {
            Open = deck.Open,
            GetCues = db.GetCues,
            SaveCues = (file, cues) => Task.Run(() =>
            {
                db.SetCues(file, cues);
                double length = track?.Duration is > 0 and var d ? d : AudioProbe.Duration(file) ?? 0;
                if (length > 0) new PlanStore(db).UpdateSeconds(file, DailyPlaylistsView.Plays(cues, length, Crossfade(kind, kind)), DateTime.Now.Date.AddHours(DateTime.Now.Hour));
            }),
            LoadWaveform = DailyPlaylistsView.Waveforms.LoadAsync,
            Crossfade = Crossfade,
            PreviewIsOnAir = deck.PreviewIsOnAir,
        };
        _tagsOpen = true;
        try
        {
            var title = SongsView.SelectedSong is { } s && s.Path == path ? s.Title : Path.GetFileNameWithoutExtension(path);
            var editor = new CueEditorWindow(host, path, title);
            await editor.ShowDialog(this);
            if (editor.Saved)
            {
                SavedText.Text = L.T("Cue points saved. The playout takes them within a minute.");
                _library.NotifyChanged();
            }
        }
        finally
        {
            _tagsOpen = false;
        }
    }

    /// <summary>The status bar: which computer is on air and which are standby (a library shared by several studios).</summary>
    void ShowStudios(StudioList? list)
    {
        var live = list?.Studios.Where(st => list.Silent(st) < TimeSpan.FromMinutes(1)).ToList() ?? [];
        if (list == null || list.Studios.Count == 0)
        {
            StatusStudios.Text = "";
            return;
        }
        var standby = live.Where(st => !st.OnAir).Select(st => st.Computer).ToList();
        string onAir = list.OnAir is { } o
            ? list.Silent(o) < TimeSpan.FromSeconds(10) ? L.F("● On air: {0}", o.Computer) : L.F("● On air: {0} (not answering for {1:0} s)", o.Computer, list.Silent(o).TotalSeconds)
            : L.T("● Nothing on air");
        StatusStudios.Text = standby.Count > 0 ? onAir + " · " + L.F("standby: {0}", string.Join(", ", standby)) : onAir;
        ToolTip.SetTip(StatusStudios, string.Join("\n", list.Studios.Select(st =>
            $"{st.Computer}: {(st.OnAir ? L.T("on air") : L.T("standby"))}, {Seen(list.Silent(st))}" + (st.Version.Length > 0 ? $", Sintoniza Emi {st.Version}" : ""))));

        static string Seen(TimeSpan ago) => ago < TimeSpan.FromSeconds(10) ? L.T("answering")
            : ago < TimeSpan.FromHours(1) ? L.F("last seen {0:0} min ago", Math.Max(1, ago.TotalMinutes))
            : L.F("last seen {0:dd/MM HH:mm}", DateTime.Now - ago);
    }
}
