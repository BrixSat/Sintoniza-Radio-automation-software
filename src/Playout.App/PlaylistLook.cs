using Avalonia;
using Avalonia.Media;

namespace Playout.App;

/// <summary>The playlist's row backgrounds by type, as hex colours ("#16271F"); null = the default.</summary>
public sealed class PlaylistColors
{
    public string? AdBreak { get; set; }
    public string? Jingle { get; set; }
    public string? Programme { get; set; }
    public string? Stream { get; set; }
    public string? Music { get; set; }
    public string? OnAir { get; set; }
}

/// <summary>Puts the chosen row colours into the app's resources (the playlist's styles use them).</summary>
public static class PlaylistLook
{
    /// <summary>Each kind: what Settings calls it, the resource the styles use, its default, and its value in the settings.</summary>
    public static readonly (string Label, string Key, string Default, Func<PlaylistColors, string?> Get, Action<PlaylistColors, string?> Set)[] Kinds =
    [
        ("Ad breaks (adverts and their jingles)", "BreakRowBg", "#16271F", c => c.AdBreak, (c, v) => c.AdBreak = v),
        ("Jingles, promos and IDs", "JingleRowBg", "#261C33", c => c.Jingle, (c, v) => c.Jingle = v),
        ("Programmes and news", "ProgrammeRowBg", "#18213A", c => c.Programme, (c, v) => c.Programme = v),
        ("Streams", "StreamRowBg", "#132A2D", c => c.Stream, (c, v) => c.Stream = v),
        ("Songs", "MusicRowBg", "#00000000", c => c.Music, (c, v) => c.Music = v),
        ("The item on air", "OnAirRowBg", "#211619", c => c.OnAir, (c, v) => c.OnAir = v),
    ];

    public static void Apply(PlaylistColors colors)
    {
        if (Application.Current is not { } app) return;
        foreach (var (_, key, fallback, get, _) in Kinds)
            app.Resources[key] = new SolidColorBrush(Parse(get(colors)) ?? Color.Parse(fallback));
    }

    /// <summary>"#RGB", "#RRGGBB" or "#AARRGGBB"; null if it isn't one.</summary>
    public static Color? Parse(string? hex) =>
        !string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex.Trim(), out var c) ? c : null;
}
