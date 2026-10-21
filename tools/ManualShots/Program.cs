// Takes the user manual's screenshots: makes a demo station (its own settings folder, an SQLite library, generated
// audio), opens every window headless, saves each as a PNG and writes where the controls the manual points at are
// (callouts.json). Run by docs/manual/make-manual.sh, never on a station's own settings:
//   XDG_CONFIG_HOME=<work>/config dotnet run --project tools/ManualShots -- <work> <out> [en|pt|fr] --device=0
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Playout.App;
using Playout.Engine;
using Playout.Library;

string work = Path.GetFullPath(args[0]), outDir = Path.GetFullPath(args[1]);
string lang = args.Length > 2 && !args[2].StartsWith("--") ? args[2] : "en";
Directory.CreateDirectory(outDir);
// The demo station's names in the manual's language.
bool pt = lang == "pt";
string T(string en, string inPortuguese) => pt ? inPortuguese : en;
const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
var callouts = new Dictionary<string, Dictionary<string, double[]>>();

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
void Pump(int n = 10) { for (int i = 0; i < n; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(40); } }
bool WaitFor(Func<bool> ok, double seconds) { var until = DateTime.Now.AddSeconds(seconds); while (DateTime.Now < until) { if (ok()) return true; Pump(2); } return ok(); }
void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
T Field<T>(object o, string name) => (T)o.GetType().GetField(name, Any)!.GetValue(o)!;

/// <summary>Saves a window as a PNG and where the named controls are in it (x, y, width, height).</summary>
void Shot(Window w, string name, params string[] controls)
{
    // The on-air screen's once-a-second update (the headless timer fires only now and then).
    if (w is MainWindow mw) for (int i = 0; i < 35; i++) mw.GetType().GetMethod("Tick", Any)!.Invoke(mw, null);
    Pump(15);
    w.CaptureRenderedFrame()!.Save(Path.Combine(outDir, name + ".png"));
    var rects = new Dictionary<string, double[]>();
    var all = w.GetVisualDescendants().OfType<Control>().Where(c => c.Name != null && c.IsEffectivelyVisible).ToList();
    foreach (var c in controls)
    {
        if (all.FirstOrDefault(x => x.Name == c) is not { } control || control.TranslatePoint(new Point(0, 0), w) is not { } p) continue;
        rects[c] = [p.X, p.Y, control.Bounds.Width, control.Bounds.Height, w.Bounds.Width, w.Bounds.Height];
    }
    callouts[name] = rects;
    Console.WriteLine($"  {name}.png ({rects.Count} callouts)");
}

// ───────────────────────── The demo station ─────────────────────────

string media = Path.Combine(work, "media");
string music = Path.Combine(media, T("Music", "Música")), jinglesDir = Path.Combine(media, "Jingles"), adsDir = Path.Combine(media, T("Adverts", "Publicidade")), newsDir = Path.Combine(media, T("News", "Noticiário"));
string[] artists = ["Luna Vale", "The Harbour Lights", "Sofia Mar", "Kairo", "Ana Brisa", "Northern Echo", "Mila Reis", "Blue Avenue", "Tiago Sol", "Paper Kites Club", "Velvet Coast", "Rui Navarro"];
string[] titles = ["Golden Hour", "Midnight Drive", "Lisboa ao Sol", "Neon Streets", "Ventos do Sul", "Satellites", "Summer Lights", "Running Home", "Ocean Avenue", "Dancing in Blue", "Stay a While", "City of Lights",
    "Wildflower", "Electric Heart", "Mar Aberto", "Late Night Radio", "Paper Planes", "Heatwave", "Silver Lining", "Coração Leve", "Afterglow", "Northern Sky", "Slow Motion", "Fireworks",
    "Gravity", "Highway Song", "Sunday Morning", "Lighthouse", "Starlight", "Rio Abaixo", "Polaroid", "Velvet Night", "Crystal Clear", "Tempo Novo", "Overdrive", "Last Summer"];
string[] years = ["2024", "1998", "2023", "1987", "2025", "1994"];

void Audio(string file, double seconds, string title, string artist, string year, bool song)
{
    if (File.Exists(file)) return;
    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
    // Music-like loudness: pink noise with a slow swell, so the waveforms look like songs.
    string filter = song ? "volume='0.35+0.65*abs(sin(t/9))*(0.6+0.4*abs(sin(t*1.7)))':eval=frame" : "volume=0.8";
    var p = Process.Start(new ProcessStartInfo("ffmpeg", ["-loglevel", "error", "-y", "-f", "lavfi", "-i", $"anoisesrc=d={seconds}:c=pink:a=0.5",
        "-af", filter, "-ac", "1", "-b:a", "48k", "-metadata", $"title={title}", "-metadata", $"artist={artist}", "-metadata", $"date={year}", file])
        { RedirectStandardError = true })!;
    p.WaitForExit();
}
Console.WriteLine("Demo audio…");
string[] folders = [T("Current", "Atuais"), "Gold", T("80s", "Anos 80")];
for (int i = 0; i < titles.Length; i++)
{
    string folder = folders[i % 3];
    Audio(Path.Combine(music, folder, $"{artists[i % artists.Length]} - {titles[i]}.mp3"), 150 + i * 47 % 110, titles[i], artists[i % artists.Length],
        i % 3 == 2 ? "198" + (i % 10) : i % 3 == 1 ? (1992 + i % 14).ToString() : (2023 + i % 3).ToString(), true);
}
string[] jingles = pt
    ? ["ID da estação", "Hora certa", "Mais música", "Meteorologia", "Abertura do noticiário", "Entrada da publicidade", "Saída da publicidade", "Fundo do trânsito"]
    : ["Station ID", "Top of the hour", "More music", "Weather sweeper", "News intro", "Ad break in", "Ad break out", "Traffic bed"];
