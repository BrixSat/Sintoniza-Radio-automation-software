using System.Globalization;
using Playout.Engine;

namespace Playout.App;

/// <summary>
/// Sintoniza Rec at work: records with <see cref="StationRecorder"/>, a new file every so many minutes (in a folder a
/// day), the archive copy beside it, deletes what is older than kept, watches for silence and files that can't be
/// written, and sends alerts. Ticked once a second by the window (on its thread).
/// </summary>
sealed class RecService : IDisposable
{
    RecSettings _s;
    StationRecorder? _recorder;
    int _main = -1, _archive = -1;
    DateTime _segment, _fileStarted, _nextSegment = DateTime.MaxValue, _nextClean = DateTime.MinValue, _aliveSent = DateTime.Today;
    DateTime? _silentSince, _startedAt;
    bool _silenceReported, _writeReported, _diskReported;
    double _levelDb = -90;

    public RecService(RecSettings settings) => _s = settings;

    /// <summary>What happened, for the window and the log.</summary>
    public event Action<string>? Message;

    public bool IsRecording => _recorder?.IsRunning == true;
    public string Source => _recorder?.Source ?? (_s.Source == RecSource.Stream ? L.F("Stream: {0}", _s.StreamUrl) : L.F("Input: {0}", _s.Input.Length > 0 ? _s.Input : L.T("the default input")));
    public string Format => _recorder?.Format ?? "";
    /// <summary>The file being written now.</summary>
    public string? CurrentFile { get; private set; }
    public DateTime? StartedAt => _startedAt;
    /// <summary>Seconds of silence so far (0: sound).</summary>
    public double SilentSeconds => _silentSince is { } since ? (DateTime.Now - since).TotalSeconds : 0;

    public RecSettings Settings
    {
        get => _s;
        set
        {
            bool restart = IsRecording && (value.Source != _s.Source || value.Input != _s.Input || value.StreamUrl != _s.StreamUrl
                || value.Folder != _s.Folder || value.Prefix != _s.Prefix || value.Format != _s.Format || value.Bitrate != _s.Bitrate
                || value.Mono != _s.Mono || value.FileMinutes != _s.FileMinutes || value.StartMinute != _s.StartMinute
                || value.Archive != _s.Archive || value.ArchiveFolder != _s.ArchiveFolder || value.ArchiveBitrate != _s.ArchiveBitrate);
            _s = value;
            if (restart) { Stop(); Start(); }
            else _recorder?.SetMonitor(_s.Monitor ? _s.MonitorOutput : null);
        }
    }

    public void Start()
    {
        if (IsRecording) return;
        try
        {
            _recorder = new StationRecorder(_s.Input, _s.Source == RecSource.Stream ? _s.StreamUrl : null);
            _recorder.Log += m => Message?.Invoke(m);
            _recorder.Start();
            _recorder.SetMonitor(_s.Monitor ? _s.MonitorOutput : null);
            _startedAt = DateTime.Now;
            _silentSince = null;
            NewSegment(DateTime.Now);
        }
        catch (Exception ex)
        {
            Message?.Invoke(L.F("Can't record: {0}", ex.Message));
            _recorder?.Dispose();
            _recorder = null;
            Alert("recording", false, L.F("can't record: {0}", ex.Message));
        }
    }

    public void Stop()
    {
        if (_recorder is not { } r) return;
        r.Stop();
        r.Dispose();
        _recorder = null;
        _main = _archive = -1;
        CurrentFile = null;
        _startedAt = null;
        _nextSegment = DateTime.MaxValue;
    }

    public void SetMonitor(bool on)
    {
        _s.Monitor = on;
        _recorder?.SetMonitor(on ? _s.MonitorOutput : null);
    }

    /// <summary>The peak levels since asked (0–1).</summary>
    public (float Left, float Right) TakePeaks() => _recorder?.TakePeaks() ?? (0, 0);

    // ───────────────────────── Files ─────────────────────────

    /// <summary>When the file that is current at <paramref name="at"/> starts: blocks of N minutes from the start minute.</summary>
    public static DateTime SegmentStart(DateTime at, int fileMinutes, int startMinute)
    {
        int minutes = Math.Clamp(fileMinutes, 1, 60);
        double sinceAnchor = (at - at.Date.AddMinutes(startMinute % 60)).TotalMinutes;
        double blocks = Math.Floor(sinceAnchor / minutes);
        return at.Date.AddMinutes(startMinute % 60 + blocks * minutes);
    }

    /// <summary>Folder/2026-10-21/RE_2026-10-21_10-00-00.mp3</summary>
    public static string FileFor(string folder, string prefix, DateTime start, string extension) =>
        Path.Combine(folder, start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            $"{prefix}{start.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture)}{extension}");

