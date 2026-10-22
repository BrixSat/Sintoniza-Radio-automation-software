using System.Text;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Playout.Engine;
using Playout.Library;

namespace Playout.App;

/// <summary>A line of the certificate as listed.</summary>
public sealed class CertificateRow(CertificateLine line)
{
    static readonly IBrush Good = new SolidColorBrush(Color.Parse("#3ECF8E")), Bad = new SolidColorBrush(Color.Parse("#E5484D")),
        Muted = new SolidColorBrush(Color.Parse("#8B93A3")), Other = new SolidColorBrush(Color.Parse("#F5A524"));

    public CertificateLine Line { get; } = line;
    public string Date => Line.When.ToString("yyyy-MM-dd");
    public string Booked => Line.Booked?.ToString("HH:mm") ?? "—";
    public string OnAir => Line.Aired?.Start.ToString("HH:mm:ss") ?? "";
    public string Duration => Line.Aired?.Seconds is { } s ? Format.Duration(s) : "";
    public string What => Line.Campaign.Length > 0 ? $"{Line.Campaign} · {Line.Advert}" : Line.Advert;
    public string Status => Certificates.StatusText(Line.Status);
    public IBrush Brush => Line.Status switch
    {
        CertificateStatus.Aired => Good,
        CertificateStatus.Missed => Bad,
        CertificateStatus.ToCome => Muted,
        _ => Other,
    };
}

/// <summary>
/// Sintoniza Pro → Clients → Certificate…: what was booked for a client against what went on air, spot by spot, to
/// print (or save as PDF) for them; and each advert as it went out, from Sintoniza Rec's recordings.
/// </summary>
public partial class CertificateWindow : Window
{
    /// <summary>A campaign in the list (null: all of them).</summary>
    sealed record CampaignChoice(AdCampaign? Campaign, string Text)
    {
        public override string ToString() => Text;
    }

    readonly AdClient _client = new();
    readonly LibraryDb? _db;
    readonly AppSettings? _settings;
    readonly string _station = "";
    List<AsRunEntry> _asRun = new();
    (DateTime From, DateTime To) _read = (DateTime.MaxValue, DateTime.MinValue);
    List<CertificateLine> _lines = new();
    PreviewDeck? _deck;
    PreviewPlayer? _player;
    readonly DispatcherTimer _stopAt = new();
    int _version;

    // Parameterless constructor for the XAML previewer.
    public CertificateWindow() => InitializeComponent();

    public CertificateWindow(AdClient client, LibraryDb? db, AppSettings? settings, string station)
    {
        InitializeComponent();
        Localization.Apply(this);
        _client = client;
        _db = db;
        _settings = settings;
        _station = station;
        ClientText.Text = client.Name;
        Title = L.F("Broadcast certificate · {0}", client.Name);
        CampaignBox.ItemsSource = new[] { new CampaignChoice(null, L.T("All its campaigns")) }
            .Concat(client.Campaigns.Select(k => new CampaignChoice(k, k.Name))).ToList();
        CampaignBox.SelectedIndex = 0;
        SetMonth(0);
        ShowRecordings();

        CampaignBox.SelectionChanged += async (_, _) => await Reload();
        FromBox.SelectedDateChanged += async (_, _) => await Reload();
        ToBox.SelectedDateChanged += async (_, _) => await Reload();
        LastMonthButton.Click += async (_, _) => { SetMonth(-1); await Reload(); };
        ThisMonthButton.Click += async (_, _) => { SetMonth(0); await Reload(); };
        LineList.SelectionChanged += (_, _) => Selected();
        LineList.DoubleTapped += (_, _) => Listen();
        ListenButton.Click += (_, _) => Listen();
        StopButton.Click += (_, _) => StopListening();
        SaveAudioButton.Click += async (_, _) => await SaveAudio();
        RecordingsButton.Click += async (_, _) => await ChooseRecordings();
        CsvButton.Click += async (_, _) => await Save(csv: true);
        HtmlButton.Click += async (_, _) => await Save(csv: false);
        CloseButton.Click += (_, _) => Close();
        _stopAt.Tick += (_, _) => StopListening();
        Opened += async (_, _) => await Reload();
        Closed += (_, _) =>
        {
            StopListening();
            _deck?.Dispose();
        };
    }

