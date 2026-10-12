namespace Playout.Engine;

public enum TrackKind { Music, Jingle, Commercial }

/// <summary>An audio file known to the engine. Immutable; the playlist holds these.</summary>
public sealed record Track(Guid Id, string Path, string Artist, string Title, double Duration, TrackKind Kind = TrackKind.Music)
{
    public string Display => string.IsNullOrWhiteSpace(Artist) ? Title : $"{Artist} - {Title}";
}

public enum PlayerState { Stopped, Playing, Paused }

public enum StreamState { Off, Connecting, OnAir, Failed }

/// <summary>Published by the engine whenever the playlist or the on-air cursor changes.</summary>
public sealed record PlaylistSnapshot(
    IReadOnlyList<Track> Items,
    Guid? CurrentId,
    Guid? NextId,
    IReadOnlySet<Guid> Played);

/// <summary>Cheap, poll-able view of the on-air player (the UI reads it ~30 times a second).</summary>
public readonly record struct PlayerStatus(
    PlayerState State,
    Track? Current,
    double Position,
    double Length,
    float LevelLeft,
    float LevelRight,
    StreamState Stream,
    string? StreamError);

public enum StreamFormat { Mp3, Ogg }

public sealed record StreamSettings(
    string Host,
    int Port,
    string Mount,
    string Password,
    StreamFormat Format = StreamFormat.Mp3,
    int Bitrate = 128,
    string StationName = "Radio Playout",
    string Genre = "Various",
    bool Shoutcast = false,
    string? Url = null);
