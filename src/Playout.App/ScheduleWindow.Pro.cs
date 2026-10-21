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
        if (_library != null) SongsView.Attach(_library);

        MenuSave.Click += (_, _) => Save();
        MenuClose.Click += (_, _) => Close();
        MenuManual.Click += (_, _) =>
        {
            if (Manual.Find() is { } pdf) Updates.OpenInBrowser(pdf);
            else SavedText.Text = L.T("The manual isn't installed with this copy: it is in docs/manual on GitHub.");
        };
        MenuAbout.Click += async (_, _) => await Dialogs.Confirm(this, AppInfo.ScheduleName,
            $"{AppInfo.ScheduleName} — {AppInfo.Name}\n" + (Updates.IsDevBuild ? L.T("Development build (built from source)") : L.F("Version {0}", Updates.CurrentText))
            + "\n\nCésar Araújo · cesararaujo.net", L.T("Close"));
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { Save(); e.Handled = true; }
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
        var data = _data;
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
                return (stats, categories.Count, last, hours, items, station);
            });
            _station = f.station;
            ShowFigures(data, now, f.stats, f.Item2, f.last, f.hours, f.items);
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
            : tab == Tabs.Items[3] || tab == Tabs.Items[0] ? May(Permission.Programming)
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
                if (_settings != null) { _settings.LastUser = user.Name; _settings.Save(); }
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
}