foreach (var j in jingles) Audio(Path.Combine(jinglesDir, $"{j}.mp3"), j == jingles[^1] ? 30 : 6, j, "Rádio Exemplo", "2025", false);
string[] adverts = ["Padaria Central - Pão quente", "Auto Sul - Revisão", "Café Lisboa - Pequeno-almoço", "Óptica Visão - Promoção"];
foreach (var a in adverts) Audio(Path.Combine(adsDir, $"{a}.mp3"), 30, a, "", "2025", false);
Audio(Path.Combine(newsDir, T("News 09h.mp3", "Noticiário 09h.mp3")), 120, T("News 09h", "Noticiário 09h"), "Redação", "2025", false);
// New music not in the library yet, for Sintoniza Import
string incoming = Path.Combine(media, T("New releases", "Novidades"));
string[] genres = ["Pop", "Dance", T("Portuguese", "Portuguesa")];
string[] newArtists = ["Marta Lume", "Echo Park Radio", "Duarte Vale", "Night Swim", "Inês Prado", "Coastline"];
string[] newTitles = ["Open Road", "Glow", "Saudade Nova", "Midnight Pool", "Sol de Inverno", "Riptide", "Better Days", "Lanterns", "Fado Moderno"];
for (int i = 0; i < newTitles.Length; i++)
    Audio(Path.Combine(incoming, genres[i % 3], $"{newArtists[i % newArtists.Length]} - {newTitles[i]}.mp3"), 170 + i * 13, newTitles[i], newArtists[i % newArtists.Length], "2025", true);

var settings = AppSettings.Load();
settings.Language = lang;
settings.StationName = "Rádio Exemplo 99.9";
settings.StationGenre = T("Pop / Hits", "Pop / Êxitos");
settings.StationUrl = "https://www.radioexemplo.pt";
settings.AutoAdvance = true;
settings.Crossfade = 3;
settings.Library = new LibrarySettings { Folders = [music, jinglesDir, adsDir] };
settings.MusicFolder = media;
settings.PlaylistItems = new();
settings.WasPlaying = false;
settings.Cartwalls = [new CartwallPage { Name = T("Morning show", "Manhãs"), Carts = jingles.Select((j, i) => new CartItem
{
    Path = Path.Combine(jinglesDir, $"{j}.mp3"),
    Color = new[] { "#3E8EDE", "#2FA37A", "#D6A21E", "#7B5CC4", "#B4232A", "#0097A7" }[i % 6],
    Icon = new[] { "jingle", "clock", "music", "weather", "news", "commercial", "commercial", "bolt" }[i],
}).Concat([new CartItem { Path = Path.Combine(adsDir, $"{adverts[0]}.mp3"), Color = "#C2185B", Icon = "heart" }]).ToList() }];
settings.ActiveCartwall = T("Morning show", "Manhãs");
// A logo: the app's own icon
string logo = Path.Combine(work, "logo.png");
if (!File.Exists(logo)) using (var s = File.Create(logo)) new Avalonia.Media.Imaging.Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri("avares://SintonizaEmi/Assets/icon.ico"))).Save(s);
settings.LogoPath = logo;
// Takes: the hours, some minutes and temperatures (silent: only their names matter here)
string takesDir = Path.Combine(media, "Takes");
for (int h = 0; h < 24; h++) Audio(Path.Combine(takesDir, $"HRS{h:00}.mp3"), 1.5, "", "", "", false);
for (int m = 1; m < 60; m++) Audio(Path.Combine(takesDir, $"MIN{m:00}.mp3"), 1, "", "", "", false);
for (int t = 0; t <= 35; t++) Audio(Path.Combine(takesDir, $"TMP{t}.mp3"), 1, "", "", "", false);
settings.Takes = new TakesSettings(takesDir, 38.7223, -9.1393);
settings.Save();
Localization.Load(lang);

Console.WriteLine("Library…");
var db = LibraryDb.Open(settings.Library);
if (db.Stats().Tracks == 0)
{
    var plan = FolderImport.Plan(db, music, "Music", null, 1); // into the station's Music category
    FolderImport.Run(db, plan, null, "music", "#3E8EDE", FolderAssign.AlsoIn);
    LibraryScanner.Scan(db, [jinglesDir, adsDir]);
    var cats0 = db.Categories();
    db.SetFolderCategory(jinglesDir, cats0.First(c => c.Name == "Jingles").Id);
    db.SetFolderRule(jinglesDir, cats0.First(c => c.Name == "Jingles").Id);
    db.SetFolderCategory(adsDir, cats0.First(c => c.Role == "spots").Id);
    db.SetFolderRule(adsDir, cats0.First(c => c.Role == "spots").Id);
}
// Tempo and energy on the songs (the music rules use them), the same every time the manual is made.
var songs = db.Search("", CategoryFilter.All, 1000).Tracks.Where(t => t.Path.StartsWith(music)).ToList();
if (songs.All(t => t.Tempo == null))
    foreach (var t in songs)
    {
        int n = t.Title.Sum(ch => ch);
        db.SetAttributes([t.Id], new TrackAttributes(Tempo: 1 + n % 5, Energy: 1 + n / 5 % 5));
    }
