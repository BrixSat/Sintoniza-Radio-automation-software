// Sintoniza's checks before a release (scripts/check.sh): the on-air engine, the on-air program and Sintoniza Pro,
// headless and with no sound (the "no sound" device), on a library and audio files of their own. Each check says what
// it looks at; the program ends with 1 if any failed. Run it with its own settings folder (XDG_CONFIG_HOME): it writes
// settings there.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Playout.App;
using Playout.Engine;
using Playout.Library;

string work = args.FirstOrDefault(a => !a.StartsWith("--")) ?? Path.Combine(Path.GetTempPath(), "sintoniza-checks");
string media = Path.Combine(work, "media");
string[] songs = Audio.Make(media, 4);
int fails = 0, passes = 0;
void Check(bool ok, string what)
{
    Console.WriteLine((ok ? "  ✓ " : "  ✗ ") + what);
    if (ok) passes++; else fails++;
}
bool Wait(Func<bool> until, double seconds)
{
    var end = DateTime.Now.AddSeconds(seconds);
    while (!until() && DateTime.Now < end) Thread.Sleep(50);
    return until();
}

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
void Pump(int n = 10) { for (int i = 0; i < n; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(40); } }
bool WaitUi(Func<bool> until, double seconds)
{
    var end = DateTime.Now.AddSeconds(seconds);
    while (!until() && DateTime.Now < end) Pump(2);
    return until();
}

// ───────────────────────── The on-air engine ─────────────────────────

Console.WriteLine("The on-air engine");
PlayoutEngine NewEngine() => new(AudioRouting.Default, new CueStore(Path.Combine(work, $"cues-{Guid.NewGuid():N}.json")), 0);
string? OnAir(PlayoutEngine e) => e.GetStatus().Current?.Path;
{
    var e = NewEngine();
    e.ConfigureFill(() => null); // fill music away (a share that doesn't answer)
    e.AddFiles([songs[0]]);
    Thread.Sleep(300);
    e.Play();
    Check(Wait(() => OnAir(e) == songs[0], 3), "Play puts the first item on air");
    Check(Wait(() => e.GetStatus().Current == null, 10), "it ends; there is no fill music to add");
    e.AddItems([new ScheduledItem(songs[1])]);
    Check(Wait(() => OnAir(e) == songs[1], 3), "what is added while waiting for fill music goes on air at once");
    e.Dispose();
}
{
    var e = NewEngine();
    e.ConfigureFill(() => songs[2]);
    Thread.Sleep(200);
    e.Play();
    Check(Wait(() => OnAir(e) == songs[2], 3), "Play with an empty playlist in AUTO starts fill music");
    e.Dispose();
}
{
    var e = NewEngine();
    e.AddFiles([songs[0], songs[1]]);
    Thread.Sleep(200);
    e.Play();
    Wait(() => OnAir(e) == songs[0], 3);
    e.Standby = true;
    Check(Wait(() => e.State == PlayerState.Stopped && e.GetStatus().Current == null, 3), "standby stops what is on air");
    e.Play();
    Thread.Sleep(800);
    Check(e.GetStatus().Current == null, "standby: Play puts nothing on air");
    e.Standby = false;
    e.Play();
    Check(Wait(() => e.GetStatus().Current != null, 3), "on air again, Play plays");
    e.Dispose();
}
{
    var e = NewEngine();
    PlaylistSnapshot? last = null;
    e.PlaylistChanged += snap => last = snap;
    e.ConfigureFill(null);
    e.AddFiles([songs[0]]);
    e.AddStream("http://10.255.255.1:8000/never", "Relay", 0); // a route that never answers: it stays connecting
    e.AddFiles([songs[3]]);
    Thread.Sleep(300);
    e.Play();
    Wait(() => OnAir(e) == songs[0], 3);
    Wait(() => OnAir(e) != songs[0], 10);
    if (last?.Items.FirstOrDefault(t => t.IsStream) is { } relay) e.Remove(relay.Id);
    Check(Wait(() => OnAir(e) == songs[3], 4), "a relay taken out while it connects: the next item plays, not the top of the list");
    e.Dispose();
}
{
    var e = NewEngine();
    e.AddFiles([songs[0]]);
    Thread.Sleep(200);
    e.Play();
    Wait(() => OnAir(e) == songs[0], 3);
    e.Dispose();
    Thread.Sleep(5000); // its end comes after Dispose
    Check(true, "no crash when an item ends after the engine was closed");
}

