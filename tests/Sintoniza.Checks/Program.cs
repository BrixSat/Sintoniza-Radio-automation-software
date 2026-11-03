// Sintoniza's checks before a release (scripts/check.sh): the on-air engine, the on-air program and Sintoniza Pro,
// headless and with no sound (the "no sound" device), on a library and audio files of their own. Each check says what
// it looks at; the program ends with 1 if any failed. Run it with its own settings folder (XDG_CONFIG_HOME): it writes
// settings there.
using Avalonia;
using Avalonia.Controls;
using ToggleButton = Avalonia.Controls.Primitives.ToggleButton;
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
    // A pause: no sound for its time (not taken for dead air), then the next item; kept with the saved playlist.
    var e = NewEngine();
    e.ConfigureSilence(new SilenceSettings(Enabled: true, Seconds: 1));
    e.AddPause("Weather and traffic", 3);
    e.AddFiles([songs[1]]);
    Thread.Sleep(300);
    e.Play();
    Check(Wait(() => e.GetStatus().Current is { IsPause: true, Title: "Weather and traffic" }, 3), "a pause goes on air, with what to talk about as its title");
    Thread.Sleep(2000);
    Check(e.GetStatus().Current?.IsPause == true, "…silent, and not skipped as dead air");
    Check(Wait(() => OnAir(e) == songs[1], 4), "…the next item when its time is up");
    var saved = PlaylistEntry.From(new Track(Guid.NewGuid(), Pause.Path, "", "Weather and traffic", 90, TrackKind.Pause));
    Check(saved.IsStream && saved.StreamSeconds == 90 && saved.StreamName == "Weather and traffic", "a pause is saved with the playlist (its time and what about)");
    e.Dispose();
}
{
    // The operator stops the player; an ad break comes due: a warning, then it plays all the same, then stopped again.
    var e = NewEngine();
    e.ConfigureFill(() => songs[2]);
    e.AddFiles([songs[0], songs[1]]);
    Thread.Sleep(200);
    e.Play();
    Wait(() => OnAir(e) == songs[0], 3);
    e.Stop();
    Wait(() => e.State == PlayerState.Stopped, 3);
    e.InsertEvent("Ad break 12:00", [new ScheduledItem(songs[3], TrackKind.Commercial, InBreak: true), new ScheduledItem(songs[2], TrackKind.Jingle, InBreak: true)], EventTiming.Soft);
    Check(Wait(() => e.StoppedBreak != null, 2) && e.GetStatus().Current == null, "an ad break due while stopped: warned, not on air yet");
    Check(Wait(() => OnAir(e) == songs[3], 13), "…on air a few seconds later all the same");
    Check(Wait(() => OnAir(e) == songs[2], 8), "…all of it");
    Check(Wait(() => e.State == PlayerState.Stopped && e.GetStatus().Current == null, 8), "…then the player stops again, as the operator left it");
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

{
    // The time signal (Stop mode) taking the air 1 s into an item (the next hour's opening jingle, crossfaded in as a
    // break ending on the hour ended): it plays again after the signal, from the start, rather than being lost.
    var any = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
    var e = NewEngine();
    e.AddFiles([songs[0], songs[1]]);
    Thread.Sleep(300);
    e.Play();
    Wait(() => OnAir(e) == songs[0], 3);
    Thread.Sleep(1000);
    var post = typeof(PlayoutEngine).GetMethod("Post", any)!;
    void Run(string method) => post.Invoke(e, [new Action(() => typeof(PlayoutEngine).GetMethod(method, any)!.Invoke(e, null))]);
    Run("TakeOffAirForSignal");
    Thread.Sleep(1000);
    Run("ResumeAfterSignal");
    Wait(() => e.GetStatus().Current != null, 3);
    Check(OnAir(e) == songs[0] && e.GetStatus().Position < 2, "the time signal: an item that had only just started plays again after it");
    e.Dispose();
}

// ───────────────────────── Fillers: one, for a small gap ─────────────────────────

Console.WriteLine("Fillers (one, only for a small gap)");
{
    string Tone(string name, double seconds) => Audio.Make(Path.Combine(work, "filler-" + name), 1, seconds: seconds, rate: 8000, channels: 1)[0];
    string f2 = Tone("2s", 2), f4 = Tone("4s", 4), f10 = Tone("10s", 10);
    PlayoutEngine GapEngine(out List<PlaylistSnapshot> snaps)
    {
        var e = NewEngine();
        var list = new List<PlaylistSnapshot>();
        e.PlaylistChanged += s => { lock (list) list.Add(s); };
        snaps = list;
        e.CrossfadeSeconds = 0;
        e.OtherCrossfadeSeconds = 0;
        string[] all = [f2, f4, f10];
        int n = 0;
        e.FillerFill = () => all[n++ % all.Length];
        e.FillerGapSeconds = 60;
        return e;
    }
    int Fillers(List<PlaylistSnapshot> snaps) { lock (snaps) return snaps.Count == 0 ? 0 : snaps[^1].Items.Count(t => t.Tag == PlayoutEngine.FillerTag); }

    // An event 6 s after the song on air ends: one filler that reaches it (10 s; 2 s and 4 s don't), faded when it starts.
    {
        var e = GapEngine(out var snaps);
        e.AddFiles([songs[0], songs[1]]);
        Thread.Sleep(300);
        var due = DateTime.UtcNow.AddSeconds(4 + 6 + 0.3);
        e.ExpectedEvents = [due];
        e.Play();
        Wait(() => e.GetStatus().Current?.Tag == PlayoutEngine.FillerTag, 8);
        Check(OnAir(e) == f10, $"6 s before an event: one filler that reaches it ({Path.GetFileName(Path.GetDirectoryName(OnAir(e) ?? "") ?? "")})");
        Check(Fillers(snaps) == 1, "…only one");
        while (DateTime.UtcNow < due) Thread.Sleep(50);
        e.InsertEvent("News", [new ScheduledItem(songs[2])], EventTiming.Soft); // the schedule puts it in at its time
        Check(Wait(() => OnAir(e) == songs[2], 2), "the event starts on time: the filler fades");
        e.Dispose();
    }
    // A gap of 30 s: no filler reaches it, the next song plays.
    {
        var e = GapEngine(out var snaps);
        e.AddFiles([songs[0], songs[1]]);
        Thread.Sleep(300);
        e.ExpectedEvents = [DateTime.UtcNow.AddSeconds(4 + 30)];
        e.Play();
        Wait(() => OnAir(e) != null && OnAir(e) != songs[0], 8);
        Check(OnAir(e) == songs[1] && Fillers(snaps) == 0, "30 s before an event, no filler that long: the next song");
        e.Dispose();
    }
    // A break that ends at its time, 6 s of gap: one filler, not one after another.
    {
        var e = GapEngine(out var snaps);
        e.AddFiles([songs[0]]);
        Thread.Sleep(300);
        e.Play();
        Wait(() => OnAir(e) == songs[0], 3);
        e.InsertEvent("Ads", [new ScheduledItem(songs[3], TrackKind.Commercial)], new EventTiming(EventStart.EndAt, EndsAt: DateTime.UtcNow.AddSeconds(3.5 + 6 + 4)));
        Thread.Sleep(3000);
        Check(Fillers(snaps) == 1, $"before a break that ends at its time: one filler ({Fillers(snaps)})");
        Thread.Sleep(3000);
        Check(Fillers(snaps) <= 1, "…never a second");
        e.Dispose();
    }
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

Console.WriteLine("Translations");
{
    // A language added as a file in the settings folder's translations folder, with no new version.
    string folder = Localization.Folders[^1];
    Directory.CreateDirectory(folder);
    File.WriteAllText(Path.Combine(folder, "de.json"), "{ \"Add pause\": \"Pause hinzufügen\", \"Remove\": \"\" }");
    File.WriteAllText(Path.Combine(folder, "pt.json"), "{ \"Add pause\": \"Pôr pausa\" }");
    Localization.Rescan();
    Check(Localization.Languages.Any(l => l.Code == "de" && l.Name == "Deutsch"), "a translation file in the translations folder adds its language (Deutsch)");
    Check(Localization.Languages.Count(l => l.Code == "pt") == 1, "…a built-in language is listed once");
    Localization.Load("de");
    Check(L.T("Add pause") == "Pause hinzufügen" && L.T("Remove") == "Remove", "…its texts are used; one left empty stays in English");
    Localization.Load("pt");
    Check(L.T("Add pause") == "Pôr pausa" && L.T("Edit pause") == "Editar pausa", "a file for a built-in language corrects its texts, the rest stay built in");
    File.WriteAllText(Path.Combine(folder, "pt-BR.json"), "{ \"Edit pause\": \"Editar a pausa\" }");
    Localization.Rescan();
    Localization.Load("pt-BR");
    Check(L.T("Edit pause") == "Editar a pausa" && L.T("Remove") == "Remover", "a country's variant (pt-BR) takes what it doesn't have from its language (pt), not English");
    File.Delete(Path.Combine(folder, "pt-BR.json"));
    File.Delete(Path.Combine(folder, "de.json"));
    File.Delete(Path.Combine(folder, "pt.json"));
    Localization.Rescan();
    Localization.Load("en");
}

// ───────────────────────── The on-air program ─────────────────────────

Console.WriteLine("The on-air program (a library of its own)");
{
    var settings = AppSettings.Load();
    settings.Library = new LibrarySettings { File = Path.Combine(work, "library.db") };
    settings.FillEnabled = false;
    settings.WasStandby = true; // even so: a library of its own is never standby
    settings.AutoAdvance = true;
    settings.Save();
    // A live show on air now: it started 10 minutes ago, for an hour.
    var showStart = DateTime.Now.AddMinutes(-10);
    showStart = showStart.AddTicks(-(showStart.Ticks % TimeSpan.TicksPerSecond));
    {
        var showDb = LibraryDb.Open(settings.Library);
        var showData = new ScheduleData { Shows = { new LiveShow { Name = "Manhãs", Presenter = "Rita", Minutes = 60, Notes = "Guest at 10:30",
            Slots = [new WeekTime { Day = showStart.DayOfWeek, Time = showStart.ToString("HH:mm:ss") }] } } };
        showData.Normalize();
        new ScheduleStore(showDb).Save(showData, null, "checks");
        var back = new ScheduleStore(showDb).Load().Data.Shows.FirstOrDefault();
        Check(back is { Presenter: "Rita", Minutes: 60, Notes: "Guest at 10:30" } && back.OnAirAt(DateTime.Now) != null,
            "a live show is kept when the schedule is saved, and is on air at its time");
    }
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

    // The show on air: ASSISTED, and the presenter view open.
    var autoSwitch = win.FindControl<ToggleButton>("AutoSwitch")!;
    var scheduler = (Scheduler)typeof(MainWindow).GetField("_scheduler", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(win)!;
    var follow = typeof(MainWindow).GetMethod("FollowShow", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
    WaitUi(() => scheduler.Loaded && scheduler.ShowAt(DateTime.Now) != null, 15);
    follow.Invoke(win, [DateTime.Now]); // as the screen does once a second (headless, its timer comes round rarely)
    Pump(5);
    Check(autoSwitch.IsChecked == false && !engine.AutoAdvance, "a live show on air: ASSISTED");
    object? Field(string name) => typeof(MainWindow).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(win);
    Check(WaitUi(() => Field("_presenter") is PresenterWindow { IsVisible: true }, 3), "…and the presenter view opens");

    // A request taken, then put on next by the presenter, with who it is for as its note.
    Check(win.AddRequest(songs[2], "Song", "Band", "Ana", "Happy birthday", "staff") == null && win.Requests.Count == 1, "a request is taken");
    var request = win.Requests[0];
    win.AcceptRequest(request.Id);
    Check(win.Requests.Count == 0 && WaitUi(() => Field("_snapshot") is PlaylistSnapshot snap && snap.Items.Any(t => t.Path == songs[2] && t.Note == "Request from Ana: Happy birthday"), 3),
        "…the presenter puts it on next, with who it is for and the message as its note");

    // The on-air screen: the status lamps, the ON AIR sign, the desk's mic fader, big counters.
    var tick = typeof(MainWindow).GetMethod("Tick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
    void Ticks() { for (int i = 0; i < 3; i++) tick.Invoke(win, null); Pump(3); }
    Ticks();
    var sign = win.FindControl<Border>("OnAirSign")!;
    var signText = win.FindControl<TextBlock>("OnAirSignText")!;
    Check(sign.IsVisible && signText.Text?.Contains("Manhãs") == true, $"the ON AIR sign is lit during the live show ({signText.Text})");
    var lamps = win.FindControl<WrapPanel>("LampPanel")!;
    var strip = win.FindControl<Grid>("ScreenStrip")!;
    var lights = win.FindControl<StackPanel>("Lights")!;
    Check(!strip.IsVisible && lights.Children.Count >= 9, $"the status lamps are in the status bar, as they were ({lights.Children.Count} lamps)");
    var onGpio = typeof(MainWindow).GetMethod("OnGpio", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
    onGpio.Invoke(win, [GpioAction.MicLive, 0]);
    Ticks();
    Check(signText.Text == "MIC LIVE" && Field("_signLit") is true, "the desk's mic fader up (GPIO): MIC LIVE, and the sign's GPIO output on");
    var settings2 = (AppSettings)Field("_settings")!;
    settings2.Screen = new ScreenSettings { LampsAsPanel = true, BigCounters = true, OnAirSign = false, HiddenLamps = ["rec"] };
    typeof(MainWindow).GetMethod("ApplyScreen", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(win, null);
    Ticks();
    Check(!sign.IsVisible && Field("_signLit") is true, "the sign switched off in Settings: not shown, its GPIO output still on");
    Check(strip.IsVisible && lamps.Children.Count >= 9 && lights.Children.Count == 0, $"…or, chosen in Settings, a panel under the deck ({lamps.Children.Count} lamps)");
    Check(win.FindControl<TextBlock>("Remaining")!.FontSize >= 60, "big counters");
    // GPIO turned off with the fader up: nobody will say it came down, so it isn't MIC LIVE for ever.
    typeof(MainWindow).GetMethod("StopGpio", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(win, null);
    Check(Field("_deskMic") is false, "GPIO stopped: the desk's mic fader no longer counts as up");
    settings2.Screen = new ScreenSettings();

    // A serial port's output set to the ON AIR sign: on with the sign, off without it (not held on).
    var pins = new TestPins();
    var serial = new GpioService(pins, new GpioSettings { Enabled = true, Dtr = GpioSignal.MicLive, Rts = GpioSignal.OnAir }, poll: false);
    bool offAtStart = !pins.DtrOn;
    serial.Show(new GpioState(true, false, false, false, MicLive: true));
    bool onWithSign = pins.DtrOn && pins.RtsOn;
    serial.Show(new GpioState(true, false, false, false, MicLive: false));
    Check(offAtStart && onWithSign && !pins.DtrOn, "a serial GPIO output for the ON AIR sign follows the sign");
    serial.Dispose();

    // The show ends: AUTO again (as the screen sees it a second after the end).
    // The screen's own once-a-second update follows the show by the real clock, where it is still on: so the end is
    // shown again each time it is looked at, and the sign updated on its own.
    var signUpdate = typeof(MainWindow).GetMethod("UpdateOnAirSign", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
    bool Ended()
    {
        follow.Invoke(win, [showStart.AddMinutes(60).AddSeconds(1)]);
        signUpdate.Invoke(win, [null]);
        return autoSwitch.IsChecked == true && engine.AutoAdvance;
    }
    Check(WaitUi(Ended, 3), "the show is over: AUTO again");
    Check(Ended() && !sign.IsVisible, "…and the ON AIR sign goes out");
    // ASSISTED: a song's ▶ puts it on air now; the song on air fades out with its button.
    autoSwitch.IsChecked = false;
    Pump(3);
    engine.AddFiles([songs[1], songs[2]]);
    var rowsById = (Dictionary<Guid, PlaylistRow>)Field("_rowsById")!;
    PlaylistRow? target = null;
    WaitUi(() => (target = rowsById.Values.FirstOrDefault(r => r.IsMusic && !r.IsPlaying && !r.IsPreview && r.Track.Path == songs[2])) != null, 5);
    Check(target is { ShowPlayButton: true, ShowFadeButton: false }, "ASSISTED: a song in the playlist has a ▶");
    var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
    typeof(MainWindow).GetMethod("RowPlay_Click", flags)!.Invoke(win, [new Button { DataContext = target }, new RoutedEventArgs()]);
    Check(WaitUi(() => engine.GetStatus().Current?.Path == songs[2], 5), "…which puts it on air now");
    Check(WaitUi(() => target!.ShowFadeButton && !target.ShowPlayButton, 3), "…and on air it has the fade-out instead");
    typeof(MainWindow).GetMethod("RowFade_Click", flags)!.Invoke(win, [new Button { DataContext = target }, new RoutedEventArgs()]);
    Pump(5);
    Check(engine.State == PlayerState.Playing, "a double-click on ▶ doesn't fade out the song it just started (the second click is ignored)");
    Thread.Sleep(700); // not the second click of a double-click on ▶ (that is ignored: ▶ turns into fade-out there)
    typeof(MainWindow).GetMethod("RowFade_Click", flags)!.Invoke(win, [new Button { DataContext = target }, new RoutedEventArgs()]);
    Check(WaitUi(() => engine.State == PlayerState.Stopped, 8), "…which fades it out, and the player waits");
    autoSwitch.IsChecked = true;
    Pump(3);
    Check(!target!.ShowPlayButton && !target.ShowFadeButton, "AUTO: no buttons on the rows");
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
    // The copy of the schedule the last check's window kept is another library's (with a show on air now): not this one's.
    foreach (var copy in new[] { "schedule-cache.json", "schedule-cache.json.bak" }.Select(f => Path.Combine(AppSettings.DataFolder, f)).Where(File.Exists))
        File.Delete(copy);

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

// ───────────────────────── Tomorrow's daily playlists ─────────────────────────

Console.WriteLine("Tomorrow's daily playlists looked over (Pro's Home, the alerts)");
{
    var db = LibraryDb.Open(new LibrarySettings { File = Path.Combine(work, "tomorrow.db") });
    var tomorrow = DateTime.Today.AddDays(1);
    var slots = new[] { 7, 8, 9, 10 }.Select(h => new WeekTime { Day = tomorrow.DayOfWeek, Time = $"{h:00}:00:00" }).ToList();
    var data = new ScheduleData { Templates = { new PlaylistTemplate { Name = "Hits", Slots = slots, Lines = [new TemplateLine { Kind = "music" }] } } };
    string gone = Path.Combine(work, "gone", "Gone - Away.mp3");
    data.Events.Add(new ScheduledEvent { Name = "News", Path = Path.Combine(work, "gone", "news.mp3"), Slots = [new WeekTime { Day = tomorrow.DayOfWeek, Time = "07:15:00" }] });
    // Not wrong ahead of time: a folder whose newest file must be fresh (it will be by then), and a series with nothing that day on purpose.
    string newsFolder = Path.Combine(work, "news-folder");
    Directory.CreateDirectory(newsFolder);
    data.Events.Add(new ScheduledEvent { Name = "Fresh news", Path = newsFolder, MaxAgeMinutes = 30, Slots = [new WeekTime { Day = tomorrow.DayOfWeek, Time = "08:00:00" }] });
    data.Events.Add(new ScheduledEvent { Name = "Horoscope", Path = "", Series = [new SeriesFile { Day = tomorrow.AddDays(1).DayOfWeek, Path = songs[0] }],
        Slots = [new WeekTime { Day = tomorrow.DayOfWeek, Time = "08:30:00" }] });
    data.Normalize();
    var plan = new PlanStore(db);
    PlanItem Song(string path, double seconds) => new() { Path = path, Artist = "Check", Title = Path.GetFileNameWithoutExtension(path), Seconds = seconds };
    PlanHour Hour(int h) => new() { Start = tomorrow.AddHours(h), TemplateId = data.Templates[0].Id, Signature = PlaylistGenerator.Signature(data.Templates[0]) };
    plan.Save(Hour(7), Enumerable.Range(0, 15).Select(i => Song(songs[i % songs.Length], 240)).ToList()); // the full hour
    // 08:00 not made; 09:00 made empty (no music for its lines); 10:00 forty minutes, one file gone.
    plan.Save(Hour(9), []);
    plan.Save(Hour(10), Enumerable.Range(0, 9).Select(i => Song(songs[i % songs.Length], 240)).Append(Song(gone, 240)).ToList());
    var options = new PlaylistGenerator.Options(0, 0, TimeSpan.FromHours(3), TimeSpan.FromHours(1));
    var check = PlanCheck.Day(db, data, tomorrow, options);
    Check(check.Planned == 4 && check.Missing.SequenceEqual([tomorrow.AddHours(8)]), $"an hour with a template and no daily playlist ({string.Join(", ", check.Missing.Select(h => h.ToString("HH:mm")))})");
    Check(check.Empty.SequenceEqual([tomorrow.AddHours(9)]), "…an hour made with nothing in it");
    Check(check.Short.Count == 1 && check.Short[0].Hour == tomorrow.AddHours(10) && Math.Round(check.Short[0].By.TotalMinutes) == 20,
        $"…an hour 20 minutes short (the full one isn't) ({string.Join(", ", check.Short.Select(x => $"{x.Hour:HH:mm} {x.By.TotalMinutes:0}"))})");
    Check(check.NotFound.Count == 1 && check.NotFound[0].What.Contains("Gone - Away") && check.NotFound[0].At == tomorrow.AddHours(10).AddMinutes(36),
        $"…a file of the daily playlists not found, with when it plays ({string.Join("; ", check.NotFound.Select(n => $"{n.At:HH:mm} {n.What}"))})");
    Check(check.Entries.Count == 1 && check.Entries[0].At == tomorrow.AddHours(7).AddMinutes(15) && check.Entries[0].What.Contains("news.mp3"),
        $"…an event the playout would leave out (its file isn't there), as it would say it ({string.Join("; ", check.Entries.Select(n => $"{n.At:HH:mm} {n.What}"))})");
    var lines = check.Lines("Tomorrow");
    Check(lines.Count == 5 && lines[0].Contains("08:00") && lines[3].Contains("a file") && lines[4].Contains("news.mp3"), $"…told in a line for each ({string.Join(" | ", lines)})");
    var unseen = PlanCheck.Day(db, data, tomorrow, options, _ => false);
    Check(unseen.FilesUnseen && unseen.NotFound.Count == 0 && !unseen.AllGood && unseen.Lines("Tomorrow").Any(l => l.Contains("can be reached")),
        "a computer that reaches none of the files (a share down, paths not set): not a day of missing files, but said (the alert never says \"ready\")");
    // An hour made for another template (the playout makes it again soon): not made yet, not "empty"; one edited by hand is kept as it is.
    plan.Save(new PlanHour { Start = tomorrow.AddHours(9), TemplateId = null, Signature = "" }, []);
    var remade = PlanCheck.Day(db, data, tomorrow, options);
    Check(remade.Missing.Contains(tomorrow.AddHours(9)) && !remade.Empty.Contains(tomorrow.AddHours(9)), "an hour made before its template was placed: still to be made, not empty");
    plan.Save(new PlanHour { Start = tomorrow.AddHours(9), TemplateId = null, Signature = "", Edited = true }, []);
    Check(PlanCheck.Day(db, data, tomorrow, options).Empty.Contains(tomorrow.AddHours(9)), "…edited by hand: it stays, and an empty one is said");
    for (int h = 8; h <= 10; h++) plan.Save(Hour(h), Enumerable.Range(0, 15).Select(i => Song(songs[i % songs.Length], 240)).ToList());
    data.Events.Clear();
    Check(PlanCheck.Day(db, data, tomorrow, options) is { AllGood: true } fine && fine.Lines("Tomorrow").Count == 0, "…all made and there: nothing to tell");
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
    Check(labels.Contains("Copy the file's path") && labels.Contains("Take out of the library…"), "…Copy the file's path, Take out of the library…");
    {
        // A song whose file is gone: taken out of the library (and its categories); a file still there isn't touched.
        string gone = Path.Combine(work, "gone.wav");
        File.Copy(songs[3], gone, true);
        LibraryScanner.ScanFile(db, gone);
        var track = db.GetByPath(gone)!;
        db.SetAlsoIn([track.Id], db.Categories().First(c => c.Role == "jingles").Id, true);
        File.Delete(gone);
        Check(db.RemoveTracks([track.Id]) == 1 && db.GetByPath(gone) == null, "Take out of the library: the song is gone from it");
        Check(File.Exists(songs[3]), "…and no file is deleted");
    }
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

// ───────────────────────── Ad breaks ─────────────────────────

Console.WriteLine("Ad breaks (parts taken from a category)");
{
    var settings = AppSettings.Load();
    var db = LibraryDb.Open(settings.Library);
    string Folder(string name, int count, int from)
    {
        string folder = Path.Combine(work, "breaks", name);
        Directory.CreateDirectory(folder);
        foreach (var (tone, n) in Audio.Make(Path.Combine(work, "tones-" + name), count).Select((t, n) => (t, n)))
            File.Copy(tone, Path.Combine(folder, $"{name} {n + from}.wav"), true);
        return folder;
    }
    string promos = Folder("Promo", 2, 1), fillers = Folder("Filler", 1, 1), adverts = Folder("Advert", 1, 1);
    LibraryScanner.Scan(db, [Path.Combine(work, "breaks")]);
    var categories = db.Categories();
    long promosId = categories.First(c => c.Role == "promos").Id, fillersId = categories.First(c => c.Role == "fillers").Id;
    db.SetFolderCategory(promos, promosId);
    db.SetFolderCategory(fillers, fillersId);
    var now = DateTime.Now;
    var at = new DateTime(now.Year, now.Month, now.Day, now.Hour, 50, 0);
    var adBreak = new AdBreak
    {
        Name = "Ads", Times = [$"{at:HH:mm}"], Start = EventStart.EndAt,
        Parts = [BreakPart.FromCategory(promosId, "Promos"), new BreakPart { Kind = PartKind.Section, Section = "Ads" }, BreakPart.FromCategory(fillersId, "Fillers")],
    };
    var data = new ScheduleData
    {
        Breaks = { adBreak },
        Clients = { new AdClient { Name = "Shop", Campaigns = { new AdCampaign { Name = "Autumn", Section = "Ads", Files = [Directory.GetFiles(adverts)[0]],
            Placements = [new AdPlacement { Day = at.DayOfWeek, Time = $"{at:HH:mm}:00" }] } } } },
    };
    data.Normalize();
    var picker = ScheduleItems.CategoryPicker(db);
    var items = ScheduleItems.For(data, adBreak, at, null, picker);
    Check(items.Count == 3 && items[0].Path.Contains("Promo") && items[1].Kind == TrackKind.Commercial && items[2].Path.Contains("Filler")
          && items.All(i => i.InBreak), "a break: a promo (from Promos), the advert, a filler (from Fillers)");
    {
        var twice = new AdBreak { Name = "Twice", Times = [$"{at:HH:mm}"],
            Parts = [BreakPart.FromCategory(promosId, "Promos"), new BreakPart { Kind = PartKind.Section, Section = "Ads" }, BreakPart.FromCategory(promosId, "Promos")] };
        var both = ScheduleItems.For(data, twice, at, null, picker);
        Check(both.Count == 3 && both[0].Path != both[2].Path, "from Promos before and after the adverts: two different promos");
    }
    db.RecordPlay(items[0].Path, "", "promo", DateTime.UtcNow, "playlist");
    var again = ScheduleItems.For(data, adBreak, at, null, picker);
    Check(again[0].Path != items[0].Path && again[0].Path.Contains("Promo"), "the promo rested longest is taken: the other one next time");
    var store = new ScheduleStore(db);
    store.Save(data, null, "checks");
    var loaded = store.Load().Data.Breaks.First(b => b.Name == "Ads");
    Check(loaded.Parts[0].CategoryId == promosId && loaded.Parts[0].Section == "Promos" && loaded.Parts[2].CategoryId == fillersId,
        "a part from a category is kept when the schedule is saved and read again");

    // Ends at its time: it starts its whole length (promo, advert, filler) before.
    var e = NewEngine();
    var log = new List<string>();
    e.Log += m => { lock (log) log.Add(m); };
    e.AddFiles([songs[0], songs[1], songs[2]]);
    Thread.Sleep(300);
    e.Play();
    Wait(() => OnAir(e) == songs[0], 3);
    var endsAt = DateTime.UtcNow.AddSeconds(60);
    e.InsertEvent("Ads", again, new EventTiming(EventStart.EndAt, EndsAt: endsAt));
    Wait(() => { lock (log) return log.Any(l => l.Contains("to end at")); }, 3);
    string line; lock (log) line = log.FirstOrDefault(l => l.Contains("to end at")) ?? "";
    var times = System.Text.RegularExpressions.Regex.Matches(line, @"\d\d:\d\d:\d\d").Select(m => TimeSpan.Parse(m.Value)).ToList();
    double planned = times.Count >= 2 ? (times[^1] - times[^2]).TotalSeconds : -1; // "starts at … to end at …" (after the log's own time)
    Check(planned is >= 9 and <= 13, $"ends at its time: it starts the length of all three before ({planned:0} s for 3 × 4 s, crossfades in){(planned < 0 ? " — " + string.Join(" | ", log) : "")}");
    e.Dispose();
}

// ───────────────────────── Categories ─────────────────────────

Console.WriteLine("Advertising (sectors, longest break, billing)");
{
    var at = DateTime.Today.AddHours(10);
    var ads = Audio.Make(Path.Combine(work, "sector-ads"), 5);
    AdClient Client(string name, string sector, int file) => new()
    {
        Name = name, Sector = sector,
        Campaigns = { new AdCampaign { Name = name + " spring", Section = "Ads", Files = [ads[file]], Placements = [new AdPlacement { Day = at.DayOfWeek, Time = "10:00:00" }] } },
    };
    var adBreak = new AdBreak { Name = "Ads", Times = ["10:00"], Parts = [new BreakPart { Kind = PartKind.Section, Section = "Ads" }] };
    // Booked one car dealer after the other: the bank and the shop go between them.
    var data = new ScheduleData { Breaks = { adBreak }, Clients = { Client("Autos Silva", "Cars", 0), Client("Motor Sul", "cars", 1), Client("Banco Norte", "Banks", 2), Client("Loja", "", 3) } };
    data.Normalize();
    var order = data.InBreak(adBreak, at);
    bool apart = order.Zip(order.Skip(1)).All(p => !p.First.Client.Clashes(p.Second.Client));
    Check(order.Count == 4 && apart, $"two clients of the same sector are kept apart in the break ({string.Join(", ", order.Select(o => o.Client.Name))})");
    // Only two car dealers: both still play (back to back, nothing to put between them).
    var two = new ScheduleData { Breaks = { adBreak }, Clients = { Client("Autos Silva", "Cars", 0), Client("Motor Sul", "Cars", 1) } };
    two.Normalize();
    Check(two.InBreak(adBreak, at).Count == 2, "…when nothing can go between them, neither is left out");
    // Booked with nothing back to back already: kept as booked.
    var asBooked = new ScheduleData { Breaks = { adBreak }, Clients = { Client("Banco Norte", "Banks", 2), Client("Autos Silva", "Cars", 0), Client("Loja", "", 3), Client("Motor Sul", "Cars", 1) } };
    asBooked.Normalize();
    Check(string.Join(",", asBooked.InBreak(adBreak, at).Select(o => o.Client.Name)) == "Banco Norte,Autos Silva,Loja,Motor Sul", "…and an order with no clash stays as booked");
    // The bank's file is missing: the break is ordered without it, the car dealers still apart.
    var missingFile = Path.Combine(work, "gone.wav");
    var gone = new ScheduleData { Breaks = { adBreak }, Clients = { Client("Autos Silva", "Cars", 0), Client("Banco Norte", "Banks", 2), Client("Motor Sul", "Cars", 1), Client("Loja", "", 3) } };
    gone.Clients[1].Campaigns[0].Files = [missingFile];
    gone.Normalize();
    var played = ScheduleItems.For(gone, adBreak, at, null).Select(i => i.Advertiser).ToList();
    Check(played.Count == 3 && played.IndexOf("Loja") == 1, $"…an advert whose file is missing is left out before the order is made ({string.Join(", ", played)})");

    // Kept when saved and read again (schema 18).
    var settings = AppSettings.Load();
    var db = LibraryDb.Open(settings.Library);
    adBreak.MaxSeconds = 90;
    var store = new ScheduleStore(db);
    store.Save(data, null, "checks");
    var loaded = store.Load().Data;
    Check(loaded.Clients.First(c => c.Name == "Motor Sul").Sector == "cars" && loaded.Breaks.First(b => b.Name == "Ads").MaxSeconds == 90,
        "a client's sector and a break's longest length are kept when the schedule is saved");

    // Billing: booked against the as-run log, as the certificates count it.
    AsRunEntry Aired(string file, DateTime start, string client) =>
        new(start, start.AddSeconds(30), "Advert", "Ads 10:00", "Ads 10:00", client, "", Path.GetFileNameWithoutExtension(file), "", "", "", "", "", file);
    var day = DateTime.Today.AddDays(-1);
    var yesterday = new DateTime(day.Year, day.Month, day.Day, 10, 0, 0);
    foreach (var c in data.Clients) c.Campaigns[0].Placements = [new AdPlacement { Date = day, Time = "10:00:00" }];
    var asRun = new List<AsRunEntry> { Aired(ads[0], yesterday.AddSeconds(5), "Autos Silva"), Aired(ads[2], yesterday.AddSeconds(40), "Banco Norte"),
        Aired(ads[4], yesterday.AddHours(3), "Autos Silva") };
    var bill = Billing.Build(data, asRun, day, day, DateTime.Now);
    var silva = bill.Where(b => b.Client == "Autos Silva").ToList();
    Check(silva.Sum(b => b.Aired) == 1 && silva.Sum(b => b.NotBooked) == 1 && silva.Sum(b => b.Seconds) == 60,
        "billing: a client's spot on air, one more on air without being booked, and their time on air");
    Check(bill.First(b => b.Client == "Motor Sul").Missed == 1 && bill.First(b => b.Client == "Banco Norte").Aired == 1,
        "…a booked spot that didn't air is counted as not on air");
    var csv = Billing.ToReport(bill, "Radio", day, day).ToCsv(';');
    Check(csv.Contains("Autos Silva;Cars;") && csv.Contains("Motor Sul;cars;"), "…and goes to the CSV with the client's sector");
}

Console.WriteLine("Categories (the eight fixed ones, and the station's own at the top)");
{
    var settings = AppSettings.Load();
    var db = LibraryDb.Open(settings.Library);
    long own = db.AddCategory("Archive", "other", null, null);
    db = LibraryDb.Open(settings.Library); // opened again: nothing moves it
    var archive = db.Categories().First(c => c.Id == own);
    Check(archive.ParentId == null && !archive.IsFixed, "a top-level category of the station's own stays at the top");
    long promos = db.Categories().First(c => c.Role == "promos").Id;
    Check(db.AddCategory("promos", "jingle", null, null) == promos, "a top-level one named as one of the eight is that one");
    bool refused = false;
    try { archive.Name = "Jingles"; db.UpdateCategory(archive); } catch (InvalidOperationException) { refused = true; }
    Check(refused, "…and an own one can't take one of their names");
    refused = false;
    try { db.DeleteCategory(promos); } catch (InvalidOperationException) { refused = true; }
    db.DeleteCategory(own);
    Check(refused && db.Categories().All(c => c.Id != own), "the eight can't be deleted; the station's own can");
    {
        // Delete a category: what is only in it leaves the library, what is also elsewhere stays there; no file goes.
        long musicId = db.Categories().First(c => c.Role == "music").Id, jingles = db.Categories().First(c => c.Role == "jingles").Id;
        long sweeper = db.AddCategory("Sweeper", "music", null, musicId);
        string a = Path.Combine(work, "sweeper-a.wav"), b = Path.Combine(work, "sweeper-b.wav");
        File.Copy(songs[0], a, true);
        File.Copy(songs[1], b, true);
        LibraryScanner.ScanFile(db, a);
        LibraryScanner.ScanFile(db, b);
        long ta = db.GetByPath(a)!.Id, tb = db.GetByPath(b)!.Id;
        db.SetCategory([ta, tb], sweeper);
        db.SetAlsoIn([tb], jingles, true);
        Check(db.SongsOnlyIn(sweeper).Select(s => s.Id).SequenceEqual([ta]), "a category: the songs only in it are known");
        int removed = db.DeleteCategoryAndSongs(sweeper);
        Check(removed == 1 && db.GetByPath(a) == null && db.Categories().All(c => c.Id != sweeper), "Delete: the category goes, with the song only in it");
        var kept = db.GetByPath(b);
        Check(kept != null && (kept.CategoryId == jingles || kept.AlsoIn.Contains(jingles)), "…the song also in Jingles stays there");
        Check(File.Exists(a) && File.Exists(b), "…and no file is deleted");
    }
    var all = db.Categories();
    var music = (List<long>)typeof(LibraryFill).GetMethod("MusicCategories", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, [all])!;
    Check(music.Contains(all.First(c => c.Role == "music").Id) && !music.Contains(all.First(c => c.Role == "fillers").Id), "fill music: songs, never the Fillers");
}

// ───────────────────────── A time window for soft events ─────────────────────────

Console.WriteLine("A window: a soft event or break may start early, and at most so late");
{
    var traffic = new ScheduledEvent { Name = "Traffic", Start = EventStart.Soft, MaxEarlyMinutes = 5, MaxDelayMinutes = 10 };
    var at = DateTime.Today.AddHours(8).AddMinutes(10);
    Check(traffic.Opens(at) == at.AddMinutes(-5) && traffic.Timing(at, at.AddMinutes(-5)).MaxDelay == TimeSpan.FromMinutes(15),
        "it goes in 5 min before its time, and its latest start is still 10 min after its time");
    Check(new ScheduledEvent { Start = EventStart.Hard, MaxEarlyMinutes = 5 }.Opens(at) == at, "…only a soft one: a hard one is on the second");
    var topOfHour = DateTime.Today.AddHours(10);
    Check(new AdBreak { Start = EventStart.Soft, MaxEarlyMinutes = 5 }.Opens(topOfHour) == topOfHour
          && new AdBreak { Start = EventStart.Soft, MaxEarlyMinutes = 10 }.Opens(topOfHour.AddMinutes(5)) == topOfHour,
        "…never before its own hour starts (a 10:00 break stays in the 10 o'clock hour)");
    // The player stopped: a break that went in early waits for its time instead of going on air after the warning.
    var e = NewEngine();
    e.AddFiles([songs[0]]);
    Thread.Sleep(200);
    e.InsertEvent("Ad break early", [new ScheduledItem(songs[3], TrackKind.Commercial, InBreak: true)],
        new EventTiming(EventStart.Soft, NotBefore: DateTime.UtcNow.AddSeconds(25)));
    Thread.Sleep(14000);
    Check(e.GetStatus().Current == null, "the player stopped: a break that went in early doesn't go on air before its time");
    e.Dispose();
    var upcoming = new UpcomingEvent(traffic.Id, "Traffic 08:10", "", at, at, EventStart.Soft, 60, false, 10, MaxEarlyMinutes: 5);
    Check(upcoming.How == L.F("between {0:HH:mm} and {1:HH:mm}", at.AddMinutes(-5), at.AddMinutes(10)), $"the playlist shows the window ({upcoming.How})");

    var db = LibraryDb.Open(AppSettings.Load().Library);
    var store = new ScheduleStore(db);
    var (data, version) = store.Load();
    traffic.Slots = [new WeekTime { Day = DayOfWeek.Monday, Time = "08:10:00" }];
    data.Events.Add(traffic);
    store.Save(data, version, "checks");
    var (back, v2) = store.Load();
    Check(back.Events.First(e => e.Id == traffic.Id).MaxEarlyMinutes == 5, "kept in the library");
    back.Events.RemoveAll(e => e.Id == traffic.Id);
    store.Save(back, v2, "checks");
}

// ───────────────────────── Seasons ─────────────────────────

Console.WriteLine("Seasons (summer, Christmas: their hour templates instead of the all-year ones)");
{
    var settings = AppSettings.Load();
    var db = LibraryDb.Open(settings.Library);
    var summer = new Season { Name = "Summer", From = "07-01", To = "08-31" };
    var christmas = new Season { Name = "Christmas", From = "12-15", To = "01-06" };
    TemplateLine Line() => new() { Source = LineSource.Folder, Folder = media };
    WeekTime Nine(DayOfWeek d) => new() { Day = d, Time = "09:00:00" };
    var allDays = Enum.GetValues<DayOfWeek>();
    var regular = new PlaylistTemplate { Name = "Mornings", Lines = [Line()], Slots = allDays.Select(Nine).ToList() };
    var sun = new PlaylistTemplate { Name = "Summer mornings", Lines = [Line()], Slots = allDays.Select(Nine).ToList(), SeasonId = summer.Id };
    var xmas = new PlaylistTemplate { Name = "Christmas mornings", Lines = [Line()], Slots = allDays.Select(Nine).ToList(), SeasonId = christmas.Id };
    var data = new ScheduleData { Templates = [regular, sun, xmas], Seasons = [summer, christmas] };
    DateTime At(int month, int day) => new DateTime(2026, month, day, 9, 0, 0);
    Check(data.TemplateAt(At(7, 1)) == sun && data.TemplateAt(At(8, 31)) == sun && data.TemplateAt(At(9, 1)) == regular && data.TemplateAt(At(6, 30)) == regular,
        "the summer template from its first day to its last, the all-year one before and after");
    Check(data.TemplateAt(At(12, 31)) == xmas && data.TemplateAt(At(1, 6)) == xmas && data.TemplateAt(At(1, 7)) == regular && data.TemplateAt(At(12, 14)) == regular,
        "a season over the new year (15 Dec to 6 Jan)");
    Check(data.TemplateAt(At(7, 15).AddHours(1)) == null, "an hour with no template at all: still none");
    var winter = new Season { Name = "Winter", From = "12-01", To = "02-28" };
    var cold = new PlaylistTemplate { Name = "Winter mornings", Lines = [Line()], Slots = allDays.Select(Nine).ToList(), SeasonId = winter.Id };
    var both = new ScheduleData { Templates = [regular, cold, xmas], Seasons = [winter, christmas] };
    Check(both.TemplateAt(At(12, 25)) == xmas && both.TemplateAt(At(2, 1)) == cold, "two seasons on at once: the shorter one (Christmas in winter)");

    var store = new ScheduleStore(db);
    var (before, version) = store.Load();
    var copy = before.Clone();
    copy.Seasons.AddRange(data.Seasons);
    copy.Templates.AddRange(data.Templates);
    store.Save(copy, version, "checks");
    var back = store.Load().Data;
    Check(back.Seasons.Count == before.Seasons.Count + 2 && back.TemplateAt(At(12, 25))?.Id == xmas.Id && back.TemplateAt(At(10, 1))?.Id == regular.Id,
        "seasons and the templates' seasons are kept in the library");
    var (now, v2) = store.Load();
    now.Seasons.RemoveAll(x => x.Id == summer.Id || x.Id == christmas.Id);
    now.Templates.RemoveAll(t => t.Id == regular.Id || t.Id == sun.Id || t.Id == xmas.Id);
    store.Save(now, v2, "checks");
}

// ───────────────────────── The hour mix editor ─────────────────────────

Console.WriteLine("The hour mix editor (an item starting over the end of the one before, this hour only)");
{
    // Three 4-second tones, a 1-second crossfade: each plays 3 s before the next starts.
    MixBlock Block(int n, double start) => new()
    {
        Item = new PlanItem { Path = songs[n], Title = $"Tone {n + 1}", Seconds = 3 }, Path = songs[n], Title = $"Tone {n + 1}",
        Start = start, Seconds = 3, FileLength = 4,
    };
    var blocks = new List<MixBlock> { Block(0, 0), Block(1, 3), Block(2, 6) };
    var mix = new HourMix(blocks, (_, _) => 1);
    Check(mix.Movable(1) && !mix.Movable(0) && Math.Abs(mix.Automatic(1) - 3) < 0.01, "the automatic join: the crossfade before the end");
    mix.Move(1, -1.5);
    Check(Math.Abs(blocks[1].Start - 1.5) < 0.01 && Math.Abs(blocks[2].Start - 4.5) < 0.01, "an item moved earlier, and everything after it with it");
    Check(Math.Abs(blocks[0].Item!.Seconds - 1.5) < 0.01 && Math.Abs(blocks[1].Item!.Overlap - 2.5) < 0.01 && mix.ByHand(1),
        $"…saved as the hour's overlap: the one before plays 1.5 s, the next starts 2.5 s before its end ({blocks[1].Item!.Overlap})");
    mix.Move(1, 10);
    Check(Math.Abs(blocks[1].Start - 3) < 0.01 && blocks[1].Item!.Overlap == 0 && !mix.ByHand(1), "…never later than the automatic join (and then nothing is saved for it)");
    mix.Move(1, -100);
    Check(blocks[0].Item!.Seconds >= 1, "…nor before the start of the one before");
    blocks.Insert(2, new MixBlock { Path = songs[3], Title = "Ad break", Kind = TrackKind.Commercial, Start = 6, Seconds = 3, FileLength = 4 });
    Check(!mix.Movable(2) && !mix.Movable(3), "what the schedule puts in the hour (an ad break) doesn't move, nor what follows it");

    // Opening: lengths written before (out of date) give way to where the playout really starts each item.
    var stale = new List<MixBlock> { Block(0, 0), Block(1, 2.5), Block(2, 5.5) };
    stale[0].Seconds = stale[0].Item!.Seconds = 2.5;
    var staleMix = new HourMix(stale, (_, _) => 1);
    staleMix.Normalize();
    Check(Math.Abs(stale[1].Start - 3) < 0.01 && Math.Abs(stale[2].Start - 6) < 0.01 && !staleMix.ByHand(1), "opened: each item where the playout starts it (an old length put right)");
    // An overlap past the limit (set before it) doesn't jump when nudged; a voice track isn't moved here.
    var far = new List<MixBlock> { new() { Item = new PlanItem { Seconds = 155 }, Path = songs[0], Title = "Song", Seconds = 155, FileLength = 200 },
        new() { Item = new PlanItem { Overlap = 45 }, Path = songs[1], Title = "Next", Start = 155, Seconds = 3, FileLength = 4 } };
    var farMix = new HourMix(far, (_, _) => 1);
    double later = farMix.Move(1, 0.1), back = farMix.Move(1, -0.1), further = farMix.Move(1, -0.1);
    Check(Math.Abs(later - 0.1) < 0.01 && Math.Abs(back + 0.1) < 0.01 && further == 0 && Math.Abs(far[1].Item!.Overlap - 45) < 0.01,
        $"an overlap already past the limit nudges by 0.1 s, back to where it was, not further (no jump: {later:0.00}, {back:0.00}, {further:0.00})");
    far[1].Item!.FollowsPrevious = true;
    Check(!farMix.Movable(1), "a voice track isn't moved in the mix editor (Voice track… does it)");
    // The hour's end leaves out what is optional; listening from inside an overlap starts the earlier item.
    var overAdvert = new List<MixBlock> { Block(0, 0), Block(1, 3) };
    overAdvert[0] = new MixBlock { Item = new PlanItem { Kind = TrackKind.Commercial, Seconds = 3 }, Path = songs[0], Title = "Advert", Kind = TrackKind.Commercial, Seconds = 3, FileLength = 4 };
    Check(!new HourMix(overAdvert, (_, _) => 1).Movable(1), "nothing is moved over a planned advert (on air it never starts over one)");
    var ends = new List<MixBlock> { Block(0, 0), Block(1, 3) };
    ends[1].Item!.Optional = true;
    var endsMix = new HourMix(ends, (_, _) => 1);
    Check(Math.Abs(endsMix.Ends - 3) < 0.01 && endsMix.At(3.5) == 0 && endsMix.At(4.5) == 1, "the hour's end without its optional items; listening in an overlap starts the earlier item");

    // On air: an overlap set in the mix editor only over the item before it in the same hour, never over an advert.
    var song = new Track(Guid.NewGuid(), songs[0], "", "Song", 4) { Tag = "plan:2026-11-02T10" };
    var after = new Track(Guid.NewGuid(), songs[1], "", "Next", 4) { Tag = "plan:2026-11-02T10:opt", Overlap = 2 };
    var advert = new Track(Guid.NewGuid(), songs[2], "", "Advert", 4, TrackKind.Commercial) { InBreak = true };
    var voice = new Track(Guid.NewGuid(), songs[3], "", "Talk", 4, TrackKind.Voice) { Overlap = 2, FollowsPrevious = true };
    Check(after.OverlapOver(song) == 2 && after.OverlapOver(advert) == 0 && after.OverlapOver(song with { Tag = "plan:2026-11-02T11" }) == 0
          && voice.OverlapOver(song) == 2, "on air an hour's overlap counts only over the item before it in that hour (a voice track's always)");

    // The window, with the no-sound engine: it reads the files, an item is moved and played through, and saved.
    var engine = NewEngine();
    engine.CrossfadeSeconds = engine.OtherCrossfadeSeconds = 1;
    var items = new List<MixBlock> { Block(0, 0), Block(1, 3), Block(2, 6) };
    IReadOnlyList<PlanItem>? saved = null;
    var window = new HourMixWindow(SegueHost.For(engine), DateTime.Today.AddHours(9), items, _ => 4, edited => { saved = edited; return Task.FromResult(true); });
    window.Show();
    var timeline = window.FindControl<Playout.App.Controls.HourMixView>("Timeline")!;
    Check(WaitUi(() => timeline.SelectedIndex == 1 && window.FindControl<StackPanel>("MovePanel")!.IsEnabled, 10), "the mix editor opens with the hour on its time line");
    window.FindControl<Button>("Earlier1")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Pump(3);
    Check(Math.Abs(items[1].Start - 2) < 0.01 && Math.Abs(items[1].Item!.Overlap - 2) < 0.01,
        $"−1 s starts it a second earlier ({window.FindControl<TextBlock>("SelectedText")!.Text})");
    window.FindControl<Button>("PlayButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    // Its timer, by hand (headless, a window's timer comes round rarely).
    var mixTick = typeof(HourMixWindow).GetMethod("Tick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
    bool Heard(double at) { mixTick.Invoke(window, null); return timeline.PlayheadAt > at; }
    Check(WaitUi(() => Heard(0.3), 5), "Play from the cursor: heard through the joins (the playhead moves)");
    Check(WaitUi(() => Heard(2.5), 6), $"…into the next item ({timeline.PlayheadAt:0.0} s)");
    window.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Check(WaitUi(() => saved != null, 5) && saved!.Count == 3 && Math.Abs(saved[1].Overlap - 2) < 0.01 && Math.Abs(saved[0].Seconds - 2) < 0.01,
        "Save gives the hour's items with the new overlap");
    Pump(5);
    engine.Dispose();
}

// ───────────────────────── Series ─────────────────────────

Console.WriteLine("Series (a file for each weekday and for a date)");
{
    var settings = AppSettings.Load();
    var db = LibraryDb.Open(settings.Library);
    var monday = DateTime.Today.AddDays(((int)DayOfWeek.Monday - (int)DateTime.Today.DayOfWeek + 7) % 7).AddHours(9);
    var horoscope = new ScheduledEvent
    {
        Name = "Horoscope", Path = songs[3],
        Series =
        [
            new SeriesFile { Day = DayOfWeek.Monday, Path = songs[0] },
            new SeriesFile { Day = DayOfWeek.Tuesday, Path = songs[1] },
            new SeriesFile { Date = monday.Date.AddDays(7), Path = songs[2] },
        ],
    };
    var data = new ScheduleData();
    data.Events.Add(horoscope);
    string? Plays(DateTime at) => ScheduleItems.For(data, horoscope, at, null).FirstOrDefault()?.Path;
    Check(Plays(monday) == songs[0] && Plays(monday.AddDays(1)) == songs[1], "Monday's file on a Monday, Tuesday's on a Tuesday");
    Check(Plays(monday.AddDays(7)) == songs[2], "…the file for a date before its weekday's");
    Check(Plays(monday.AddDays(3)) == songs[3], "…a day with none: the event's own file");
    horoscope.Series.Add(new SeriesFile { Date = monday.Date.AddDays(14) });
    Check(Plays(monday.AddDays(14)) == null, "…a date left empty: nothing that day, not Monday's file");
    horoscope.Series.RemoveAt(3);
    horoscope.Path = "";
    var said = new List<string>();
    Check(ScheduleItems.For(data, horoscope, monday.AddDays(3), said.Add).Count == 0 && said.Any(m => m.Contains("series")), $"…or nothing, and why ({said.FirstOrDefault()})");

    var store = new ScheduleStore(db);
    var (before, version) = store.Load();
    var copy = before.Clone();
    copy.Events.Add(horoscope);
    store.Save(copy, version, "checks");
    var back = store.Load().Data.Events.First(e => e.Id == horoscope.Id);
    Check(back.Series.Count == 3 && back.PathOn(monday.AddDays(7)) == songs[2] && back.PathOn(monday.AddDays(1)) == songs[1],
        "a series is kept in the library (weekdays and dates)");
    var (now, v2) = store.Load();
    now.Events.RemoveAll(e => e.Id == horoscope.Id);
    store.Save(now, v2, "checks");
}

// ───────────────────────── Now playing for the website ─────────────────────────

Console.WriteLine("Now playing for the website (templates, files, the delay, songs kept off it)");
{
    var settings = AppSettings.Load();
    var db = LibraryDb.Open(settings.Library);
    db.Refresh(songs[0]);
    db.Refresh(songs[1]);
    var hidden = db.GetByPath(songs[1])!;
    db.SetAttributes([hidden.Id], new TrackAttributes(NotForWeb: true));
    Check(db.IsNotForWeb(songs[1]) && !db.IsNotForWeb(songs[0]) && db.GetByIds([hidden.Id])[0].NotForWeb, "a song is marked Not on the website in the library");

    var info = new NowPlayingInfo("Rádio Check", "", new NowPlayingItem("", "Só título", "music", 185, null), null, new ShowInfo("Manhãs", "Rita", ""));
    Check(MetadataTemplate.Fill("{artist} - {title} | {presenter}", info) == "Só título | Rita", "a template: an empty artist leaves no separator");
    Check(MetadataTemplate.Fill("<now>{title} &amp; {duration}</now>", info with { Now = info.Now! with { Title = "Rock & Roll" } }, "now.xml") == "<now>Rock &amp; Roll &amp; 3:05</now>",
        "…escaped in an XML file");
    Check(MetadataTemplate.Fill("{artist} - {title}", info with { Now = null }) == "Rádio Check", "…nothing on air: the station's name");
    Check(MetadataTemplate.Fill("♪ {artist} - {title}", info) == "♪ Só título" && MetadataTemplate.Fill("{station} - {artist} - {title}", info) == "Rádio Check - Só título",
        "…an empty field in the middle takes one separator with it");
    var odd = info with { Now = new NowPlayingItem("{title}", "Re:  Two  Spaces -", "music", 0, null) };
    Check(MetadataTemplate.Fill("{artist} / {title}", odd) == "{title} / Re:  Two  Spaces -", "…the song's own text is never changed (braces, spaces, a dash at the end)");
    var mode = info with { Now = new NowPlayingItem("", "Mode:", "music", 0, null), Show = null };
    Check(MetadataTemplate.Fill("{title} - {artist}", mode) == "Mode:" && MetadataTemplate.Fill("Now: {show} | {title}", mode) == "Now: Mode:",
        "…a separator stuck to a word stays (\"Mode:\", \"Now:\")");
    Check(MetadataTemplate.Fill("<a title='{title}'>", info with { Now = info.Now! with { Title = "O'Neil" } }, "a.html") == "<a title='O&#39;Neil'>", "…quotes escaped in HTML");

    string text = Path.Combine(work, "web", "nowplaying.txt");
    string xml = Path.Combine(work, "web", "nowplaying.xml");
    if (Directory.Exists(Path.GetDirectoryName(text))) Directory.Delete(Path.GetDirectoryName(text)!, true);
    var web = new WebNowPlayingSettings
    {
        Enabled = true, DelaySeconds = 2,
        Outputs =
        [
            new MetadataOutput { Template = "{artist} - {title}", Target = text },
            new MetadataOutput { Template = "<song><artist>{artist}</artist><next>{next_title}</next></song>", Target = xml },
        ],
    };
    using var sender = new WebNowPlaying(web, "Rádio Check", db.IsNotForWeb);
    var one = new Track(Guid.NewGuid(), songs[0], "Check 1", "Tone 1", 4);
    var two = new Track(Guid.NewGuid(), songs[1], "Check 2", "Tone 2", 4);
    sender.Update(one, two);
    Thread.Sleep(1000);
    Check(!File.Exists(text), "held back by the delay");
    Check(Wait(() => File.Exists(text) && File.ReadAllText(text) == "Check 1 - Tone 1", 4), "…then written to the file from its template");
    Check(File.Exists(xml) && File.ReadAllText(xml) == "<song><artist>Check 1</artist><next></next></song>", "…a song kept off the website isn't shown as next either");
    sender.Update(two, null);
    Thread.Sleep(3000);
    Check(File.ReadAllText(text) == "Check 1 - Tone 1", "a song kept off the website: the file keeps the last song");
    sender.Update(new Track(Guid.NewGuid(), songs[2], "Check 3", "Tone 3", 4, TrackKind.Jingle), null);
    Thread.Sleep(3000);
    Check(File.ReadAllText(text) == "Check 1 - Tone 1", "Songs only: a jingle leaves it too");
    sender.Update(new Track(Guid.NewGuid(), songs[3], "Check 4", "Tone 4", 4), null);
    Check(Wait(() => File.ReadAllText(text) == "Check 4 - Tone 4", 4), "the next song is written");
    db.SetAttributes([hidden.Id], new TrackAttributes(NotForWeb: false));

    // An FTP server that takes the connection and never answers, and a fallback picture that isn't there: the
    // file beside them is written at once, and the others fail with a reason instead of hanging.
    var silent = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    silent.Start();
    var held = new List<System.Net.Sockets.TcpClient>();
    _ = Task.Run(async () => { try { while (true) held.Add(await silent.AcceptTcpClientAsync()); } catch { } });
    MetadataOutput.FtpTimeout = TimeSpan.FromSeconds(2);
    string beside = Path.Combine(work, "web", "beside.txt");
    var stalled = new WebNowPlayingSettings
    {
        Enabled = true,
        Outputs =
        [
            new MetadataOutput { Target = $"ftp://127.0.0.1:{((System.Net.IPEndPoint)silent.LocalEndpoint).Port}/np.txt" },
            new MetadataOutput { Kind = MetadataOutputKind.Cover, Target = Path.Combine(work, "web", "cover.jpg"), Fallback = Path.Combine(work, "no-logo.png") },
            new MetadataOutput { Template = "{title}", Target = beside },
        ],
    };
    using var stuck = new WebNowPlaying(stalled, "Rádio Check");
    var started = DateTime.UtcNow;
    stuck.Update(new Track(Guid.NewGuid(), songs[2], "Check 3", "Tone 3", 4), null);
    Check(Wait(() => File.Exists(beside), 1.5), "a stalled FTP server holds up no other output");
    var sendNow = stuck.SendNowAsync();
    // Waited for with the UI thread running: the send finishes on it, as when the Send now button awaits it.
    Check(WaitUi(() => sendNow.IsCompleted, 8) && sendNow.Result is { Length: > 0 }, $"…it gives up with a reason ({(sendNow.IsCompleted ? sendNow.Result : "still waiting")})");
    Check(stuck.Error != null, "…and the picture that isn't there is reported, not skipped");
    silent.Stop();
    held.ForEach(c => c.Dispose());
}

// ───────────────────────── The website's Test buttons ─────────────────────────

Console.WriteLine("Station settings → Website: Test buttons (the page, each text and the cover), also in Sintoniza Pro");
{
    string logo = Path.Combine(work, "web-test", "station-logo.png");
    Directory.CreateDirectory(Path.GetDirectoryName(logo)!);
    File.WriteAllBytes(logo, [0x89, 0x50, 0x4E, 0x47, 9, 9]);
    var edited = new AppSettings { StationName = "Rádio Check", LogoPath = logo };
    var sw = new SettingsWindow(edited, null, null, SettingsScope.Station, _ => null);
    var onAir = new Track(Guid.NewGuid(), songs[0], "Check 1", "Tone 1", 4);
    sw.OnAirForTest = () => new WebTestNow(onAir, DateTime.UtcNow.AddSeconds(-30), null, null, Partial: true);
    var testOutput = typeof(SettingsWindow).GetMethod("TestOutput", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
    var testPost = typeof(SettingsWindow).GetMethod("TestPost", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
    string RunOutput(MetadataOutput o)
    {
        var result = new TextBlock();
        var task = (Task)testOutput.Invoke(sw, [o, result, new Button()])!;
        WaitUi(() => task.IsCompleted, 20);
        return result.Text ?? "";
    }
    string textFile = Path.Combine(work, "web-test", "now.txt");
    string said = RunOutput(new MetadataOutput { Kind = MetadataOutputKind.Text, Template = "{artist} - {title}", Target = textFile });
    Check(File.Exists(textFile) && File.ReadAllText(textFile) == "Check 1 - Tone 1" && said.StartsWith("✓"), $"Test on a text: written once with what is on air ({said})");
    said = RunOutput(new MetadataOutput { Kind = MetadataOutputKind.Text, Template = "{started}", Target = textFile, Enabled = false });
    Check(File.ReadAllText(textFile) == DateTime.Now.AddSeconds(-30).ToString("HH:mm") && said.Contains(L.T("(this one is switched off: on air it isn't written)")),
        $"…when it started, not when Test was clicked; a row switched off is written, and it says so ({said})");
    var jingle = new Track(Guid.NewGuid(), songs[1], "", "Jingle Manhãs", 4, TrackKind.Jingle);
    sw.OnAirForTest = () => new WebTestNow(jingle, DateTime.UtcNow, null, null);
    File.WriteAllText(textFile, "before");
    said = RunOutput(new MetadataOutput { Kind = MetadataOutputKind.Text, Template = "{title}", Target = textFile });
    Check(File.ReadAllText(textFile) == "before" && said.StartsWith("Nothing sent"), $"…the rules of the air: with Songs only, a jingle on air isn't written ({said})");
    sw.OnAirForTest = () => new WebTestNow(onAir, DateTime.UtcNow, null, null);
    sw.NotForWebForTest = path => path == songs[0];
    said = RunOutput(new MetadataOutput { Kind = MetadataOutputKind.Text, Template = "{title}", Target = textFile });
    Check(File.ReadAllText(textFile) == "before", $"…nor a song kept off the website ({said})");
    sw.NotForWebForTest = null;
    sw.OnAirForTest = () => null;
    RunOutput(new MetadataOutput { Kind = MetadataOutputKind.Text, Template = "{artist} - {title}", Target = textFile });
    Check(File.ReadAllText(textFile) == "Rádio Check", "…nothing on air: the station's name, never made-up test words on the live website");

    string picture = Path.Combine(work, "web-test", "logo.png");
    byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    File.WriteAllBytes(picture, png);
    string coverFile = Path.Combine(work, "web-test", "cover.png");
    said = RunOutput(new MetadataOutput { Kind = MetadataOutputKind.Cover, Target = coverFile, Fallback = picture });
    Check(File.Exists(coverFile) && File.ReadAllBytes(coverFile).SequenceEqual(png) && said.StartsWith("✓"), $"Test on the cover: a song without a cover sends the picture for those ({said})");
    File.Delete(coverFile);
    said = RunOutput(new MetadataOutput { Kind = MetadataOutputKind.Cover, Target = coverFile });
    Check(!File.Exists(coverFile) && !said.StartsWith("✓"), $"…no cover and no picture: nothing written (not even the logo: on air it isn't), and it says why ({said})");
    Check(RunOutput(new MetadataOutput { Kind = MetadataOutputKind.Text, Target = "" }).Length > 0 && !RunOutput(new MetadataOutput { Kind = MetadataOutputKind.Cover, Target = "" }).StartsWith("✓"),
        "…with no address: asks for one");

    // The page (the PHP): what it got, and when it refuses.
    var port = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    port.Start();
    int free = ((System.Net.IPEndPoint)port.LocalEndpoint).Port;
    port.Stop();
    using var page = new System.Net.HttpListener();
    page.Prefixes.Add($"http://127.0.0.1:{free}/");
    page.Start();
    var got = new List<(string Method, string Path, string Key, string Body)>();
    _ = Task.Run(async () =>
    {
        while (page.IsListening)
        {
            System.Net.HttpListenerContext ctx;
            try { ctx = await page.GetContextAsync(); } catch { break; }
            string body = new StreamReader(ctx.Request.InputStream).ReadToEnd();
            string key = ctx.Request.Headers["X-Api-Key"] ?? "";
            lock (got) got.Add((ctx.Request.HttpMethod, ctx.Request.Url!.PathAndQuery, key, body));
            ctx.Response.StatusCode = key == "secret" ? 200 : 403;
            ctx.Response.Close();
        }
    });
    sw.OnAirForTest = () => new WebTestNow(onAir, DateTime.UtcNow, null, null);
    sw.WebUrlBox.Text = $"http://127.0.0.1:{free}/nowplaying.php";
    sw.WebKeyBox.Text = "secret";
    var post = (Task)testPost.Invoke(sw, [])!;
    WaitUi(() => post.IsCompleted, 20);
    lock (got) Check(got.Count == 1 && got[0].Method == "POST" && got[0].Body.Contains("\"title\":\"Tone 1\"") && sw.WebPostTestText.Text!.StartsWith("✓"),
        $"Test on the page's address: the JSON posted once, with the key ({sw.WebPostTestText.Text})");
    sw.WebKeyBox.Text = "wrong";
    post = (Task)testPost.Invoke(sw, [])!;
    WaitUi(() => post.IsCompleted, 20);
    Check(sw.WebPostTestText.Text!.StartsWith("✗") && sw.WebPostTestText.Text.Contains("403"), $"…a page that refuses it: what it answered ({sw.WebPostTestText.Text})");
    said = RunOutput(new MetadataOutput { Kind = MetadataOutputKind.Cover, Target = $"http://127.0.0.1:{free}/nowplaying.php?file=cover.png", Password = "secret", Fallback = picture });
    lock (got) Check(said.StartsWith("✓") && got[^1].Method == "PUT" && got[^1].Path.EndsWith("file=cover.png") && got[^1].Body.Length > 0, $"…the cover to the PHP page, in a PUT with its key ({said})");
    page.Stop();
    Directory.Delete(Path.Combine(work, "web-test"), true);
}

// ───────────────────────── RDS: RT+ over UECP ─────────────────────────

Console.WriteLine("RDS: RT+ over UECP (as an encoder reads the frames)");
{
    var rds = new RdsSettings { Protocol = RdsProtocol.Uecp, RtPlus = true, RtPlusGroup = 11, SiteAddress = 1, EncoderAddress = 2 };
    var song = new Track(Guid.NewGuid(), songs[0], "Dulce Pontes", "Canção do Mar", 200);
    var (ps, rt) = RdsText.Compose(rds, song, "Rádio Check");
    var tags = RdsText.RtPlusTags(rds, song, rt);
    byte[] bytes = RdsText.Uecp(rds, ps, rt, true, tags, toggle: true);

    // The frames: FE … FF, with FD 00/01/02 standing for FD/FE/FF; address, sequence, length, message, CRC.
    var messages = new List<byte[]>();
    bool crcOk = true;
    for (int i = 0; i < bytes.Length; i++)
    {
        if (bytes[i] != 0xFE) continue;
        var body = new List<byte>();
        for (i++; bytes[i] != 0xFF; i++) body.Add(bytes[i] == 0xFD ? (byte)(0xFD + bytes[++i]) : bytes[i]);
        ushort crc = (ushort)(body[^2] << 8 | body[^1]);
        crcOk &= RdsText.Crc(body.Take(body.Count - 2)) == crc && body[0] == 0x00 && body[1] == 0x42 && body[3] == body.Count - 6;
        messages.Add(body.Skip(4).Take(body[3]).ToArray());
    }
    Check(crcOk && messages.Count == 6, $"PS, RT and four RT+ messages, each in a frame with its address and a good CRC ({messages.Count})");
    var config = messages.Where(m => m[0] == 0x40).ToList();
    Check(config.Count == 2 && config[0].SequenceEqual(new byte[] { 0x40, 0x16, 0x4B, 0xD7, 0x03, 0, 0, 0 }) && config[1].SequenceEqual(new byte[] { 0x40, 0x16, 0x4B, 0xD7, 0x02, 0, 0, 3 }),
        "the RT+ application (AID 4BD7) announced in group 11A, replacing what the encoder had (silent for 3 minutes: the encoder says so)");
    Check(RdsText.RtPlusUecp(rds, tags, false, announce: false).All(m => m[0] == 0x42), "…announced once per connection: after that, only the tags");
    var group = messages.Last(m => m[0] == 0x42);
    ulong bits = (ulong)(group[3] & 0x1F) << 32 | (ulong)group[4] << 24 | (ulong)group[5] << 16 | (ulong)group[6] << 8 | group[7];
    int Field(int from, int width) => (int)(bits >> (37 - from - width) & ((1UL << width) - 1));
    string Tagged(int start, int len) => rt.Substring(start, len + 1);
    Check(group[1] == 0x16 && group[2] == 0x02 && Field(0, 1) == 1 && Field(1, 1) == 1, "the tags in a free-format group 11A, item toggle and item running set");
    Check(Field(2, 6) == RdsText.RtPlusTitle && Tagged(Field(8, 6), Field(14, 6)) == "Canção do Mar"
          && Field(20, 6) == RdsText.RtPlusArtist && Tagged(Field(26, 6), Field(32, 5)) == "Dulce Pontes",
        $"…which mark the title and the artist in the radio text ({rt})");

    // The item toggle changes with each new song, even when its tags fall in the same places as the last one's.
    using var output = new RdsOutput(rds.Clone(), "Rádio Check");
    bool Toggle() => (bool)typeof(RdsOutput).GetField("_toggle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(output)!;
    output.Update(song);
    bool first = Toggle();
    var next = new Track(Guid.NewGuid(), songs[1], "Dulce Pontez", "Canção do Mal", 200); // the same lengths
    output.Update(next);
    bool second = Toggle();
    output.Update(next, new ShowInfo("Manhãs", "Rita", "")); // the same song, the show's name changed
    Check(second != first && Toggle() == second, $"RT+ item toggle: changed for a new song with equally long names, not for the same song ({first}, {second}, {Toggle()})");
}

// ───────────────────────── Release notes ─────────────────────────

Console.WriteLine("Release notes in the update window");
{
    const string body = "## What's new\n- **Website**: files by FTP\n- See [the manual](https://example.org)\n\n**Full Changelog**: https://github.com/x/y/compare/v1...v2\n\n"
        + "<!-- downloads -->\n## Downloads\n\n<table>\n<tr><td>Windows</td><td><a href=\"https://x\">setup.exe</a></td></tr>\n</table>\n<!-- /downloads -->";
    string plain = Updates.PlainNotes(body);
    Check(plain == "What's new\n• Website: files by FTP\n• See the manual", $"plain text: no downloads table, links or Markdown ({plain.Replace("\n", " | ")})");
    Check(Updates.PlainNotes("**Full Changelog**: https://x\n\n<!-- downloads -->\n<table></table>\n<!-- /downloads -->") == "", "…nothing else: empty (\"No release notes.\")");
    string text = Updates.PlainNotes("- Mixer: level < 0 dB now warns\n- Website: \\\"Send now\\\" fixed\n- Cart -> next; List<string> kept");
    Check(text == "• Mixer: level < 0 dB now warns\n• Website: \"Send now\" fixed\n• Cart -> next; List<string> kept", $"…a < or > in a note is text, not a tag ({text.Replace("\n", " | ")})");
}

// ───────────────────────── Listener counts ─────────────────────────

Console.WriteLine("Listener counts (Icecast and Shoutcast statistics, every minute, hour by hour)");
{
    const string one = """{"icestats":{"admin":"x","source":{"listenurl":"http://s.example:8000/live","listeners":12,"title":"A \"B\""}}}""";
    const string two = """{"icestats":{"source":[{"listenurl":"http://s.example:8000/live","listeners":12},{"listenurl":"http://s.example:8000/live.aac","listeners":"5"},{"listenurl":"http://s.example:8000/other","listeners":40}]}}""";
    Check(ListenerStats.Parse(one, ListenerStats.MountList("", "/live"))?.Count == 12, "Icecast: the stream's mount point (one source: an object, not a list)");
    Check(ListenerStats.Parse(two, ListenerStats.MountList("/live, live.aac", "/live"))?.Count == 17 && ListenerStats.Parse(two, ListenerStats.MountList("*", "/live"))?.Count == 57,
        "…several mount points added up (\"live.aac\" without the slash too), * counts them all, another station's mount isn't counted");
    var gone = ListenerStats.Parse(two, ListenerStats.MountList("", "/gone"));
    Check(gone is { Count: null } && gone.Problem!.Contains("/live.aac") && ListenerStats.Parse("""{"icestats":{"admin":"x"}}""", ["/live"])?.Count == 0,
        $"…a mount point the server doesn't have: no count, and what it has (not 0 for ever: {gone?.Problem}); nothing on air: 0");
    Check(ListenerStats.Parse("""{"icestats":{"source":{"listenurl":"http://h:8000/r%C3%A1dio","listeners":3}}}""", ListenerStats.MountList("/Rádio", ""))?.Count == 3,
        "…a mount point with an accent, or in other capitals");
    const string several = """{"currentlisteners":30,"streams":[{"id":1,"streampath":"/stream","currentlisteners":10},{"id":2,"streampath":"/two","currentlisteners":20}]}""";
    Check(ListenerStats.Parse(several, ListenerStats.MountList("/two", ""))?.Count == 20 && ListenerStats.Parse(several, ListenerStats.MountList("*", ""))?.Count == 30,
        "Shoutcast 2 with several stations on the server: only this one's stream");
    Check(ListenerStats.Parse("""{"totalstreams":1,"currentlisteners":33,"peaklisteners":50}""", [])?.Count == 33
          && ListenerStats.Parse("<HTML><meta http-equiv=\"Pragma\"><body>21,1,44,500,19,128,Song</body></html>", [])?.Count == 21,
        "Shoutcast 2 (statistics?json=1) and Shoutcast 1 (7.html)");
    Check(ListenerStats.Parse("<html>404</html>", []) == null && ListenerStats.Parse("{broken", []) == null, "…a page that isn't statistics: no count (not 0)");
    var pages = ListenerStats.Pages("stream.example:8000", shoutcast: false);
    Check(pages.Count == 3 && pages[0].ToString() == "http://stream.example:8000/status-json.xsl" && ListenerStats.Pages("stream.example:8000", true)[0].AbsolutePath == "/statistics",
        "the server's address: Icecast's page first (Shoutcast's first for a Shoutcast stream), the others if it doesn't answer");
    Check(ListenerStats.Pages("https://host/stats?sid=1&json=1", false) is [var only] && only.Query == "?sid=1&json=1" && ListenerStats.Pages("", false).Count == 0,
        "…a statistics page of its own: only that one");
    var pasted = ListenerStats.Pages("http://stream.example:8000/stream", false);
    Check(pasted.Count == 6 && pasted[0].AbsolutePath == "/stream/status-json.xsl" && pasted[3].AbsolutePath == "/status-json.xsl" && ListenerStats.MountIn("http://stream.example:8000/stream") == "/stream"
          && ListenerStats.MountIn("stream.example:8000") == null,
        "…the stream's address pasted (…:8000/stream): the server's pages (under it, then at the root), counting that mount point");

    // A server that answers, and the counts kept in the library.
    var port = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    port.Start();
    int free = ((System.Net.IPEndPoint)port.LocalEndpoint).Port;
    port.Stop();
    using var server = new System.Net.HttpListener();
    server.Prefixes.Add($"http://127.0.0.1:{free}/");
    server.Start();
    var asked = new List<string>();
    _ = Task.Run(async () =>
    {
        while (server.IsListening)
        {
            System.Net.HttpListenerContext ctx;
            try { ctx = await server.GetContextAsync(); } catch { break; }
            lock (asked) asked.Add(ctx.Request.Url!.PathAndQuery);
            // An Icecast server: no Shoutcast statistics.
            string path = ctx.Request.Url!.AbsolutePath;
            byte[] body = System.Text.Encoding.UTF8.GetBytes(path == "/status-json.xsl" ? two : path == "/audio/7.html" ? "ID3 not statistics" : "not here");
            ctx.Response.StatusCode = path is "/status-json.xsl" or "/audio/7.html" ? 200 : 404;
            if (path == "/audio/7.html") ctx.Response.ContentType = "audio/mpeg";
            ctx.Response.OutputStream.Write(body);
            ctx.Response.Close();
        }
    });
    var settings = AppSettings.Load();
    var db = LibraryDb.Open(settings.Library);
    long station = CurrentStation.Id;
    using (var counter = new ListenerCounter(new ListenerSettings { Enabled = true, Mounts = "/live,/live.aac" }, "127.0.0.1", free, "/live", shoutcast: true, () => db))
    {
        Check(Wait(() => counter.Count != null, 10) && counter.Count == 17 && counter.Error == null,
            $"the computer on air reads the listeners from the stream server ({counter.Count}, {counter.Error ?? "ok"})");
        lock (asked) Check(asked.Count == 3 && asked[0].StartsWith("/statistics") && asked[1] == "/7.html" && asked[2] == "/status-json.xsl", $"…a Shoutcast stream on an Icecast server: Shoutcast's pages, then Icecast's ({string.Join(", ", asked)})");
    }
    var now = ListenersDb.Now(db, DateTime.Now);
    Check(now is { Listeners: 17 } && ListenersDb.Read(db, DateTime.Today, DateTime.Today).Any(l => l.Listeners == 17), "…and keeps the count in the library, for Sintoniza Pro");
    Check(ListenersDb.Now(db, DateTime.Now, station + 1) == null, "…for its own station only");
    using (var down = new ListenerCounter(new ListenerSettings { Enabled = true, Server = $"http://127.0.0.1:{free}/nothing" }, "", 0, "/live", false, () => db))
        Check(Wait(() => down.Error != null, 10) && down.Count == null, $"a server that doesn't give statistics: an error, no count ({down.Error})");
    using (var pastedMount = new ListenerCounter(new ListenerSettings { Enabled = true, Server = $"127.0.0.1:{free}/live.aac" }, "", 0, "/live", false, () => null))
        Check(Wait(() => pastedMount.Count != null, 10) && pastedMount.Count == 5, $"the stream's address in Statistics from: that mount point counted ({pastedMount.Count}, {pastedMount.Error ?? "ok"})");
    using (var audio = new ListenerCounter(new ListenerSettings { Enabled = true, Server = $"http://127.0.0.1:{free}/audio/7.html" }, "", 0, "/live", false, () => null))
        Check(Wait(() => audio.Error != null, 10) && audio.Count == null && audio.Error!.Contains("http://host:port"), $"…audio where statistics should be: not read, and said so ({audio.Error})");
    server.Stop();

    // A day of counts: hour by hour, and the listeners when something started.
    var day = DateTime.Today.AddDays(-3);
    foreach (var (m, n) in new[] { (8 * 60 + 0, 10), (8 * 60 + 1, 20), (8 * 60 + 59, 30), (9 * 60 + 30, 5) }) ListenersDb.Add(db, station, day.AddMinutes(m), n);
    ListenersDb.Add(db, station, day.AddMinutes(8 * 60 + 1), 24); // the same minute again: replaced
    var samples = ListenersDb.Read(db, day, day);
    var hours = ListenersDb.Hours(samples);
    Check(samples.Count == 4 && hours.Count == 2 && hours[0] is { Peak: 30, Low: 10, Counts: 3 } && Math.Abs(hours[0].Average - 64 / 3.0) < 0.01 && hours[1].Peak == 5,
        $"hour by hour: the average, the most and the fewest ({string.Join("; ", hours.Select(h => $"{h.Hour:HH} {h.Average:0.0}/{h.Peak}/{h.Low}"))})");
    Check(ListenersDb.At(samples, day.AddHours(8).AddSeconds(90)) == 24 && ListenersDb.At(samples, day.AddHours(9).AddMinutes(34)) == 5
          && ListenersDb.At(samples, day.AddHours(9).AddMinutes(40)) == null && ListenersDb.At(samples, day.AddHours(7)) == null,
        "the listeners when something started: its minute's count, else the one before (up to 5 minutes)");
    var entries = new List<AsRunEntry> { new(day.AddHours(8).AddMinutes(1).AddSeconds(30), null, "Music", "playlist", "", "", "Check 1", "Tone 1", "", "", "", "", "", songs[0]) };
    var everything = Report.Build(ReportKind.Everything, entries, day, day, "Rádio Check", listeners: samples);
    var perHour = Report.Build(ReportKind.Listeners, entries, day, day, "Rádio Check", listeners: samples);
    Check(everything.Columns[^1] == L.T("Listeners") && everything.Rows[0][^1] == "24" && Report.Build(ReportKind.Everything, entries, day, day, "x").Columns.Count == 16,
        "the as-run report: the listeners beside each item (no column without counts)");
    Check(perHour.Rows.Count == 2 && perHour.Rows[0][1] == "08:00" && perHour.Rows[0][3] == "30", "the listeners report: one row per hour");

    // Sintoniza Pro: the day's chart and what played in an hour.
    AsRunDb.Add(db, entries);
    var view = new ListenersView(db);
    var holder = new Window { Content = view, Width = 900, Height = 600 };
    holder.Show();
    view.DayBox.SelectedDate = day;
    Check(WaitUi(() => view.Chart.Children.OfType<Panel>().Count(c => c.Children.Count > 0) == 2, 5), "Sintoniza Pro → Listeners: a bar for each hour counted");
    view.ShowHour(8);
    Check(view.PlayedList.ItemsSource is IEnumerable<object> rows && rows.Cast<string>().Any(r => r.Contains("24") && r.Contains("Check 1 - Tone 1")),
        "…and for an hour, what played with the listeners when it started");
    holder.Close();
}

// ───────────────────────── Several stations in one library ─────────────────────────

Console.WriteLine("Stations");
{
    var settings = AppSettings.Load();
    Check(CurrentStation.Id == settings.Station, "the station is this computer's setting");
    var db = LibraryDb.Open(settings.Library);
    Thread.Sleep(1100); // the plays of the checks above are before this (times are to the second)
    var since = DateTime.UtcNow;
    Thread.Sleep(1100);
    db.RecordPlay(songs[0], "Check 1", "Tone 1", DateTime.UtcNow, "playlist");
    long first = CurrentStation.Id;
    CurrentStation.Id = first + 1;
    Check(db.PlaysSince(since).Count == 0 && db.LastStarted(songs[0]) == null, "another station doesn't count this station's plays");
    db.RecordPlay(songs[1], "Check 2", "Tone 2", DateTime.UtcNow, "playlist");
    CurrentStation.Id = first;
    Check(db.PlaysSince(since).Count == 1 && db.LastStarted(songs[0]) != null, $"…and this station only its own ({db.PlaysSince(since).Count} plays since, last {db.LastStarted(songs[0])})");

    // A second station: made with a copy of the first one's settings under its own name.
    var stationStore = new StationStore(db);
    var mine = stationStore.Load().Settings;
    long second = stationStore.AddStation("Rádio Dois");
    var list = db.Stations();
    Check(list.Count >= 2 && list.Any(st => st.Id == second && st.Name == "Rádio Dois"), "a new station in the library's list");
    CurrentStation.Id = second;
    var copied = stationStore.Load().Settings;
    Check(mine == null || copied?.StationName == "Rádio Dois" && copied.StreamHost == "" && copied.StreamPassword == "" && !copied.WebNowPlaying.Enabled,
        "…with a copy of the first one's settings, under its own name, and nothing going out (stream, website) until set for it");
    var source = new ScheduleSource(() => db);
    long? emptyVersion = null;
    string? openError = null;
    try { emptyVersion = source.Version(); } catch (Exception ex) { openError = ex.Message; }
    Check(openError == null && emptyVersion == 0 && ScheduleSource.LoadLocal().Events.Count == 0,
        $"…it opens empty: no other station's schedule imported into it ({openError ?? "ok"})");
    Check(db.AddStation("Rádio Dois") is var third && db.Stations().First(st => st.Id == third).Name == "Rádio Dois (2)", "…a name already taken gets a number");
    CurrentStation.Id = first;

    // Rights per station: a user only for the second one may not work on the first.
    var users = new UserStore(db);
    long id = users.Save(new SintonizaUser { Name = "only-two", Permissions = Permission.Presenter.ToHashSet(), Stations = [second] }, "pw");
    var back = users.All().First(u => u.Id == id);
    Check(back.Stations.SetEquals([second]) && back.MayUse(second) && !back.MayUse(first) && users.All().First(u => u.Name == "admin").MayUse(first),
        "a user limited to some stations (the others, everywhere)");
    users.Delete(id);

    // Sintoniza Pro on another station than this computer's on-air program: each keeps its own when saving.
    var emiCopy = AppSettings.Load(); // the on-air program's settings, read before Pro chose its station
    AppSettings.ForPro = true;
    AppSettings.Change(x => x.ProStation = second);
    AppSettings.ForPro = false;
    emiCopy.LastUser = "on air " + Guid.NewGuid().ToString("N"); // the on-air program saves (as it does every few seconds)
    emiCopy.Save();
    var computer = AppSettings.Load();
    Check(computer.ProStation == second && CurrentStation.Id == first, $"Sintoniza Pro's station stays when the on-air program saves; this computer's stays the on-air one's ({computer.ProStation}, {CurrentStation.Id})");
    Check(AppSettings.StationFile("as-run").EndsWith("as-run") && (CurrentStation.Id = second) == second && AppSettings.StationFile("as-run").EndsWith("as-run-" + second),
        "the as-run log and local copies are each station's own (as-run, as-run-2…)");
    CurrentStation.Id = first;
    computer.ProStation = 0;
    computer.Save();
    CurrentStation.Id = first;
}

Console.WriteLine(fails == 0 ? $"All {passes} checks passed." : $"{fails} of {passes + fails} checks FAILED.");
Environment.Exit(fails == 0 ? 0 : 1);

/// <summary>A serial port's lines, for the GPIO checks: the outputs as last set.</summary>
sealed class TestPins : IGpioPins
{
    public bool DtrOn, RtsOn;
    public bool Cts => false;
    public bool Dsr => false;
    public bool Dcd => false;
    public bool Dtr { set => DtrOn = value; }
    public bool Rts { set => RtsOn = value; }
    public void Dispose() { }
}

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
