using System.Text.Json;

namespace Playout.App;

/// <summary>
/// The on-air program's red sign (MIC LIVE, ON AIR · show) for the studio clock on the same computer (Sintoniza Tempo):
/// written to the settings folder when it changes and every few seconds while Emi runs; a sign not written for a while is
/// out (Emi closed).
/// </summary>
public static class StudioSign
{
    sealed record State(DateTime AtUtc, string? Text);

    static string FilePath => Path.Combine(AppSettings.DataFolder, "sign.json");
    static readonly TimeSpan Every = TimeSpan.FromSeconds(5), Stale = TimeSpan.FromSeconds(15);
    static string? _written;
    static DateTime _writtenAt;

    /// <summary>What the sign shows now (null: out); written only when it changes or every few seconds.</summary>
    public static void Show(string? text)
    {
        var now = DateTime.UtcNow;
        if (text == _written && now - _writtenAt < Every) return;
        Write(text, now);
    }

    /// <summary>Out at once (the on-air program closing): the clock doesn't keep MIC LIVE lit until it goes stale.</summary>
    public static void Clear() => Write(null, DateTime.UtcNow);

    static void Write(string? text, DateTime now)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.DataFolder);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(new State(now, text)));
            File.Move(tmp, FilePath, overwrite: true);
            // Only once it is there: a write that failed (the clock reading it just then) is tried again next time.
            _written = text;
            _writtenAt = now;
        }
        catch (Exception) { /* tried again with the next update */ }
    }

    /// <summary>The sign as the on-air program last wrote it; null when it is out or Emi isn't running here.</summary>
    public static string? Read()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            // Read without holding it: the on-air program may put a new one in its place meanwhile.
            using var file = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var state = JsonSerializer.Deserialize<State>(file);
            return state != null && (DateTime.UtcNow - state.AtUtc).Duration() < Stale ? state.Text : null;
        }
        catch (Exception) { return null; }
    }
}
