using System.Globalization;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Playout.Engine;

namespace Playout.App;

/// <summary>
/// Sintoniza Rec: records the station's sound around the clock (as the Lei da Rádio asks: kept at least 30 days), with
/// its level, live listening, the recordings of any day to listen back, and a stretch exported in one file. It starts
/// recording when it opens.
/// </summary>
public partial class RecWindow : Window
{
    RecSettings _settings = new();
    readonly RecService _service = null!;
    readonly DispatcherTimer? _fast, _tick;
    PreviewDeck? _deck;
    PreviewPlayer? _player;
    RecFile? _playing;
    float _secondPeak;
    bool _closing, _seeking;
    DateTime _nextList;

    /// <summary>A recording file of the day.</summary>
    sealed record RecFile(string Path, DateTime Start, double Seconds, long Bytes)
    {
        public override string ToString() =>
            $"{Start:HH:mm:ss} – {Start.AddSeconds(Seconds):HH:mm:ss}    {Format.Duration(Seconds)}    {Bytes / 1e6:0} MB    {System.IO.Path.GetFileName(Path)}";
    }

    // Parameterless constructor for the XAML previewer.
    public RecWindow() : this(false) { }

    /// <param name="start">Start recording now (as when it is opened as a program).</param>
    public RecWindow(bool start)
    {
        InitializeComponent();
        Localization.Apply(this);
        Title = AppInfo.RecName;
        _settings = RecSettings.Load();
        if (_settings.StationName.Length == 0) _settings.StationName = AppSettings.Load().StationName;
        _service = new RecService(_settings);
        _service.Message += m => Dispatcher.UIThread.Post(() => Log(m));

        DayBox.SelectedDate = ExportDayBox.SelectedDate = DateTime.Today;
        DayBox.SelectedDateChanged += (_, _) => ShowDay();
        TodayButton.Click += (_, _) => DayBox.SelectedDate = DateTime.Today;
        RecButton.Click += async (_, _) => await ToggleRecording();
        MonitorButton.IsChecked = _settings.Monitor;
        MonitorButton.IsCheckedChanged += (_, _) =>
        {
            _service.SetMonitor(MonitorButton.IsChecked == true);
            _settings.Save();
        };
        FolderButton.Click += (_, _) => OpenFolder(_settings.Folder);
        SettingsButton.Click += async (_, _) => await OpenSettings();
        FileList.SelectionChanged += (_, _) => PlayButton.IsEnabled = FileList.SelectedItem is RecFile;
        FileList.DoubleTapped += (_, _) => Play();
        PlayButton.Click += (_, _) => { if (_player?.IsPlaying == true) _player.Pause(); else if (_player != null && Equals(_playing, FileList.SelectedItem)) _player.Play(); else Play(); };
        StopPlayButton.Click += (_, _) => StopPlaying();
        PositionSlider.ValueChanged += (_, e) =>
        {
            if (_seeking || _player == null) return;
            _player.Seek(e.NewValue * _player.Length);
        };
        ExportButton.Click += async (_, _) => await Export();

        _fast = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Render, (_, _) => Meters());
        _tick = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Tick());
        _fast.Start();
        _tick.Start();
        Closing += async (_, e) =>
        {
            if (_closing || !_service.IsRecording || e.IsProgrammatic) return;
            e.Cancel = true;
            if (!await Dialogs.Confirm(this, L.T("Stop recording?"), L.T("Closing stops the recording of the station. Close anyway?"), L.T("Stop and close"))) return;
            _closing = true;
            Close();
        };
        Closed += (_, _) =>
        {
            _fast.Stop();
            _tick.Stop();
            StopPlaying();
            _deck?.Dispose();
            _service.Dispose();
        };
        ShowSettings();
        if (start) _service.Start();
        Tick();
        ShowDay();
    }

    internal RecService Service => _service;

    void Log(string message)
    {
        LastLog.Text = $"{DateTime.Now:HH:mm:ss}  {message}";
        AppLog.Write("Rec: " + message);
    }

    void ShowSettings()
    {
        StationText.Text = _settings.StationName.Length > 0 ? _settings.StationName : AppInfo.RecName;
    }

    // ───────────────────────── Recording ─────────────────────────

    async Task ToggleRecording()
    {
        if (_service.IsRecording)
        {
            if (!await Dialogs.Confirm(this, L.T("Stop recording?"), L.T("The station isn't recorded while it is stopped."), L.T("Stop"))) return;
            _service.Stop();
            Log(L.T("Recording stopped by hand"));
        }
        else _service.Start();
        Tick();
    }

    void Meters()
    {
        var (left, right) = _service.TakePeaks();
        MeterL.Push(left);
        MeterR.Push(right);
        _secondPeak = Math.Max(_secondPeak, Math.Max(left, right));
    }

    void Tick()
    {
        var now = DateTime.Now;
        _service.Level(_secondPeak);
        _secondPeak = 0;
        _service.Tick();
        ClockText.Text = now.ToString("HH:mm:ss");
        bool rec = _service.IsRecording;
        bool silent = rec && _service.SilentSeconds >= 5;
        RecBadge.Classes.Set("on", rec && !silent);
        RecBadge.Classes.Set("silent", silent);
        RecText.Text = !rec ? L.T("STOPPED") : silent ? L.F("SILENCE {0}", Format.Duration(_service.SilentSeconds)) : L.T("● REC");
        RecButton.Content = rec ? L.T("■ Stop") : L.T("● Record");
        SinceText.Text = _service.StartedAt is { } since ? L.F("recording for {0}", Format.Duration((now - since).TotalSeconds)) : L.T("not recording");
        SourceText.Text = _service.Source + (_service.Format.Length > 0 ? " · " + _service.Format : "");
        FileText.Text = _service.CurrentFile is { } f ? L.F("Writing {0}", f) : "";
        DiskText.Text = RecService.FreeGb(_settings.Folder) is { } gb
            ? L.F("{0:0.0} GB free · recordings kept {1} days · a new file every {2} min", gb, _settings.KeepDays, _settings.FileMinutes) : "";
        // The day's list as files are added (today)
        if (now >= _nextList && DayBox.SelectedDate?.Date == now.Date) ShowDay();
        if (_player != null && !_seeking)
        {
            _seeking = true;
            PositionSlider.Value = _player.Length > 0 ? _player.Position / _player.Length : 0;
            _seeking = false;
            PositionText.Text = _playing is { } p ? $"{p.Start.AddSeconds(_player.Position):HH:mm:ss}  ({Format.Duration(_player.Position)} / {Format.Duration(_player.Length)})" : "";
            PlayButton.Content = _player.IsPlaying ? "❚❚" : "▶";
        }
    }

    // ───────────────────────── Listening back ─────────────────────────

    /// <summary>The recordings of a day (in its day folder), in time order.</summary>
    static List<RecFile> Files(string folder, DateTime day)
    {
        string dir = Path.Combine(folder, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (!Directory.Exists(dir)) return [];
        return Directory.GetFiles(dir)
            .Where(f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            .Select(f => RecService.StartOf(f) is { } start ? new RecFile(f, start, AudioProbe.Duration(f) ?? 0, new FileInfo(f).Length) : null)
            .OfType<RecFile>().OrderBy(f => f.Start).ToList();
    }

    void ShowDay()
    {
        _nextList = DateTime.Now.AddSeconds(30);
        var day = DayBox.SelectedDate?.Date ?? DateTime.Today;
        var selected = (FileList.SelectedItem as RecFile)?.Path;
        var files = Files(_settings.Folder, day);
        FileList.ItemsSource = files;
        FileList.SelectedItem = files.FirstOrDefault(f => f.Path == selected);
        DayInfo.Text = files.Count == 0 ? L.T("no recordings") : L.F("{0} files · {1} recorded", files.Count, Format.Duration(files.Sum(f => f.Seconds)));
    }

    void Play()
    {
        if (FileList.SelectedItem is not RecFile file) return;
        StopPlaying();
        try
        {
            _deck ??= new PreviewDeck(AudioRouting.Default with { Preview = new OutputRoute(_settings.MonitorOutput) });
            _player = _deck.Open(file.Path);
        }
        catch (Exception ex)
        {
            Log(L.F("Can't play {0}: {1}", Path.GetFileName(file.Path), ex.Message));
            return;
        }
        if (_player == null) { Log(L.F("Can't play {0}", Path.GetFileName(file.Path))); return; }
        _playing = file;
        _player.Play();
        PositionSlider.IsEnabled = StopPlayButton.IsEnabled = true;
    }

    void StopPlaying()
    {
        _player?.Dispose();
        _player = null;
        _playing = null;
        PositionSlider.IsEnabled = StopPlayButton.IsEnabled = false;
        PositionText.Text = "";
        PlayButton.Content = "▶";
    }

    // ───────────────────────── Export ─────────────────────────

    static TimeSpan? Time(string? text) =>
        TimeSpan.TryParseExact(text?.Trim(), [@"h\:mm\:ss", @"hh\:mm\:ss", @"h\:mm", @"hh\:mm"], CultureInfo.InvariantCulture, out var t) ? t : null;

    async Task Export()
    {
        var day = ExportDayBox.SelectedDate?.Date ?? DateTime.Today;
        if (Time(FromBox.Text) is not { } from || Time(ToBox.Text) is not { } to)
        {
            ExportText.Text = L.T("Write the times as 10:00 or 10:00:00.");
            return;
        }
        var start = day + from;
        var end = day + to;
        if (end <= start) end = end.AddDays(1); // past midnight
        var files = Files(_settings.Folder, start.Date).Concat(end.Date != start.Date ? Files(_settings.Folder, end.Date) : [])
            .Select(f => (f.Path, f.Start)).ToList();
        if (files.Count == 0)
        {
            ExportText.Text = L.T("There are no recordings of that day.");
            return;
        }
        var (format, bitrate, ext) = ExportFormatBox.SelectedIndex switch { 1 => (RecFormat.Mp3, 192, "mp3"), 2 => (RecFormat.Wav, 0, "wav"), _ => (RecFormat.Mp3, 128, "mp3") };
        string name = $"{(_settings.Prefix.Length > 0 ? _settings.Prefix : "")}{start:yyyy-MM-dd_HH-mm-ss}_{end:HH-mm-ss}.{ext}";
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L.T("Export a stretch of the recordings"),
            SuggestedFileName = name,
            DefaultExtension = ext,
            FileTypeChoices = [new FilePickerFileType(ext.ToUpperInvariant()) { Patterns = [$"*.{ext}"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        await ExportTo(files, start, end, path, format, bitrate);
    }

    internal async Task<bool> ExportTo(List<(string Path, DateTime Start)> files, DateTime start, DateTime end, string path, RecFormat format, int bitrate)
    {
        ExportButton.IsEnabled = false;
        ExportProgress.IsVisible = true;
        ExportProgress.Value = 0;
        ExportText.Text = L.T("Exporting…");
        var progress = new Progress<double>(p => ExportProgress.Value = p);
        try
        {
            double seconds = await Task.Run(() => RecordingExport.Export(files, start, end, path, format, bitrate, progress));
            if (seconds < 0.5)
            {
                File.Delete(path);
                ExportText.Text = L.T("Nothing was recorded at those times.");
                return false;
            }
            double wanted = (end - start).TotalSeconds;
            ExportText.Text = L.F("Saved {0}: {1}.", Path.GetFileName(path), Format.Duration(seconds))
                              + (seconds < wanted - 2 ? " " + L.F("{0} of it wasn't recorded (the recorder was stopped).", Format.Duration(wanted - seconds)) : "");
            Log(L.F("Exported {0:yyyy-MM-dd HH:mm:ss}–{1:HH:mm:ss} to {2}", start, end, path));
            return true;
        }
        catch (Exception ex)
        {
            ExportText.Text = L.F("Not exported: {0}", ex.Message);
            return false;
        }
        finally
        {
            ExportButton.IsEnabled = true;
            ExportProgress.IsVisible = false;
        }
    }

    // ───────────────────────── Settings ─────────────────────────

    async Task OpenSettings()
    {
        var window = new RecSettingsWindow(_settings.Clone());
        if (await window.ShowDialog<RecSettings?>(this) is not { } changed) return;
        changed.Monitor = MonitorButton.IsChecked == true;
        changed.Save();
        _service.Settings = changed; // restarts the recording if where or how it records changed
        _settings = changed;
        ShowSettings();
        ShowDay();
        Log(L.T("Settings saved"));
    }

    static void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch { /* no file manager */ }
    }
}