    AdCampaign? Campaign => (CampaignBox.SelectedItem as CampaignChoice)?.Campaign;

    /// <summary>A month: 0 this one (to today), -1 the one before.</summary>
    void SetMonth(int offset)
    {
        var first = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(offset);
        FromBox.SelectedDate = first;
        ToBox.SelectedDate = offset == 0 ? DateTime.Today : first.AddMonths(1).AddDays(-1);
    }

    (DateTime From, DateTime To) Period()
    {
        var (from, to) = (FromBox.SelectedDate ?? DateTime.Today, ToBox.SelectedDate ?? DateTime.Today);
        return to < from ? (to.Date, from.Date) : (from.Date, to.Date);
    }

    async Task Reload()
    {
        var (from, to) = Period();
        int version = ++_version;
        SummaryText.Text = L.T("Reading…");
        try
        {
            // The as-run log is read again only for days not read yet.
            if (from < _read.From || to > _read.To)
            {
                var db = _db;
                var entries = await Task.Run(() => AsRunDb.ReadAll(db, AsRunLog.DefaultFolder, from, to));
                if (version != _version) return;
                (_asRun, _read) = (entries, (from, to));
            }
            _lines = Certificates.Build(_client, Campaign, _asRun, from, to, DateTime.Now);
            LineList.ItemsSource = _lines.Select(l => new CertificateRow(l)).ToList();
            SummaryText.Text = _lines.Count == 0 ? L.T("Nothing booked nor on air in these days.") : Certificates.Summary(_lines);
            SummaryText.Foreground = _lines.Any(l => l.Status == CertificateStatus.Missed) ? new SolidColorBrush(Color.Parse("#E5484D")) : Brushes.White;
            CsvButton.IsEnabled = HtmlButton.IsEnabled = _lines.Count > 0;
        }
        catch (Exception ex)
        {
            SummaryText.Text = L.F("The as-run log can't be read: {0}", ex.Message);
        }
    }

    // ───────────────────────── The recordings ─────────────────────────

    string RecordingsFolder => _settings?.RecordingsFolder is { Length: > 0 } f ? f : RecSettings.Load().Folder;

    void ShowRecordings()
    {
        string folder = RecordingsFolder;
        RecordingsText.Text = Directory.Exists(folder) ? folder : L.F("{0} (not found: choose where they are)", folder);
    }

