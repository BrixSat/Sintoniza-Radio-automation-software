using System.ComponentModel;
using System.Runtime.CompilerServices;
using Playout.Engine;

namespace Playout.App;

/// <summary>One playlist line in the UI. Rows are reused across engine snapshots so selection and scroll survive.</summary>
public sealed class PlaylistRow : INotifyPropertyChanged
{
    public PlaylistRow(Track track) => Track = track;

    public Track Track { get; }
    public Guid Id => Track.Id;
    public string Title => Track.Title;
    public string Artist => Track.Artist;
    public bool HasArtist => !string.IsNullOrWhiteSpace(Track.Artist);
    public string Length => Format.Duration(Track.Duration);
    public bool IsMusic => Track.Kind == TrackKind.Music;
    public bool IsJingle => Track.Kind == TrackKind.Jingle;
    public bool IsCommercial => Track.Kind == TrackKind.Commercial;

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
}
