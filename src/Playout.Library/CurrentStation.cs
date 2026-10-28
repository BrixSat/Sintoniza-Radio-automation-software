namespace Playout.Library;

/// <summary>
/// The station this computer works for. One library can hold several stations: the music and its categories are
/// shared, while each station's schedule, daily playlists, settings, studios, play history and as-run log are kept apart
/// by <c>station_id</c>. Set at start from this computer's settings; 1 (the only station) until stations can be chosen.
/// </summary>
public static class CurrentStation
{
    static long _id = 1;

    public static long Id
    {
        get => Interlocked.Read(ref _id);
        set => Interlocked.Exchange(ref _id, Math.Max(1, value));
    }
}
