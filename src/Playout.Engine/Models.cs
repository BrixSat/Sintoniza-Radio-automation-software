namespace Playout.Engine;

/// <summary>
/// Stream: another station's internet stream, played for <see cref="Track.Duration"/> seconds or, with 0, until Stop
/// or Next. Programme: speech and shows put on air by a scheduled event (news bulletins…): not a song for RDS, the
/// website or music reports.
/// </summary>
public enum TrackKind { Music, Jingle, Commercial, Stream, Programme }

/// <summary>
/// The usual radio markers for a file, in seconds from the start of the file. Null = not set.
/// <list type="bullet">
/// <item><b>Cue In</b>: where playback starts (skips silence or a count-in).</item>
/// <item><b>Intro</b>: end of the instrumental intro; the presenter can talk until here.</item>
/// <item><b>Outro</b>: start of the ending; the presenter can talk over the rest.</item>
/// <item><b>Mix</b>: where the next item starts. This item keeps playing, unfaded, until its Cue Out.
///   Without it, the next item starts one crossfade before Cue Out and this one fades out.</item>
/// <item><b>Cue Out</b>: where playback stops (cuts silence or a long fade).</item>
/// </list>
/// </summary>
public sealed record CuePoints(double? CueIn = null, double? Intro = null, double? Outro = null, double? Mix = null, double? CueOut = null)
{
    public static readonly CuePoints None = new();

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => this == None;
}

/// <summary>An audio file or internet stream known to the engine. Immutable; the playlist holds these.</summary>
public sealed record Track(Guid Id, string Path, string Artist, string Title, double Duration, TrackKind Kind = TrackKind.Music, CuePoints? Cues = null)
{
    public string Display => string.IsNullOrWhiteSpace(Artist) ? Title : $"{Artist} - {Title}";

    /// <summary>The scheduled event or ad break that put this item in the playlist (e.g. "Ad break 10:30"); null for the rest.</summary>
    public string? Event { get; init; }

    /// <summary>Who an advert is for, for the proof of broadcast.</summary>
    public string? Advertiser { get; init; }

    /// <summary>What the operator may do with it (set by the schedule): anything, move it, or nothing.</summary>
    public ItemLock Lock { get; init; }

    /// <summary>
    /// The latest it may start (UTC): if it hasn't started by then, what is on air fades out and it starts. Set on the
    /// first item of a scheduled event or break with a time limit; null for the rest.
    /// </summary>
    public DateTime? StartBy { get; init; }

    /// <summary>
    /// The playlist before it is adjusted (backtimed) so it starts at <see cref="StartBy"/>: songs that wouldn't fit
    /// are moved after it, and a gap is filled with a song that fits.
    /// </summary>
    public bool Backtime { get; init; }

    public bool IsStream => Kind == TrackKind.Stream;

    /// <summary>A stream with no set play time: it stays on air until Stop or Next.</summary>
    public bool IsOpenEnded => IsStream && Duration <= 0;

    /// <summary>Where playback starts (Cue In).</summary>
    public double Start => Math.Max(0, Cues?.CueIn ?? 0);

    /// <summary>Where playback stops (Cue Out, else the end of the file).</summary>
    public double End => Math.Max(Start, Cues?.CueOut ?? Duration);

    /// <summary>How long the item is on air, Cue In to Cue Out.</summary>
    public double PlayLength => End - Start;

    /// <summary>When the next item starts: the Mix marker, else one crossfade before the end. Items no longer than two crossfades (stingers) play out in full.</summary>
    public double MixPoint(double crossfade)
    {
        if (Cues?.Mix is { } mix) return Math.Clamp(mix, Start, End);
        return crossfade > 0 && PlayLength > crossfade * 2 ? End - crossfade : End;
    }
}

/// <summary>
/// An item a scheduled event puts in the playlist: a file, or (with <see cref="StreamName"/>) an internet stream played
/// for <see cref="StreamSeconds"/> (0 = until Stop or Next). <see cref="Kind"/> overrides what the file name says
/// (adverts are adverts wherever they are kept).
/// </summary>
/// <param name="StartBy">Restoring a saved playlist: the item's latest start (UTC), see <see cref="Track.StartBy"/>.</param>
/// <param name="Backtime">Restoring a saved playlist: see <see cref="Track.Backtime"/>.</param>
public sealed record ScheduledItem(string Path, TrackKind? Kind = null, string? StreamName = null, double StreamSeconds = 0, string? Advertiser = null,
    ItemLock Lock = ItemLock.Free, DateTime? StartBy = null, bool Backtime = false);

/// <summary>What the operator may do with a playlist item put there by the schedule.</summary>
public enum ItemLock
{
    /// <summary>Anything: remove, move, replace.</summary>
    Free,
    /// <summary>Move it, but not remove it.</summary>
    Movable,
    /// <summary>Neither move nor remove it (adverts, legal IDs).</summary>
    Locked,
}

/// <summary>
/// How a scheduled event or break starts. Soft: after the item on air ends (with <see cref="EventTiming.MaxDelay"/>,
/// at most that late: then what is on air fades out). Hard: at once, fading out what is on air. EndAt: it finishes
/// at <see cref="EventTiming.EndsAt"/>: it starts that much earlier, and the playlist before it is adjusted.
/// </summary>
public enum EventStart { Soft, Hard, EndAt }

public sealed record EventTiming(EventStart Start, TimeSpan? MaxDelay = null, DateTime? EndsAt = null)
{
    public static readonly EventTiming Soft = new(EventStart.Soft);
    public static readonly EventTiming Hard = new(EventStart.Hard);
}

public enum PlayerState { Stopped, Playing, Paused }

public enum StreamState { Off, Connecting, OnAir, Failed }

/// <summary>Published by the engine whenever the playlist or the on-air cursor changes.</summary>
public sealed record PlaylistSnapshot(
    IReadOnlyList<Track> Items,
    Guid? CurrentId,
    Guid? NextId,
    IReadOnlySet<Guid> Played);

/// <summary>Cheap, poll-able view of the on-air player (the UI reads it ~30 times a second). Silence: seconds of dead air so far (0 = sound).</summary>
public readonly record struct PlayerStatus(
    PlayerState State,
    Track? Current,
    double Position,
    double Length,
    float LevelLeft,
    float LevelRight,
    StreamState Stream,
    string? StreamError,
    double Silence);

/// <summary>
/// Dead air: while an item is playing and the output stays under <paramref name="ThresholdDb"/> (peak, dBFS) for
/// <paramref name="Seconds"/>, AUTO skips to the next item (ASSISTED only warns).
/// </summary>
public sealed record SilenceSettings(bool Enabled = true, double ThresholdDb = -45, double Seconds = 15);

public enum StreamFormat { Mp3, Ogg }

public sealed record StreamSettings(
    string Host,
    int Port,
    string Mount,
    string Password,
    StreamFormat Format = StreamFormat.Mp3,
    int Bitrate = 128,
    string StationName = "Sintoniza Radio Broadcast system",
    string Genre = "Various",
    bool Shoutcast = false,
    string? Url = null);
