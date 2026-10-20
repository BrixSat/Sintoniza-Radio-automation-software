using Avalonia.Media;

namespace Playout.App;

/// <summary>Preset colours and icons for cart buttons. Dark enough that white text stays readable.</summary>
static class CartLook
{
    public static readonly (string Name, string Hex)[] Colors =
    [
        ("Red", "#9B2C2C"),
        ("Orange", "#A0521B"),
        ("Yellow", "#8A6D12"),
        ("Green", "#1F6F4A"),
        ("Teal", "#146B6B"),
        ("Blue", "#24508F"),
        ("Purple", "#5B3F99"),
        ("Pink", "#8E2D63"),
        ("Grey", "#4A5263"),
    ];

    /// <summary>Key (saved in the settings), menu name and path data on a 16×16 grid.</summary>
    public static readonly (string Key, string Name, string Data)[] Icons =
    [
        ("jingle", "Jingle", "M8,0 L9.9,6.1 L16,8 L9.9,9.9 L8,16 L6.1,9.9 L0,8 L6.1,6.1 Z"),
        ("music", "Music", "M5.5,2.2 L15,0 V11.3 A2.6,2.2 0 1 1 13.4,9.3 V3.4 L7.1,4.9 V13.3 A2.6,2.2 0 1 1 5.5,11.3 Z"),
        ("commercial", "Commercial", "M0,5 H3 L12,0 V16 L3,11 H0 Z M13.5,6 H16 V10 H13.5 Z"),
        ("mic", "Microphone", "M5,3 A3,3 0 0 1 11,3 V8 A3,3 0 0 1 5,8 Z M2.5,7 H4 A4,4 0 0 0 12,7 H13.5 A5.5,5.5 0 0 1 8.75,12.4 V14.5 H11 V16 H5 V14.5 H7.25 V12.4 A5.5,5.5 0 0 1 2.5,7 Z"),
        ("news", "News", "M1,2 H13 V13 A1.5,1.5 0 0 1 11.5,14.5 H2.5 A1.5,1.5 0 0 1 1,13 Z M3,4 V7 H7 V4 Z M8.5,4 V5 H11 V4 Z M8.5,6 V7 H11 V6 Z M3,9 V10 H11 V9 Z M3,11 V12 H11 V11 Z M13.8,5 H15.5 V13 A1.5,1.5 0 0 1 14,14.5 H13.8 Z"),
        ("weather", "Weather", "M8,4.5 A3.5,3.5 0 1 1 7.99,4.5 Z M7.25,0 H8.75 V3 H7.25 Z M7.25,13 H8.75 V16 H7.25 Z M0,7.25 H3 V8.75 H0 Z M13,7.25 H16 V8.75 H13 Z M2.3,3.4 L3.4,2.3 L5.5,4.4 L4.4,5.5 Z M13.7,3.4 L12.6,2.3 L10.5,4.4 L11.6,5.5 Z M2.3,12.6 L3.4,13.7 L5.5,11.6 L4.4,10.5 Z M13.7,12.6 L12.6,13.7 L10.5,11.6 L11.6,10.5 Z"),
        ("clock", "Time", "M8,0 A8,8 0 1 1 7.99,0 Z M8,2 A6,6 0 1 0 8.01,2 Z M7.1,3.5 H8.9 V7.6 L11.6,10.3 L10.3,11.6 L7.1,8.4 Z"),
        ("bell", "Bell", "M8,0.5 A1.3,1.3 0 0 1 9.3,1.8 C11.6,2.4 13,4.4 13,7 V10.5 L14.5,12.5 H1.5 L3,10.5 V7 C3,4.4 4.4,2.4 6.7,1.8 A1.3,1.3 0 0 1 8,0.5 Z M6,13.5 H10 A2,2 0 0 1 6,13.5 Z"),
        ("bolt", "Effect", "M9.5,0 L2,9 H7 L6,16 L14,6.5 H9 Z"),
        ("heart", "Heart", "M8,15 C8,15 0,10 0,4.8 A4,4 0 0 1 8,3 A4,4 0 0 1 16,4.8 C16,10 8,15 8,15 Z"),
    ];

    static readonly Dictionary<string, Geometry> Geometries = new();

    public static Geometry? Icon(string? key)
    {
        if (key == null) return null;
        if (Geometries.TryGetValue(key, out var g)) return g;
        if (Icons.FirstOrDefault(i => i.Key == key) is not { Data: { } data }) return null;
        return Geometries[key] = StreamGeometry.Parse(data);
    }

    public static IBrush? Brush(string? hex) => hex != null && Color.TryParse(hex, out var c) ? new SolidColorBrush(c) : null;
}