// ───────────────────────── Settings shared by the programs ─────────────────────────

Console.WriteLine("Settings");
{
    var pro = AppSettings.Load();
    var emi = AppSettings.Load();
    emi.PlaylistItems = [new PlaylistEntry { Path = songs[0] }, new PlaylistEntry { Path = songs[1] }];
    emi.OnAirIndex = 1;
    emi.Save();
    pro.LastUser = "ana";
    AppSettings.Change(s => s.LastUser = "ana");
    var after = AppSettings.Load();
    Check(after.PlaylistItems.Count == 2 && after.OnAirIndex == 1 && after.LastUser == "ana",
        "Sintoniza Pro saving its settings keeps the on-air program's playlist");
    after.PlaylistItems = [];
    after.OnAirIndex = -1;
    after.Save();
}

// ───────────────────────── The on-air program ─────────────────────────

Console.WriteLine("The on-air program (a library of its own)");
{
    var settings = AppSettings.Load();
    settings.Library = new LibrarySettings { File = Path.Combine(work, "library.db") };
    settings.FillEnabled = false;
    settings.WasStandby = true; // even so: a library of its own is never standby
    settings.Save();
    var win = new MainWindow { Width = 1400, Height = 860 };
    win.Show();
    var engine = (PlayoutEngine)typeof(MainWindow).GetField("_engine", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(win)!;
    WaitUi(() => !engine.Standby, 10);
    Pump(40);
    Check(!win.FindControl<Border>("StandbyBanner")!.IsVisible && !engine.Standby, "it is on air (never standby with a library of its own)");
    engine.AddFiles([songs[0]]);
    Pump(10);
    engine.Play();
    Check(WaitUi(() => engine.State == PlayerState.Playing, 5), "it plays");
    var editor = new CueEditorWindow(engine, songs[0], "check");
    _ = editor.ShowDialog(win);
    Pump(20);
    var box = editor.GetLogicalDescendants().OfType<TextBox>().First(b => b.Classes.Contains("time"));
    box.Text = "0:01.0";
    box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
    Pump(5);
    editor.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Pump(20);
    Check(editor.Saved && engine.GetCues(songs[0]).CueIn is { } cueIn && Math.Abs(cueIn - 1) < 0.01, "Edit cue points saves them");
    win.Close();
    Pump(10);
}

// ───────────────────────── A restart ─────────────────────────

Console.WriteLine("A restart (yesterday's playlist saved, a daily playlist for every hour)");
{
    // Songs long enough for an hour of them, in their own folder and library.
    string restartMedia = Path.Combine(work, "restart");
    var tracks = Audio.Make(restartMedia, 12, seconds: 150, rate: 8000, channels: 1);
    var settings = AppSettings.Load();
    settings.Library = new LibrarySettings { File = Path.Combine(work, "restart.db") };
    var db = LibraryDb.Open(settings.Library);
    LibraryScanner.Scan(db, [restartMedia]);
    long music = db.Categories().First(c => c.Role == "music").Id;
    db.SetFolderCategory(restartMedia, music);
    var store = new ScheduleStore(db);
    var everyHour = Enum.GetValues<DayOfWeek>().SelectMany(d => Enumerable.Range(0, 24).Select(h => new WeekTime { Day = d, Time = $"{h:00}:00:00" })).ToList();
    var data = new ScheduleData
    {
        Templates = { new PlaylistTemplate { Name = "Hits", Slots = everyHour,
            Lines = Enumerable.Range(0, 12).Select(_ => new TemplateLine { Source = LineSource.Category, CategoryId = music, CategoryName = "Music", Kind = "music" }).ToList() } },
    };
    data.Normalize();
    store.Save(data, null, "checks");
    // Closed yesterday: 50 played, 3 jingles still to play.
    settings.PlaylistItems = Enumerable.Range(0, 50).Select(i => new PlaylistEntry { Path = tracks[i % tracks.Length], PlayedAt = DateTime.UtcNow.AddDays(-1).AddMinutes(i * 3) })
        .Concat(Enumerable.Range(0, 3).Select(i => new PlaylistEntry { Path = tracks[i], Kind = TrackKind.Jingle })).ToList();
    settings.OnAirIndex = 49;
    settings.WasPlaying = true;
    settings.AutoAdvance = true;
    settings.ResumeOnStart = true;
    settings.FillEnabled = false;
    settings.WasStandby = false;
    settings.Save();
    foreach (var file in new[] { "settings.json", "running" }.Select(f => Path.Combine(AppSettings.DataFolder, f)).Where(File.Exists))
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-1));

    var started = DateTime.Now;
    var win = new MainWindow { Width = 1400, Height = 860 };
    win.Show();
    var engine = (PlayoutEngine)typeof(MainWindow).GetField("_engine", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(win)!;
    var tick = typeof(MainWindow).GetMethod("Tick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
    var now = DateTime.Now;
    string thisHour = PlanPlayer.Tag(new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0), false)[..18];
    bool OnAirFromPlan() => engine.GetStatus().Current?.Tag?.StartsWith(thisHour) == true;
    while (!OnAirFromPlan() && (DateTime.Now - started).TotalSeconds < 30)
    {
        Dispatcher.UIThread.RunJobs();
        tick.Invoke(win, null); // the screen's update (the headless timer seldom fires)
        Thread.Sleep(200);
    }
    double seconds = (DateTime.Now - started).TotalSeconds;
    Check(OnAirFromPlan() && seconds < 15, $"the hour now, from the daily playlist, is on air by itself ({seconds:0.0} s after the start)");
    Pump(20);
    var rows = (List<PlaylistRow>)typeof(MainWindow).GetField("_order", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(win)!;
    Check(rows.Count(r => r.IsPlayed) <= 10, $"yesterday's history isn't all put back ({rows.Count(r => r.IsPlayed)} played rows)");
    Check(rows.Any(r => r.IsPlaying && r.Track.Tag?.StartsWith(thisHour) == true), "the screen shows it on air");
    win.Close();
    Pump(10);
}

// ───────────────────────── Sintoniza Pro ─────────────────────────

Console.WriteLine("Sintoniza Pro");
{
    var settings = AppSettings.Load();
    var db = LibraryDb.Open(settings.Library);
    LibraryScanner.Scan(db, [media]);
    var store = new ScheduleStore(db);
    var (data, version) = store.Load();
    var library = new LibraryService();
    _ = library.OpenAsync(settings.Library);
    var win = new ScheduleWindow(data, "", store, version, null, library, settings) { Width = 1440, Height = 880 };
    win.Show();
    Pump(60);
    win.FindControl<Button>("NavSongs")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    var songsView = win.FindControl<TrackLibraryView>("SongsView")!;
    var list = songsView.GetLogicalDescendants().OfType<ListBox>().First(l => l.Name == "TrackList");
    Check(WaitUi(() => list.ItemCount >= songs.Length, 20), $"Songs lists the library ({list.ItemCount})");
    list.SelectedIndex = 0;
    Pump(5);
    string path = songsView.SelectedSong!.Value.Path;
    var bar = win.FindControl<ListenBar>("SongsListening")!;
    bar.ListenOrPause();
    Pump(10);
    Check(bar.Path == path, "Songs: ▶ listens to the selected song");
    bar.ListenOrPause();
    Pump(5);
    Check(bar.GetLogicalDescendants().OfType<Button>().First(b => b.Name == "ListenButton").Content as string == "▶", "…▶ again pauses");
    bar.Stop();
    var menu = new ContextMenu();
    typeof(TrackLibraryView).GetMethod("BuildMenu", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(songsView, [menu]);
    var labels = menu.Items.OfType<MenuItem>().Select(m => m.Header as string).ToList();
    Check(labels.Contains("Listen") && labels.Contains("Edit cue points…") && labels.Contains("Edit tags…") && !labels.Contains("Add to playlist"),
        "Songs' menu: Listen, Edit cue points…, Edit tags…, no Add to playlist");
    menu.Items.OfType<MenuItem>().First(m => (m.Header as string) == "Edit cue points…").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    WaitUi(() => win.OwnedWindows.OfType<CueEditorWindow>().Any(), 5);
    var editor = win.OwnedWindows.OfType<CueEditorWindow>().FirstOrDefault();
    Check(editor != null, "Songs: Edit cue points… opens the editor");
    if (editor != null)
    {
        var box = editor.GetLogicalDescendants().OfType<TextBox>().First(b => b.Classes.Contains("time"));
        box.Text = "0:02.0";
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Pump(5);
        editor.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WaitUi(() => !editor.IsVisible, 5);
        Check(db.GetCues(path).CueIn is { } c && Math.Abs(c - 2) < 0.01, "…and saves them in the library");
    }
    win.FindControl<Button>("NavDaily")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Pump(40);
    Check(win.GetLogicalDescendants().OfType<DailyPlaylistsView>().Any(d => d.GetLogicalDescendants().OfType<ListenBar>().Any()),
        "Daily playlists opens, with its listen bar");
    win.Close();
    Pump(10);
}

// ───────────────────────── Several stations in one library ─────────────────────────

Console.WriteLine("Stations");
{
    var settings = AppSettings.Load();
    Check(CurrentStation.Id == settings.Station, "the station is this computer's setting");
    var db = LibraryDb.Open(settings.Library);
    var since = DateTime.UtcNow.AddSeconds(-1); // not the plays of the checks above
    db.RecordPlay(songs[0], "Check 1", "Tone 1", DateTime.UtcNow, "playlist");
    long first = CurrentStation.Id;
    CurrentStation.Id = first + 1;
    Check(db.PlaysSince(since).Count == 0 && db.LastStarted(songs[0]) == null, "another station doesn't count this station's plays");
    db.RecordPlay(songs[1], "Check 2", "Tone 2", DateTime.UtcNow, "playlist");
    CurrentStation.Id = first;
    Check(db.PlaysSince(since).Count == 1 && db.LastStarted(songs[0]) != null, "…and this station only its own");
}

Console.WriteLine(fails == 0 ? $"All {passes} checks passed." : $"{fails} of {passes + fails} checks FAILED.");
Environment.Exit(fails == 0 ? 0 : 1);

/// <summary>Short test tones as WAV files (nothing else needed to make them).</summary>
static class Audio
{
    public static string[] Make(string folder, int count, double seconds = 4, int rate = 44100, int channels = 2)
    {
        Directory.CreateDirectory(folder);
        return Enumerable.Range(1, count).Select(n =>
        {
            string file = Path.Combine(folder, $"Check {n} - Tone {n}.wav");
            if (!File.Exists(file)) Write(file, 300 + 50 * n, seconds, rate, channels);
            return file;
        }).ToArray();
    }

    static void Write(string file, double hz, double seconds, int rate, int channels)
    {
        int frames = (int)(rate * seconds);
        using var w = new BinaryWriter(File.Create(file));
        int bytes = frames * channels * 2;
        w.Write("RIFF"u8); w.Write(36 + bytes); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)channels); w.Write(rate); w.Write(rate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
        w.Write("data"u8); w.Write(bytes);
        for (int i = 0; i < frames; i++)
        {
            short v = (short)(Math.Sin(2 * Math.PI * hz * i / rate) * 8000);
            for (int c = 0; c < channels; c++) w.Write(v);
        }
    }
}
