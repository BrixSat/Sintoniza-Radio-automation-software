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
            foreach (var name in new[] { nameof(Track), nameof(Title), nameof(Artist), nameof(HasArtist), nameof(Length), nameof(HasCues), nameof(HasEvent), nameof(EventTag), nameof(EventTip), nameof(IsLocked), nameof(LockTip), nameof(IsTimed), nameof(TimedTip), nameof(IsAdBreak), nameof(IsJingleRow), nameof(IsProgramme), nameof(IsStreamRow) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public Guid Id => Track.Id;

    /// <summary>
    /// A scheduled event or break shown ahead, where it will play: not in the engine's playlist yet (it is put there
    /// when it is due), so it can't be played, moved or removed from here.
    /// </summary>
    public bool IsPreview { get; init; }
    public string Title => Track.Title;
    /// <summary>
    /// The second line: the artist (a stream: its address). An advert: its advertiser. The first item of a scheduled
    /// break or event with a latest start: how it starts.
    /// </summary>
    public string Artist
    {
        get
        {
            if (IsPreview) return Track.Artist;
            string text = Track.IsStream ? Track.Path : Track.Kind == TrackKind.Commercial && Track.Advertiser is { } a ? a : Track.Artist;
            if (Track.StartBy is { } by)
            {
                string how = Track.Backtime ? L.F("timed to start at {0:HH:mm:ss} and end on time", by.ToLocalTime()) : L.F("can wait until {0:HH:mm:ss}", by.ToLocalTime());
                text = text.Length > 0 ? $"{text} · {how}" : how;
            }
            return text;
        }
    }
    public bool HasArtist => !string.IsNullOrWhiteSpace(Artist);
    /// <summary>On-air length, Cue In to Cue Out; ∞ for a stream that plays until Stop or Next.</summary>
    public string Length => Track.IsOpenEnded ? "∞" : Format.Duration(Track.PlayLength);
    public bool HasCues => Track.Cues != null;
    /// <summary>Put in the playlist by a scheduled event or ad break: its name and time, shown on the row.</summary>
    public bool HasEvent => Track.Event != null;
    public string EventTag => Track.Event?.ToUpperInvariant() ?? "";
    public string EventTip => Track.Advertiser is { } a ? L.F("{0} · advertiser: {1}", Track.Event, a) : L.F("Scheduled: {0}", Track.Event);
    /// <summary>The schedule doesn't let the operator remove it (and, locked, not move it either).</summary>
    public bool IsLocked => Track.Lock != ItemLock.Free;
    public string LockTip => Track.Lock == ItemLock.Locked
        ? L.T("Locked by the schedule: it can't be removed or moved")
        : L.T("The schedule lets it be moved, but not removed");
    /// <summary>Has a latest start time: its start time is shown in colour.</summary>
    public bool IsTimed => Track.StartBy != null;
    public string? TimedTip => Track.StartBy is { } at
        ? Track.Backtime
            ? L.F("Starts at {0:HH:mm:ss} to end on time: the songs before it are adjusted, and what is on air then fades out", at.ToLocalTime())
            : L.F("Starts by {0:HH:mm:ss} at the latest: what is on air then fades out", at.ToLocalTime())
        : null;
    public bool IsMusic => Track.Kind == TrackKind.Music;
    public bool IsJingle => Track.Kind == TrackKind.Jingle;
    public bool IsCommercial => Track.Kind == TrackKind.Commercial;
    public bool IsProgramme => Track.Kind is TrackKind.Programme or TrackKind.Voice;

    /// <summary>Part of an ad break: an advert, or the jingles a break opens and closes with. Shown with its own background.</summary>
    public bool IsAdBreak => Track.Kind == TrackKind.Commercial || Track.InBreak;

    // Everything that isn't a song has its own background, so it stands out in the list.
    /// <summary>A jingle, promo or ID (not one of an ad break's).</summary>
    public bool IsJingleRow => Track.Kind == TrackKind.Jingle && !IsAdBreak;
    /// <summary>Another station's stream.</summary>
    public bool IsStreamRow => Track.IsStream;

    /// <summary>This computer's currency sign (€, $, £…).</summary>
    public static string SystemCurrency { get; } =
        System.Globalization.NumberFormatInfo.CurrentInfo.CurrencySymbol is { Length: > 0 and <= 2 } c && c != "¤" ? c : "€";

    /// <summary>The money sign on advert rows: the one set in Settings, else this computer's.</summary>
    public static string Currency { get; set; } = SystemCurrency;
    public string CurrencySign => Currency;

    /// <summary>The money sign changed in Settings.</summary>
    public void RefreshCurrency() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrencySign)));
    public bool IsStream => Track.IsStream;

    double _progress;
    /// <summary>0..1 while on air, drives the row's background fill.</summary>
    public double Progress { get => _progress; set => Set(ref _progress, value); }

    int _number;
    public int Number
    {
        get => _number;
        set
        {
            if (_number == value) return;
            Set(ref _number, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NumberText)));
        }
    }
    /// <summary>The number shown: none for a scheduled preview (it isn't in the playlist yet).</summary>
    public string NumberText => IsPreview ? "" : _number.ToString();

    bool _isPlaying, _isNext, _isPlayed, _isEmptyBreak;
    /// <summary>A break shown ahead with no adverts booked: nothing will play.</summary>
    public bool IsEmptyBreak { get => _isEmptyBreak; set => Set(ref _isEmptyBreak, value); }
    public bool IsPlaying { get => _isPlaying; set => Set(ref _isPlaying, value); }
    public bool IsNext { get => _isNext; set => Set(ref _isNext, value); }
    public bool IsPlayed { get => _isPlayed; set => Set(ref _isPlayed, value); }

    string _startTime = "";
    public string StartTime { get => _startTime; set => Set(ref _startTime, value); }
    /// <summary>When it started or will start (local); null when not known (the player stopped).</summary>
    public DateTime? StartAt { get; set; }

    bool _isOverHour;
    /// <summary>A planned item that would start after its hour has ended: left out so the next hour starts on time.</summary>
    public bool IsOverHour
    {
        get => _isOverHour;
        set
        {
            if (_isOverHour == value) return;
            Set(ref _isOverHour, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OverHourTip)));
        }
    }
    public string? OverHourTip => !_isOverHour ? null
        : CutBy is { } cut ? L.F("It would start after {0} has to start ({1:HH:mm:ss}): it won't play here (in AUTO a song is moved after it)", cut.Name, cut.At)
        : PlanPlayer.ReadTag(Track.Tag) is { } t ? L.F("Past the end of its hour: left out so {0:HH}:00 starts on time", t.Hour.AddHours(1)) : null;

    /// <summary>The timed item (a break, an event) that starts before this one could: it won't play where it is.</summary>
    public (string Name, DateTime At)? CutBy { get; set; }

    string _hourMark = "";
    /// <summary>The hour this item starts ("12:00"), when it is the first in a new hour; "" otherwise.</summary>
    public string HourMark
    {
        get => _hourMark;
        set
        {
            if (_hourMark == value) return;
            Set(ref _hourMark, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasHourMark)));
        }
    }
    public bool HasHourMark => _hourMark.Length > 0;

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