var cats = db.Categories();
long Cat(string path) => cats.First(c => CategoryTree.PathOf(cats, c.Id) == path).Id;
TemplateLine Line(string path, string kind = "music") => new() { Source = LineSource.Category, CategoryId = Cat(path), CategoryName = path, Kind = kind };

Console.WriteLine("Schedule…");
var weekdays = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
List<WeekTime> Hours(IEnumerable<DayOfWeek> days, int from, int to) => days.SelectMany(d => Enumerable.Range(from, to - from).Select(h => new WeekTime { Day = d, Time = $"{h:00}:00:00" })).ToList();
string current = T("Music › Current", "Music › Atuais"), gold = "Music › Gold", eighties = T("Music › 80s", "Music › Anos 80");
var morning = new PlaylistTemplate { Name = T("Morning show", "Manhãs"), Slots = Hours(weekdays, 7, 10), ArtistHours = 2, MaxSlowInRow = 2, Lines =
    [Line(current), Line(gold), Line("Jingles", "jingle"), Line(current), Line(eighties), new TemplateLine { Source = LineSource.Take, Folder = Takes.PathFor(TakeKind.TimeAndTemperature), Kind = "jingle" }, Line("Jingles", "jingle")] };
morning.Lines[0].TempoMin = 3; // the morning starts lively
morning.Lines[0].EnergyMin = 3;
var afternoons = new PlaylistTemplate { Name = T("Afternoons", "Tardes"), Slots = Hours(weekdays, 14, 19), Lines =
    [Line(current), Line(current), Line("Jingles", "jingle"), Line(gold)] };
var allDays = Enum.GetValues<DayOfWeek>();
var taken = morning.Slots.Concat(afternoons.Slots).Select(s => (s.Day, s.At)).ToHashSet();
var nonstop = new PlaylistTemplate { Name = T("Non-stop hits", "Êxitos sem parar"), Slots = Hours(allDays, 0, 24).Where(s => !taken.Contains((s.Day, s.At))).ToList(), Lines =
    [Line(current), Line(gold), Line(eighties), Line("Jingles", "jingle")] };
var adBreak = new AdBreak { Name = T("Ad break", "Bloco publicitário"), Times = ["*:50"], Start = EventStart.Soft, MaxDelayMinutes = 5, Parts =
[
    new BreakPart { Kind = PartKind.Jingle, Path = Path.Combine(jinglesDir, jingles[5] + ".mp3") },
    new BreakPart { Kind = PartKind.Section, Section = T("Adverts", "Anúncios") },
    new BreakPart { Kind = PartKind.Jingle, Path = Path.Combine(jinglesDir, jingles[6] + ".mp3") },
] };
var news = new ScheduledEvent { Name = T("News", "Noticiário"), Path = newsDir, Times = ["09:00", "13:00", "18:00"], Start = EventStart.Hard };
// Another station's stream put in today's playlist, once (Daily playlists → Stream…).
var relay = new ScheduledEvent { Name = T("Network show", "Programa em rede"), Path = "https://stream.example.org/live.mp3", StreamMinutes = 15,
    Start = EventStart.Hard, Dates = [DateTime.Today.AddHours(DateTime.Now.Hour).AddMinutes(40)] };
var data = new ScheduleData { Templates = [morning, afternoons, nonstop], Breaks = [adBreak], Events = [news, relay] };
data.Normalize();
AdClient Client(string name, params string[] files)
{
    var k = new AdCampaign { Name = name.Split(" - ")[0] + " 2025", Section = T("Adverts", "Anúncios"), Files = files.Select(f => Path.Combine(adsDir, f + ".mp3")).ToList(),
        From = DateTime.Today.AddDays(-10), To = DateTime.Today.AddDays(30) };
    foreach (var s in adBreak.Slots) k.Placements.Add(new AdPlacement { Day = s.Day, Time = s.Time });
    return new AdClient { Name = name.Split(" - ")[0], Campaigns = [k] };
}
data.Clients = [Client(adverts[0], adverts[0]), Client(adverts[1], adverts[1]), Client(adverts[2], adverts[2]), Client(adverts[3], adverts[3])];
var store = new ScheduleStore(db);
store.Save(data, null, "demo");
(data, long version) = store.Load();