    async Task ChooseRecordings()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = L.T("Sintoniza Rec's recordings") });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } folder) return;
        if (_settings != null)
        {
            _settings.RecordingsFolder = folder;
            _settings.Save();
        }
        ShowRecordings();
        Selected();
    }

    CertificateLine? SelectedLine => (LineList.SelectedItem as CertificateRow)?.Line;

    void Selected()
    {
        bool aired = SelectedLine?.Aired != null;
        ListenButton.IsEnabled = SaveAudioButton.IsEnabled = aired;
        AudioText.Text = "";
    }

    /// <summary>The selected advert's stretch of the recordings: from a little before it to a little after.</summary>
    (DateTime From, DateTime To)? Stretch() =>
        SelectedLine?.Aired is { } e ? (e.Start.AddSeconds(-2), (e.End ?? e.Start.AddSeconds(30)).AddSeconds(2)) : null;

    void Listen()
    {
        if (Stretch() is not { } stretch) return;
        StopListening();
        if (RecService.Find(RecordingsFolder, stretch.From) is not { } found)
        {
            AudioText.Text = L.T("There is no recording of that moment.");
            return;
        }
        try
        {
            _deck ??= new PreviewDeck(_settings?.Routing ?? AudioRouting.Default, MainWindow.ForcedDeviceFromArgs());
            _player = _deck.Open(found.Path);
        }
        catch (Exception ex)
        {
            AudioText.Text = L.F("Can't play {0}: {1}", Path.GetFileName(found.Path), ex.Message);
            return;
        }
        if (_player == null)
        {
            AudioText.Text = L.F("Can't play {0}", Path.GetFileName(found.Path));
            return;
        }
        _player.Seek(found.Seconds);
        _player.Play();
        _stopAt.Interval = stretch.To - stretch.From;
        _stopAt.Start();
        StopButton.IsEnabled = true;
        AudioText.Text = L.F("Playing {0} from {1:HH:mm:ss}", Path.GetFileName(found.Path), stretch.From);
    }

    void StopListening()
    {
        _stopAt.Stop();
        _player?.Dispose();
        _player = null;
        StopButton.IsEnabled = false;
    }

    async Task SaveAudio()
    {
        if (Stretch() is not { } stretch || SelectedLine?.Aired is not { } aired) return;
        var files = RecService.Recordings(RecordingsFolder, stretch.From, stretch.To);
        if (files.Count == 0)
        {
            AudioText.Text = L.T("There is no recording of that moment.");
            return;
        }
        string name = string.Concat($"{_client.Name}-{aired.Start:yyyy-MM-dd_HH-mm-ss}".Split(Path.GetInvalidFileNameChars())).Replace(' ', '-');
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L.T("Save the advert as it went on air"),
            SuggestedFileName = name + ".mp3",
            DefaultExtension = "mp3",
            FileTypeChoices = [new FilePickerFileType("MP3") { Patterns = ["*.mp3"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        AudioText.Text = L.T("Saving…");
        try
        {
            double seconds = await Task.Run(() => RecordingExport.Export(files, stretch.From, stretch.To, path, RecFormat.Mp3, 192));
            AudioText.Text = seconds > 0 ? L.F("Saved {0}: {1}.", Path.GetFileName(path), Format.Duration(seconds)) : L.T("There is no recording of that moment.");
        }
        catch (Exception ex)
        {
            AudioText.Text = L.F("Not saved: {0}", ex.Message);
        }
    }

    // ───────────────────────── The certificate ─────────────────────────

    async Task Save(bool csv)
    {
        if (_lines.Count == 0) return;
        var (from, to) = Period();
        var report = Certificates.ToReport(_lines, _station, _client, Campaign, from, to);
        string days = from == to ? $"{from:yyyy-MM-dd}" : $"{from:yyyy-MM-dd}_{to:yyyy-MM-dd}";
        string name = string.Concat($"{L.T("certificate")}-{_client.Name}-{days}".Split(Path.GetInvalidFileNameChars())).Replace(' ', '-');
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = csv ? L.T("Save the report as CSV") : L.T("Save the report as a page to print"),
            SuggestedFileName = $"{name}.{(csv ? "csv" : "html")}",
            DefaultExtension = csv ? "csv" : "html",
            FileTypeChoices = [csv ? new FilePickerFileType(L.T("CSV")) { Patterns = ["*.csv"] } : new FilePickerFileType(L.T("Web page")) { Patterns = ["*.html"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try
        {
            if (csv) await File.WriteAllTextAsync(path, report.ToCsv(';'), new UTF8Encoding(true));
            else
            {
                await File.WriteAllTextAsync(path, report.ToHtml(L.F("Made by {0} on {1:yyyy-MM-dd HH:mm}", AppInfo.ScheduleName, DateTime.Now)), new UTF8Encoding(false));
                Updates.OpenInBrowser(path);
            }
            SummaryText.Text = L.F("Saved {0}", Path.GetFileName(path)) + " · " + Certificates.Summary(_lines);
        }
        catch (Exception ex)
        {
            SummaryText.Text = L.F("Not saved: {0}", ex.Message);
        }
    }
}
