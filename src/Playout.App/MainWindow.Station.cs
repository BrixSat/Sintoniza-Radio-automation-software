using Avalonia.Threading;
using Playout.Engine;
using Playout.Library;

namespace Playout.App;

/// <summary>The station's settings (Sintoniza Pro): read from the database at start and when they change.</summary>
public partial class MainWindow
{
    long _stationVersion = -1;
    DateTime _nextStationCheck;
    bool _stationBusy;

    /// <summary>
    /// Every 30 seconds: when Pro saved the station's settings (or at start), they are put over this computer's
    /// and applied. A database that has none yet gets this computer's (the first studio after the upgrade).
    /// </summary>
    void SyncStationSettings(DateTime now)
    {
        if (now < _nextStationCheck || _stationBusy || _library.Db is not { } db) return;
        _nextStationCheck = now.AddSeconds(30);
        _stationBusy = true;
        long known = _stationVersion;
        var mine = _settings;
        Task.Run(() =>
        {
            var store = new StationStore(db);
            long version = store.Version();
            if (version == known) return ((StationSettings?)null, version);
            if (version == 0)
                try { return (null, store.Save(StationSettings.From(mine), 0, $"{Environment.UserName}@{Environment.MachineName}")); }
                catch (ScheduleStore.ChangedException) { /* another studio published theirs meanwhile: read those */ }
            return store.Load();
        }).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            _stationBusy = false;
            if (!t.IsCompletedSuccessfully)
            {
                AppLog.Write($"Station settings not read: {t.Exception?.GetBaseException().Message}");
                return;
            }
            var (station, version) = t.Result;
            _stationVersion = version;
            _settings.StationSettingsStation = CurrentStation.Id; // the copy is this station's now
            if (station == null) { StartHeldStream(); ApplyListeners(); return; }
            bool first = known < 0;
            station.ApplyTo(_settings);
            _settings.Save(); // this computer's copy, for when the database can't be reached
            ApplyStationSettings();
            if (!first) LastLog.Text = L.T("The station's settings changed (Sintoniza Pro)");
            StartHeldStream();
        }));
    }

    /// <summary>
    /// This computer's copy of the station's settings is this station's (not the one it was on air for before the
    /// station was changed in Settings): only then does the stream start with them, never to another station's server.
    /// </summary>
    bool StationCopyIsOurs => _settings.StationSettingsStation == CurrentStation.Id || _settings.StationSettingsStation == 0 && CurrentStation.Id <= 1;

    /// <summary>The stream waits for this station's settings (just switched station, the database away at start).</summary>
    bool _streamHeld;

    void StartHeldStream()
    {
        if (!_streamHeld || !StationCopyIsOurs || IsStandby || _engine is not { } engine) return;
        _streamHeld = false;
        if (_settings.StreamingOn && _settings.UsesStreaming && !string.IsNullOrWhiteSpace(_settings.StreamHost)
            && engine.GetStatus().Stream is StreamState.Off or StreamState.Failed) ToggleStreaming();
    }

    /// <summary>Whether the stream may start now; if not (the settings copy is another station's), it waits for them.</summary>
    bool MayStream()
    {
        if (StationCopyIsOurs) return true;
        if (!_streamHeld) AppLog.Write("Stream: waiting for this station's settings (the copy here is another station's)");
        _streamHeld = true;
        return false;
    }

    /// <summary>Applies what the station's settings change (after Pro saved them, or this computer's Settings).</summary>
    void ApplyStationSettings()
    {
        PlaylistLook.Apply(_settings.PlaylistColors);
        ShowRows(_order); // how much of the next hour shows may have changed
        if (_engine != null)
        {
            _engine.CrossfadeSeconds = _settings.Crossfade;
            _engine.OtherCrossfadeSeconds = _settings.OtherCrossfade;
            _engine.ConfigureTimeSignal(_settings.TimeSignal);
            _takes.Settings = _settings.Takes ?? new();
            ApplySafety();
            ApplyRds();
            ApplyLoudness();
        }
        ApplyWebNowPlaying();
        ApplyListeners();
        ApplyAutoScan();
        ApplyStationBranding();
        ShowStationType();
        // The cartwalls: the one shown, or the first when it is gone.
        if (_settings.Cartwalls.Count > 0)
        {
            var name = _settings.Cartwalls.Any(c => c.Name == _settings.ActiveCartwall) ? _settings.ActiveCartwall! : _settings.Cartwalls[0].Name;
            ShowCartwall(name);
        }
    }
}