Console.WriteLine("Daily playlists…");
var options = new PlaylistGenerator.Options(settings.Crossfade, settings.OtherCrossfade, TimeSpan.FromHours(3), TimeSpan.FromMinutes(60));
var planStore = new PlanStore(db);
var nowHour = DateTime.Today.AddHours(DateTime.Now.Hour);
PlanMaker.Make(planStore, data, options, Enumerable.Range(-2, 30).Select(h => nowHour.AddHours(h)).ToList(), new List<string>(), true);
// Made "on studio", not on this computer's name (it shows in the screenshots).
using (var c = db.Connect()) Dapper.SqlMapper.Execute(c, "UPDATE plan_hours SET generated_by = 'studio'");
// What already went on air this hour (and the hour before): the play history and the as-run log
var history = new List<AsRunEntry>();
foreach (var hour in new[] { nowHour.AddHours(-1), nowHour })
{
    var at = hour;
    foreach (var item in planStore.Items(hour))
    {
        if (at.AddSeconds(item.Seconds) > DateTime.Now.AddMinutes(-2)) break;
        db.RecordPlay(item.Path, item.Artist, item.Title, at.ToUniversalTime(), "playlist");
        history.Add(new AsRunEntry(at, at.AddSeconds(item.Seconds), AsRunLog.TypeName(item.Kind),
            "playlist", "", item.Kind == TrackKind.Commercial ? item.Title.Split(" - ")[0] : "", item.Artist, item.Title, "", "", "", "", "", item.Path));
        at = at.AddSeconds(item.Seconds);
    }
}
foreach (var a in adverts) history.Add(new AsRunEntry(nowHour.AddMinutes(-10), nowHour.AddMinutes(-9.5), "Advert", adBreak.Name, adBreak.Name + " " + nowHour.AddMinutes(-10).ToString("HH:mm"), a.Split(" - ")[0], "", a, "", "", "", "", "", Path.Combine(adsDir, a + ".mp3")));
AsRunDb.Add(db, history);

// ───────────────────────── The on-air program ─────────────────────────

Console.WriteLine("Screenshots…");
var main = new MainWindow { Width = 1480, Height = 920 };
main.Show();
var engine = Field<PlayoutEngine>(main, "_engine");
var planner = Field<object>(main, "_plan");
List<PlaylistRow> Rows() => Field<List<PlaylistRow>>(main, "_order");
void PlanTick()
{
    planner.GetType().GetField("_nextCheck", Any)!.SetValue(planner, DateTime.MinValue);
    planner.GetType().GetMethod("Check")!.Invoke(planner, [null, true, Rows()]);
}
WaitFor(() => { PlanTick(); return Rows().Count(r => !r.IsPreview) > 3; }, 60);
// The next hour too, so the playlist shows the hour change
planner.GetType().GetMethod("Load", Any)!.Invoke(planner, [nowHour.AddHours(1), null]);
Pump(20);
engine.Play();
Thread.Sleep(2500);
WaitFor(() => engine.GetStatus().State == PlayerState.Playing, 10);
for (int i = 0; i < 40; i++) { Pump(2); Thread.Sleep(60); }
// It opens locked (on air carries on) until a presenter logs in
var lockPanel = main.FindControl<LoginPanel>("LockLogin")!;
WaitFor(() => Field<LibraryService>(main, "_library").Db != null, 20);
Pump(10);
lockPanel.FindControl<TextBox>("UserBox")!.Text = "dj";
Shot(main, "main-locked", "UserBox", "PasswordBox", "LoginButton", "LockNote");
main.LogIn(new UserStore(db).LogIn("dj", "dj"));
Pump(10);
string[] deck = ["OnAirBadge", "NowTitle", "WaveMain", "Elapsed", "PlayButton", "PauseButton", "NextButton", "StopButton", "Remaining", "AutoSwitch",
    "StreamToggle", "Clock", "TopOfHourText", "PlaylistEndsText", "NextEventPanel", "MeterL", "Playlist", "OnAirButton", "HourViewButton", "AddStreamButton",
    "RemoveButton", "ClearButton", "CartwallTab", "LibraryTab", "FoldersTab", "Cartwall", "CartwallPicker", "CartwallMenuButton", "SettingsButton", "ReportsButton", "LogoutButton", "LastLog"];
Shot(main, "main", deck);