    /// <summary>When a recording starts, from its file name (null: not one of ours).</summary>
    public static DateTime? StartOf(string file)
    {
        string name = Path.GetFileNameWithoutExtension(file);
        if (name.Length < 19) return null;
        // The date and time are the last 19 characters, perhaps followed by -2, -3 (a stream that came back).
        int dash = name.LastIndexOf('-');
        for (int cut = 0; cut < 2; cut++)
        {
            if (name.Length >= 19 && DateTime.TryParseExact(name[^19..], "yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) return t;
            if (dash < 0) break;
            name = name[..dash];
            dash = name.LastIndexOf('-');
        }
        return null;
    }

    void NewSegment(DateTime now)
    {
        if (_recorder is not { } r) return;
        _segment = SegmentStart(now, _s.FileMinutes, _s.StartMinute);
        _nextSegment = _segment.AddMinutes(Math.Clamp(_s.FileMinutes, 1, 60));
        // The new file starts before the old one stops: nothing is lost between them.
        int oldMain = _main, oldArchive = _archive;
        var file = FileFor(_s.Folder, _s.Prefix, now, _s.Extension);
        _fileStarted = now;
        try
        {
            _main = r.StartFile(file, _s.Format, _s.Bitrate, _s.Mono);
            CurrentFile = file;
            if (_s.Archive && _s.ArchiveFolder.Length > 0)
                _archive = r.StartFile(FileFor(_s.ArchiveFolder, _s.Prefix, now, ".mp3"), RecFormat.Mp3, Math.Clamp(_s.ArchiveBitrate, 8, 320), mono: true);
        }
        catch (Exception ex)
        {
            Message?.Invoke(L.F("Can't write {0}: {1}", file, ex.Message));
        }
        if (oldMain >= 0) r.StopFile(oldMain);
        if (oldArchive >= 0) r.StopFile(oldArchive);
    }

    // ───────────────────────── Every second ─────────────────────────

    /// <summary>The level now, in dBFS (for silence), given by the window from the peaks it shows.</summary>
    public void Level(float peak) => _levelDb = peak > 0 ? 20 * Math.Log10(peak) : -90;

    public void Tick()
    {
        var now = DateTime.Now;
        if (IsRecording && now >= _nextSegment) NewSegment(now);
        if (IsRecording) Watch(now);
        if (now >= _nextClean)
        {
            _nextClean = now.AddHours(1);
            Task.Run(() => Clean(_s.Clone()));
        }
        if (_s.AliveHour is { } hour && now.Hour == hour && _aliveSent < now.Date)
        {
            _aliveSent = now.Date;
            Alert("alive", true, IsRecording ? L.F("still recording ({0} GB free)", FreeGb(_s.Folder)?.ToString("0.0") ?? "?") : L.T("running, but NOT recording"));
        }
    }

    void Watch(DateTime now)
    {
        // Silence (or a stream / input that sends nothing)
        bool quiet = _levelDb < _s.SilenceDb || (_recorder?.SecondsWithoutData ?? 0) > 5;
        if (quiet) _silentSince ??= now;
        else
        {
            if (_silenceReported) Alert("silence", true, L.T("sound again"));
            _silentSince = null;
            _silenceReported = false;
        }
        if (_silentSince is { } since && !_silenceReported && (now - since).TotalSeconds >= Math.Max(5, _s.SilenceSeconds))
        {
            _silenceReported = true;
            Message?.Invoke(L.F("Silence for {0:0} s", (now - since).TotalSeconds));
            Alert("silence", false, L.F("silence for {0:0} s", (now - since).TotalSeconds));
        }
        // The file being written
        bool writing = _recorder is { } r && _main >= 0 && r.IsWriting(_main);
        if (!writing && (_recorder?.SecondsWithoutData ?? 0) < 5 && (now - _fileStarted).TotalSeconds > 10)
        {
            if (!_writeReported)
            {
                _writeReported = true;
                Message?.Invoke(L.F("The recording isn't being written: {0}", CurrentFile));
                Alert("recording", false, L.F("the recording isn't being written ({0})", CurrentFile));
            }
            NewSegment(now); // try again with a new file
        }
        else if (writing && _writeReported)
        {
            _writeReported = false;
            Alert("recording", true, L.T("recording again"));
        }
        // The disk
        if (FreeGb(_s.Folder) is { } free)
        {
            if (free < 2 && !_diskReported) { _diskReported = true; Alert("disk", false, L.F("only {0:0.0} GB free for the recordings", free)); }
            else if (free > 3 && _diskReported) { _diskReported = false; Alert("disk", true, L.F("{0:0.0} GB free again", free)); }
        }
    }

    public static double? FreeGb(string folder)
    {
        try
        {
            string? root = Directory.Exists(folder) ? folder : Path.GetDirectoryName(folder);
            return root == null ? null : new DriveInfo(Path.GetFullPath(root)).AvailableFreeSpace / 1e9;
        }
        catch { return null; }
    }

    /// <summary>Deletes the day folders older than kept (main), and the archive's older than its months.</summary>
    void Clean(RecSettings s)
    {
        void Older(string folder, DateTime before)
        {
            if (folder.Length == 0 || !Directory.Exists(folder)) return;
            foreach (var day in Directory.GetDirectories(folder))
            {
                if (!DateTime.TryParseExact(Path.GetFileName(day), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) || d >= before) continue;
                try
                {
                    Directory.Delete(day, recursive: true);
                    Message?.Invoke(L.F("Deleted the recordings of {0:yyyy-MM-dd} (kept {1} days)", d, (DateTime.Today - before).Days));
                }
                catch (Exception ex) { Message?.Invoke(L.F("Can't delete {0}: {1}", day, ex.Message)); }
            }
        }
        Older(s.Folder, DateTime.Today.AddDays(-Math.Max(1, s.KeepDays)));
        if (s.Archive && s.ArchiveKeepMonths > 0) Older(s.ArchiveFolder, DateTime.Today.AddMonths(-s.ArchiveKeepMonths));
    }

    void Alert(string kind, bool ok, string message)
    {
        if (!_s.Alerts.HasChannel) return;
        string station = _s.StationName.Length > 0 ? $"{_s.StationName} · {AppInfo.RecName}" : AppInfo.RecName;
        _ = AlertSender.SendAsync(_s.Alerts, station, kind, ok, message).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully && t.Result.Length > 0) Message?.Invoke(L.F("Alert not sent: {0}", t.Result));
        });
    }

    public void Dispose() => Stop();
}
