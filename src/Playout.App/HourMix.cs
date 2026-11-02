using Playout.Engine;

namespace Playout.App;

/// <summary>
/// An item of an hour in the mix editor: where it starts in the hour, how long it plays before the next starts, and
/// how much of its file plays (Cue In to Cue Out).
/// </summary>
public sealed class MixBlock
{
    /// <summary>The daily playlist's item; null for what the schedule puts in the hour (an ad break, an event).</summary>
    public PlanItem? Item { get; init; }
    public required string Path { get; init; }
    public required string Title { get; init; }
    public TrackKind Kind { get; init; }
    public CuePoints Cues { get; set; } = CuePoints.None;
    /// <summary>The file's length (seconds; 0 = not known).</summary>
    public double FileLength { get; set; }
    /// <summary>Seconds from the start of the hour.</summary>
    public double Start { get; set; }
    /// <summary>How long it plays before the next item starts.</summary>
    public double Seconds { get; set; }
    /// <summary>How long it plays to its end, when nothing cuts it.</summary>
    public double Length => FileLength > 0 ? Math.Max(0.5, Math.Min(Cues.CueOut ?? FileLength, FileLength) - (Cues.CueIn ?? 0)) : Math.Max(0.5, Seconds);
    public double End => Start + Length;
    public Waveform? Wave { get; set; }
}

/// <summary>
/// The hour mix editor's sums. An item can start earlier, over the end of the item before it (this hour only: its
/// <see cref="PlanItem.Overlap"/>; the one before then plays on to its end, as under a voice track), never later than
/// the automatic crossfade.
/// </summary>
public sealed class HourMix
{
    /// <summary>The most an item may start over the one before it.</summary>
    public const double MaxOverlap = 30;

    readonly Func<TrackKind, TrackKind, double> _crossfade;

    public IReadOnlyList<MixBlock> Blocks { get; }

    /// <param name="crossfade">The crossfade between two kinds of item (song to song, or with a jingle or advert).</param>
    public HourMix(IReadOnlyList<MixBlock> blocks, Func<TrackKind, TrackKind, double> crossfade)
    {
        Blocks = blocks;
        _crossfade = crossfade;
        Remember();
    }

    /// <summary>How long each item played before the next when the editor opened: an overlap already past the limit can go back there.</summary>
    readonly Dictionary<MixBlock, double> _opened = new();

    void Remember()
    {
        _opened.Clear();
        foreach (var b in Blocks) _opened[b] = b.Seconds;
    }

    /// <summary>Whether block <paramref name="i"/> can be moved: it and the one before it are the daily playlist's.</summary>
    public bool Movable(int i) => i > 0 && i < Blocks.Count && Blocks[i].Item is { FollowsPrevious: false } item && item.Kind != TrackKind.Voice
                                  && Blocks[i - 1].Item != null && Blocks[i - 1].FileLength > 0
                                  && Blocks[i - 1].Kind != TrackKind.Commercial; // never over an advert (the playout doesn't either)

    /// <summary>How long the item before <paramref name="i"/> plays before it starts, with the automatic crossfade.</summary>
    public double Automatic(int i)
    {
        var before = Blocks[i - 1];
        return DailyPlaylistsView.Plays(before.Cues, before.FileLength, _crossfade(before.Kind, Blocks[i].Kind));
    }

    /// <summary>How many seconds block <paramref name="i"/> starts over the end of the one before it (0: no overlap).</summary>
    public double Overlap(int i) => i <= 0 ? 0 : Math.Max(0, Blocks[i - 1].End - Blocks[i].Start);

    /// <summary>Whether block <paramref name="i"/> starts earlier than the automatic crossfade (set by hand, this hour).</summary>
    public bool ByHand(int i) => Movable(i) && Blocks[i].Item!.Overlap > 0;

    /// <summary>
    /// Every item where the playout starts it, from the cue points as they are now: at the automatic crossfade, or as
    /// far over the one before as its overlap (set here, or a voice track's) says; then the start times follow. The
    /// hour's own lengths can be out of date (a cue point changed, an item moved), so this is done when it opens and
    /// after a segue edit.
    /// </summary>
    public void Normalize()
    {
        for (int i = 1; i < Blocks.Count; i++)
        {
            var before = Blocks[i - 1];
            if (before.Item == null || before.FileLength <= 0) continue;
            double auto = Automatic(i);
            double overlap = Blocks[i].Item?.Overlap ?? 0;
            double seconds = overlap > 0 ? Math.Max(1, Math.Min(auto, before.Length - overlap)) : auto;
            before.Seconds = before.Item.Seconds = Math.Round(seconds, 2);
        }
        for (int i = 1; i < Blocks.Count; i++) Blocks[i].Start = Blocks[i - 1].Start + Blocks[i - 1].Seconds;
        Remember();
    }

    /// <summary>
    /// Moves block <paramref name="i"/> by <paramref name="by"/> seconds (earlier: negative), within what can be: from
    /// the automatic crossfade to <see cref="MaxOverlap"/> over the one before. Everything after it moves with it.
    /// Returns how far it moved.
    /// </summary>
    public double Move(int i, double by) => SetSeconds(i, Blocks[i - 1].Seconds + by);

    /// <summary>Back to the automatic crossfade.</summary>
    public double MakeAutomatic(int i) => SetSeconds(i, Automatic(i));

    double SetSeconds(int i, double seconds)
    {
        if (!Movable(i)) return 0;
        var before = Blocks[i - 1];
        double auto = Automatic(i);
        // Never past the limit, but no jump either when it was already further over (a voice track's, set before).
        double min = Math.Max(1, Math.Min(auto, Math.Min(_opened.GetValueOrDefault(before, before.Seconds), before.Length - MaxOverlap)));
        double value = Math.Round(Math.Clamp(seconds, min, auto), 2);
        double moved = value - before.Seconds;
        if (Math.Abs(moved) < 0.001) return 0;
        before.Seconds = value;
        before.Item!.Seconds = value;
        // Under the engine's rule the next starts at the earlier of the automatic mix point and its end less this.
        Blocks[i].Item!.Overlap = value >= auto - 0.005 ? 0 : Math.Round(before.Length - value, 2);
        for (int j = i; j < Blocks.Count; j++) Blocks[j].Start += moved;
        return moved;
    }

    /// <summary>When the hour's last item ends, from the start of the hour (seconds).</summary>
    public double Ends => Blocks.LastOrDefault(b => b.Item?.Optional != true) is { } last ? last.Start + last.Seconds : 0;

    /// <summary>
    /// Where listening from <paramref name="at"/> seconds into the hour starts: the first block heard then (in an
    /// overlap, the earlier one, so its end is heard too); between two, the next; or -1.
    /// </summary>
    public int At(double at)
    {
        for (int i = 0; i < Blocks.Count; i++)
            if (Blocks[i].Start <= at + 0.001 && at < Blocks[i].End) return i;
        for (int i = 0; i < Blocks.Count; i++)
            if (Blocks[i].Start > at) return i;
        return Blocks.Count - 1;
    }
}