// The web remote's page with the demo station's status, levels, waveform and logo (make-manual.sh photographs it at a
// computer's and at a phone's size)
{
    T Ask<T>(Task<T> task) { while (!task.IsCompleted) Pump(2); return task.Result; }
    var jsonOptions = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
    string Json(object? o) => System.Text.Json.JsonSerializer.Serialize(o, jsonOptions);
    WaitFor(() => Ask(main.Waveform()) != null, 20);
    string status = Json(Ask(main.Status())), levels = Json(Ask(main.Levels())), waveform = Json(Ask(main.Waveform()));
    var logoPng = Ask(main.Logo());
    var pageBytes = (byte[])typeof(RemoteServer).GetMethod("Page", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
    string remoteHtml = System.Text.Encoding.UTF8.GetString(pageBytes).Replace("<script>",
        "<script>window.fetch=async(u)=>({status:200,json:async()=>(u.includes('status')?" + status + ":u.includes('levels')?" + levels
        + ":u.includes('waveform')?" + waveform + ":{ok:true})});</script><script>", StringComparison.Ordinal);
    if (logoPng != null) remoteHtml = remoteHtml.Replace("\"/api/logo\"", "\"data:image/png;base64," + Convert.ToBase64String(logoPng) + "\"", StringComparison.Ordinal);
    File.WriteAllText(Path.Combine(outDir, "remote-mock.html"), remoteHtml);
}
main.FindControl<Avalonia.Controls.Primitives.ToggleButton>("LibraryTab")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Pump(30);
var tracks = main.FindControl<TrackLibraryView>("Tracks")!;
Shot(main, "main-library", "SearchBox", "CategoryBox", "EditCategoriesButton", "TrackList", "ScanButton", "LibraryTab");
main.FindControl<Avalonia.Controls.Primitives.ToggleButton>("FoldersTab")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
main.FindControl<LibraryBrowser>("Library")!.SetRoot(media);
Pump(30);
Shot(main, "main-folders", "FoldersTab", "Library");
main.FindControl<Avalonia.Controls.Primitives.ToggleButton>("CartwallTab")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Pump(10);

// Settings, tab by tab: this computer's (the on-air program) and the station's (Sintoniza Pro)
string[] settingsTabs = ["station", "audio", "microphone", "timesignal", "takes", "safety", "library", "shortcuts", "streaming", "rds", "website", "alerts", "playlist", "remote", "about"];
void SettingsShots(SettingsWindow sw, string prefix)
{
    sw.Show();
    Pump(10);
    var tabs = sw.GetVisualDescendants().OfType<TabControl>().First();
    var items = tabs.Items.OfType<TabItem>().ToList();
    for (int i = 0; i < settingsTabs.Length && i < items.Count; i++)
    {
        if (!items[i].IsVisible) continue;
        tabs.SelectedIndex = i;
        Pump(15);
        Shot(sw, prefix + settingsTabs[i], "LibraryFoldersList", "MusicDeviceBox", "PreviewDeviceBox", "CrossfadeBox", "OtherCrossfadeBox", "StationNameBox",
            "NextMinutesBox", "PlanDaysBox", "LanguageBox", "LibraryTypeBox", "TestLibraryButton", "ResumeBox", "BackupBox", "SilenceBox", "FillBox", "RdsPsBox", "LoudnessBox", "RemoteBox", "RemotePortBox", "RemoteAddressText", "TakesFolderText", "TakesFolderNote", "LatitudeBox", "TemperatureButton");
    }
    sw.Close();
}
SettingsShots(new SettingsWindow(settings, engine, main) { Width = 1100, Height = 760 }, "settings-");

// Cue points and tags of a song
string song = Directory.GetFiles(Path.Combine(music, folders[0])).Order().First();
var cue = new CueEditorWindow(engine, song, Path.GetFileNameWithoutExtension(song)) { Width = 1100, Height = 560 };
cue.Show();
Pump(40);
Shot(cue, "cue-editor");
cue.Close();

// The segue from one song into another
string song2 = Directory.GetFiles(Path.Combine(music, folders[1])).Order().First();
var segueHost = new SegueHost
{
    Open = engine.OpenPreview, GetCues = engine.GetCues, SaveCues = (_, _) => Task.CompletedTask, LoadWaveform = engine.LoadWaveformAsync,
    Crossfade = (_, _) => engine.CrossfadeSeconds, PreviewIsOnAir = false,
};
string Display(string file) => db.GetByPath(file) is { } t ? $"{t.Artist} - {t.Title}" : Path.GetFileNameWithoutExtension(file);
var segue = new SegueEditorWindow(segueHost, new SegueItem(song, Display(song), TrackKind.Music), new SegueItem(song2, Display(song2), TrackKind.Music))
    { Width = 1100, Height = 680 };
segue.Show();
Pump(40);
Shot(segue, "segue", "Join", "MixText", "OutText", "InText", "PlayButton", "SummaryText");
segue.Close();
var tags = new TagEditorWindow(engine, song);
tags.Show();
Pump(30);
Shot(tags, "tag-editor");
tags.Close();

// Reports
var reports = new ReportsWindow(AsRunLog.DefaultFolder, settings.StationName) { Database = () => db, Width = 1100, Height = 700 };
reports.Show();
Pump(40);
Shot(reports, "reports", "KindBox", "FromBox", "ToBox", "CsvButton", "HtmlButton");
reports.FindControl<ComboBox>("KindBox")!.SelectedIndex = 2;
Pump(40);
Shot(reports, "reports-adverts", "KindBox", "AdvertiserBox", "HtmlButton");
reports.Close();

// Categories editor
var library = Field<LibraryService>(main, "_library");
var categories = new CategoriesWindow(library, main.CategoryFolders(_ => { }));
categories.Show();
Pump(40);
var catList = categories.FindControl<ListBox>("CategoryList")!;
catList.SelectedItem = catList.ItemsSource!.Cast<CategoryRow>().FirstOrDefault(r => r.Name == folders[0]);
Pump(20);
Shot(categories, "categories", "CategoryList", "AddChildButton", "ParentBox", "Swatches", "FolderList", "AddFolderButton", "ApplyButton");
catList.SelectedIndex = 0; // Music: one of the station's eight
Pump(20);
Shot(categories, "categories-fixed", "CategoryList", "FixedPanel", "AddChildButton");
categories.Close();
engine.Stop();
main.Close();

// ───────────────────────── Sintoniza Schedule ─────────────────────────

// Hours the on-air program made meanwhile: also "on studio".
using (var c = db.Connect()) Dapper.SqlMapper.Execute(c, "UPDATE plan_hours SET generated_by = 'studio'");
var scheduleLibrary = new LibraryService();
_ = scheduleLibrary.OpenAsync(settings.Library);
// Logged in as the first user, admin (its first password still: Home asks to change it).
var users = new UserStore(db);
var schedule = new ScheduleWindow(data, media, store, version, null, scheduleLibrary, settings, users.LogIn("admin", "admin")) { Width = 1500, Height = 920 };
schedule.Show();
// It opens on Home: the clock, on air, today, what needs attention, the library, the database, this computer.
var proTick = typeof(ScheduleWindow).GetMethod("Tick", BindingFlags.NonPublic | BindingFlags.Instance)!;
for (int i = 0; i < 60; i++) { proTick.Invoke(schedule, [store]); Pump(2); } // the headless timer seldom fires
Shot(schedule, "production", "MainMenu", "SaveButton", "NavPanel", "HomeClock", "HomeToday", "HomeWarnings", "HomeComputer", "NowTitleText", "BoxStationText", "StatusVersion", "StatusClock");
var stabs = schedule.FindControl<TabControl>("Tabs")!;
void Select(string list, int index) { var l = schedule.FindControl<ListBox>(list)!; l.SelectedIndex = index; Pump(10); }
stabs.SelectedIndex = 0; Pump(20); Select("EventList", 0);
Shot(schedule, "schedule-events", "EventList", "EventGrid", "EventAddTimeBox", "EventAddTimeButton", "SaveButton");


// The station's settings and cartwalls (published by the on-air program above, the first time it started)
var stationStore = new StationStore(db);
for (int i = 0; i < 50 && stationStore.Version() == 0; i++) { Pump(4); }
var station = stationStore.Load().Settings ?? StationSettings.From(settings);
var stationSettings = new AppSettings();
station.ApplyTo(stationSettings);
SettingsShots(new SettingsWindow(stationSettings, null, null, SettingsScope.Station, _ => null) { Width = 1100, Height = 760 }, "station-");
var cartwalls = new CartwallsWindow(station.Cartwalls.Count > 0 ? station.Cartwalls : settings.Cartwalls, settings.Routing, _ => null) { Width = 1100, Height = 700 };
cartwalls.Show();
Pump(30);
((Button)cartwalls.FindControl<Avalonia.Controls.Primitives.UniformGrid>("CartGrid")!.Children[4]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Pump(20);
Shot(cartwalls, "cartwalls", "PageList", "CartGrid", "ChooseFileButton", "ColorPanel", "IconPanel", "SaveButton");
cartwalls.Close();
stabs.SelectedIndex = 1; Pump(20); Select("BreakList", 0);
Shot(schedule, "schedule-breaks", "BreakList", "BreakGrid", "PartList", "SaveButton");
stabs.SelectedIndex = 2; Pump(20); Select("ClientList", 0); Select("CampaignList", 0);
for (int i = 0; i < 40; i++) { Pump(3); Thread.Sleep(50); } // the breaks' lengths are measured in the background
Shot(schedule, "schedule-clients", "ClientList", "CampaignList", "SpotGrid", "SaveButton");
stabs.SelectedIndex = 3; Pump(20); Select("TemplateList", 0);
Shot(schedule, "schedule-templates", "TemplateList", "TemplateGrid", "LineList", "TempoMinBox", "LineCategoryBox", "AddCategoryLineButton", "AddFolderLineButton", "SaveButton");
// The template's music rules, further down its panel
schedule.FindControl<NumericUpDown>("MaxSlowBox")!.BringIntoView();
Pump(10);
Shot(schedule, "schedule-rules", "LineList", "TempoMinBox", "EnergyMinBox", "ArtistHoursBox", "AlbumHoursBox", "MaxSlowBox");
schedule.ShowDailyPlaylists();
var daily = (DailyPlaylistsView)schedule.FindControl<TabItem>("DailyTab")!.Content!;
var itemList = daily.FindControl<ListBox>("ItemList")!;
WaitFor(() => itemList.ItemCount > 5, 60);
Pump(40);
var firstSong = itemList.ItemsSource!.Cast<PlanRow>().FirstOrDefault(r => r.Item != null && r.Kind == TrackKind.Music && r.PlayedAt == null && !r.IsOnAir);
if (firstSong != null) { itemList.SelectedItems!.Clear(); itemList.SelectedItems.Add(firstSong); }
Pump(20);
Shot(schedule, "schedule-daily", "DayBox", "HourList", "OnAirText", "ItemList", "StreamButton", "SegueButton", "TagsButton", "ReplaceButton", "RemoveButton", "GenerateHourButton", "GenerateDayButton", "Tracks", "ListenButton", "TotalText");

// A voice track after that song (a recorded take), and the recorder with it
const BindingFlags AnyMember = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
string vtFolder = Path.Combine(media, T("Voice tracks", "Voice tracks"));
Directory.CreateDirectory(vtFolder);
string takeFile = Path.Combine(vtFolder, "take-demo.mp3");
Audio(takeFile, 9, "", "", "", false);
if (firstSong?.Item is { } vtSong)
{
    var dailyHour = (DateTime)typeof(DailyPlaylistsView).GetField("_hour", AnyMember)!.GetValue(daily)!;
    var inserted = (Task)typeof(DailyPlaylistsView).GetMethod("InsertVoiceTrack", AnyMember)!.Invoke(daily, [db, dailyHour, vtSong, new VoiceTake(takeFile, 3.5, 6.5, 9)])!;
    WaitFor(() => inserted.IsCompleted, 20);
    Pump(30);
    var vtRow = itemList.ItemsSource!.Cast<PlanRow>().FirstOrDefault(r => r.Kind == TrackKind.Voice);
    if (vtRow != null) { itemList.SelectedItems!.Clear(); itemList.SelectedItems.Add(vtRow); itemList.ScrollIntoView(vtRow); }
    Pump(20);
    Shot(schedule, "schedule-voice", "ItemList", "VoiceButton");

    var vtDeck = new PreviewDeck(settings.Routing, 0);
    var vtHost = new SegueHost { Open = vtDeck.Open, GetCues = db.GetCues, SaveCues = (_, _) => Task.CompletedTask, LoadWaveform = new WaveformLoader().LoadAsync, Crossfade = (_, _) => 5 };
    var afterRow = itemList.ItemsSource!.Cast<PlanRow>().SkipWhile(r => r != vtRow).Skip(1).FirstOrDefault(r => r.Path.Length > 0);
    string vtName(PlanRow r) => r.HasArtist ? $"{r.Artist} - {r.Title}" : r.Title;
    var vtWindow = new VoiceTrackWindow(vtHost, new SegueItem(firstSong.Path, vtName(firstSong), TrackKind.Music),
        new SegueItem(afterRow?.Path ?? firstSong.Path, afterRow != null ? vtName(afterRow) : "", TrackKind.Music), vtFolder, "") { Width = 900, Height = 600 };
    vtWindow.Show();
    Pump(10);
    var vtCopy = Path.Combine(vtFolder, "take-shot.mp3");
    Audio(vtCopy, 9, "", "", "", false);
    void SetVt(string f, object v) => typeof(VoiceTrackWindow).GetField(f, AnyMember)!.SetValue(vtWindow, v);
    SetVt("_take", vtCopy); SetVt("_length", 9.0); SetVt("_overlap", 3.5); SetVt("_nextAt", (double?)6.5); SetVt("_vt", vtDeck.Open(vtCopy)!);
    var vtStage = typeof(VoiceTrackWindow).GetNestedType("Stage", AnyMember)!;
    typeof(VoiceTrackWindow).GetMethod("Show", AnyMember, [vtStage])!.Invoke(vtWindow, [Enum.Parse(vtStage, "Done")]);
    Pump(20);
    Shot(vtWindow, "voicetrack", "InputBox", "PreRollBox", "StartButton", "TalkButton", "NextButton", "OverlapText", "NextText", "PlayBackButton", "SaveButton");
    vtWindow.Close();
    vtDeck.Dispose();
}

// ERC quotas: last month as it went on air, the Portuguese artists classified (by artist), a few songs left to do
{
    string[] portugueseArtists = ["Sofia Mar", "Ana Brisa", "Mila Reis", "Tiago Sol", "Rui Navarro"];
    var demoSongs = db.Search("", CategoryFilter.All, 1000).Tracks.Where(t => t.Path.StartsWith(music)).OrderBy(t => t.Path).ToList();
    var ptSongs = demoSongs.Where(t => portugueseArtists.Contains(t.Artist)).ToList();
    for (int i = 0; i < ptSongs.Count; i++)
        db.SetAttributes([ptSongs[i].Id], new TrackAttributes(ErcPortuguese: true, Language: i % 5 == 4 ? "en" : "pt-PT", ErcNationality: "045",
            ErcRelease: (i % 3 == 0 ? DateTime.Today.AddMonths(-4) : new DateTime(2019 + i % 4, 1 + i % 12, 1)).ToString("yyyy-MM")));
    var foreign = demoSongs.Where(t => !portugueseArtists.Contains(t.Artist)).ToList();
    db.SetAttributes(foreign.Skip(3).Select(t => t.Id), new TrackAttributes(ErcPortuguese: false)); // three left to classify
    var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1);
    var month = new List<AsRunEntry>();
    int n = 0;
    for (var d = monthStart; d < monthStart.AddMonths(1); d = d.AddDays(1))
        for (var at = d.AddHours(6); at < d.AddHours(23); at = at.AddMinutes(4))
        {
            // A Portuguese song about every third, a little more in the daytime
            var pick = n++ % (Erc.InWeekdayHours(at) ? 2 : 3) == 0 ? ptSongs : demoSongs;
            var t = pick[n * 7 % pick.Count];
            month.Add(new AsRunEntry(at, at.AddSeconds(t.Duration), "Music", "playlist", "", "", t.Artist, t.Title, "", "", "", "", "", t.Path));
        }
    AsRunDb.Add(db, month);
    var ercStore = new StationStore(db);
    var (ercStation, ercVersion) = ercStore.Load();
    if (ercStation != null)
    {
        ercStation.ErcAlias = T("EXAMPLE RADIO 99.9", "RADIO EXEMPLO 99.9");
        ercStation.ErcCountries = [new ErcCountry { Name = "Portugal", Code = "045", Eu = true }];
        ercStore.Save(ercStation, ercVersion, "manual");
    }
    stabs.SelectedItem = schedule.FindControl<TabItem>("ErcTab");
    Pump(10);
    var ercView = (ErcView)schedule.FindControl<TabItem>("ErcTab")!.Content!;
    WaitFor(() => ercView.FindControl<StackPanel>("AllPanel")!.Children.Count == 3 && ercView.FindControl<ListBox>("MissingList")!.ItemCount > 0, 60);
    Pump(20);
    Shot(schedule, "erc", "FromBox", "CheckButton", "AllPanel", "DaytimePanel", "MissingList", "ClassifyButton", "ExportButton", "AliasBox", "CountryPanel");
    var ercSong = new ErcSongWindow(db, [ptSongs[0].Id], [new ErcCountry { Name = "Portugal", Code = "045", Eu = true }]) { Width = 520 };
    ercSong.Show();
    Pump(20);
    Shot(ercSong, "erc-song", "PortugueseBox", "LanguageBox", "CountryBox", "ReleaseBox");
    ercSong.Close();
    // A Christmas song: only in December, from 7:00 to 20:00, twice a day at most
    db.SetRestrictions([demoSongs[0].Id], new SongRestrictions(new DateTime(DateTime.Today.Year, 12, 1), new DateTime(DateTime.Today.Year, 12, 31), null,
        SongRestrictions.Mask(Enumerable.Range(0, 24).Select(h => h is >= 7 and < 20).ToList()), 2, null));
    var restrict = new RestrictionsWindow(db, [demoSongs[0].Id]) { Width = 640 };
    restrict.Show();
    Pump(20);
    Shot(restrict, "restrictions", "FromBox", "UntilBox", "DayGrid", "HourGrid", "DayHoursButton", "MaxDayBox", "MaxWeekBox", "RemoveButton");
    restrict.Close();
    db.SetRestrictions([demoSongs[0].Id], null);
}

// The station's users: a presenter of the demo station next to the first two
if (users.All().All(u => u.Name != "ana")) users.Save(new SintonizaUser { Name = "ana", Permissions = [.. Permission.Presenter] }, "demo");
stabs.SelectedItem = schedule.FindControl<TabItem>("UsersTab");
Pump(20);
var userList = ((UsersView)schedule.FindControl<TabItem>("UsersTab")!.Content!).FindControl<ListBox>("UserList")!;
userList.SelectedIndex = userList.ItemsSource!.Cast<string>().ToList().FindIndex(n => n.StartsWith("ana"));
Pump(20);
Shot(schedule, "users", "UserList", "AddButton", "NameBox", "PasswordBox", "EnabledBox", "AdminPresetButton", "PermissionPanel", "SaveUserButton");
schedule.Close();

// The log-in, as Sintoniza Pro opens
var login = new LoginWindow(db, AppInfo.ScheduleName, null, null, station.StationName) { Width = 420 };
login.FindControl<LoginPanel>("Panel")!.FindControl<TextBox>("UserBox")!.Text = "ana";
login.Show();
Pump(20);
Shot(login, "login", "UserBox", "PasswordBox", "LoginButton");
login.Close();

// ───────────────────────── Sintoniza Import ─────────────────────────

var import = new ImportWindow(db, null);
import.Show();
Pump(20);
import.FindControl<TextBox>("FolderBox")!.Text = incoming;
import.FindControl<TextBox>("NameBox")!.Text = T("New releases", "Novidades");
import.FindControl<ComboBox>("LevelsBox")!.SelectedIndex = 1;
Click(import.FindControl<Button>("PreviewButton")!);
WaitFor(() => import.FindControl<ListBox>("PlanList")!.ItemCount > 1, 30);
Pump(20);
Shot(import, "import", "FolderBox", "ChooseButton", "NameBox", "ParentBox", "KindBox", "LevelsBox", "AlsoInRadio", "PreviewButton", "PlanList", "ImportButton");
import.Close();

// ───────────────────────── Sintoniza Rec ─────────────────────────

// The recordings of this morning, an hour a file, in a folder of the demo station (never the user's Music folder).
string recFolder = Path.Combine(work, T("Recordings", "Gravações"));
for (int h = 6; h < 10; h++)
{
    var start = DateTime.Today.AddHours(h);
    string recFile = RecService.FileFor(recFolder, "RE_", start, ".mp3");
    if (File.Exists(recFile)) continue;
    Directory.CreateDirectory(Path.GetDirectoryName(recFile)!);
    Process.Start(new ProcessStartInfo("ffmpeg", ["-loglevel", "error", "-y", "-f", "lavfi", "-i", "anoisesrc=d=3600:c=pink:a=0.3",
        "-ac", "1", "-b:a", "32k", recFile]) { RedirectStandardError = true })!.WaitForExit();
}
new RecSettings { Folder = recFolder, Prefix = "RE_", Bitrate = 128, KeepDays = 45, SilenceSeconds = 60 }.Save();
var rec = new RecWindow(start: false);
rec.Show();
Pump(20);
rec.FindControl<ListBox>("FileList")!.SelectedIndex = 2;
rec.FindControl<TextBox>("FromBox")!.Text = "08:15";
rec.FindControl<TextBox>("ToBox")!.Text = "08:45";
Pump(20);
Shot(rec, "rec", "RecText", "SourceText", "MeterL", "DiskText", "RecButton", "MonitorButton", "SettingsButton", "DayBox", "FileList", "PlayButton", "FromBox", "ExportButton");
var recSettings = new RecSettingsWindow(RecSettings.Load());
recSettings.Show();
Pump(20);
Shot(recSettings, "rec-settings", "StationBox", "InputRadio", "InputBox", "StreamRadio", "StreamBox", "AutostartBox");
var recTabs = recSettings.GetVisualDescendants().OfType<TabControl>().First();
recTabs.SelectedIndex = 1;
Pump(20);
Shot(recSettings, "rec-files", "FolderBox", "FormatBox", "BitrateBox", "MinutesBox", "KeepDaysBox", "SpaceText", "ArchiveBox");
recTabs.SelectedIndex = 3;
Pump(20);
Shot(recSettings, "rec-alerts", "SilenceDbBox", "AliveBox", "TelegramTokenBox", "SmtpServerBox", "WebhookBox", "TestAlertButton");
recSettings.Close();
rec.Close();

File.WriteAllText(Path.Combine(outDir, "callouts.json"), JsonSerializer.Serialize(callouts, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("Done.");
Environment.Exit(0);
