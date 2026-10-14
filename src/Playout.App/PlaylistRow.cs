using System.ComponentModel;
using System.Runtime.CompilerServices;
using Playout.Engine;

namespace Playout.App;

/// <summary>One playlist line in the UI. Rows are reused across engine snapshots so selection and scroll survive.</summary>
public sealed class PlaylistRow : INotifyPropertyChanged
{
    public PlaylistRow(Track track) => _track = track;

    Track _track;
    /// <summary>The engine replaces the item when its cue points or exact length change; the row stays.</summary>
    public Track Track
    {
        get => _track;
        set
        {
            if (Equals(_track, value)) return;
            _track = value;
            foreach (var name in new[] { nameof(Track), nameof(Title), nameof(Artist), nameof(HasArtist), nameof(Length), nameof(HasCues) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public Guid Id => Track.Id;
    public string Title => Track.Title;
    /// <summary>The artist; for a stream, its address.</summary>
    public string Artist => Track.IsStream ? Track.Path : Track.Artist;
    public bool HasArtist => !string.IsNullOrWhiteSpace(Artist);
    /// <summary>On-air length, Cue In to Cue Out; ∞ for a stream that plays until Stop or Next.</summary>
    public string Length => Track.IsOpenEnded ? "∞" : Format.Duration(Track.PlayLength);
    public bool HasCues => Track.Cues != null;
    public bool IsMusic => Track.Kind == TrackKind.Music;
    public bool IsJingle => Track.Kind == TrackKind.Jingle;
    public bool IsCommercial => Track.Kind == TrackKind.Commercial;
    public bool IsStream => Track.IsStream;

    double _progress;
    /// <summary>0..1 while on air, drives the row's background fill.</summary>
    public double Progress { get => _progress; set => Set(ref _progress, value); }

    int _number;
    public int Number { get => _number; set => Set(ref _number, value); }

    bool _isPlaying, _isNext, _isPlayed;
    public bool IsPlaying { get => _isPlaying; set => Set(ref _isPlaying, value); }
    public bool IsNext { get => _isNext; set => Set(ref _isNext, value); }
    public bool IsPlayed { get => _isPlayed; set => Set(ref _isPlayed, value); }

    string _startTime = "";
    public string StartTime { get => _startTime; set => Set(ref _startTime, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

static class Format
{
    public static string Duration(double seconds)
    {
        if (seconds < 0 || double.IsNaN(seconds)) seconds = 0;
        var t = TimeSpan.FromSeconds(Math.Floor(seconds));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");
    }

    /// <summary>Cue point time with tenths, e.g. "1:23.4".</summary>
    public static string CueTime(double seconds)
    {
        seconds = Math.Max(0, Math.Round(seconds, 1));
        int minutes = (int)(seconds / 60);
        return $"{minutes}:{(seconds - minutes * 60).ToString("00.0", System.Globalization.CultureInfo.InvariantCulture)}";
    }

    /// <summary>Reads "1:23.4", "83.4" or "0:01:23.4" (a comma works as the decimal point too); null if it isn't a time.</summary>
    public static double? ParseCueTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        double total = 0;
        foreach (var part in text.Trim().Replace(',', '.').Split(':'))
        {
            if (!double.TryParse(part, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) || v < 0)
                return null;
            total = total * 60 + v;
        }
        return total;
    }
}
